using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S148 C-13 (ADR-026 D2). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UserOrganisationSeeded"/>. TENANT_TARGETED; target_org_id = the context's resolved
/// target organisation; target_resource_id = the user id.
/// </summary>
public sealed class UserOrganisationSeededAuditMapper : IAuditProjectionMapper<UserOrganisationSeeded>
{
    public AuditProjectionRowData Map(UserOrganisationSeeded @event, AuditProjectionContext context)
    {
        var details = new
        {
            userId = @event.UserId,
            assignmentId = @event.AssignmentId,
            orgId = @event.OrgId,
            effectiveFrom = @event.EffectiveFrom,
            rowVersion = @event.RowVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
