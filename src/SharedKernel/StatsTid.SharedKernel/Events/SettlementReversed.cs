using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68-defined / S71-extended (ADR-033 D4/D5; SPRINT-71 R10) — the operator-authorized settlement
/// reversal fact. Reversal = a COMPENSATING entry, never a rollback (P3) and never an automatic
/// cascade (ADR-013): one atomic tx transitions the active row → REVERSED (STATE-ONLY — the row
/// keeps its own sequence and its original D3 snapshot; no new row sequence is allocated) and
/// EITHER supersedes it with a fresh settlement at the R1 next-generation sequence (SUPERSEDED)
/// OR parks the tuple behind the durable bare-reversal not-due marker (BARE, R3).
///
/// <para>
/// R10 PAYLOAD EXTENSION BEFORE FIRST EMISSION (S71 / TASK-7101): the class was DEFINE-ONLY from
/// S68 — registered for replay but never emitted — so extending/renaming its payload is clean (no
/// stored stream carries the old shape). Renames vs the S68 shape, declared per SPRINT-71:
/// <c>ReversedSequence</c> → <see cref="SettlementSequence"/> (the REVERSED row's settlement-ROW
/// sequence, the R2 vocabulary) and the S68 <c>Sequence</c> ("this reversal entry's own
/// sequence") is DELETED — a reversal allocates NO row sequence, and compensating-line EXPORT
/// sequences derive consumer-side per R1 (even <c>2g</c>), so the field misdescribed the design.
/// </para>
///
/// <para>
/// NO staged-line references on the payload (R10): emission cannot know consumption state — the
/// Payroll consumer derives its compensation TARGETS from its own staged-line records (R9) and
/// takes QUANTITIES from this payload's per-bucket recorded day-counts. All quantities are
/// POSITIVE day-counts of the reversed row — direction is the export line's
/// <c>line_kind = 'REVERSAL'</c>, never a negative quantity (R8).
/// </para>
///
/// Stream: <c>employee-{EmployeeId}</c>. Emitted by the slice-3b reversal service (TASK-7104) in
/// the same atomic tx as the row transition, under the ADR-032 D4 employee advisory lock (R12).
/// ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson (non-required nullable snapshot/optionals +
/// defaulted value-type day-counts) — a registered-but-unemitted class must NOT break replay
/// coverage.
/// </summary>
public sealed class SettlementReversed : DomainEventBase
{
    public override string EventType => "SettlementReversed";

    /// <summary><see cref="ReversalKind"/> value: bare reversal — no successor; the tuple parks behind the R3 marker (TERMINAL in 3b).</summary>
    public const string ReversalKindBare = "BARE";

    /// <summary><see cref="ReversalKind"/> value: reverse-then-re-settle — a superseding row at <see cref="SuccessorSequence"/> rides the same tx.</summary>
    public const string ReversalKindSuperseded = "SUPERSEDED";

    // Original settlement-ROW identity (ADR-033 D5; SPRINT-71 R2 — settlement sequence, not export sequence).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }

    /// <summary>
    /// The REVERSED row's own settlement-ROW sequence (SPRINT-71 R2; renamed from the S68
    /// <c>ReversedSequence</c>). Consumers derive the compensating line's even EXPORT sequence
    /// per R1 — never from this field directly.
    /// </summary>
    public int SettlementSequence { get; init; }

    /// <summary>
    /// <see cref="ReversalKindBare"/> ("BARE") or <see cref="ReversalKindSuperseded"/>
    /// ("SUPERSEDED") — SPRINT-71 R10. Bare reversal writes the durable
    /// <c>bare_reversal_not_due</c> marker (R3); supersession allocates the next-generation
    /// settlement row in the same tx (R1/R4).
    /// </summary>
    public required string ReversalKind { get; init; }

    /// <summary>
    /// The superseding settlement row's sequence (R1: <c>2g−1</c> of the next generation) when
    /// <see cref="ReversalKind"/> is SUPERSEDED; null on a BARE reversal.
    /// </summary>
    public int? SuccessorSequence { get; init; }

    /// <summary>The reversed row's trigger (<c>'YEAR_END'</c> or <c>'TERMINATION'</c> — the <c>vacation_settlements.trigger</c> vocabulary).</summary>
    public required string Trigger { get; init; }

    /// <summary>The reversed row's PRESERVED immutable settle-time input snapshot (ADR-033 D3 — the row survives as history).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    // Per-bucket recorded quantities of the REVERSED row (R10) — positive day-counts; the
    // compensating line copies the quantity and marks direction via line_kind (R8).

    /// <summary>The reversed row's §21 transfer bucket day-count (<c>transfer_days</c>).</summary>
    public decimal TransferDays { get; init; }

    /// <summary>The reversed row's payout bucket day-count (<c>payout_days</c>).</summary>
    public decimal PayoutDays { get; init; }

    /// <summary>The reversed row's §34 forfeiture bucket day-count (<c>forfeit_days</c>).</summary>
    public decimal ForfeitDays { get; init; }

    /// <summary>
    /// The reversed row's crystallized §26 day-count (snapshot <c>CrystallizedDays</c>) — set on
    /// <c>trigger=TERMINATION</c> rows (whose bucket columns are all zero, SPRINT-70 R5); null
    /// elsewhere. Lets the consumer compensate a zero-bucket TERMINATION line (the S68
    /// bucket-delta-only shape could not — SPRINT-71 in-scope rationale).
    /// </summary>
    public decimal? CrystallizedDays { get; init; }

    /// <summary>
    /// The reversed row's resolved §7/waiver claim quantity
    /// (<c>vacation_settlements.claim_disposition_days</c>, SPRINT-71 R5) — set when the row was
    /// resolved MODREGNING/WAIVED; null elsewhere.
    /// </summary>
    public decimal? ClaimDispositionDays { get; init; }
}
