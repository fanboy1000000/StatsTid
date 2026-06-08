using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S68 / ADR-033 D5 (§21). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="VacationCarryoverExecuted"/> — the written-agreement §21 transfer of the
/// &gt;4-week tranche into the next entitlement year's <c>carryover_in</c> (balance-only,
/// no payroll line).
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — a settlement is a specific employee's personal data,
/// tenant-scoped; D12's "GLOBAL" governs the rule/config, not the per-employee audit row);
/// target_org_id = <c>context.ResolvedTargetOrgId</c> (the BackgroundService dispatch site
/// resolves employee → <c>users.primary_org_id</c> before invoking — the mapper is pure and
/// performs no lookups, per ADR-026 D2); target_resource_id = employee_id. Mirrors the S59
/// <see cref="EmployeeEntitlementEligibilitySet"/> cross-process precedent.
/// </para>
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 <c>e0d1dc3</c> lesson): the
/// catalog-parity / per-class visibility test constructs the event via
/// <c>Activator.CreateInstance</c>, which bypasses <c>required</c> init, so <c>EmployeeId</c>,
/// <c>EntitlementType</c> and the nullable <c>Snapshot</c> may be null here — every reference
/// member is null-coalesced so <see cref="Map"/> never NREs.
/// </para>
/// </summary>
public sealed class VacationCarryoverExecutedAuditMapper : IAuditProjectionMapper<VacationCarryoverExecuted>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(VacationCarryoverExecuted @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "VacationCarryoverExecuted",
            paragraph = "§21",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The §21 transfer bucket day-count.
            transferDays = @event.TransferDays,
            // Useful settle-time operands (null-tolerant — Snapshot is null on the catalog-parity instance).
            carryoverMax = snapshot?.CarryoverMax,
            transferAgreementDays = snapshot?.TransferAgreementDays,
            okVersion = snapshot?.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
