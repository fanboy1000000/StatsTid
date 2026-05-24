using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class UserAgreementCodeSupersededAuditMapper : IAuditProjectionMapper<UserAgreementCodeSuperseded>
{
    public AuditProjectionRowData Map(UserAgreementCodeSuperseded @event, AuditProjectionContext context)
    {
        var details = new
        {
            predecessorAssignmentId = @event.PredecessorAssignmentId,
            newAssignmentId = @event.NewAssignmentId,
            userId = @event.UserId,
            predecessorEffectiveFrom = @event.PredecessorEffectiveFrom,
            predecessorEffectiveTo = @event.PredecessorEffectiveTo,
            newEffectiveFrom = @event.NewEffectiveFrom,
            oldAgreementCode = @event.OldAgreementCode,
            newAgreementCode = @event.NewAgreementCode,
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
