using System.Text.Json;
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
        // ADR-018 D3 atomic write: the mapping INSERT, audit-row INSERT, and outbox enqueue
        // commit in a single PostgreSQL transaction via the repo's (conn, tx) overloads
        // (TASK-2401). A separate per-service OutboxPublisher drains outbox_events to the
        // canonical event store at-least-once (ADR-018 D4).
        //
        // Sets `ETag: "1"` on the 201 response (DB DEFAULT for first-create) so the next
        // PUT/DELETE can If-Match on this version. No If-* parsing — create endpoints have
        // no preceding row to assert against. The audit row uses the v2 audit overload
        // (preserved atomic-outbox primitive); first-create has no version-transition pair
        // to record (version_before is NULL by design).
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

            var mapping = new WageTypeMapping
            {
                TimeType = body.TimeType,
                WageType = body.WageType,
                OkVersion = body.OkVersion,
                AgreementCode = body.AgreementCode,
                Position = body.Position ?? "",
                Description = body.Description,
            };

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var success = await repo.CreateAsync(conn, tx, mapping, ct);
            if (!success)
                return Results.Conflict(new { error = "A mapping with this key already exists" });

            // Audit trail (Pattern B — repo owns audit emission via (conn, tx) v2 overload).
            // First-create has no preceding version to record — TASK-2503 / TASK-2504 set
            // the precedent that POST handlers stay on the v2 audit overload (atomic-outbox
            // primitive, S24 ForcedRollbackHarness consumer). version_before / version_after
            // remain NULL on the audit row, which the v3-with-version-pair overload would
            // otherwise have populated as (NULL, 1).
            await repo.AppendAuditAsync(
                conn, tx,
                body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "",
                "CREATED", null, JsonSerializer.Serialize(body),
                actorId, actorRole, ct);

            // Enqueue domain event INSIDE the same transaction (ADR-018 D3).
            var @event = new WageTypeMappingCreated
            {
                TimeType = body.TimeType,
                WageType = body.WageType,
                OkVersion = body.OkVersion,
                AgreementCode = body.AgreementCode,
                Position = body.Position ?? "",
                ActorId = actorId,
                ActorRole = actorRole,
                CorrelationId = actor.CorrelationId,
            };
            await outbox.EnqueueAsync(
                conn, tx,
                $"wage-type-mapping-{body.AgreementCode}-{body.OkVersion}-{body.TimeType}",
                @event, ct);

            await tx.CommitAsync(ct);

            // ETag for the 201 response — first-create is always version 1 (DB DEFAULT).
            context.Response.Headers.ETag = "\"1\"";
            return Results.Created($"/api/admin/wage-type-mappings/agreement/{body.AgreementCode}/{body.OkVersion}", new
            {
                timeType = body.TimeType,
                wageType = body.WageType,
                okVersion = body.OkVersion,
                agreementCode = body.AgreementCode,
                position = body.Position ?? "",
                description = body.Description,
                version = 1L,
            });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 4. PUT /api/admin/wage-type-mappings — Update mapping
        //
        // ADR-018 D3 atomic write — see POST handler above for rationale.
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

            // 1. Parse If-Match (admin-strict mode — rejects If-None-Match: *).
            //    Missing or malformed → 428 Precondition Required with the helper's hint.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence check — gives the caller a clean 404 before we open a
            //    tx, AND lets us echo `currentState` in any subsequent 412 response below.
            var previous = await repo.GetByKeyAsync(body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "", ct);
            if (previous is null)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            var mapping = new WageTypeMapping
            {
                TimeType = body.TimeType,
                WageType = body.WageType,
                OkVersion = body.OkVersion,
                AgreementCode = body.AgreementCode,
                Position = body.Position ?? "",
                Description = body.Description,
            };

            // 3. Atomic state-change + audit (with version-transition pair) + outbox enqueue
            //    (ADR-018 D3). The v3 UpdateAsync(conn, tx, mapping, expectedVersion, ...)
            //    enforces ETag/If-Match optimistic concurrency under SELECT ... FOR UPDATE
            //    and surfaces OptimisticConcurrencyException on stale version.
            SaveWageTypeMappingResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.UpdateAsync(conn, tx, mapping, expectedVersion, ct);

                    // v3 audit overload — captures (versionBefore = expectedVersion,
                    // versionAfter = saveResult.Version) into wage_type_mapping_audit's new
                    // version_before / version_after columns (TASK-2501 schema).
                    await repo.AppendAuditAsync(
                        conn, tx,
                        body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "",
                        "UPDATED",
                        JsonSerializer.Serialize(previous),
                        JsonSerializer.Serialize(body),
                        actorId, actorRole,
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new WageTypeMappingUpdated
                    {
                        TimeType = body.TimeType,
                        WageType = body.WageType,
                        OkVersion = body.OkVersion,
                        AgreementCode = body.AgreementCode,
                        Position = body.Position ?? "",
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
                // ADR-019 (pending) / ADR-018 D7: 412 Precondition Failed with the actual
                // current state surfaced for the caller's retry logic.
                var currentState = await repo.GetByKeyAsync(body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "", ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapToResponse(currentState),
                }, statusCode: 412);
            }

            // 4. Set ETag for the next If-Match and return the post-write snapshot.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(MapToResponse(saveResult.Mapping));
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 5. DELETE /api/admin/wage-type-mappings — Delete mapping
        //
        // ADR-018 D3 atomic write — see POST handler above for rationale.
        //
        // Admin-strict If-Match: "<version>" required.
        // 204 No Content on success — NO ETag header (resource gone; nothing to ETag).
        // 404 on not-found, 412 on stale, 428 on missing If-Match.
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

            // 3. Atomic delete (ADR-018 D3) — v3 DeleteAsync enforces ETag/If-Match
            //    optimistic concurrency under SELECT ... FOR UPDATE; concurrent delete
            //    between pre-check and our FOR UPDATE manifests as `false` → 404.
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    var success = await repo.DeleteAsync(
                        conn, tx, timeType, okVersion, agreementCode, pos, expectedVersion, ct);
                    if (!success)
                    {
                        // Row vanished between pre-check and our FOR UPDATE — surface 404.
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "Wage type mapping not found" });
                    }

                    // v3 audit overload — captures the version transition for replay
                    // determinism. DELETE doesn't bump version (the row is gone); we
                    // record (versionBefore = expectedVersion, versionAfter = expectedVersion)
                    // — the audit row marks the deletion at that version.
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

    private sealed class CreateWageTypeMappingRequest
    {
        public required string TimeType { get; init; }
        public required string WageType { get; init; }
        public required string OkVersion { get; init; }
        public required string AgreementCode { get; init; }
        public string? Position { get; init; }
        public string? Description { get; init; }
    }

    private sealed class UpdateWageTypeMappingRequest
    {
        public required string TimeType { get; init; }
        public required string WageType { get; init; }
        public required string OkVersion { get; init; }
        public required string AgreementCode { get; init; }
        public string? Position { get; init; }
        public string? Description { get; init; }
    }
}
