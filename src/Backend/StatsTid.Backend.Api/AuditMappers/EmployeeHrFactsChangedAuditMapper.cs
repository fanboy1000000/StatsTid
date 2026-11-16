using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S148 C-11 (ADR-026 D2). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeHrFactsChanged"/>. TENANT_TARGETED; target_org_id = the context's resolved
/// target organisation; target_resource_id = the employee id.
/// </summary>
public sealed class EmployeeHrFactsChangedAuditMapper : IAuditProjectionMapper<EmployeeHrFactsChanged>
{
    public AuditProjectionRowData Map(EmployeeHrFactsChanged @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            factsId = @event.FactsId,
            action = @event.Action,
            overtimeRegime = @event.OvertimeRegime,
            onFlexScheme = @event.OnFlexScheme,
            effectiveFrom = @event.EffectiveFrom,
            effectiveTo = @event.EffectiveTo,
            precedingFactsId = @event.PrecedingFactsId,
            versionBefore = @event.VersionBefore,
            versionAfter = @event.VersionAfter,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
