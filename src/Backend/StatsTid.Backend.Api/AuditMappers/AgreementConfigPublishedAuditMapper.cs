using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class AgreementConfigPublishedAuditMapper : IAuditProjectionMapper<AgreementConfigPublished>
{
    public AuditProjectionRowData Map(AgreementConfigPublished @event, AuditProjectionContext context)
    {
        var details = new
        {
            configId = @event.ConfigId,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            archivedConfigId = @event.ArchivedConfigId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.ConfigId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
