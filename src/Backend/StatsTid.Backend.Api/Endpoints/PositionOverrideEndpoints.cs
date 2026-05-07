using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
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
        }).RequireAuthorization("GlobalAdminOnly");

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
        }).RequireAuthorization("GlobalAdminOnly");

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
        }).RequireAuthorization("GlobalAdminOnly");

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

            Guid overrideId;
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                overrideId = await repo.CreateAsync(conn, tx, entity, ct);

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
                await outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

                await tx.CommitAsync(ct);
            }

            // Re-read to get DB-generated timestamps + version (post-commit; outside the write tx).
            var created = await repo.GetByIdAsync(overrideId, ct);

            // ETag for the 201 response. Sourced from the DB column when re-read succeeds;
            // falls back to the static "1" (DB DEFAULT) if the re-read returned null.
            context.Response.Headers.ETag = $"\"{(created?.Version ?? 1L)}\"";
            return Results.Created($"/api/admin/position-overrides/{overrideId}",
                created is not null ? MapEntityToResponse(created) : (object)new { overrideId });
        }).RequireAuthorization("GlobalAdminOnly");

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
                    await outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

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
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 6. POST /api/admin/position-overrides/{overrideId:guid}/deactivate — Deactivate
        //    Admin-strict If-Match required. Same 412/428/ETag contract as PUT.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/position-overrides/{overrideId:guid}/deactivate", async (
            Guid overrideId,
            DbConnectionFactory connectionFactory,
            PositionOverrideRepository repo,
            IOutboxEnqueue outbox,
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
                    await outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

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

            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new
            {
                overrideId,
                status = saveResult.Status,
                deactivated = true,
            });
        }).RequireAuthorization("GlobalAdminOnly");

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
                    await outbox.EnqueueAsync(conn, tx, $"position-override-{overrideId}", @event, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
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

            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new
            {
                overrideId,
                status = saveResult.Status,
                activated = true,
            });
        }).RequireAuthorization("GlobalAdminOnly");

        return app;
    }

    // ── Response Mapping ──

    private static object MapEntityToResponse(PositionOverrideConfigEntity e) => new
    {
        overrideId = e.OverrideId,
        agreementCode = e.AgreementCode,
        okVersion = e.OkVersion,
        positionCode = e.PositionCode,
        status = e.Status,
        // Row-version optimistic-concurrency token (TASK-2501 schema, ADR-019 pending).
        // Surfaced in body for list responses where multiple rows preclude a single ETag
        // header; by-id GET also sets the matching ETag header.
        version = e.Version,
        maxFlexBalance = e.MaxFlexBalance,
        flexCarryoverMax = e.FlexCarryoverMax,
        normPeriodWeeks = e.NormPeriodWeeks,
        weeklyNormHours = e.WeeklyNormHours,
        createdBy = e.CreatedBy,
        createdAt = e.CreatedAt,
        updatedAt = e.UpdatedAt,
        description = e.Description,
    };

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
