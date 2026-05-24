using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class ConfigBugCorrectedAuditMapper : IAuditProjectionMapper<ConfigBugCorrected>
{
    public AuditProjectionRowData Map(ConfigBugCorrected @event, AuditProjectionContext context)
    {
        var details = new
        {
            configSurface = @event.ConfigSurface,
            configKey = @event.ConfigKey,
            fromValue = @event.FromValue,
            toValue = @event.ToValue,
            source = @event.Source,
            classifier = @event.Classifier,
            action = @event.Action,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.ConfigKey,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
