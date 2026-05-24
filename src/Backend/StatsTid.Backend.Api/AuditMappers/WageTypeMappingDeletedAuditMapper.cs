using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class WageTypeMappingDeletedAuditMapper : IAuditProjectionMapper<WageTypeMappingDeleted>
{
    public AuditProjectionRowData Map(WageTypeMappingDeleted @event, AuditProjectionContext context)
    {
        var details = new
        {
            timeType = @event.TimeType,
            okVersion = @event.OkVersion,
            agreementCode = @event.AgreementCode,
            position = @event.Position,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: $"{@event.TimeType}:{@event.AgreementCode}:{@event.OkVersion}:{@event.Position}",
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
