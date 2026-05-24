using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class UserAgreementCodeChangedAuditMapper : IAuditProjectionMapper<UserAgreementCodeChanged>
{
    public AuditProjectionRowData Map(UserAgreementCodeChanged @event, AuditProjectionContext context)
    {
        var details = new
        {
            userId = @event.UserId,
            oldAgreementCode = @event.OldAgreementCode,
            newAgreementCode = @event.NewAgreementCode,
            effectiveFrom = @event.EffectiveFrom,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
