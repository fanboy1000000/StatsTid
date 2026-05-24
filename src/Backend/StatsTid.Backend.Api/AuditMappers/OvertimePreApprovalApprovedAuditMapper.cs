using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-44xx. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OvertimePreApprovalApproved"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (endpoint resolves from employee →
/// users.primary_org_id); target_resource_id = pre-approval ID;
/// details = preApprovalId, employeeId, approvedBy, reason.
/// </summary>
public sealed class OvertimePreApprovalApprovedAuditMapper : IAuditProjectionMapper<OvertimePreApprovalApproved>
{
    public AuditProjectionRowData Map(OvertimePreApprovalApproved @event, AuditProjectionContext context)
    {
        var details = new
        {
            preApprovalId = @event.PreApprovalId,
            employeeId = @event.EmployeeId,
            approvedBy = @event.ApprovedBy,
            reason = @event.Reason,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.PreApprovalId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
