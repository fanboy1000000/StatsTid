using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5/D9 (§26 + §7) — DEFINE-ONLY in slice 1a (contract fixed now; automates in
/// slice 3). The composite termination settlement, written when <c>users.employment_end_date</c>
/// is set: it crystallizes the final balance at the termination date and settles the
/// year-of-termination with <c>trigger = TERMINATION</c> (which CONSUMES that year's later
/// YEAR_END due-check — a termination IS the settlement of that year, ADR-033 D5/D9).
///
/// One composite event carries all three §-buckets (ADR-033 D9):
/// <list type="bullet">
///   <item><see cref="PayoutDays"/> — §26 payout of earned-untaken (a day-count payout line);</item>
///   <item><see cref="ModregningDays"/> — §7 stk.1 modregning of over-taken ferie (a DEDUCTION line,
///         capped at outstanding pay — the SLS contract + cap are NOT yet verified, slice-3 Step-0,
///         ADR-033 D1; a bare day-count cannot prove a monetary cap);</item>
///   <item><see cref="UnearnedAdvanceDays"/> — §7 unearned-advance / ferie-uden-løn udligning (same
///         §7 mechanism — there is NO separate §8; ADR-033 D9 drops the ADR-030-D7 "§8-equivalent").</item>
/// </list>
///
/// Stream: <c>employee-{EmployeeId}</c>. Replay reads <see cref="Snapshot"/> verbatim (ADR-033 D3).
/// ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson (non-required nullable snapshot + defaulted day-counts).
/// </summary>
public sealed class TerminationSettled : DomainEventBase
{
    public override string EventType => "TerminationSettled";

    // Settlement identity (ADR-033 D5). EntitlementYear = the year-of-termination being settled.
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot crystallized at the termination date (ADR-033 D3/D9).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>§26 earned-untaken payout bucket day-count (<c>payout_days</c>; ADR-033 D5/D9).</summary>
    public decimal PayoutDays { get; init; }

    /// <summary>
    /// §7 stk.1 modregning bucket day-count for over-taken ferie — a DEDUCTION line. Capped at
    /// outstanding pay; the cap is enforced SLS-side and is NOT verified as a bare day-count
    /// (slice-3 Step-0 gate, ADR-033 D1/D9).
    /// </summary>
    public decimal ModregningDays { get; init; }

    /// <summary>
    /// §7 unearned-advance / ferie-uden-løn udligning bucket day-count (same §7 mechanism; no
    /// separate §8 — ADR-033 D9).
    /// </summary>
    public decimal UnearnedAdvanceDays { get; init; }
}
