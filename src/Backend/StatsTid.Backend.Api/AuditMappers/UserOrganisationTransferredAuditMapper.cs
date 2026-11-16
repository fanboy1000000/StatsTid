using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S148 C-13 (ADR-026 D2). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="UserOrganisationTransferred"/>. TENANT_TARGETED; target_org_id = the context's
/// resolved target organisation; target_resource_id = the user id.
/// </summary>
public sealed class UserOrganisationTransferredAuditMapper : IAuditProjectionMapper<UserOrganisationTransferred>
{
    public AuditProjectionRowData Map(UserOrganisationTransferred @event, AuditProjectionContext context)
    {
        var details = new
        {
            userId = @event.UserId,
            assignmentId = @event.AssignmentId,
            predecessorAssignmentId = @event.PredecessorAssignmentId,
            oldOrgId = @event.OldOrgId,
            newOrgId = @event.NewOrgId,
            effectiveFrom = @event.EffectiveFrom,
            inPlace = @event.InPlace,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
