using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class RoleConfigOverrideCreatedAuditMapper : IAuditProjectionMapper<RoleConfigOverrideCreated>
{
    public AuditProjectionRowData Map(RoleConfigOverrideCreated @event, AuditProjectionContext context)
    {
        var details = new
        {
            overrideId = @event.OverrideId,
            employmentCategory = @event.EmploymentCategory,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            effectiveFrom = @event.EffectiveFrom,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: $"{@event.EmploymentCategory}:{@event.AgreementCode}:{@event.OkVersion}",
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
