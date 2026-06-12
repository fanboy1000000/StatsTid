using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S71 / ADR-033 slice 3b (SPRINT-71 R6). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="TerminationPayoutRequested"/> — the §26 anmodning fact (Ferieloven §26 stk.1 pays
/// <i>efter anmodning</i>) that drives the staged <c>SLS_TBD_S26</c> day-count line. Carries the
/// settlement-ROW sequence (R2), the request evidence, the snapshot-copied
/// <c>CrystallizedDays</c> the line will carry, and the R11 <c>SettlementBoundaryDate</c> lønart
/// anchor — day-counts only, SLS owns all kroner (ADR-033 D1).
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
public sealed class TerminationPayoutRequestedAuditMapper : IAuditProjectionMapper<TerminationPayoutRequested>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(TerminationPayoutRequested @event, AuditProjectionContext context)
    {
        var details = new
        {
            kind = "TerminationPayoutRequested",
            paragraph = "§26",
            // Settlement-ROW identity (employee, type, year, settlement sequence) — ADR-033 D5 / SPRINT-71 R2.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            settlementSequence = @event.SettlementSequence,
            // Request evidence (recordedBy = the base actor, projected on the audit row itself).
            requestDate = @event.RequestDate,
            evidenceNote = @event.EvidenceNote,
            // The snapshot-copied quantity the staged line carries + the R11 lønart asOf anchor.
            crystallizedDays = @event.CrystallizedDays,
            settlementBoundaryDate = @event.SettlementBoundaryDate,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
