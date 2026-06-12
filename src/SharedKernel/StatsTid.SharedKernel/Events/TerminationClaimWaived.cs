namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S71 / ADR-033 slice 3b (SPRINT-71 R5, owner decision D-C) — the waive-in-full resolution of a
/// §7-shaped termination claim. A negative-pre-clamp TERMINATION row parked PENDING_REVIEW holds
/// an over-taken-ferie claim the employer COULD modregne under Ferieloven §7 stk.1; THIS event
/// records the operator's explicit decision to WAIVE that claim in full. NO Payroll line ever
/// stages from it (R9 — waiver has no consumer); it exists for the audit trail + replay record.
///
/// <para>
/// The §7 deduct-in-full counterpart (<c>TerminationModregningApplied</c>) is PARKED behind the
/// SLS-dialogue task (slice Step-0 gate (i): the outstanding-pay cap is not confirmably
/// SLS-enforceable on a day-count input, so the event contract would bake an unverified payload
/// shape) — slice 3b ships the waiver branch only.
/// </para>
///
/// <para>
/// Emitted from the waiver CAS resolve verb (TASK-7103) in ONE atomic tx with the settlement-row
/// transition PENDING_REVIEW → SETTLED (<c>review_disposition = 'WAIVED'</c>,
/// <c>claim_disposition_days = <see cref="WaivedDays"/></c> — NEVER left in <c>forfeit_days</c>;
/// a waived claim must not read as §34 forfeiture, R5), the outbox append and the ADR-026 audit
/// projection, under the ADR-032 D4 employee advisory lock (R12). No resolve disposition writes
/// <c>carryover_in</c> (the restated 3a invariant). NO settlement-row version pair on the
/// payload: the settlement event family carries the row identity only (S68 convention —
/// inspected: none of the S68/S70 settlement events carry versions; CAS rides the ADR-019
/// If-Match + <c>vacation_settlements.version</c>).
/// </para>
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (value-typed members default cleanly; required strings are production-always-bound).
/// </summary>
public sealed class TerminationClaimWaived : DomainEventBase
{
    public override string EventType => "TerminationClaimWaived";

    // Settlement-ROW identity (ADR-033 D5; SPRINT-71 R2 — settlement sequence, not export sequence).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }

    /// <summary>The settlement-ROW sequence of the PENDING_REVIEW row being resolved (SPRINT-71 R2).</summary>
    public int SettlementSequence { get; init; }

    /// <summary>
    /// The absolute (|pre-clamp|) §7-shaped claim day-count being waived in full — mirrors the
    /// <c>vacation_settlements.claim_disposition_days</c> column (non-negative; SPRINT-71 R5).
    /// Quantities come from the row/snapshot, never recomputed.
    /// </summary>
    public decimal WaivedDays { get; init; }
}
