using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
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
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/wage-type-mappings", async (
            CreateWageTypeMappingRequest body,
            WageTypeMappingRepository repo,
            IEventStore eventStore,
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

            var success = await repo.CreateAsync(mapping, ct);
            if (!success)
                return Results.Conflict(new { error = "A mapping with this key already exists" });

            // Audit trail
            await repo.AppendAuditAsync(
                body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "",
                "CREATED", null, JsonSerializer.Serialize(body),
                actorId, actorRole, ct);

            // Emit domain event
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
            await eventStore.AppendAsync($"wage-type-mapping-{body.AgreementCode}-{body.OkVersion}-{body.TimeType}", @event, ct);

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
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/wage-type-mappings", async (
            UpdateWageTypeMappingRequest body,
            WageTypeMappingRepository repo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // Get previous data for audit
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

            var success = await repo.UpdateAsync(mapping, ct);
            if (!success)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            // Audit trail
            await repo.AppendAuditAsync(
                body.TimeType, body.OkVersion, body.AgreementCode, body.Position ?? "",
                "UPDATED",
                JsonSerializer.Serialize(previous),
                JsonSerializer.Serialize(body),
                actorId, actorRole, ct);

            // Emit domain event
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
            await eventStore.AppendAsync($"wage-type-mapping-{body.AgreementCode}-{body.OkVersion}-{body.TimeType}", @event, ct);

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
        // ═══════════════════════════════════════════
        app.MapDelete("/api/admin/wage-type-mappings", async (
            string timeType,
            string okVersion,
            string agreementCode,
            string? position,
            WageTypeMappingRepository repo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var pos = position ?? "";

            // Get data for audit before deletion
            var existing = await repo.GetByKeyAsync(timeType, okVersion, agreementCode, pos, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            var success = await repo.DeleteAsync(timeType, okVersion, agreementCode, pos, ct);
            if (!success)
                return Results.NotFound(new { error = "Wage type mapping not found" });

            // Audit trail
            await repo.AppendAuditAsync(
                timeType, okVersion, agreementCode, pos,
                "DELETED",
                JsonSerializer.Serialize(existing),
                null,
                actorId, actorRole, ct);

            // Emit domain event
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
            await eventStore.AppendAsync($"wage-type-mapping-{agreementCode}-{okVersion}-{timeType}", @event, ct);

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
