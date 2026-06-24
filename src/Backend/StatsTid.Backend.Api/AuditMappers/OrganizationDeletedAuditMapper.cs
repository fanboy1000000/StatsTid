using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S98 / ADR-035. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OrganizationDeleted"/>. TENANT_TARGETED; target_org_id = the org being
/// soft-deleted (from event payload); details = the org identity at delete time. Mirrors
/// <see cref="OrganizationUpdatedAuditMapper"/>.
/// </summary>
public sealed class OrganizationDeletedAuditMapper : IAuditProjectionMapper<OrganizationDeleted>
{
    public AuditProjectionRowData Map(OrganizationDeleted @event, AuditProjectionContext context)
    {
        var details = new
        {
            orgId = @event.OrgId,
            orgName = @event.OrgName,
            orgType = @event.OrgType,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrgId,
            TargetResourceId: @event.OrgId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
