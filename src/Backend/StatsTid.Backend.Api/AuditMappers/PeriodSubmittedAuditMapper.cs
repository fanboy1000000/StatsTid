using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class PeriodSubmittedAuditMapper : IAuditProjectionMapper<PeriodSubmitted>
{
    public AuditProjectionRowData Map(PeriodSubmitted @event, AuditProjectionContext context)
    {
        var details = new
        {
            periodId = @event.PeriodId,
            employeeId = @event.EmployeeId,
            periodStart = @event.PeriodStart,
            periodEnd = @event.PeriodEnd,
            periodType = @event.PeriodType,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.PeriodId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
