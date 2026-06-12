using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

/// <summary>
/// S71 / ADR-033 D4/D5 (SPRINT-71 R10). <see cref="IAuditProjectionMapper{TEvent}"/> for
/// <see cref="SettlementReversed"/> — the operator-authorized reversal fact (FIRST mapper: the
/// event was define-only from S68 with no mapper/catalog row; S71 extends the payload before
/// first emission and the slice-3b reversal service begins emitting it). Projects the reversed
/// row's identity at its settlement-ROW sequence (R2), the reversal kind (BARE vs SUPERSEDED +
/// the successor's sequence), the reversed row's trigger and per-bucket recorded day-counts
/// (positive quantities — direction is the export line's <c>line_kind</c>, R8). NO staged-line
/// references (R10 — compensation targets derive consumer-side per R9).
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
public sealed class SettlementReversedAuditMapper : IAuditProjectionMapper<SettlementReversed>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditProjectionRowData Map(SettlementReversed @event, AuditProjectionContext context)
    {
        var snapshot = @event.Snapshot;
        var details = new
        {
            kind = "SettlementReversed",
            // Original settlement-ROW identity (employee, type, year, settlement sequence) — ADR-033 D5 / SPRINT-71 R2.
            employeeId = @event.EmployeeId,
            entitlementType = @event.EntitlementType,
            entitlementYear = @event.EntitlementYear,
            settlementSequence = @event.SettlementSequence,
            // BARE vs SUPERSEDED (+ the superseding row's sequence when present) — SPRINT-71 R10.
            reversalKind = @event.ReversalKind,
            successorSequence = @event.SuccessorSequence,
            // The reversed row's trigger (YEAR_END / TERMINATION).
            trigger = @event.Trigger,
            // Per-bucket recorded day-counts of the REVERSED row (positive; direction = line_kind, R8).
            transferDays = @event.TransferDays,
            payoutDays = @event.PayoutDays,
            forfeitDays = @event.ForfeitDays,
            crystallizedDays = @event.CrystallizedDays,
            claimDispositionDays = @event.ClaimDispositionDays,
            // Useful settle-time operand (null-tolerant — Snapshot is null on the catalog-parity instance).
            okVersion = snapshot?.OkVersion,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId ?? string.Empty,
            DetailsJson: JsonSerializer.Serialize(details, JsonOptions));
    }
}
