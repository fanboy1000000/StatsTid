using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class PeriodEmployeeApprovedAuditMapper : IAuditProjectionMapper<PeriodEmployeeApproved>
{
    public AuditProjectionRowData Map(PeriodEmployeeApproved @event, AuditProjectionContext context)
    {
        var details = new
        {
            periodId = @event.PeriodId,
            employeeId = @event.EmployeeId,
            periodStart = @event.PeriodStart,
            periodEnd = @event.PeriodEnd,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.PeriodId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
