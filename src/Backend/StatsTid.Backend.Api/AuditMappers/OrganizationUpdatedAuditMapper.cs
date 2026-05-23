using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-4409. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="OrganizationUpdated"/>. TENANT_TARGETED; target_org_id = the
/// org being updated (from event payload); details = changed fields.
/// </summary>
public sealed class OrganizationUpdatedAuditMapper : IAuditProjectionMapper<OrganizationUpdated>
{
    public AuditProjectionRowData Map(OrganizationUpdated @event, AuditProjectionContext context)
    {
        var details = new
        {
            orgId = @event.OrgId,
            orgName = @event.OrgName,
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
