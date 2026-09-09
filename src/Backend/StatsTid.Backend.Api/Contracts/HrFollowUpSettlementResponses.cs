using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S140 / TASK-14003 (refinement B1; HR follow-up register rows HRP-005, HRP-005b, HRP-007,
// HRP-010) — the SETTLEMENT-FAMILY wire shapes for the HR follow-up surface (PAT-012 typed
// contracts, camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default).
//
// WHY THESE EXIST, in plain language. The system repeatedly hands HR a job and then walks away:
// a vacation settlement is flagged for manual review and nothing lists it; a departing
// employee's holiday payout needs a formal §26 request and nothing lists who is waiting; the
// §21 fifth-week transfer agreement can only be WRITTEN through the API — nothing could read
// back what was recorded, and nothing told HR who still needed one before 31 December. These
// three reads are the visible surface for that work. They are READ-ONLY in S140 (owner ruling
// OQ-4, 2026-09-08): the acting endpoints (resolve / reconcile / record-agreement) already
// exist and are untouched.
//
// PINNED PROHIBITION inherited from SPRINT-117 (see SettlementResponses.cs): no Contracts type
// may embed VacationSettlementSnapshot — its conditionally-absent [JsonIgnore] members must
// never enter a response closure. Every record below copies SCALARS only.
//
// ADR-040 D7 (as tightened for this task) — NO EMPLOYMENT DATE ANYWHERE. Not in a payload, not
// in an error body. This is a live trap on this surface, not a theoretical one: a TERMINATION
// snapshot's SettlementBoundaryDate IS the employment end date (VacationSettlementService
// `valuationBoundary = terminationCutoff ?? boundaryDate`), and TerminationDate is the end date
// outright. Neither is carried by HRP-007 below, and the §21 population predicate reads the
// employment window in SQL without ever projecting it.

// ═══════════════════════════════════════════════════════════════════════════════════════════
// HRP-005 + HRP-005b — settlements flagged for manual review
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// One settlement awaiting HR's manual review, from EITHER of the two sources the system
/// actually produces (ADR-033 D5/D10).
///
/// <para>
/// <b><paramref name="Source"/> = <c>row</c></b> — a <c>vacation_settlements</c> row parked in
/// <c>PENDING_REVIEW</c>: the automated close resolved the §21/§24/§26 buckets it is allowed to
/// resolve and FAILED CLOSED on the remainder rather than wrongly forfeiting it (D10). The
/// remainder is real and unadjudicated; <paramref name="FlaggedDays"/> carries it verbatim from
/// the row's <c>forfeit_days</c> (NUMERIC(6,2), never recomputed here). Read that number as
/// "days AWAITING a decision", NOT as "days that will be forfeited": on a PENDING_REVIEW row
/// <c>forfeit_days</c> is the S68 FLAG convention — the quantity parked for HR to disposition
/// into §34 forfeiture, §22 feriehindring or (on a termination) a §7 claim. All three
/// PENDING_REVIEW-producing paths stamp their flagged quantity into that column, which is why
/// one field serves them all.
/// </para>
///
/// <para>
/// <b><paramref name="Source"/> = <c>event</c></b> (HRP-005b) — a REFUSED termination. When a
/// TERMINATION settlement collides with an already-active YEAR_END row for the same ferieår, the
/// system writes NO settlement row at all: it emits <c>SettlementManualReviewFlagged</c> carrying
/// the CONFLICTING row's identity, a null snapshot and zero flagged days (a conflict SIGNAL, not
/// a quantity). So there is nothing in <c>vacation_settlements</c> to list — the item is derived
/// from the event stream, and <paramref name="FlaggedDays"/> is 0 by construction. The exit is a
/// settlement reversal (HRP-009), which flips the referenced row to REVERSED and drops the item.
/// </para>
///
/// <para>
/// <b>The two sources can BOTH fire on one settlement — by design.</b> The refusal flags the
/// conflicting ACTIVE row, and "active" includes <c>PENDING_REVIEW</c>. So a refused termination
/// whose conflicting year-end row is itself parked for review produces TWO items with the same
/// (employee, type, year, sequence): one <c>row</c> item carrying the flagged remainder and one
/// <c>event</c> item carrying 0 days. That is not double-counting — they are two different jobs
/// on one settlement (disposition the remainder; resolve the termination conflict), and collapsing
/// them would lose the conflict signal on exactly the tuples that carry two problems at once. A
/// consumer that counts "settlements needing attention" should group by the identity tuple; a
/// consumer that counts "jobs waiting" should not.
/// </para>
///
/// <para>
/// <paramref name="SettlementState"/>/<paramref name="Trigger"/>/<paramref name="ReviewDisposition"/>/
/// <paramref name="Version"/> are the LIVE state of the referenced row, derived at read time
/// (PAT-026) — never a cached flag. <paramref name="ReviewDisposition"/> is non-null when HR has
/// already acted without closing the item — <c>DEFER</c> PARKS a row as suspected §22
/// feriehindring without RESOLVING it, so the row stays PENDING_REVIEW and still needs a terminal
/// verb (<c>FERIEHINDRING</c> or <c>FORFEIT</c>). The tile can therefore separate "untouched" from
/// "looked at, not finished". <paramref name="AgeAnchor"/> is the timestamp the age is measured
/// from — the row's <c>created_at</c> for a <c>row</c> item, the EARLIEST <c>occurred_at</c> of the
/// flag for an <c>event</c> item (two call paths reach the one conflict-emitting site, so the same
/// row can be flagged more than once; the FIRST flag is when HR's job started).
/// <paramref name="AgeDays"/> is that anchor's distance from the Copenhagen business date of THIS
/// request, floored at 0 — an age is never negative.
/// </para>
/// </summary>
public sealed record PendingSettlementReviewItem(
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int SettlementSequence,
    [property: AllowedValues("row", "event")] string Source,
    [property: AllowedValues("PENDING_REVIEW", "SETTLED", "REVERSED")] string SettlementState,
    [property: AllowedValues("YEAR_END", "TERMINATION")] string Trigger,
    string? ReviewDisposition,
    decimal FlaggedDays,
    long Version,
    DateTimeOffset AgeAnchor,
    int AgeDays,
    string PrimaryOrgId);

/// <summary>
/// The <c>GET /api/hr/follow-up/settlement-reviews</c> 200 envelope. <paramref name="Items"/> is
/// OLDEST-FIRST by <c>ageAnchor</c> (the landing page derives "oldest: N days" from the first
/// element); <paramref name="Today"/> is the ONE Copenhagen business date this response was
/// computed against (PAT-028), so the caller can see which day the ages are relative to.
///
/// <para>
/// <b><paramref name="EventSourceLagNote"/> — declared eventual consistency.</b> The
/// <c>event</c>-sourced items (HRP-005b) are read from the canonical <c>events</c> table, which
/// the outbox publisher fills ONE POLL CYCLE AFTER the domain transaction commits — not in the
/// same transaction (ADR-018 D3 is about the outbox row, not the projected event). The publisher
/// polls every 250 ms while busy and every second when quiet, so a termination refused just now
/// appears here within a few seconds, not instantly. This is a small window, but it is real, and
/// it is ACCEPTED and STATED rather than worked around: reading the outbox to make it instant would
/// couple a diagnostic read to delivery internals and could show HR an item that never lands.
/// The field carries that sentence so the tile can display it.
/// </para>
/// </summary>
public sealed record PendingSettlementReviewListResponse(
    IReadOnlyList<PendingSettlementReviewItem> Items,
    int Count,
    DateOnly Today,
    string EventSourceLagNote);

// ═══════════════════════════════════════════════════════════════════════════════════════════
// HRP-007 — settled terminations awaiting a §26 payout request
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// One SETTLED termination settlement whose crystallised holiday days still need HR to record
/// the formal §26 payout request (<c>POST /api/admin/employees/{employeeId}/termination-payout-request</c>).
///
/// <para>
/// <paramref name="CrystallizedDays"/> is COPIED from the settlement's immutable snapshot
/// (ADR-033 D3 — the same accessor the §26 endpoint uses), never recomputed.
/// <paramref name="Version"/> is the settlement row version the §26 request will demand as
/// <c>If-Match</c>, and <paramref name="SettlementSequence"/> is the generation it must bind to
/// (SPRINT-71 R2), so the tile can hand the operator everything the write needs.
/// <paramref name="AgeAnchor"/> is the settlement row's <c>created_at</c> — the moment the
/// crystallisation was recorded, which is when HR's job began.
/// </para>
///
/// <para>
/// <b>Deliberately absent:</b> the snapshot's <c>settlementBoundaryDate</c> and
/// <c>terminationDate</c>. On a TERMINATION snapshot both ARE the employment end date, which no
/// response body may carry (ADR-040 D7 as tightened for this task).
/// </para>
/// </summary>
public sealed record TerminationPayoutUnrequestedItem(
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int SettlementSequence,
    decimal CrystallizedDays,
    long Version,
    DateTimeOffset AgeAnchor,
    int AgeDays,
    string PrimaryOrgId);

/// <summary>
/// The <c>GET /api/hr/follow-up/termination-payouts-unrequested</c> 200 envelope. OLDEST-FIRST by
/// <c>ageAnchor</c>; <paramref name="Today"/> is the one business date the ages are relative to.
///
/// <para>
/// <b>What is NOT here, and why (all four predicate terms matter).</b> A REVERSED settlement is
/// absent — a reversal voids the request AND marks the row REVERSED in the same transaction, so
/// requiring <c>settlement_state = 'SETTLED'</c> is what keeps a reversed termination off the
/// tile; under reverse-and-supersede the SUCCESSOR row (next sequence) appears instead, until its
/// own request is recorded. A settlement whose §7 claim was WAIVED is absent — a waived claim
/// never gets a §26 request, so leaving it here would park an item that can never be closed. A
/// zero-crystallisation termination is absent (there is no quantity to request), and so is any
/// row that already has a live (non-<c>VOIDED_BY_REVERSAL</c>) request.
/// </para>
/// </summary>
public sealed record TerminationPayoutUnrequestedListResponse(
    IReadOnlyList<TerminationPayoutUnrequestedItem> Items,
    int Count,
    DateOnly Today);

// ═══════════════════════════════════════════════════════════════════════════════════════════
// HRP-010 — the §21 stk.2 fifth-week transfer agreement: the record, and who still needs one
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// One recorded §21 stk.2 written transfer agreement (ADR-033 D8). <paramref name="Deadline"/> is
/// the statutory 31-December deadline of THAT ferieår's holiday-taking period, derived from the
/// shared <c>EntitlementPeriodResolver</c> geometry (reset-month 9 ⇒ ferieår E closes 31 Aug E+1
/// and its §21 deadline is 31 Dec E+1) — a derived convenience, not a stored column.
/// <paramref name="Version"/> is the row version the edit PUT demands as <c>If-Match</c>.
/// </summary>
public sealed record VacationTransferAgreementRecordItem(
    int EntitlementYear,
    [property: AllowedValues("VACATION")] string EntitlementType,
    decimal TransferDays,
    DateOnly AgreementDate,
    string RecordedBy,
    long Version,
    DateOnly Deadline);

/// <summary>
/// The <c>GET /api/vacation-transfer-agreements/{employeeId}</c> 200 envelope — what was actually
/// recorded for this employee, in ferieår order. Until S140 this record was WRITE-ONLY: POST and
/// PUT existed with no reader anywhere, so an agreement HR had entered could not be verified
/// through the API at all. An employee with no agreement returns an empty list with
/// <c>count: 0</c> (not a 404 — "nothing recorded" is a legitimate answer, and the law's default
/// is §24 auto-payout).
/// </summary>
public sealed record VacationTransferAgreementRecordResponse(
    string EmployeeId,
    IReadOnlyList<VacationTransferAgreementRecordItem> Agreements,
    int Count);

/// <summary>
/// One employee who appears to still need a §21 stk.2 agreement for the target ferieår.
///
/// <para>
/// <paramref name="UnderCapDays"/> is the §21/§24 tranche of the settlement partition:
/// <c>min(disposable, carryover_max)</c> — the untaken remainder of the ferieår, capped at the
/// statutory fifth-week ceiling (<paramref name="CarryoverMax"/>). It is the portion that MAY be
/// transferred under §21 or paid out under §24; anything above the cap is the §34
/// forfeiture-candidate instead. A positive value is therefore exactly "this employee still has
/// fifth-week days that a written agreement could carry forward".
/// </para>
///
/// <para>
/// It is computed by the ONE settlement valuation
/// (<c>VacationSettlementService.Partition(snapshot).UnderCap</c>) over a snapshot captured by
/// the settlement service's own read-only entry point. There is deliberately NO second
/// implementation of this quantity: two implementations of a legal day-count diverge, and the
/// divergence surfaces as a wrong number in front of an employee.
/// </para>
///
/// <para>
/// <paramref name="DaysToDeadline"/> counts from the business date of this request to
/// <paramref name="Deadline"/> (31 December). <paramref name="AgeAnchorDate"/> is the date the
/// untaken days became §21/§24 candidates — the ferieår END. It is the same value for every row
/// (the VACATION ferieår geometry is uniform: <c>reset_month</c> is CHECK-pinned to 9), so this
/// tile shows DAYS TO THE DEADLINE rather than "oldest: N days"; the anchor is carried anyway so
/// the shape matches the other follow-up lists.
/// </para>
/// </summary>
public sealed record VacationTransferAgreementNeededItem(
    string EmployeeId,
    string PrimaryOrgId,
    int EntitlementYear,
    decimal UnderCapDays,
    decimal CarryoverMax,
    DateOnly Deadline,
    int DaysToDeadline,
    DateOnly AgeAnchorDate);

/// <summary>
/// One employee the §21 list could NOT value, kept visible instead of being silently dropped.
/// The settlement valuation FAILS CLOSED per employee (ADR-033 D10): missing dated
/// agreement-code, entitlement-config or employee-profile history at the ferieår start throws
/// rather than valuing against today's live data. One such employee must not empty the tile for
/// everyone, and must not vanish either — HR needs to know a name is unaccounted for.
///
/// <para>
/// <paramref name="Reason"/> is a stable CODE, not the exception text: the failure messages name
/// dated-history anchors, and an error string on the wire is exactly how a date leaks out of a
/// redacted field (ADR-040 D7 / D3). The detail is logged server-side against the employee id.
/// </para>
/// </summary>
public sealed record VacationTransferAgreementCannotComputeItem(
    string EmployeeId,
    string PrimaryOrgId,
    [property: AllowedValues("VALUATION_FAILED")] string Reason);

/// <summary>
/// The <c>GET /api/hr/follow-up/transfer-agreements-needed</c> 200 envelope.
///
/// <para>
/// <b>Which ferieår this is about (<paramref name="EntitlementYear"/>) — the easiest thing to get
/// wrong.</b> The target is NOT the ferieår containing today. It is the ferieår whose §21 deadline
/// is THIS 31 December: the year E for which <c>EntitlementPeriodResolver.ResolveForYear(VACATION,
/// resetMonth, E).Boundary.Year == today.Year</c>. Under the state-sector September reset, ferieår
/// E closes 31 August E+1 and its §21 deadline is 31 December E+1 — so in November 2025 the
/// answer is <b>E = 2024</b>, not 2025. Listing 2025 would list the wrong people in the only
/// window the rule applies.
/// </para>
///
/// <para>
/// <b>The window (<paramref name="WindowOpen"/> / <paramref name="WindowOpensOn"/>).</b> The
/// reminder runs from 1 November to the 31 December deadline (owner ruling, 2026-09-08). Outside
/// it the response is <c>windowOpen: false</c> with an EMPTY item list, so the tile can say
/// "opens 1 November" instead of showing a count — that is the difference between "out of season"
/// and "nothing to do", which a count alone cannot express.
/// </para>
///
/// <para>
/// <b><paramref name="ProjectionNote"/> — declared approximation (owner-ratified).</b> Before the
/// year-end close the system has no exact §21 residual, only an accrual-side projection: the
/// authoritative partition is computed at settlement. So this list can name an employee the close
/// later clears. The owner accepted that trade — a projection-based reminder in November beats an
/// exact list that only exists in January, after the deadline has passed.
/// </para>
/// </summary>
public sealed record VacationTransferAgreementNeededListResponse(
    IReadOnlyList<VacationTransferAgreementNeededItem> Items,
    int Count,
    int EntitlementYear,
    bool WindowOpen,
    DateOnly WindowOpensOn,
    DateOnly Deadline,
    int DaysToDeadline,
    IReadOnlyList<VacationTransferAgreementCannotComputeItem> CannotCompute,
    int CannotComputeCount,
    DateOnly Today,
    string ProjectionNote);
