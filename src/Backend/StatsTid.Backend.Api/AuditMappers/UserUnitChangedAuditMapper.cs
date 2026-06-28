using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S103 / ADR-038 D2 (Enhedsspor). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UserUnitChanged"/> (the single-unit membership move that replaces the S97
/// multi-tag <see cref="UserEnhederChanged"/>). TENANT_TARGETED; target_org_id = the DERIVED
/// Organisation home AFTER the change (= the user's recomputed <c>primary_org_id</c>, from the
/// event payload); details = the old/new unit delta. target_resource_id = the affected user.
/// </summary>
public sealed class UserUnitChangedAuditMapper : IAuditProjectionMapper<UserUnitChanged>
{
    public AuditProjectionRowData Map(UserUnitChanged @event, AuditProjectionContext context)
    {
        var details = new
        {
            userId = @event.UserId,
            oldUnitId = @event.OldUnitId,
            newUnitId = @event.NewUnitId,
            organisationId = @event.OrganisationId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
