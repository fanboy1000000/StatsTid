using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

public sealed class RoleConfigOverrideSupersededAuditMapper : IAuditProjectionMapper<RoleConfigOverrideSuperseded>
{
    public AuditProjectionRowData Map(RoleConfigOverrideSuperseded @event, AuditProjectionContext context)
    {
        var details = new
        {
            predecessorOverrideId = @event.PredecessorOverrideId,
            successorOverrideId = @event.SuccessorOverrideId,
            effectiveFrom = @event.EffectiveFrom,
            employmentCategory = @event.EmploymentCategory,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
            TargetOrgId: null,
            TargetResourceId: @event.PredecessorOverrideId.ToString(),
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
