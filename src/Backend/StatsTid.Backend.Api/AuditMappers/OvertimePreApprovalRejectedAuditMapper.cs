using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-44xx. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OvertimePreApprovalRejected"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (endpoint resolves from employee →
/// users.primary_org_id); target_resource_id = pre-approval ID;
/// details = preApprovalId, employeeId, rejectedBy, reason.
/// </summary>
public sealed class OvertimePreApprovalRejectedAuditMapper : IAuditProjectionMapper<OvertimePreApprovalRejected>
{
    public AuditProjectionRowData Map(OvertimePreApprovalRejected @event, AuditProjectionContext context)
    {
        var details = new
        {
            preApprovalId = @event.PreApprovalId,
            employeeId = @event.EmployeeId,
            rejectedBy = @event.RejectedBy,
            reason = @event.Reason,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.PreApprovalId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
