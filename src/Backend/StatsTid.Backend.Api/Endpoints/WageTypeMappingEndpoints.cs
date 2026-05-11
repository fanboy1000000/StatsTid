using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

public static class WageTypeMappingEndpoints
{
    public static WebApplication MapWageTypeMappingEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/wage-type-mappings — List all wage type mappings
        //    The list response includes `version` per row so the frontend can compose
        //    `If-Match: "<version>"` for the next mutation. There is no GET-by-id surface
        //    on this resource (composite primary key), so the list response is the single
        //    source of truth for ETag composition.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/wage-type-mappings", async (
            WageTypeMappingRepository repo,
            CancellationToken ct) =>
        {
            var mappings = await repo.GetAllAsync(ct);
            return Results.Ok(mappings.Select(MapToResponse).ToList());
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 2. GET /api/admin/wage-type-mappings/agreement/{agreementCode}/{okVersion} — Get by agreement
        //    Same shape as the list — `version` per row in body (no single ETag header
        //    since multiple rows are returned).
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/wage-type-mappings/agreement/{agreementCode}/{okVersion}", async (
            string agreementCode,
            string okVersion,
            WageTypeMappingRepository repo,
            CancellationToken ct) =>
        {
            var mappings = await repo.GetByAgreementAsync(agreementCode, okVersion, ct);
            return Results.Ok(mappings.Select(MapToResponse).ToList());
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 3. POST /api/admin/wage-type-mappings — Create mapping
        //
        // S29 / TASK-2908 — ADR-020 D2 three-case routing under a same-tx natural-key
        // `SELECT ... FOR UPDATE WHERE effective_to = today` lock:
        //
        //   Case A (0 rows — no closed-on-today predecessor)
        //     → fresh INSERT at version=1, CREATED audit, WageTypeMappingCreated outbox event.
        //     The partial-unique-index `idx_wtm_natural_key_open WHERE effective_to IS NULL`
        //     enforces at-most-one-open-row; a racing concurrent CREATE wins on the index and
        //     surfaces here as PostgresException 23505 → translated to 412 (mirror of S22
        //     `LocalAgreementProfileRepository.SupersedeAndCreateAsync` empty-slot precedent).
        //
        //   Case B (1 row, predecessor.effective_from < today)
        //     → predecessor stays closed at (predecessor.effective_from, today). Fresh INSERT
        //     new open row at (effective_from=today, version=1). CREATED audit (the
        //     predecessor's DELETED was already audited at its own DELETE request).
        //     WageTypeMappingCreated outbox event.
        //
        //   Case C (1 row, predecessor.effective_from = today — zero-width [today, today))
        //     → UPDATE-and-reopen: stamp effective_to = NULL on the closed-today row, bump
        //     version, apply new field values from body. UPDATED audit (collapses the prior
        //     DELETE-plus-current-CREATE intent per ADR-020 D2). WageTypeMappingUpdated
        //     outbox event.
        //
        // Same-day-only-edit validator (cycle 3 user adjudication — symmetric forbid; refinement
        // L127): `body.EffectiveFrom != today` is rejected with 422 BEFORE the tx opens. 422
        // (RFC 4918), not 400 — explicit predicate-shape contrast with the S22 PUT validator
        // which rejects only `> today` with 400.
        //
        // ADR-018 D3 atomic write: the state mutation, audit-row INSERT, and outbox enqueue
        // all commit in a single PostgreSQL transaction; the publisher drains at-least-once
        // (ADR-018 D4). Sets `ETag: "<version>"` on the 201 response so the next PUT/DELETE
        // can If-Match on this version.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/wage-type-mappings", async (
            CreateWageTypeMappingRequest body,
            WageTypeMappingRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Same-day-only-edit validator (refinement L127, cycle 3 symmetric forbid).
            //    POST defaults a missing body.EffectiveFrom to today (preserves the common
            //    admin-create-now case); any other supplied date → 422.
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var requestedEffectiveFrom = body.EffectiveFrom ?? today;
            if (requestedEffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "effective_from must equal today (same-day-only edits permitted in S29)",
                    suppliedEffectiveFrom = requestedEffectiveFrom,
                    today = today,
                });
            }

            var position = body.Position ?? "";
            var streamId = $"wage-type-mapping-{body.AgreementCode}-{body.OkVersion}-{body.TimeType}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 2. Acquire the natural-key lock on any row CLOSED-TODAY (effective_to = today).
                //    Routes the three ADR-020 D2 cases:
                //      0 rows                                → Case A (no predecessor closed today)
                //      1 row, effective_from < today         → Case B (cross-day reopen)
                //      1 row, effective_from = today         → Case C (zero-width reopen / UPDATE)
                WageTypeMapping? closedToday = null;
                await using (var lockCmd = new NpgsqlCommand(
                    """
                    SELECT mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                           description, version, effective_from, effective_to
                    FROM wage_type_mappings
                    WHERE time_type = @timeType
                      AND ok_version = @okVersion
                      AND agreement_code = @agreementCode
                      AND position = @position
                      AND effective_to = @today
                    FOR UPDATE
                    """, conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("timeType", body.TimeType);
                    lockCmd.Parameters.AddWithValue("okVersion", body.OkVersion);
                    lockCmd.Parameters.AddWithValue("agreementCode", body.AgreementCode);
                    lockCmd.Parameters.AddWithValue("position", position);
                    lockCmd.Parameters.AddWithValue("today", today);
                    await using var reader = await lockCmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        closedToday = new WageTypeMapping
                        {
                            MappingId = reader.GetGuid(0),
                            TimeType = reader.GetString(1),
                            WageType = reader.GetString(2),
                            OkVersion = reader.GetString(3),
                            AgreementCode = reader.GetString(4),
                            Position = reader.GetString(5),
                            Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                            Version = reader.GetInt64(7),
                            EffectiveFrom = reader.GetFieldValue<DateOnly>(8),
                            EffectiveTo = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                        };
                    }
                }

                long persistedVersion;
                string auditAction;
                string? previousDataJson;
                IDomainEvent @event;

                if (closedToday is null)
                {
                    // ── Case A — no closed-today predecessor → fresh INSERT (version=1).
                    //
                    //   The partial-unique-index idx_wtm_natural_key_open enforces
                    //   at-most-one-open-row per natural key. A concurrent CREATE that wins
                    //   the race surfaces here as PostgresException 23505 → 412 (mirrors S22
                    //   LocalAgreementProfileRepository empty-slot precedent at L317-336).
                    var mapping = new WageTypeMapping
                    {
                        TimeType = body.TimeType,
                        WageType = body.WageType,
                        OkVersion = body.OkVersion,
                        AgreementCode = body.AgreementCode,
                        Position = position,
                        Description = body.Description,
                        EffectiveFrom = today,
                    };
                    try
                    {
                        await repo.CreateAsync(conn, tx, mapping, ct);
                    }
                    catch (PostgresException ex) when (
                        ex.SqlState == "23505" && ex.ConstraintName == "idx_wtm_natural_key_open")
                    {
                        // Concurrent CREATE for the same natural key won the partial-unique-index
                        // race. Roll back our tx and surface 412 (current state changed under us);
                        // the caller's retry can read the freshly-current open row and proceed.
                        await tx.RollbackAsync(ct);
                        var currentState = await repo.GetByKeyAsync(
                            body.TimeType, body.OkVersion, body.AgreementCode, position, ct);
                        return Results.Json(new
                        {
                            error = "Concurrency precondition failed",
                            message = "Another mapping was created concurrently for this natural key; refresh and retry.",
                            currentState = currentState is null ? null : MapToResponse(currentState),
                        }, statusCode: 412);
                    }
                    persistedVersion = 1L;
                    auditAction = "CREATED";
                    previousDataJson = null;
                    @event = new WageTypeMappingCreated
                    {
                        TimeType = body.TimeType,
                        WageType = body.WageType,
                        OkVersion = body.OkVersion,
                        AgreementCode = body.AgreementCode,
                        Position = position,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                }
                else if (closedToday.EffectiveFrom < today)
                {
                    // ── Case B — cross-day reopen. Predecessor stays closed at
                    //     (effective_from, today); INSERT new open row at (today, NULL, v=1).
                    //
                    //   Note: the predecessor's own DELETED audit was emitted at its DELETE
                    //   request; we do NOT re-audit it now. We audit ONLY the new row as
                    //   CREATED + emit WageTypeMappingCreated.
                    var mapping = new WageTypeMapping
                    {
                        TimeType = body.TimeType,
                        WageType = body.WageType,
                        OkVersion = body.OkVersion,
                        AgreementCode = body.AgreementCode,
                        Position = position,
                        Description = body.Description,
                        EffectiveFrom = today,
                    };
                    await repo.CreateAsync(conn, tx, mapping, ct);
                    persistedVersion = 1L;
                    auditAction = "CREATED";
                    previousDataJson = null;
                    @event = new WageTypeMappingCreated
                    {
                        TimeType = body.TimeType,
                        WageType = body.WageType,
                        OkVersion = body.OkVersion,
                        AgreementCode = body.AgreementCode,
                        Position = position,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                }
                else
                {
                    // ── Case C — zero-width close-then-reopen on the same day. The row
                    //     has effective_from = effective_to = today (a DELETE earlier today
                    //     followed by a re-CREATE). Collapse intent into an in-place UPDATE
                    //     that reopens the row (effective_to = NULL), bumps version, and
                    //     replaces the field values. UPDATED audit, WageTypeMappingUpdated.
                    long updatedVersion;
                    await using (var reopenCmd = new NpgsqlCommand(
                        """
                        UPDATE wage_type_mappings SET
                            wage_type    = @wageType,
                            description  = @description,
                            effective_to = NULL,
                            version      = version + 1
                        WHERE mapping_id = @mappingId
                        RETURNING version
                        """, conn, tx))
                    {
                        reopenCmd.Parameters.AddWithValue("wageType", body.WageType);
                        reopenCmd.Parameters.AddWithValue("description",
                            (object?)body.Description ?? DBNull.Value);
                        reopenCmd.Parameters.AddWithValue("mappingId", closedToday.MappingId);
                        var versionObj = await reopenCmd.ExecuteScalarAsync(ct);
                        if (versionObj is null)
                        {
                            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
                            throw new InvalidOperationException(
                                "Case-C reopen UPDATE produced no row despite holding FOR UPDATE lock.");
                        }
                        updatedVersion = (long)versionObj;
                    }
                    persistedVersion = updatedVersion;
                    auditAction = "UPDATED";
                    previousDataJson = JsonSerializer.Serialize(closedToday);
                    @event = new WageTypeMappingUpdated
                    {
                        TimeType = body.TimeType,
                        WageType = body.WageType,
                        OkVersion = body.OkVersion,
                        AgreementCode = body.AgreementCode,
                        Position = position,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                }

                // 3. Audit + outbox (ADR-018 D3 atomic — same conn+tx). Case A/B use the v2
                //    overload (first-create, no prior version); Case C uses the v3 overload
                //    (UPDATED records the version transition).
                if (auditAction == "UPDATED")
                {
                    await repo.AppendAuditAsync(
                        conn, tx,
                        body.TimeType, body.OkVersion, body.AgreementCode, position,
                        auditAction,
                        previousDataJson,
                        JsonSerializer.Serialize(body),
                        actorId, actorRole,
                        versionBefore: closedToday!.Version, versionAfter: persistedVersion, ct);
                }
                else
                {
                    await repo.AppendAuditAsync(
                        conn, tx,
                        body.TimeType, body.OkVersion, body.AgreementCode, position,
                        auditAction, previousDataJson, JsonSerializer.Serialize(body),
                        actorId, actorRole, ct);
                }
                await outbox.EnqueueAsync(conn, tx, streamId, @event, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{persistedVersion}\"";
                return Results.Created(
                    $"/api/admin/wage-type-mappings/agreement/{body.AgreementCode}/{body.OkVersion}",
                    new
                    {
                        timeType = body.TimeType,
                        wageType = body.WageType,
                        okVersion = body.OkVersion,
                        agreementCode = body.AgreementCode,
                        position,
                        description = body.Description,
                        version = persistedVersion,
                    });
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 4. PUT /api/admin/wage-type-mappings — Update mapping
        //
        // S29 / TASK-2908 — routes through `SupersedeAndCreateAsync` per ADR-020 D2. The repo
        // dispatch logic (Assumption #15) routes between two branches based on
        // `newMapping.EffectiveFrom == predecessor.EffectiveFrom`:
        //
        //   Same-day (predecessor.EffectiveFrom == today, body.EffectiveFrom == today)
        //     → in-place UPDATE on the open row + version bump. UPDATED audit;
        //     WageTypeMappingUpdated outbox event.
        //
        //   Cross-day (predecessor.EffectiveFrom < today, body.EffectiveFrom == today)
        //     → close predecessor at (effective_to = today) + INSERT new open row at
        //     (effective_from = today, version = 1). SUPERSEDED audit; WageTypeMappingSuperseded
        //     outbox event. This is the typical admin flow for day-old / seed predecessors.
        //
        // The cycle 3 symmetric-forbid validator constrains the REQUESTED effective_from to
        // today; the predecessor's age determines the routing. Backdates (body.EffectiveFrom
        // < predecessor.EffectiveFrom) are unreachable through the validator (today > any
        // past day).
        //
        // ADR-018 D3 atomic write — repo state mutation, audit-row INSERT, and outbox enqueue
        // all commit in a single tx; publisher drains at-least-once.
        //
        // Admin-strict If-Match: "<version>" required (rejects If-None-Match: *).
        // Stale → 412 with body {expectedVersion, actualVersion, currentState}.
        // Missing → 428 Precondition Required with hint.
        // Sets ETag: "<new-version>" on 200.
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/wage-type-mappings", async (
            UpdateWageTypeMappingRequest body,
            WageTypeMappingRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Same-day-only-edit validator (refinement L127, cycle 3 symmetric forbid) —
            //    runs BEFORE opening the tx. PUT requires body.EffectiveFrom; rejects any
            //    value != today with 422.
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            if (body.EffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "effective_from must equal today (same-day-only edits permitted in S29)",
                    suppliedEffectiveFrom = body.EffectiveFrom,
                    today = today,
                });
            }

            // 2. Parse If-Match (admin-strict mode — rejects If-None-Match: *).
            //    Missing or malformed → 428 Precondition Required with the helper's hint.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Pre-flight existence check — gives the caller a clean 404 before we open a
            //    tx, AND lets us echo `currentState` in any subsequent 412 response below.
            var position = body.Position ?? "";
            var previous = await repo.GetByKeyAsync(
                body.TimeType, body.OkVersion, body.AgreementCode, position, ct);
            if (previous is null)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            var newMapping = new WageTypeMapping
            {
                TimeType = body.TimeType,
                WageType = body.WageType,
                OkVersion = body.OkVersion,
                AgreementCode = body.AgreementCode,
                Position = position,
                Description = body.Description,
                EffectiveFrom = body.EffectiveFrom,
            };

            // 4. Atomic supersession + audit (version-transition pair) + outbox enqueue
            //    (ADR-018 D3). SupersedeAndCreateAsync routes same-day vs cross-day inside
            //    the repo under SELECT ... FOR UPDATE.
            SaveWageTypeMappingResult saveResult;
            bool isCrossDay;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.SupersedeAndCreateAsync(
                        conn, tx, newMapping, expectedCurrentVersion: expectedVersion, ct);

                    // Cross-day → SUPERSEDED + WageTypeMappingSuperseded; same-day → UPDATED
                    // + WageTypeMappingUpdated. Routing matches the repo's internal branch.
                    isCrossDay = previous.EffectiveFrom < body.EffectiveFrom;
                    var auditAction = isCrossDay ? "SUPERSEDED" : "UPDATED";

                    await repo.AppendAuditAsync(
                        conn, tx,
                        body.TimeType, body.OkVersion, body.AgreementCode, position,
                        auditAction,
                        JsonSerializer.Serialize(previous),
                        JsonSerializer.Serialize(body),
                        actorId, actorRole,
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    IDomainEvent @event = isCrossDay
                        ? new WageTypeMappingSuperseded
                        {
                            TimeType = body.TimeType,
                            WageType = body.WageType,
                            OkVersion = body.OkVersion,
                            AgreementCode = body.AgreementCode,
                            Position = position,
                            ActorId = actorId,
                            ActorRole = actorRole,
                            CorrelationId = actor.CorrelationId,
                        }
                        : new WageTypeMappingUpdated
                        {
                            TimeType = body.TimeType,
                            WageType = body.WageType,
                            OkVersion = body.OkVersion,
                            AgreementCode = body.AgreementCode,
                            Position = position,
                            ActorId = actorId,
                            ActorRole = actorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                    await outbox.EnqueueAsync(
                        conn, tx,
                        $"wage-type-mapping-{body.AgreementCode}-{body.OkVersion}-{body.TimeType}",
                        @event, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (KeyNotFoundException)
            {
                // Concurrent delete between pre-check and our FOR UPDATE — surface as 404.
                return Results.NotFound(new { error = "Wage type mapping not found" });
            }
            catch (OptimisticConcurrencyException ex)
            {
                // ADR-018 D7: 412 Precondition Failed with the actual current state surfaced
                // for the caller's retry logic.
                var currentState = await repo.GetByKeyAsync(
                    body.TimeType, body.OkVersion, body.AgreementCode, position, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapToResponse(currentState),
                }, statusCode: 412);
            }

            // 5. Set ETag for the next If-Match and return the post-write snapshot.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(MapToResponse(saveResult.Mapping));
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 5. DELETE /api/admin/wage-type-mappings — Soft-delete mapping
        //
        // ADR-018 D3 atomic write — see POST handler above for rationale.
        //
        // S29 / TASK-2904 — soft-delete via `effective_to = today` (preserves replay
        // determinism through the closed row). Migrated in commit e3f851e — DO NOT
        // re-migrate. Admin-strict If-Match: "<version>" required. 204 No Content on
        // success — NO ETag header (resource gone; nothing to ETag).
        // ═══════════════════════════════════════════
        app.MapDelete("/api/admin/wage-type-mappings", async (
            string timeType,
            string okVersion,
            string agreementCode,
            string? position,
            WageTypeMappingRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var pos = position ?? "";

            // 1. Parse If-Match (admin-strict mode).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence check (also captures the data for the audit row).
            var existing = await repo.GetByKeyAsync(timeType, okVersion, agreementCode, pos, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            // 3. Atomic soft-delete (ADR-018 D3 + ADR-020 D2). SoftDeleteAsync stamps
            //    effective_to = today under SELECT ... FOR UPDATE; concurrent delete
            //    between pre-check and our FOR UPDATE manifests as `false` → 404.
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    // S29 / TASK-2904: hard DeleteAsync(conn, tx, ..., expectedVersion) was
                    // replaced by SoftDeleteAsync (sets effective_to = today on the open row)
                    // per ADR-020 D2. Replay determinism preserved — past forward-calcs against
                    // the (then-open) row continue to read the closed row via GetByKeyAtAsync.
                    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
                    var success = await repo.SoftDeleteAsync(
                        conn, tx, timeType, okVersion, agreementCode, pos, expectedVersion, today, ct);
                    if (!success)
                    {
                        // Row vanished between pre-check and our FOR UPDATE — surface 404.
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "Wage type mapping not found" });
                    }

                    // v3 audit overload — captures the version transition for replay
                    // determinism. DELETE doesn't bump version (the row is closed in place);
                    // we record (versionBefore = expectedVersion, versionAfter = expectedVersion)
                    // — the audit row marks the closure at that version.
                    await repo.AppendAuditAsync(
                        conn, tx,
                        timeType, okVersion, agreementCode, pos,
                        "DELETED",
                        JsonSerializer.Serialize(existing),
                        null,
                        actorId, actorRole,
                        versionBefore: expectedVersion, versionAfter: expectedVersion, ct);

                    var @event = new WageTypeMappingDeleted
                    {
                        TimeType = timeType,
                        OkVersion = okVersion,
                        AgreementCode = agreementCode,
                        Position = pos,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(
                        conn, tx,
                        $"wage-type-mapping-{agreementCode}-{okVersion}-{timeType}",
                        @event, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                // 412 Precondition Failed with the actual current state. The row still
                // exists at this point (the OCC threw before the DELETE), so re-read it.
                var currentState = await repo.GetByKeyAsync(timeType, okVersion, agreementCode, pos, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapToResponse(currentState),
                }, statusCode: 412);
            }

            // 204 No Content — no body, no ETag header (resource gone).
            return Results.NoContent();
        }).RequireAuthorization("GlobalAdminOnly");

        return app;
    }

    // ── Response Mapping ──

    /// <summary>
    /// Map the entity to the admin response shape — surfaces <c>version</c> for the
    /// frontend to compose <c>If-Match</c> on subsequent mutations (ADR-019 pending).
    /// </summary>
    private static object MapToResponse(WageTypeMapping m) => new
    {
        timeType = m.TimeType,
        wageType = m.WageType,
        okVersion = m.OkVersion,
        agreementCode = m.AgreementCode,
        position = m.Position,
        description = m.Description,
        version = m.Version,
    };

    // ── Request DTOs (co-located) ──

    /// <summary>
    /// POST request body. <see cref="EffectiveFrom"/> is OPTIONAL — when omitted, the
    /// endpoint defaults it to today (preserves the common admin-create-now case). When
    /// supplied, the same-day-only-edit validator (refinement L127) requires it == today.
    /// </summary>
    private sealed class CreateWageTypeMappingRequest
    {
        public required string TimeType { get; init; }
        public required string WageType { get; init; }
        public required string OkVersion { get; init; }
        public required string AgreementCode { get; init; }
        public string? Position { get; init; }
        public string? Description { get; init; }

        // S29 / TASK-2908: optional same-day-only field. Defaulted to today by the endpoint
        // when omitted; rejected with 422 when supplied with any other value.
        public DateOnly? EffectiveFrom { get; init; }
    }

    /// <summary>
    /// PUT request body. <see cref="EffectiveFrom"/> is REQUIRED and must equal today
    /// per the same-day-only-edit validator (refinement L127, cycle 3 symmetric forbid).
    /// </summary>
    private sealed class UpdateWageTypeMappingRequest
    {
        public required string TimeType { get; init; }
        public required string WageType { get; init; }
        public required string OkVersion { get; init; }
        public required string AgreementCode { get; init; }
        public string? Position { get; init; }
        public string? Description { get; init; }

        // S29 / TASK-2908: required. Validator rejects != today with 422.
        public required DateOnly EffectiveFrom { get; init; }
    }
}
