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
            // S73 / TASK-7301 (M3) — project the D-A full-day-only legal flag so the config
            // supersession is visible in the operational audit projection. Additive +
            // null-tolerant (the payload field is bool?; pre-S73 events carry null).
            fullDayOnly = @event.FullDayOnly,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.ConfigId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
