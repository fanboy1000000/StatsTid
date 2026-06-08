using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (Cirkulære 021-24 §15 stk.2 + §17) — DEFINE-ONLY in slice 1a (contract fixed
/// now; automates in slice 2). The state særlige-feriedage 2½% cash godtgørelse: the entitlement
/// is settled as a day-count payout line within the §12 stk.2 taking window. A payout emits a
/// <b>day-count payout line</b> (ADR-033 D1/D7) — SLS owns the 2½% rate; StatsTid carries only
/// <see cref="PayoutDays"/>. (§17's 2½% ≠ §10's 2,02% — distinct, never conflated; ADR-033 D12.)
///
/// EntitlementType for this event is <c>SPECIAL_HOLIDAY</c> (the user-facing "Særlige feriedage").
/// Registered now so the slice-2 godtgørelse emission is a replayable, contract-fixed event.
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (non-required nullable snapshot + defaulted day-count).
/// </summary>
public sealed class SaerligeFeriedagePaidOut : DomainEventBase
{
    public override string EventType => "SaerligeFeriedagePaidOut";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §15 stk.2/§17 godtgørelse payout bucket day-count (<c>payout_days</c>; ADR-033 D5).</summary>
    public decimal PayoutDays { get; init; }
}
