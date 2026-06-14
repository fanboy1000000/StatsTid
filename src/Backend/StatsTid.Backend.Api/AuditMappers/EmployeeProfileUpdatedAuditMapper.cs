using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeProfileUpdated"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = employee_id.
/// </summary>
public sealed class EmployeeProfileUpdatedAuditMapper : IAuditProjectionMapper<EmployeeProfileUpdated>
{
    public AuditProjectionRowData Map(EmployeeProfileUpdated @event, AuditProjectionContext context)
    {
        var details = new
        {
            profileId = @event.ProfileId,
            employeeId = @event.EmployeeId,
            partTimeFraction = @event.PartTimeFraction,
            position = @event.Position,
            enhedLabel = @event.EnhedLabel,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
