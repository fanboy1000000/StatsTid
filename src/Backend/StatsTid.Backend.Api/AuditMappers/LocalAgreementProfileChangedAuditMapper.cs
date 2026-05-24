using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="LocalAgreementProfileChanged"/>. TENANT_TARGETED; target_org_id =
/// <c>@event.OrgId</c> (from event payload, NOT context.ResolvedTargetOrgId);
/// target_resource_id = profile_id. ChangedFields intentionally excluded from
/// details — it contains a complex <c>FieldChange</c> type that may not
/// serialize cleanly with <see cref="AuditMapperJsonOptions.Default"/>.
/// </summary>
public sealed class LocalAgreementProfileChangedAuditMapper : IAuditProjectionMapper<LocalAgreementProfileChanged>
{
    public AuditProjectionRowData Map(LocalAgreementProfileChanged @event, AuditProjectionContext context)
    {
        var details = new
        {
            profileId = @event.ProfileId,
            orgId = @event.OrgId,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            effectiveFrom = @event.EffectiveFrom,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrgId,
            TargetResourceId: @event.ProfileId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
