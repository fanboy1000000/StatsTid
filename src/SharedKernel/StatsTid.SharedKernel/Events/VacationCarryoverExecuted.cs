using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (§21). Emitted from the atomic period-close settlement (ADR-033 D3/D6)
/// when the written-agreement §21 transfer moves the &gt;4-week tranche into the next
/// entitlement year's <c>carryover_in</c>. Transfer is BALANCE-ONLY — it emits NO payroll
/// line (ADR-033 D1/D7); the carried <see cref="TransferDays"/> is the provenance-keyed §21
/// component of the next-year carryover total (ADR-033 D6).
///
/// Stream: consolidated <c>employee-{EmployeeId}</c> (ADR-018 D6). Replay reads the recorded
/// <see cref="Snapshot"/> verbatim — deterministic, no re-derivation (ADR-033 D3).
///
/// ROUND-TRIPPABILITY: <see cref="Snapshot"/> is non-required + nullable and the day-count is
/// a non-required defaulted value type, so the EventSerializerCoverageTests uninitialized
/// instance round-trips (S66 e0d1dc3 lesson; ADR-033 D5).
/// </summary>
public sealed class VacationCarryoverExecuted : DomainEventBase
{
    public override string EventType => "VacationCarryoverExecuted";

    // Settlement identity (employee_id, entitlement_type, entitlement_year, sequence) — ADR-033 D5.
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §21 transfer bucket day-count (<c>transfer_days</c>; ADR-033 D5 shared vocabulary).</summary>
    public decimal TransferDays { get; init; }
}
