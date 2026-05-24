using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class RoleConfigOverrideUpdatedAuditMapper : IAuditProjectionMapper<RoleConfigOverrideUpdated>
{
    public AuditProjectionRowData Map(RoleConfigOverrideUpdated @event, AuditProjectionContext context)
    {
        var details = new
        {
            overrideId = @event.OverrideId,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.OverrideId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
