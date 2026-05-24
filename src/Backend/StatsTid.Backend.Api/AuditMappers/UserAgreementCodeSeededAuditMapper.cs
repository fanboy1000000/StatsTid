using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class UserAgreementCodeSeededAuditMapper : IAuditProjectionMapper<UserAgreementCodeSeeded>
{
    public AuditProjectionRowData Map(UserAgreementCodeSeeded @event, AuditProjectionContext context)
    {
        var details = new
        {
            userId = @event.UserId,
            agreementCode = @event.AgreementCode,
            effectiveFrom = @event.EffectiveFrom,
            rowVersion = @event.RowVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.UserId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
