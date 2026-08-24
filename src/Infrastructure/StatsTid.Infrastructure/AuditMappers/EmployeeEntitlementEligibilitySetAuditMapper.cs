using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S59 / ADR-029. <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EmployeeEntitlementEligibilitySet"/>. TENANT_TARGETED;
/// target_org_id = <c>context.ResolvedTargetOrgId</c> (the emitting endpoint
/// resolves employee → <c>users.primary_org_id</c> before invoking — the mapper
/// is pure and performs no lookups, per ADR-026 D2); target_resource_id =
/// employee_id.
/// </summary>
public sealed class EmployeeEntitlementEligibilitySetAuditMapper : IAuditProjectionMapper<EmployeeEntitlementEligibilitySet>
{
    public AuditProjectionRowData Map(EmployeeEntitlementEligibilitySet @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            eligible = @event.Eligible,
            effectiveFrom = @event.EffectiveFrom,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
