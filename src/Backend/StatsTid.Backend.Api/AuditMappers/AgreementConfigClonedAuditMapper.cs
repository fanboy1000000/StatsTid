using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class AgreementConfigClonedAuditMapper : IAuditProjectionMapper<AgreementConfigCloned>
{
    public AuditProjectionRowData Map(AgreementConfigCloned @event, AuditProjectionContext context)
    {
        var details = new
        {
            configId = @event.ConfigId,
            sourceConfigId = @event.SourceConfigId,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.ConfigId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
