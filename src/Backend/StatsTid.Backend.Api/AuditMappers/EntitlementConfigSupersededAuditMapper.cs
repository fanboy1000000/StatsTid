using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class EntitlementConfigSupersededAuditMapper : IAuditProjectionMapper<EntitlementConfigSuperseded>
{
    public AuditProjectionRowData Map(EntitlementConfigSuperseded @event, AuditProjectionContext context)
    {
        var details = new
        {
            configId = @event.ConfigId,
            entitlementType = @event.EntitlementType,
            supersededByConfigId = @event.SupersededByConfigId,
            effectiveFrom = @event.EffectiveFrom,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.ConfigId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
