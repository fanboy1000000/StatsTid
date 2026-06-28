using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UnitCreated"/>. TENANT_TARGETED; target_org_id = the unit's owning Organisation
/// (from event payload); details = the full unit payload. Mirrors
/// <see cref="OrganizationCreatedAuditMapper"/>.
/// </summary>
public sealed class UnitCreatedAuditMapper : IAuditProjectionMapper<UnitCreated>
{
    public AuditProjectionRowData Map(UnitCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            unitId = @event.UnitId,
            organisationId = @event.OrganisationId,
            parentUnitId = @event.ParentUnitId,
            type = @event.Type,
            name = @event.Name,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId,
            TargetResourceId: @event.UnitId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
