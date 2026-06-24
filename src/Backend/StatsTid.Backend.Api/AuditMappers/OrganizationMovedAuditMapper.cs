using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S98 / ADR-035. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OrganizationMoved"/>. TENANT_TARGETED; target_org_id = the org being moved
/// (from event payload); details = the old+new parent + old+new materialized_path (the full
/// re-parent delta). Mirrors <see cref="OrganizationUpdatedAuditMapper"/>.
/// </summary>
public sealed class OrganizationMovedAuditMapper : IAuditProjectionMapper<OrganizationMoved>
{
    public AuditProjectionRowData Map(OrganizationMoved @event, AuditProjectionContext context)
    {
        var details = new
        {
            orgId = @event.OrgId,
            oldParentOrgId = @event.OldParentOrgId,
            newParentOrgId = @event.NewParentOrgId,
            oldMaterializedPath = @event.OldMaterializedPath,
            newMaterializedPath = @event.NewMaterializedPath,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrgId,
            TargetResourceId: @event.OrgId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
