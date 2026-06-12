using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S71 / ADR-033 slice 3b (SPRINT-71 R5, owner decision D-C). <see cref="IAuditProjectionMapper{TEvent}"/>
/// for <see cref="TerminationClaimWaived"/> — the waive-in-full resolution of a §7-shaped
/// over-taken termination claim. NO Payroll line ever stages from this event (R9 — waiver has no
/// consumer); the audit row IS the operational record of the operator's waiver decision.
/// <c>WaivedDays</c> mirrors <c>vacation_settlements.claim_disposition_days</c> — recorded
/// distinctly so a waived claim never reads as §34 forfeiture (R5).
///
/// <para>
/// TENANT_TARGETED (ADR-025 D3 — per-employee personal data, tenant-scoped); target_org_id =
/// <c>context.ResolvedTargetOrgId</c> (the dispatch site resolves employee →
/// <c>users.primary_org_id</c> before invoking — pure mapper, no I/O, per ADR-026 D2);
/// target_resource_id = employee_id. Mirrors the S59
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
public sealed class TerminationClaimWaivedAuditMapper : IAuditProjectionMapper<TerminationClaimWaived>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(TerminationClaimWaived @event, AuditProjectionContext context)
    {
        var details = new
        {
            kind = "TerminationClaimWaived",
            paragraph = "§7",
            disposition = "WAIVED",
            // Settlement-ROW identity (employee, type, year, settlement sequence) — ADR-033 D5 / SPRINT-71 R2.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            settlementSequence = @event.SettlementSequence,
            // The |pre-clamp| §7-shaped claim day-count waived in full (claim_disposition_days, R5).
            waivedDays = @event.WaivedDays,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
