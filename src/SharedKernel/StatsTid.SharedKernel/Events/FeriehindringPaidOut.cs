using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (§25) — DEFINE-ONLY in slice 1a (contract fixed now; automates in slice 4).
/// Payout for a PERSISTENT feriehindring (§25): when the impediment continues such that the days
/// cannot be transferred/taken, they are paid out. A payout emits a <b>day-count payout line</b>
/// (ADR-033 D1/D7) — SLS owns the rate; StatsTid carries only <see cref="PayoutDays"/>.
///
/// NOT auto-executed pre-modeling (ADR-033 D10); the impediment signal is modeled in slice 4.
/// Registered now so the contract is fixed and any emission path is replayable.
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (non-required nullable snapshot + defaulted day-count).
/// </summary>
public sealed class FeriehindringPaidOut : DomainEventBase
{
    public override string EventType => "FeriehindringPaidOut";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §25 persistent-impediment payout bucket day-count (<c>payout_days</c>; ADR-033 D5).</summary>
    public decimal PayoutDays { get; init; }
}
