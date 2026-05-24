using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class EntitlementConfigSeededAuditMapper : IAuditProjectionMapper<EntitlementConfigSeeded>
{
    public AuditProjectionRowData Map(EntitlementConfigSeeded @event, AuditProjectionContext context)
    {
        var details = new
        {
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            configCount = @event.ConfigCount,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: $"{@event.AgreementCode}:{@event.OkVersion}",
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
