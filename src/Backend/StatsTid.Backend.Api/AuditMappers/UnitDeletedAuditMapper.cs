using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UnitDeleted"/>. TENANT_TARGETED; target_org_id = the unit's owning Organisation
/// (from event payload); details = the unit identity at delete time. Mirrors
/// <see cref="OrganizationDeletedAuditMapper"/>.
/// </summary>
public sealed class UnitDeletedAuditMapper : IAuditProjectionMapper<UnitDeleted>
{
    public AuditProjectionRowData Map(UnitDeleted @event, AuditProjectionContext context)
    {
        var details = new
        {
            unitId = @event.UnitId,
            organisationId = @event.OrganisationId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId,
            TargetResourceId: @event.UnitId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
