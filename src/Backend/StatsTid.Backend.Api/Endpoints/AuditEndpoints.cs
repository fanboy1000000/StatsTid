using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class AuditEndpointsExtension
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/audit", async (
            AuditProjectionRepository auditRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            string? eventTypes,
            string? targetOrgId,
            string? actorId,
            string? from,
            string? to,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
        {
            var actor = context.GetActorContext();
            // S76 / TASK-7600 B1 (completeness-sweep find): HROrAbove read → LocalHR floor on
            // the accessible-org union. Pre-fix a mixed-role actor (HR@A + Leader@B) had B's
            // subtree unioned in via the non-admin Leader scope, leaking B's audit rows into
            // the result — the same picker-leak class the original 7600 closed on person-search.
            var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);

            if (accessibleOrgIds is { Count: 0 })
                return Results.Forbid();

            var filter = new AuditQueryFilter(
                EventTypes: string.IsNullOrWhiteSpace(eventTypes) ? null : eventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                TargetOrgId: targetOrgId,
                ActorId: actorId,
                OccurredAtFrom: DateTimeOffset.TryParse(from, out var parsedFrom) ? parsedFrom : null,
                OccurredAtTo: DateTimeOffset.TryParse(to, out var parsedTo) ? parsedTo : null);

            var (rows, totalCount) = await auditRepo.QueryByOrgScopeAsync(accessibleOrgIds, filter, page, pageSize, ct);

            return Results.Ok(new
            {
                rows = rows.Select(r => new
                {
                    projectionId = r.ProjectionId,
                    eventId = r.EventId,
                    eventType = r.EventType,
                    visibilityScope = r.VisibilityScope,
                    targetOrgId = r.TargetOrgId,
                    targetResourceId = r.TargetResourceId,
                    actorId = r.ActorId,
                    actorPrimaryOrgId = r.ActorPrimaryOrgId,
                    occurredAt = r.OccurredAt,
                    correlationId = r.CorrelationId,
                    details = r.DetailsJson,
                    projectedAt = r.ProjectedAt,
                }),
                totalCount,
                page,
                pageSize,
            });
        }).RequireAuthorization("HROrAbove");
    }
}
