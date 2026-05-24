using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="MerarbejdeDiscretionary"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = employee_id.
/// Mapper-only — no emit site.
/// </summary>
public sealed class MerarbejdeDiscretionaryAuditMapper : IAuditProjectionMapper<MerarbejdeDiscretionary>
{
    public AuditProjectionRowData Map(MerarbejdeDiscretionary @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            date = @event.Date,
            merarbejdeHours = @event.MerarbejdeHours,
            employmentCategory = @event.EmploymentCategory,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
