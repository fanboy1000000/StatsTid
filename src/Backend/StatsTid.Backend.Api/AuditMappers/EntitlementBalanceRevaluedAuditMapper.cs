using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S66 / TASK-6604 (ADR-032 D4). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="EntitlementBalanceRevalued"/> — the profile-change revaluation balance event
/// emitted from the EmployeeProfile PUT transaction (one per affected (entitlementType,
/// entitlementYear) group). TENANT_TARGETED; target_org_id = <c>context.ResolvedTargetOrgId</c>
/// (the employee's primary org, resolved at the endpoint before the mapper runs);
/// target_resource_id = employee_id — mirroring the EmployeeProfile family conventions for the
/// same PUT surface. The details capture the revaluation outcome: entitlement type/year, the
/// aggregate used-delta, the affected-absence count, the per-absence replacement set, and the
/// triggering profile-change event id (audit lineage back to the profile mutation).
/// </summary>
public sealed class EntitlementBalanceRevaluedAuditMapper : IAuditProjectionMapper<EntitlementBalanceRevalued>
{
    public AuditProjectionRowData Map(EntitlementBalanceRevalued @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            usedDelta = @event.UsedDelta,
            affectedAbsenceCount = @event.Replacements.Count,
            replacements = @event.Replacements,
            triggeringProfileEventId = @event.TriggeringProfileEventId,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
