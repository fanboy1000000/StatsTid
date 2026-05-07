using System.Text.Json;
using StatsTid.Auth;
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
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/wage-type-mappings", async (
            WageTypeMappingRepository repo,
            CancellationToken ct) =>
        {
            var mappings = await repo.GetAllAsync(ct);
            return Results.Ok(mappings);
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 2. GET /api/admin/wage-type-mappings/agreement/{agreementCode}/{okVersion} — Get by agreement
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/wage-type-mappings/agreement/{agreementCode}/{okVersion}", async (
            string agreementCode,
            string okVersion,
            WageTypeMappingRepository repo,
            CancellationToken ct) =>
        {
            var mappings = await repo.GetByAgreementAsync(agreementCode, okVersion, ct);
            return Results.Ok(mappings);
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 3. POST /api/admin/wage-type-mappings — Create mapping
        //
        // ADR-018 D3 atomic write: the mapping INSERT, audit-row INSERT, and outbox enqueue
        // commit in a single PostgreSQL transaction via the repo's (conn, tx) overloads
        // (TASK-2401). A separate per-service OutboxPublisher drains outbox_events to the
        // canonical event store at-least-once (ADR-018 D4) — this supersedes the prior
        // post-commit eventStore.AppendAsync shape.
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

            // Audit trail (Pattern B — repo owns audit emission via (conn, tx) overload)
            await repo.AppendAuditAsync(
                conn, tx,
                body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "",
                "CREATED", null, JsonSerializer.Serialize(body),
                actorId, actorRole, ct);

            // Enqueue domain event INSIDE the same transaction (ADR-018 D3) — replaces the
            // prior post-commit eventStore.AppendAsync shape.
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

            return Results.Created($"/api/admin/wage-type-mappings/agreement/{body.AgreementCode}/{body.OkVersion}", new
            {
                timeType = body.TimeType,
                wageType = body.WageType,
                okVersion = body.OkVersion,
                agreementCode = body.AgreementCode,
                position = body.Position ?? "",
            });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 4. PUT /api/admin/wage-type-mappings — Update mapping
        //
        // ADR-018 D3 atomic write — see POST handler above for rationale.
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

            // Get previous data for audit (read outside the write tx — pre-existing shape).
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

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var success = await repo.UpdateAsync(conn, tx, mapping, ct);
            if (!success)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            // Audit trail (Pattern B — (conn, tx) overload).
            await repo.AppendAuditAsync(
                conn, tx,
                body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "",
                "UPDATED",
                JsonSerializer.Serialize(previous),
                JsonSerializer.Serialize(body),
                actorId, actorRole, ct);

            // Enqueue domain event INSIDE the same transaction (ADR-018 D3).
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

            return Results.Ok(new
            {
                timeType = body.TimeType,
                wageType = body.WageType,
                okVersion = body.OkVersion,
                agreementCode = body.AgreementCode,
                position = body.Position ?? "",
                updated = true,
            });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 5. DELETE /api/admin/wage-type-mappings — Delete mapping
        //
        // ADR-018 D3 atomic write — see POST handler above for rationale.
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

            // Get data for audit before deletion (read outside the write tx — pre-existing shape).
            var existing = await repo.GetByKeyAsync(timeType, okVersion, agreementCode, pos, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var success = await repo.DeleteAsync(conn, tx, timeType, okVersion, agreementCode, pos, ct);
            if (!success)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            // Audit trail (Pattern B — (conn, tx) overload).
            await repo.AppendAuditAsync(
                conn, tx,
                timeType, okVersion, agreementCode, pos,
                "DELETED",
                JsonSerializer.Serialize(existing),
                null,
                actorId, actorRole, ct);

            // Enqueue domain event INSIDE the same transaction (ADR-018 D3).
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

            return Results.Ok(new { timeType, okVersion, agreementCode, position = pos, deleted = true });
        }).RequireAuthorization("GlobalAdminOnly");

        return app;
    }

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
