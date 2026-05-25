using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S48 / TASK-4807. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="ReportingLineAssigned"/>. TENANT_TARGETED; target_org_id =
/// tree_root_org_id from the event; details = key reporting-line fields.
/// </summary>
public sealed class ReportingLineAssignedAuditMapper : IAuditProjectionMapper<ReportingLineAssigned>
{
    public AuditProjectionRowData Map(ReportingLineAssigned @event, AuditProjectionContext context)
    {
        var details = new
        {
            reportingLineId = @event.ReportingLineId,
            employeeId = @event.EmployeeId,
            managerId = @event.ManagerId,
            treeRootOrgId = @event.TreeRootOrgId,
            relationship = @event.Relationship,
            effectiveFrom = @event.EffectiveFrom.ToString("yyyy-MM-dd"),
            source = @event.Source,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.TreeRootOrgId,
            TargetResourceId: @event.ReportingLineId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
