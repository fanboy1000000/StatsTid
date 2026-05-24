using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="LocalConfigurationChanged"/>. TENANT_TARGETED; target_org_id =
/// <c>@event.OrgId</c> (from event payload); target_resource_id = config_id.
/// Mapper-only — no emit site; retained for backward compatibility.
/// </summary>
public sealed class LocalConfigurationChangedAuditMapper : IAuditProjectionMapper<LocalConfigurationChanged>
{
    public AuditProjectionRowData Map(LocalConfigurationChanged @event, AuditProjectionContext context)
    {
        var details = new
        {
            configId = @event.ConfigId,
            orgId = @event.OrgId,
            configArea = @event.ConfigArea,
            configKey = @event.ConfigKey,
            configValue = @event.ConfigValue,
            previousValue = @event.PreviousValue,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: @event.OrgId,
            TargetResourceId: @event.ConfigId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
