using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (§22) — DEFINE-ONLY in slice 1a (contract fixed now; automates in slice 4).
/// Feriehindring (sickness/barsel) automatic transfer of the ≤4-week tranche into the next
/// entitlement year's <c>carryover_in</c>. Like §21, this is BALANCE-ONLY — it emits NO payroll
/// line (ADR-033 D1/D7); <see cref="TransferDays"/> is the §22 provenance-keyed component of the
/// next-year carryover total, which §22 prioritizes over §21 if their sum would exceed the
/// combined statutory transfer ceiling (ADR-033 D6).
///
/// NOT auto-executed pre-modeling (ADR-033 D10) — the impediment signal is modeled in slice 4;
/// until then the PENDING_REVIEW manual path is the compliant default. The event is registered
/// now so slice-4 automation (and any manual resolution) emits a replayable event.
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (non-required nullable snapshot + defaulted day-count).
/// </summary>
public sealed class FeriehindringTransferred : DomainEventBase
{
    public override string EventType => "FeriehindringTransferred";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §22 feriehindring transfer bucket day-count (<c>transfer_days</c>; ADR-033 D5/D6).</summary>
    public decimal TransferDays { get; init; }
}
