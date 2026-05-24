using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class PositionOverrideDeactivatedAuditMapper : IAuditProjectionMapper<PositionOverrideDeactivated>
{
    public AuditProjectionRowData Map(PositionOverrideDeactivated @event, AuditProjectionContext context)
    {
        var details = new
        {
            overrideId = @event.OverrideId,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            positionCode = @event.PositionCode,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.OverrideId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
