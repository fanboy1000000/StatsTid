using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D4/D5 — DEFINE-ONLY in slice 1a (contract fixed now; emission automates later).
/// Reversal = a COMPENSATING entry, never a rollback (P3): when a settled year is later
/// retroactively corrected, an operator-authorized (ADR-013 — never an automatic cascade) atomic
/// tx transitions the active row SETTLED → REVERSED (it survives as history with its original D3
/// snapshot) and the export side gets a compensating reversal line.
///
/// Carries the ORIGINAL settlement's <c>(identity, sequence)</c> via <see cref="EntitlementYear"/>
/// + <see cref="ReversedSequence"/>, the immutable <see cref="Snapshot"/>, and the compensating
/// bucket deltas. Per ADR-033 D4/D5 the compensating export line is checkpointed at a sequence /
/// bucket DISTINCT from both the original line and any superseding-settlement line (the
/// sequence-allocation invariant — export-line uniqueness on <c>(identity, sequence, bucket)</c>);
/// the exact allocation is a slice-Step-0b mechanism, this event only carries the payload.
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (non-required nullable snapshot + defaulted value-type deltas) — a registered-but-unemitted
/// class must NOT break replay coverage.
/// </summary>
public sealed class SettlementReversed : DomainEventBase
{
    public override string EventType => "SettlementReversed";

    // Settlement identity (ADR-033 D5). Sequence is THIS reversal entry's own sequence;
    // ReversedSequence names the original settlement sequence being compensated.
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The original settlement <c>sequence</c> being reversed (ADR-033 D4 — original identity).</summary>
    public int ReversedSequence { get; init; }

    /// <summary>The immutable settle-time input snapshot of the reversed settlement (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>Compensating §21 transfer-bucket delta (negates the original <c>transfer_days</c>; ADR-033 D4).</summary>
    public decimal TransferDays { get; init; }

    /// <summary>Compensating payout-bucket delta (negates the original <c>payout_days</c>; ADR-033 D4).</summary>
    public decimal PayoutDays { get; init; }

    /// <summary>Compensating §34 forfeiture-bucket delta (negates the original <c>forfeit_days</c>; ADR-033 D4).</summary>
    public decimal ForfeitDays { get; init; }
}
