using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-4412b. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="RoleAssignmentRevoked"/>. TENANT_TARGETED. target_org_id =
/// the affected user's primary_org_id (catalog L59), which is NOT in the
/// event payload. Endpoint resolves and passes via
/// <see cref="AuditProjectionContext.ResolvedTargetOrgId"/> per ADR-026
/// D2 L134.
/// </summary>
public sealed class RoleAssignmentRevokedAuditMapper : IAuditProjectionMapper<RoleAssignmentRevoked>
{
    public AuditProjectionRowData Map(RoleAssignmentRevoked @event, AuditProjectionContext context)
    {
        var targetOrgId = context.ResolvedTargetOrgId
            ?? throw new InvalidOperationException(
                $"RoleAssignmentRevoked mapper requires context.ResolvedTargetOrgId (user's primary_org_id); endpoint must resolve before invoking. UserId={@event.UserId} AssignmentId={@event.AssignmentId}");

        var details = new
        {
            assignmentId = @event.AssignmentId,
            userId = @event.UserId,
            roleId = @event.RoleId,
            reason = @event.Reason,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: targetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
