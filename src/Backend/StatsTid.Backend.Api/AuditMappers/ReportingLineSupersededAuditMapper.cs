using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S48 / TASK-4807. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="ReportingLineSuperseded"/>. TENANT_TARGETED; target_org_id =
/// tree_root_org_id from the event; details = supersession fields including
/// previous and new manager.
/// </summary>
public sealed class ReportingLineSupersededAuditMapper : IAuditProjectionMapper<ReportingLineSuperseded>
{
    public AuditProjectionRowData Map(ReportingLineSuperseded @event, AuditProjectionContext context)
    {
        var details = new
        {
            reportingLineId = @event.ReportingLineId,
            employeeId = @event.EmployeeId,
            previousManagerId = @event.PreviousManagerId,
            newManagerId = @event.NewManagerId,
            treeRootOrgId = @event.TreeRootOrgId,
            effectiveFrom = @event.EffectiveFrom.ToString("yyyy-MM-dd"),
            effectiveTo = @event.EffectiveTo.ToString("yyyy-MM-dd"),
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.TreeRootOrgId,
            TargetResourceId: @event.ReportingLineId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
