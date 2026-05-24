using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeProfileSuperseded"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = employee_id.
/// </summary>
public sealed class EmployeeProfileSupersededAuditMapper : IAuditProjectionMapper<EmployeeProfileSuperseded>
{
    public AuditProjectionRowData Map(EmployeeProfileSuperseded @event, AuditProjectionContext context)
    {
        var details = new
        {
            predecessorProfileId = @event.PredecessorProfileId,
            newProfileId = @event.NewProfileId,
            employeeId = @event.EmployeeId,
            predecessorEffectiveFrom = @event.PredecessorEffectiveFrom,
            newEffectiveFrom = @event.NewEffectiveFrom,
            weeklyNormHours = @event.WeeklyNormHours,
            partTimeFraction = @event.PartTimeFraction,
            position = @event.Position,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
