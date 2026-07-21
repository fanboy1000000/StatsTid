using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
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
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            var entity = new PositionOverrideConfigEntity
            {
                OverrideId = Guid.Empty, // Assigned by repo
                AgreementCode = body.AgreementCode,
                OkVersion = body.OkVersion,
                PositionCode = body.PositionCode,
                MaxFlexBalance = body.MaxFlexBalance,
                FlexCarryoverMax = body.FlexCarryoverMax,
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
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                created = await repo.CreateReturningAsync(conn, tx, entity, ct);
                overrideId = created.OverrideId;

                // Create still uses the v2 AppendAuditAsync overload — the first-create has
                // no prior version-transition to record (version_before is NULL by design).
                await repo.AppendAuditAsync(
                    conn, tx, overrideId, "CREATED", null, JsonSerializer.Serialize(body),
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

            // ETag for the 201 response — sourced from the RETURNING-hydrated row (DB DEFAULT 1
            // on first-create; the `?? 1L` fallback died with the re-read, ruling #1).
            context.Response.Headers.ETag = $"\"{created.Version}\"";
            return Results.Created($"/api/admin/position-overrides/{overrideId}",
                MapEntityToResponse(created));
        }).RequireAuthorization("LocalAdminOrAbove")
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

            // 2. Pre-flight existence + status check. Same pre-check as before — gives the
            //    caller a clean 404/409 before we open a tx, AND lets us echo `currentState`
            //    in any subsequent 412 response below.
            var previous = await repo.GetByIdAsync(overrideId, ct);
            if (previous is null)
                return Results.NotFound(new { error = "Position override not found" });

            if (!string.Equals(previous.Status, "ACTIVE", StringComparison.Ordinal))
                return Results.Json(new { error = "Only ACTIVE position overrides can be updated" }, statusCode: 409);

            var updatedEntity = new PositionOverrideConfigEntity
            {
                OverrideId = overrideId,
                AgreementCode = body.AgreementCode,
                OkVersion = body.OkVersion,
                PositionCode = body.PositionCode,
                MaxFlexBalance = body.MaxFlexBalance,
                FlexCarryoverMax = body.FlexCarryoverMax,
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
                    saveResult = await repo.UpdateAsync(conn, tx, overrideId, expectedVersion, updatedEntity, ct);

                    // v3 audit overload — captures (versionBefore = expectedVersion,
                    // versionAfter = saveResult.Version) into position_override_config_audit's
                    // new version_before / version_after columns (TASK-2501 schema).
                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "UPDATED",
                        JsonSerializer.Serialize(previous),
                        JsonSerializer.Serialize(body),
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

            // 4. Set ETag for the next If-Match and return the post-write snapshot.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(MapEntityToResponse(saveResult.Override));
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<PositionOverrideResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ═══════════════════════════════════════════
        // 6. POST /api/admin/position-overrides/{overrideId:guid}/deactivate — Deactivate
        //    Admin-strict If-Match required. Same 412/428/ETag contract as PUT.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/position-overrides/{overrideId:guid}/deactivate", async (
            Guid overrideId,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PositionOverrideDeactivated> deactivatedMapper,
            AuditProjectionRepository auditRepo,
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

            // 3. Atomic deactivate (ADR-018 D3) — v3 DeactivateAsync(conn, tx, overrideId,
            //    expectedVersion, ...) enforces ETag/If-Match optimistic concurrency.
            SavePositionOverrideResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.DeactivateAsync(conn, tx, overrideId, expectedVersion, ct);

                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "DEACTIVATED",
                        JsonSerializer.Serialize(existing), null,
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

            // S118 / TASK-11800: the bespoke lifecycle envelope is now a named record.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new PositionOverrideDeactivateResponse(
                OverrideId: overrideId,
                Status: saveResult.Status,
                Deactivated: true));
        }).RequireAuthorization("LocalAdminOrAbove")
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
        //        (agreement, ok, position) was concurrently activated, violating the
        //        partial-unique-index `idx_position_override_active_unique`.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/position-overrides/{overrideId:guid}/activate", async (
            Guid overrideId,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<PositionOverrideActivated> activatedMapper,
            AuditProjectionRepository auditRepo,
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

            // 3. Atomic activate (ADR-018 D3) — v3 ActivateAsync(conn, tx, overrideId,
            //    expectedVersion, ...) enforces ETag/If-Match optimistic concurrency under
            //    SELECT ... FOR UPDATE. The partial-unique-index may fire 23505 on the
            //    UPDATE if a sibling override for the same (agreement, ok, position) raced
            //    in an activation; that's a different race class than version concurrency,
            //    so it's caught + mapped to 409 (NOT 412).
            SavePositionOverrideResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await repo.ActivateAsync(conn, tx, overrideId, expectedVersion, ct);

                    await repo.AppendAuditAsync(
                        conn, tx, overrideId, "ACTIVATED",
                        null, JsonSerializer.Serialize(existing),
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

            // S118 / TASK-11800: the bespoke lifecycle envelope is now a named record.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new PositionOverrideActivateResponse(
                OverrideId: overrideId,
                Status: saveResult.Status,
                Activated: true));
        }).RequireAuthorization("LocalAdminOrAbove")
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
        FlexCarryoverMax: e.FlexCarryoverMax,
        NormPeriodWeeks: e.NormPeriodWeeks,
        WeeklyNormHours: e.WeeklyNormHours,
        CreatedBy: e.CreatedBy,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt,
        Description: e.Description);

    // ── Request DTOs (co-located) ──

    private sealed class CreatePositionOverrideRequest
    {
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public required string PositionCode { get; init; }
        public decimal? MaxFlexBalance { get; init; }
        public decimal? FlexCarryoverMax { get; init; }
        public int? NormPeriodWeeks { get; init; }
        public decimal? WeeklyNormHours { get; init; }
        public string? Description { get; init; }
    }

    private sealed class UpdatePositionOverrideRequest
    {
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public required string PositionCode { get; init; }
        public decimal? MaxFlexBalance { get; init; }
        public decimal? FlexCarryoverMax { get; init; }
        public int? NormPeriodWeeks { get; init; }
        public decimal? WeeklyNormHours { get; init; }
        public string? Description { get; init; }
    }
}
