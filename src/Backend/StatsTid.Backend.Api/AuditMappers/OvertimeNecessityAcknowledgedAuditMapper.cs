using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OvertimeNecessityAcknowledged"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = pre_approval_id.
/// Mapper-only — no emit site. The full <c>AcknowledgedForEntries</c> list is
/// NOT serialized (unbounded payload risk) — only the count is included.
/// </summary>
public sealed class OvertimeNecessityAcknowledgedAuditMapper : IAuditProjectionMapper<OvertimeNecessityAcknowledged>
{
    public AuditProjectionRowData Map(OvertimeNecessityAcknowledged @event, AuditProjectionContext context)
    {
        var details = new
        {
            preApprovalId = @event.PreApprovalId,
            necessityReason = @event.NecessityReason,
            acknowledgedEntryCount = @event.AcknowledgedForEntries?.Count ?? 0,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.PreApprovalId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
