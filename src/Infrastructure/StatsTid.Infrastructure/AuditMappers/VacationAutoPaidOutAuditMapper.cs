using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S68 / ADR-033 D5 (§24). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="VacationAutoPaidOut"/> — the automatic post-period payout of the &gt;4-week
/// tranche NOT transferred by a §21 agreement (the law's default; emits a day-count payout
/// line, SLS owns the rate). §24 is PAYOUT, never carryover provenance (ADR-033 D6).
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — per-employee personal data, tenant-scoped); target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the BackgroundService dispatch site resolves
/// employee → <c>users.primary_org_id</c> before invoking — pure mapper, no I/O, per
/// ADR-026 D2); target_resource_id = employee_id. Mirrors the S59
/// <see cref="EmployeeEntitlementEligibilitySet"/> cross-process precedent.
/// </para>
///
/// <para>
/// NULL-TOLERANT of its <c>required</c> reference members (S66 <c>e0d1dc3</c> lesson): the
/// catalog-parity / per-class visibility test constructs the event via
/// <c>Activator.CreateInstance</c> (bypasses <c>required</c> init) — every reference member is
/// null-coalesced so <see cref="Map"/> never NREs.
/// </para>
/// </summary>
public sealed class VacationAutoPaidOutAuditMapper : IAuditProjectionMapper<VacationAutoPaidOut>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(VacationAutoPaidOut @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "VacationAutoPaidOut",
            paragraph = "§24",
            // Settlement identity (employee, type, year, sequence) — ADR-033 D5.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            sequence = @event.Sequence,
            // The §24 payout bucket day-count.
            payoutDays = @event.PayoutDays,
            // Useful settle-time operands (null-tolerant — Snapshot is null on the catalog-parity instance).
            carryoverMax = snapshot?.CarryoverMax,
            okVersion = snapshot?.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
