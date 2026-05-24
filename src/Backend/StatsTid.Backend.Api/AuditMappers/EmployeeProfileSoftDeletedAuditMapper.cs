using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeProfileSoftDeleted"/>. TENANT_TARGETED; target_org_id =
/// <c>context.ResolvedTargetOrgId</c>; target_resource_id = employee_id.
/// </summary>
public sealed class EmployeeProfileSoftDeletedAuditMapper : IAuditProjectionMapper<EmployeeProfileSoftDeleted>
{
    public AuditProjectionRowData Map(EmployeeProfileSoftDeleted @event, AuditProjectionContext context)
    {
        var details = new
        {
            profileId = @event.ProfileId,
            employeeId = @event.EmployeeId,
            effectiveTo = @event.EffectiveTo,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
