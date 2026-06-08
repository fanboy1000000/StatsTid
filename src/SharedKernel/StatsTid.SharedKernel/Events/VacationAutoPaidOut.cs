using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (§24). Emitted from the atomic period-close settlement (ADR-033 D3) for the
/// automatic post-period payout of the &gt;4-week tranche that was NOT transferred by a §21
/// written agreement (the law's default). A payout emits a <b>day-count payout line</b>
/// (ADR-033 D1/D7) — SLS owns the rate; StatsTid carries only <see cref="PayoutDays"/>.
///
/// §24 is PAYOUT, never carryover provenance (ADR-033 D6 — the cycle-1 legal correction).
///
/// Stream: <c>employee-{EmployeeId}</c>. Replay reads <see cref="Snapshot"/> verbatim (ADR-033 D3).
/// ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson (non-required nullable snapshot + defaulted day-count).
/// </summary>
public sealed class VacationAutoPaidOut : DomainEventBase
{
    public override string EventType => "VacationAutoPaidOut";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §24 payout bucket day-count (<c>payout_days</c>; ADR-033 D5 shared vocabulary).</summary>
    public decimal PayoutDays { get; init; }
}
