using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UnitMoved"/>. TENANT_TARGETED; target_org_id = the unit's owning Organisation
/// (from event payload — units never move cross-Organisation, D8); details = the old+new parent
/// re-parent delta. Mirrors <see cref="OrganizationMovedAuditMapper"/>.
/// </summary>
public sealed class UnitMovedAuditMapper : IAuditProjectionMapper<UnitMoved>
{
    public AuditProjectionRowData Map(UnitMoved @event, AuditProjectionContext context)
    {
        var details = new
        {
            unitId = @event.UnitId,
            organisationId = @event.OrganisationId,
            oldParentUnitId = @event.OldParentUnitId,
            newParentUnitId = @event.NewParentUnitId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrganisationId,
            TargetResourceId: @event.UnitId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
