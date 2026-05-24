using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-44xx. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OvertimePreApprovalCreated"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (endpoint resolves from employee →
/// users.primary_org_id); target_resource_id = employee ID (no PreApprovalId
/// on this event); details = employeeId, periodStart, periodEnd, maxHours, status.
/// </summary>
public sealed class OvertimePreApprovalCreatedAuditMapper : IAuditProjectionMapper<OvertimePreApprovalCreated>
{
    public AuditProjectionRowData Map(OvertimePreApprovalCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            periodStart = @event.PeriodStart,
            periodEnd = @event.PeriodEnd,
            maxHours = @event.MaxHours,
            status = @event.Status,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
