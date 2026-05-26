using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeProfileCreated"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = employee_id.
/// </summary>
public sealed class EmployeeProfileCreatedAuditMapper : IAuditProjectionMapper<EmployeeProfileCreated>
{
    public AuditProjectionRowData Map(EmployeeProfileCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            profileId = @event.ProfileId,
            employeeId = @event.EmployeeId,
            partTimeFraction = @event.PartTimeFraction,
            position = @event.Position,
            effectiveFrom = @event.EffectiveFrom,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
