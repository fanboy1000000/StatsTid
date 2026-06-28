using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S103 / ADR-038 D3/D4 (Enhedsspor). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UnitLeaderRemoved"/>. TENANT_TARGETED; target_org_id = the unit's owning
/// Organisation (from event payload); details = the (unit, user) removal. This withdraws the
/// D4 path-2 approval authority, so it is audited. target_resource_id = the affected user.
/// </summary>
public sealed class UnitLeaderRemovedAuditMapper : IAuditProjectionMapper<UnitLeaderRemoved>
{
    public AuditProjectionRowData Map(UnitLeaderRemoved @event, AuditProjectionContext context)
    {
        var details = new
        {
            unitId = @event.UnitId,
            userId = @event.UserId,
            organisationId = @event.OrganisationId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
