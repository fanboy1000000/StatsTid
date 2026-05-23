using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-4408. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OrganizationCreated"/>. TENANT_TARGETED; target_org_id = the
/// org being created (from event payload); details = full org payload.
/// </summary>
public sealed class OrganizationCreatedAuditMapper : IAuditProjectionMapper<OrganizationCreated>
{
    public AuditProjectionRowData Map(OrganizationCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            orgId = @event.OrgId,
            orgName = @event.OrgName,
            orgType = @event.OrgType,
            parentOrgId = @event.ParentOrgId,
            materializedPath = @event.MaterializedPath,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrgId,
            TargetResourceId: @event.OrgId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
