namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S71 / ADR-033 slice 3b (SPRINT-71 R6) — the §26 anmodning fact. Ferieloven §26 stk.1 pays
/// earned-untaken termination ferie <i>efter anmodning</i>: the HR-recorded REQUEST, not the
/// settlement itself, is what drives the staged <c>SLS_TBD_S26</c> payout line. Emitted from the
/// §26 request endpoint ONLY when a line should stage (guards: active SETTLED
/// <c>trigger=TERMINATION</c> row, <c>CrystallizedDays &gt; 0</c>, end date passed, no non-voided
/// request) and rides ONE atomic tx with the <c>termination_payout_requests</c> OPEN row, the
/// outbox append and the ADR-026 audit projection, under the ADR-032 D4 employee advisory
/// lock (R12).
///
/// <para>
/// The slice-3b §26 Payroll consumer (TASK-7105) stages an exactly-once, money-free day-count
/// line off THIS event: <c>hours = <see cref="CrystallizedDays"/></c> (copied here from the
/// settlement snapshot at request time so the consumer NEVER re-derives — the S69 snapshot-keyed
/// precedent / ADR-033 D3) and resolves the lønart fail-closed at
/// <c>asOf = <see cref="SettlementBoundaryDate"/></c> (R11 — for TERMINATION snapshots that IS
/// the employment end date, the legally coherent anchor for termination-time entitlements).
/// </para>
///
/// <para>
/// SPRINT-71 R2 — <see cref="SettlementSequence"/> is the settlement-ROW sequence (row identity:
/// request FK, CAS, history), NEVER the export sequence; consumers derive the even EXPORT
/// sequence per R1. RecordedBy = the base <see cref="DomainEventBase.ActorId"/> (the HR caller —
/// the durable <c>termination_payout_requests.recorded_by</c> column mirrors the same actor);
/// <see cref="RequestDate"/> is the as-stated anmodning date, which may differ from
/// <see cref="DomainEventBase.OccurredAt"/> (insertion time).
/// </para>
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (nullable <see cref="EvidenceNote"/> is non-required; value-typed members default cleanly).
/// </summary>
public sealed class TerminationPayoutRequested : DomainEventBase
{
    public override string EventType => "TerminationPayoutRequested";

    // Settlement-ROW identity (ADR-033 D5; SPRINT-71 R2 — settlement sequence, not export sequence).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }

    /// <summary>
    /// The settlement-ROW sequence of the SETTLED TERMINATION row the request targets
    /// (SPRINT-71 R2). Consumers derive the export sequence per R1 — never from this field.
    /// </summary>
    public int SettlementSequence { get; init; }

    /// <summary>
    /// The as-stated HR-recorded anmodning date (mirrors
    /// <c>termination_payout_requests.request_date</c> — the evidence-date convention;
    /// <see cref="DomainEventBase.OccurredAt"/> records emission time).
    /// </summary>
    public DateOnly RequestDate { get; init; }

    /// <summary>Free-text request evidence (mirrors <c>termination_payout_requests.evidence_note</c>); nullable.</summary>
    public string? EvidenceNote { get; init; }

    /// <summary>
    /// The crystallized §26 day-count the staged line will carry — COPIED from the settlement
    /// snapshot's <c>CrystallizedDays</c> at request time (ADR-033 D3: the consumer never
    /// re-derives). Money-free: a day-count, never an amount (ADR-033 D1).
    /// </summary>
    public decimal CrystallizedDays { get; init; }

    /// <summary>
    /// The R11 lønart-resolution <c>asOf</c> anchor — copied from the snapshot's
    /// <c>SettlementBoundaryDate</c> (for TERMINATION snapshots, the employment end date,
    /// SPRINT-70 R5). The §26 consumer resolves the ADR-020 dated wage-type natural key at
    /// exactly this date, fail-closed (no live lookup).
    /// </summary>
    public DateOnly SettlementBoundaryDate { get; init; }
}
