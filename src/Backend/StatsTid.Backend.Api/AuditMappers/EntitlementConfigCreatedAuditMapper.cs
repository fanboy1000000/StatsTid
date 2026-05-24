using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class EntitlementConfigCreatedAuditMapper : IAuditProjectionMapper<EntitlementConfigCreated>
{
    public AuditProjectionRowData Map(EntitlementConfigCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            configId = @event.ConfigId,
            entitlementType = @event.EntitlementType,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            effectiveFrom = @event.EffectiveFrom,
            annualQuota = @event.AnnualQuota,
            accrualModel = @event.AccrualModel,
            resetMonth = @event.ResetMonth,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.ConfigId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
