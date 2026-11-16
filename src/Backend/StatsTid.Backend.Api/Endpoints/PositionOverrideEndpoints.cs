using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Validation;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

public static class PositionOverrideEndpoints
{
    public static WebApplication MapPositionOverrideEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/position-overrides — List all position overrides
        //    The list response includes `version` per row so the frontend can compose
        //    `If-Match: "<version>"` for the next mutation without a separate by-id GET.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/position-overrides", async (
            PositionOverrideRepository repo,
            CancellationToken ct) =>
        {
            var overrides = await repo.GetAllAsync(ct);
            return Results.Ok(overrides.Select(MapEntityToResponse).ToList());
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<IEnumerable<PositionOverrideResponse>>(StatusCodes.Status200OK); // S118 / TASK-11800 — BARE array

        // ═══════════════════════════════════════════
        // 2. GET /api/admin/position-overrides/{overrideId:guid} — Get single by ID
        //    Sets `ETag: "<version>"` so the frontend can use it as the next If-Match.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/position-overrides/{overrideId:guid}", async (
            Guid overrideId,
            PositionOverrideRepository repo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var entity = await repo.GetByIdAsync(overrideId, ct);
            if (entity is null)
                return Results.NotFound(new { error = "Position override not found" });

            context.Response.Headers.ETag = $"\"{entity.Version}\"";
            return Results.Ok(MapEntityToResponse(entity));
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<PositionOverrideResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ═══════════════════════════════════════════
        // 3. GET /api/admin/position-overrides/agreement/{agreementCode}/{okVersion} — Get by agreement
        //    List response — `version` per row in body (no single ETag header — there are
        //    multiple rows). Frontend composes If-Match from the row it intends to mutate.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/position-overrides/agreement/{agreementCode}/{okVersion}", async (
            string agreementCode,
            string okVersion,
            PositionOverrideRepository repo,
            CancellationToken ct) =>
        {
            var overrides = await repo.GetByAgreementAsync(agreementCode, okVersion, ct);
            return Results.Ok(overrides.Select(MapEntityToResponse).ToList());
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<IEnumerable<PositionOverrideResponse>>(StatusCodes.Status200OK); // S118 / TASK-11800 — BARE array

        // ═══════════════════════════════════════════
        // 4. POST /api/admin/position-overrides — Create new override
        //    Sets `ETag: "1"` on the 201 response (DB DEFAULT for first-create) so the
        //    next PUT/Activate/Deactivate can If-Match on this version. No If-* parsing —
        //    create endpoint has no preceding row to assert against.
        // ═══════════════════════════════════════════
        // Atomic shape (S24 TASK-2404 / ADR-018 D2/D3): the position-override INSERT,
        // the audit-row INSERT, and the outbox enqueue commit in a single PostgreSQL
        // transaction via the repository's (conn, tx) overloads. A separate per-service
        // OutboxPublisher drains outbox_events to the canonical event store at-least-
        // once (ADR-018 D4), replacing the pre-S24 post-commit eventStore.AppendAsync
        // shape and closing the silent partial-failure window.
        app.MapPost("/api/admin/position-overrides", async (
            CreatePositionOverrideRequest body,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PositionOverrideCreated> createdMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // SEC-033: numeric range validation (→ 400) before persist. Every field is nullable;
            // null means "don't override" and is skipped — only supplied values are range-checked.
            var (rangesValid, rangeError) = PositionOverrideValidator.ValidateRanges(
                body.WeeklyNormHours, body.NormPeriodWeeks, body.MaxFlexBalance);
            if (!rangesValid)
                return Results.BadRequest(new { error = rangeError });

            // The applies-from date (NR-P8, QUAL-182): omitted means today in Copenhagen, read once
            // (PAT-028); a future date is refused here (R-18). The predecessor and Monday rules need
            // the key lock and are decided inside the repository writer.
            var (appliesFrom, dateRefusal) = AppliesFromValidation.Resolve(
                body.AppliesFrom, CopenhagenBusinessDate.Today(timeProvider));
            if (dateRefusal is not null)
                return dateRefusal;

            var entity = new PositionOverrideConfigEntity
            {
                OverrideId = Guid.Empty, // Assigned by repo
                AgreementCode = body.AgreementCode,
                OkVersion = body.OkVersion,
                PositionCode = body.PositionCode,
                MaxFlexBalance = body.MaxFlexBalance,
                NormPeriodWeeks = body.NormPeriodWeeks,
                WeeklyNormHours = body.WeeklyNormHours,
                Description = body.Description,
                Status = "ACTIVE",
                CreatedBy = actorId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            // S118 / TASK-11800 (owner ruling #1, the dead-branch class): the INSERT now runs
            // via CreateReturningAsync (INSERT … RETURNING *) INSIDE this same transaction —
            // the post-commit re-read and its `created is not null ? … : {overrideId}` fork
            // are structurally dead. The audit/outbox/audit-projection appends stay in-tx
            // AFTER the INSERT, byte-order unchanged (moving them would be a P3 violation).
            Guid overrideId;
            PositionOverrideConfigEntity created;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    // The head row and its first version row, dated appliesFrom (NR-P8, NR-P21).
                    created = await repo.CreateReturningAsync(conn, tx, entity, appliesFrom, ct);
                    overrideId = created.OverrideId;

                    // Create still uses the v2 AppendAuditAsync overload — the first-create has
                    // no prior version-transition to record (version_before is NULL by design).
                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "CREATED", null,
                        SerializeBodyForAudit(
                            body.AgreementCode, body.OkVersion, body.PositionCode, body.MaxFlexBalance,
                            body.NormPeriodWeeks, body.WeeklyNormHours, body.Description, appliesFrom),
                        actorId, actorRole, ct);

                    // Emit domain event via outbox INSIDE the transaction so the publisher only
                    // sees the row after the position-override + audit commit together.
                    var @event = new PositionOverrideCreated
                    {
                        OverrideId = overrideId,
                        AgreementCode = body.AgreementCode,
                        OkVersion = body.OkVersion,
                        PositionCode = body.PositionCode,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write — atomic with the
                    // position_override row + outbox row per ADR-018 D3/D13).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = createdMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (AppliesFromRefusedException ex)
            {
                // Refused under the key lock, before any write; the inner catch rolled back.
                return AppliesFromValidation.BadRequest(ex);
            }

            // ETag for the 201 response — sourced from the RETURNING-hydrated row (DB DEFAULT 1
            // on first-create; the `?? 1L` fallback died with the re-read, ruling #1).
            context.Response.Headers.ETag = $"\"{created.Version}\"";
            return Results.Created($"/api/admin/position-overrides/{overrideId}",
                MapEntityToResponse(created));
        }).RequireAuthorization("GlobalAdminOnly") // SEC-032: global config write — GlobalAdmin only (was LocalAdminOrAbove)
        .Produces<PositionOverrideResponse>(StatusCodes.Status201Created); // S118 / TASK-11800 — ruling #1: ALWAYS the full entity

        // ═══════════════════════════════════════════
        // 5. PUT /api/admin/position-overrides/{overrideId:guid} — Update an active override
        //    Admin-strict If-Match: "<version>" required (rejects If-None-Match: *).
        //    Stale → 412 with body {expectedVersion, actualVersion, currentState}.
        //    Missing → 428 Precondition Required with hint.
        //    Sets ETag: "<new-version>" on 200.
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/position-overrides/{overrideId:guid}", async (
            Guid overrideId,
            UpdatePositionOverrideRequest body,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PositionOverrideUpdated> updatedMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // SEC-033: numeric range validation (→ 400) before persist. Every field is nullable;
            // null means "don't override" and is skipped — only supplied values are range-checked.
            var (rangesValid, rangeError) = PositionOverrideValidator.ValidateRanges(
                body.WeeklyNormHours, body.NormPeriodWeeks, body.MaxFlexBalance);
            if (!rangesValid)
                return Results.BadRequest(new { error = rangeError });

            // 1. Parse If-Match (admin-strict mode — rejects If-None-Match: *).
            //    Missing or malformed → 428 Precondition Required with the helper's hint.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence + status check. Same pre-check as before — gives the
            //    caller a clean 404/409 before we open a tx, AND lets us echo `currentState`
            //    in any subsequent 412 response below.
            var previous = await repo.GetByIdAsync(overrideId, ct);
            if (previous is null)
                return Results.NotFound(new { error = "Position override not found" });

            if (!string.Equals(previous.Status, "ACTIVE", StringComparison.Ordinal))
                return Results.Json(new { error = "Only ACTIVE position overrides can be updated" }, statusCode: 409);

            // SEC-034: an override's identity triple (agreement_code, ok_version, position_code)
            // is IMMUTABLE. The repo's UpdateAsync writes only the VALUE columns, so the DB row's
            // identity never moves — but a PUT whose body carries a DIFFERENT triple would still
            // stamp the audit row (`new_data`), the PositionOverrideUpdated event, and its
            // projection with the BODY's (wrong) triple: a "phantom-identity" audit claiming the
            // override moved when the stored row did not. Reject such a PUT with 409 (consistent
            // with the non-ACTIVE 409 above) BEFORE any audit/outbox/projection emit — that is what
            // closes the phantom-audit path. A value-only PUT (norm/flex/description) is unaffected
            // and still returns 200 (body identity == stored identity on that path).
            if (!string.Equals(body.AgreementCode, previous.AgreementCode, StringComparison.Ordinal) ||
                !string.Equals(body.OkVersion, previous.OkVersion, StringComparison.Ordinal) ||
                !string.Equals(body.PositionCode, previous.PositionCode, StringComparison.Ordinal))
            {
                return Results.Json(new
                {
                    error = "An override's AgreementCode, OkVersion, and PositionCode are immutable; deactivate and recreate to move it.",
                }, statusCode: 409);
            }

            // The applies-from date (NR-P8): an edit writes a new dated version. Omitted means today in
            // Copenhagen, read once; a future date is refused here (R-18); the predecessor and Monday
            // rules are decided under the key lock inside UpdateAsync.
            var (appliesFrom, dateRefusal) = AppliesFromValidation.Resolve(
                body.AppliesFrom, CopenhagenBusinessDate.Today(timeProvider));
            if (dateRefusal is not null)
                return dateRefusal;

            var updatedEntity = new PositionOverrideConfigEntity
            {
                OverrideId = overrideId,
                // SEC-034 defence-in-depth: source the identity triple from the STORED row, never
                // the body. The guard above already rejects a mismatched triple, so on the 200 path
                // these equal the body values; sourcing from `previous` guarantees the entity can
                // never carry a phantom identity even if that guard were ever bypassed. (The repo's
                // UpdateAsync ignores identity columns on update anyway — belt-and-suspenders.)
                AgreementCode = previous.AgreementCode,
                OkVersion = previous.OkVersion,
                PositionCode = previous.PositionCode,
                MaxFlexBalance = body.MaxFlexBalance,
                NormPeriodWeeks = body.NormPeriodWeeks,
                WeeklyNormHours = body.WeeklyNormHours,
                Description = body.Description,
                Status = previous.Status,
                CreatedBy = previous.CreatedBy,
                CreatedAt = previous.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
            };

            // 3. Atomic state-change + audit (with version-transition pair) + outbox enqueue
            //    (ADR-018 D3). The v3 UpdateAsync(conn, tx, overrideId, expectedVersion, ...)
            //    enforces ETag/If-Match optimistic concurrency under SELECT ... FOR UPDATE
            //    and surfaces OptimisticConcurrencyException on stale version OR concurrent
            //    state change (e.g. row deactivated between pre-check and our FOR UPDATE).
            SavePositionOverrideResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.UpdateAsync(
                        conn, tx, overrideId, expectedVersion, updatedEntity, appliesFrom, actorId, ct);

                    // v3 audit overload — captures (versionBefore = expectedVersion,
                    // versionAfter = saveResult.Version) into position_override_config_audit's
                    // new version_before / version_after columns (TASK-2501 schema). new_data is
                    // the body with the resolved applies-from date (AC-C2l).
                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "UPDATED",
                        JsonSerializer.Serialize(previous),
                        SerializeBodyForAudit(
                            body.AgreementCode, body.OkVersion, body.PositionCode, body.MaxFlexBalance,
                            body.NormPeriodWeeks, body.WeeklyNormHours, body.Description, appliesFrom),
                        actorId, actorRole,
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new PositionOverrideUpdated
                    {
                        OverrideId = overrideId,
                        AgreementCode = body.AgreementCode,
                        OkVersion = body.OkVersion,
                        PositionCode = body.PositionCode,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = updatedMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

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
                // ADR-019 (pending) / ADR-018 D7: 412 Precondition Failed with the actual
                // current state surfaced for the caller's retry logic.
                var currentState = await repo.GetByIdAsync(overrideId, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapEntityToResponse(currentState),
                }, statusCode: 412);
            }
            catch (AppliesFromRefusedException ex)
            {
                // Refused under the key lock, before any write; the inner catch rolled back.
                return AppliesFromValidation.BadRequest(ex);
            }

            // 4. Set ETag for the next If-Match and return the post-write snapshot.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(MapEntityToResponse(saveResult.Override));
        }).RequireAuthorization("GlobalAdminOnly") // SEC-032: global config write — GlobalAdmin only (was LocalAdminOrAbove)
        .Produces<PositionOverrideResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ═══════════════════════════════════════════
        // 6. POST /api/admin/position-overrides/{overrideId:guid}/deactivate — Deactivate
        //    Admin-strict If-Match required. Same 412/428/ETag contract as PUT. An optional
        //    body carries `appliesFrom`: "no override from this date" (NR-P8); omitted means
        //    today in Copenhagen.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/position-overrides/{overrideId:guid}/deactivate", async (
            Guid overrideId,
            PositionOverrideTransitionRequest? request,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PositionOverrideDeactivated> deactivatedMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Parse If-Match.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence + status check.
            var existing = await repo.GetByIdAsync(overrideId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Position override not found" });

            if (!string.Equals(existing.Status, "ACTIVE", StringComparison.Ordinal))
                return Results.Json(new { error = "Only ACTIVE position overrides can be deactivated" }, statusCode: 409);

            // 2b. The applies-from date: omitted means today in Copenhagen, read once; a future
            //     date is refused here (R-18); the predecessor and Monday rules (switching off an
            //     override that sets the weekly norm needs a Monday) inside DeactivateAsync.
            var (appliesFrom, dateRefusal) = AppliesFromValidation.Resolve(
                request?.AppliesFrom, CopenhagenBusinessDate.Today(timeProvider));
            if (dateRefusal is not null)
                return dateRefusal;

            // 3. Atomic deactivate (ADR-018 D3) — v3 DeactivateAsync(conn, tx, overrideId,
            //    expectedVersion, appliesFrom, ...) takes the key lock, enforces ETag/If-Match
            //    optimistic concurrency, then the date rules, and writes the INACTIVE version row.
            SavePositionOverrideResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.DeactivateAsync(
                        conn, tx, overrideId, expectedVersion, appliesFrom, actorId, ct);

                    // new_data is the head as written: INACTIVE, dated appliesFrom (AC-C2l).
                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "DEACTIVATED",
                        JsonSerializer.Serialize(existing), JsonSerializer.Serialize(saveResult.Override),
                        actorId, actorRole,
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new PositionOverrideDeactivated
                    {
                        OverrideId = overrideId,
                        AgreementCode = existing.AgreementCode,
                        OkVersion = existing.OkVersion,
                        PositionCode = existing.PositionCode,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = deactivatedMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

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
                var currentState = await repo.GetByIdAsync(overrideId, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapEntityToResponse(currentState),
                }, statusCode: 412);
            }
            catch (AppliesFromRefusedException ex)
            {
                // Refused under the key lock, before any write; the inner catch rolled back.
                return AppliesFromValidation.BadRequest(ex);
            }

            // S118 / TASK-11800: the bespoke lifecycle envelope is now a named record.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new PositionOverrideDeactivateResponse(
                OverrideId: overrideId,
                Status: saveResult.Status,
                Deactivated: true));
        }).RequireAuthorization("GlobalAdminOnly") // SEC-032: global config write — GlobalAdmin only (was LocalAdminOrAbove)
        .Produces<PositionOverrideDeactivateResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ═══════════════════════════════════════════
        // 7. POST /api/admin/position-overrides/{overrideId:guid}/activate — Activate
        //    Admin-strict If-Match required. Same 412/428/ETag contract as PUT, PLUS an
        //    explicit 23505 catch for the partial-unique-index race
        //    `WHERE status='ACTIVE'` on (agreement_code, ok_version, position_code) — a
        //    distinct race class from row-version concurrency:
        //      - 412 (OptimisticConcurrencyException): caller's If-Match version stale OR
        //        the row is no longer INACTIVE on the FOR UPDATE re-read.
        //      - 409 (PostgresException SqlState=23505): a sibling override for the same
        //        (agreement, ok, position) is ACTIVE, violating the
        //        partial-unique-index `idx_position_override_active_unique`.
        //      - 400: the optional body's `appliesFrom` (NR-P8; omitted means today in
        //        Copenhagen) is in the future, before the key's newest version, or not a
        //        Monday when the activation changes the in-force weekly norm.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/position-overrides/{overrideId:guid}/activate", async (
            Guid overrideId,
            PositionOverrideTransitionRequest? request,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PositionOverrideActivated> activatedMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Parse If-Match.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence + status check.
            var existing = await repo.GetByIdAsync(overrideId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Position override not found" });

            if (!string.Equals(existing.Status, "INACTIVE", StringComparison.Ordinal))
                return Results.Json(new { error = "Only INACTIVE position overrides can be activated" }, statusCode: 409);

            // 2b. The applies-from date: omitted means today in Copenhagen, read once; a future
            //     date is refused here (R-18); the predecessor and Monday rules inside ActivateAsync.
            var (appliesFrom, dateRefusal) = AppliesFromValidation.Resolve(
                request?.AppliesFrom, CopenhagenBusinessDate.Today(timeProvider));
            if (dateRefusal is not null)
                return dateRefusal;

            // 3. Atomic activate (ADR-018 D3) — v3 ActivateAsync(conn, tx, overrideId,
            //    expectedVersion, appliesFrom, ...) takes the key lock, enforces ETag/If-Match
            //    optimistic concurrency, then the date rules, and writes the ACTIVE version row.
            //    The partial-unique-index may fire 23505 on the UPDATE if a sibling override for
            //    the same (agreement, ok, position) is ACTIVE; that's a different race class than
            //    version concurrency, so it's caught + mapped to 409 (NOT 412).
            SavePositionOverrideResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.ActivateAsync(
                        conn, tx, overrideId, expectedVersion, appliesFrom, actorId, ct);

                    // new_data is the head as written: ACTIVE, dated appliesFrom (AC-C2l).
                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "ACTIVATED",
                        null, JsonSerializer.Serialize(saveResult.Override),
                        actorId, actorRole,
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new PositionOverrideActivated
                    {
                        OverrideId = overrideId,
                        AgreementCode = existing.AgreementCode,
                        OkVersion = existing.OkVersion,
                        PositionCode = existing.PositionCode,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = activatedMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

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
                // ADR-019 D6 textual ordering: OptimisticConcurrencyException (412) catch
                // ALWAYS precedes PostgresException 23505 (409). The two exception types are
                // disjoint so first-match semantics produce identical behavior either way,
                // but the ADR-prose ordering is the canonical contract source for readers.
                var currentState = await repo.GetByIdAsync(overrideId, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapEntityToResponse(currentState),
                }, statusCode: 412);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                // Concurrent activation of a sibling override for the same
                // (agreement_code, ok_version, position_code) triple violated
                // `idx_position_override_active_unique` (partial-unique-index
                // WHERE status='ACTIVE'). Distinct from row-version 412 — different
                // race class than OptimisticConcurrencyException. The tx is already
                // rolled back by the inner catch; we only need to return 409.
                return Results.Conflict(new
                {
                    error = "Another override is already ACTIVE for this (agreement, ok, position)",
                });
            }
            catch (AppliesFromRefusedException ex)
            {
                // Refused under the key lock, before any write; the inner catch rolled back.
                return AppliesFromValidation.BadRequest(ex);
            }

            // S118 / TASK-11800: the bespoke lifecycle envelope is now a named record.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new PositionOverrideActivateResponse(
                OverrideId: overrideId,
                Status: saveResult.Status,
                Activated: true));
        }).RequireAuthorization("GlobalAdminOnly") // SEC-032: global config write — GlobalAdmin only (was LocalAdminOrAbove)
        .Produces<PositionOverrideActivateResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        return app;
    }

    // ── Response Mapping ──

    // S118 / TASK-11800 (PAT-012 retrofit Pass 5): the anonymous shape became the named
    // PositionOverrideResponse record — an EXACT shape-copy (same member names, same order,
    // same nullability; BYTE-IDENTICAL wire JSON). Also embedded (untyped) in the 412
    // error-body `currentState` envelopes, which stay anonymous per the S118 exclusions.
    private static PositionOverrideResponse MapEntityToResponse(PositionOverrideConfigEntity e) => new(
        OverrideId: e.OverrideId,
        AgreementCode: e.AgreementCode,
        OkVersion: e.OkVersion,
        PositionCode: e.PositionCode,
        Status: e.Status,
        Version: e.Version,
        MaxFlexBalance: e.MaxFlexBalance,
        NormPeriodWeeks: e.NormPeriodWeeks,
        WeeklyNormHours: e.WeeklyNormHours,
        AppliesFrom: e.AppliesFrom,
        CreatedBy: e.CreatedBy,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt,
        Description: e.Description);

    /// <summary>
    /// The CREATED and UPDATED audit rows' <c>new_data</c>: the request body's members, with the RESOLVED
    /// applies-from date in place of the body's (an omitted date is recorded as today, never null; AC-C2l).
    /// </summary>
    private static string SerializeBodyForAudit(
        string agreementCode, string okVersion, string positionCode,
        decimal? maxFlexBalance, int? normPeriodWeeks, decimal? weeklyNormHours, string? description,
        DateOnly appliesFrom) =>
        JsonSerializer.Serialize(new
        {
            AgreementCode = agreementCode,
            OkVersion = okVersion,
            PositionCode = positionCode,
            MaxFlexBalance = maxFlexBalance,
            NormPeriodWeeks = normPeriodWeeks,
            WeeklyNormHours = weeklyNormHours,
            Description = description,
            AppliesFrom = appliesFrom,
        });

    // ── Request DTOs (co-located) ──

    // AppliesFrom on every write (NR-P8, QUAL-182): optional on the wire; omitted means today in
    // Copenhagen, never 0001-01-01 (a nullable member, A-14).
    private sealed class CreatePositionOverrideRequest
    {
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public required string PositionCode { get; init; }
        public decimal? MaxFlexBalance { get; init; }
        public int? NormPeriodWeeks { get; init; }
        public decimal? WeeklyNormHours { get; init; }
        public string? Description { get; init; }
        public DateOnly? AppliesFrom { get; init; }
    }

    private sealed class UpdatePositionOverrideRequest
    {
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public required string PositionCode { get; init; }
        public decimal? MaxFlexBalance { get; init; }
        public int? NormPeriodWeeks { get; init; }
        public decimal? WeeklyNormHours { get; init; }
        public string? Description { get; init; }
        public DateOnly? AppliesFrom { get; init; }
    }

    /// <summary>
    /// The deactivate and activate routes' optional body (the house pattern of
    /// <c>OvertimeApprovalRequest?</c>): the date the transition applies from. Absent, <c>{}</c> or null
    /// means today in Copenhagen (A-14).
    /// </summary>
    public record PositionOverrideTransitionRequest(DateOnly? AppliesFrom);
}
