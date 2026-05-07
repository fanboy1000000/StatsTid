using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

public static class PositionOverrideEndpoints
{
    public static WebApplication MapPositionOverrideEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/position-overrides — List all position overrides
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/position-overrides", async (
            PositionOverrideRepository repo,
            CancellationToken ct) =>
        {
            var overrides = await repo.GetAllAsync(ct);
            return Results.Ok(overrides);
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 2. GET /api/admin/position-overrides/{overrideId:guid} — Get single by ID
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/position-overrides/{overrideId:guid}", async (
            Guid overrideId,
            PositionOverrideRepository repo,
            CancellationToken ct) =>
        {
            var entity = await repo.GetByIdAsync(overrideId, ct);
            if (entity is null)
                return Results.NotFound(new { error = "Position override not found" });

            return Results.Ok(entity);
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 3. GET /api/admin/position-overrides/agreement/{agreementCode}/{okVersion} — Get by agreement
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/position-overrides/agreement/{agreementCode}/{okVersion}", async (
            string agreementCode,
            string okVersion,
            PositionOverrideRepository repo,
            CancellationToken ct) =>
        {
            var overrides = await repo.GetByAgreementAsync(agreementCode, okVersion, ct);
            return Results.Ok(overrides);
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 4. POST /api/admin/position-overrides — Create new override
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

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var overrideId = await repo.CreateAsync(conn, tx, entity, ct);

            // Audit trail (Pattern B in-tx audit emission per S24 TASK-2401).
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

            return Results.Created($"/api/admin/position-overrides/{overrideId}", new { overrideId });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 5. PUT /api/admin/position-overrides/{overrideId:guid} — Update an active override
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

            // Get previous data for audit (read path, no tx needed).
            var previous = await repo.GetByIdAsync(overrideId, ct);
            if (previous is null)
                return Results.BadRequest(new { error = "Override not found or not ACTIVE" });

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

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var success = await repo.UpdateAsync(conn, tx, overrideId, updatedEntity, ct);
            if (!success)
            {
                await tx.RollbackAsync(ct);
                return Results.BadRequest(new { error = "Override not found or not ACTIVE" });
            }

            // Audit trail (Pattern B in-tx audit emission per S24 TASK-2401).
            await repo.AppendAuditAsync(
                conn, tx, overrideId, "UPDATED",
                JsonSerializer.Serialize(previous),
                JsonSerializer.Serialize(body),
                actorId, actorRole, ct);

            // Emit domain event via outbox in-tx.
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

            return Results.Ok(new { overrideId, updated = true });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 6. POST /api/admin/position-overrides/{overrideId:guid}/deactivate — Deactivate
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

            var existing = await repo.GetByIdAsync(overrideId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Override not found" });

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var success = await repo.DeactivateAsync(conn, tx, overrideId, ct);
            if (!success)
            {
                await tx.RollbackAsync(ct);
                return Results.BadRequest(new { error = "Override not ACTIVE" });
            }

            // Audit trail (Pattern B in-tx audit emission per S24 TASK-2401).
            await repo.AppendAuditAsync(
                conn, tx, overrideId, "DEACTIVATED", JsonSerializer.Serialize(existing), null,
                actorId, actorRole, ct);

            // Emit domain event via outbox in-tx.
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

            return Results.Ok(new { overrideId, deactivated = true });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 7. POST /api/admin/position-overrides/{overrideId:guid}/activate — Activate
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

            var existing = await repo.GetByIdAsync(overrideId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Override not found" });

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var success = await repo.ActivateAsync(conn, tx, overrideId, ct);
            if (!success)
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new { error = "Another active override exists for this combination" });
            }

            // Audit trail (Pattern B in-tx audit emission per S24 TASK-2401).
            await repo.AppendAuditAsync(
                conn, tx, overrideId, "ACTIVATED", null, JsonSerializer.Serialize(existing),
                actorId, actorRole, ct);

            // Emit domain event via outbox in-tx.
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

            return Results.Ok(new { overrideId, activated = true });
        }).RequireAuthorization("GlobalAdminOnly");

        return app;
    }

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
