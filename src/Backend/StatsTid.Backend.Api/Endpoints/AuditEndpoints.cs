using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
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

            // S115 / TASK-11501 — named records (BYTE-IDENTICAL wire JSON). `details` stays the
            // RAW JSONB text as a plain string passthrough (NO reshaping — the caller
            // deserializes per the audit-projection catalog).
            return Results.Ok(new AuditLogResponse(
                Rows: rows.Select(r => new AuditLogRow(
                    ProjectionId: r.ProjectionId,
                    EventId: r.EventId,
                    EventType: r.EventType,
                    VisibilityScope: r.VisibilityScope,
                    TargetOrgId: r.TargetOrgId,
                    TargetResourceId: r.TargetResourceId,
                    ActorId: r.ActorId,
                    ActorPrimaryOrgId: r.ActorPrimaryOrgId,
                    OccurredAt: r.OccurredAt,
                    CorrelationId: r.CorrelationId,
                    Details: r.DetailsJson,
                    ProjectedAt: r.ProjectedAt)).ToList(),
                TotalCount: totalCount,
                Page: page,
                PageSize: pageSize));
        }).RequireAuthorization("HROrAbove")
        .Produces<AuditLogResponse>(StatusCodes.Status200OK); // S115 / TASK-11501
    }
}
