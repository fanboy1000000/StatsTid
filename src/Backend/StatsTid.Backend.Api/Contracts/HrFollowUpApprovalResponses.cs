using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S140 / TASK-14004 (refinement B2) — typed response records for the HR follow-up APPROVAL /
// LIFECYCLE / ORGANISATION reads (PAT-012 strict types: [property: AllowedValues] closed-set
// discriminators are emitted as spec enums by the ResponseStrictTypesFilter → TS literal unions).
//
// ALL of these are READ-ONLY diagnostic lists (owner ruling OQ-4: S140 ships the reads, not the
// write actions). Every one is gated `HROrAbove` with the LocalHR per-scope floor bound to the
// SUBJECT employee's CURRENT organisation.
//
// WHY EVERY ITEM CARRIES AN AGE ANCHOR AND A DEADLINE SOURCE. A follow-up list is only as
// trustworthy as its dates. Each item therefore names the date its age is measured FROM
// (`ageAnchor`) and whether that date was READ from the period row (`stored`) or DERIVED from the
// ratified institutional default because the row predates the deadline columns — or does not exist
// at all (`computed`). A missing deadline is never quietly treated as "on time": that silent pass
// is exactly the overclaim S140 exists to remove.
//
// NO EMPLOYMENT DATES CROSS THIS BOUNDARY (ADR-040 D7). A leaver item names the YEAR and MONTH its
// final month falls in — the process's subject — never the employment end date itself.

/// <summary>
/// One (employee × month) item shared by the past-deadline, leaver-final-month and
/// approved-not-exported reads. The three lists have the same shape on purpose: they are three
/// classifications of ONE enumeration, so a month can never appear with different facts depending
/// on which list you opened.
/// </summary>
/// <param name="PeriodStatus">
/// The period row's persisted status, or <c>NONE</c> when NO ROW EXISTS. A period row is born only
/// when a month is SENT, and there is no <c>OPEN</c> status — so "never sent" is the absence of a
/// row, which is why these reads enumerate months rather than query rows.
/// </param>
/// <param name="PeriodId">Null exactly when <paramref name="PeriodStatus"/> is <c>NONE</c>.</param>
/// <param name="AgeAnchor">
/// The date this item's age is measured from: the EMPLOYEE deadline (month-end + 2) for the
/// employee-late list, otherwise the MANAGER deadline (month-end + 5), which is also the ratified
/// payroll export cutoff.
/// </param>
/// <param name="DaysPastAnchor">
/// <c>today − ageAnchor</c> in days; POSITIVE means overdue. The past-deadline lists contain only
/// overdue months, so this is always positive there. The leaver-final-month and
/// approved-not-exported lists show every qualifying month whether or not it is overdue, so a
/// not-yet-due item legitimately carries zero or a negative value.
/// </param>
/// <param name="DeadlineSource">
/// <c>stored</c> = the period row's deadline column. <c>computed</c> = derived from the ratified
/// provisional institutional default (month-end + 2 / + 5) because no row exists or the row's
/// column is NULL.
/// </param>
public sealed record HrFollowUpMonthItem(
    string EmployeeId,
    string DisplayName,
    string OrgId,
    int Year,
    int Month,
    [property: AllowedValues("NONE", "DRAFT", "EMPLOYEE_APPROVED", "SUBMITTED", "APPROVED", "REJECTED")] string PeriodStatus,
    Guid? PeriodId,
    DateOnly AgeAnchor,
    int DaysPastAnchor,
    [property: AllowedValues("stored", "computed")] string DeadlineSource);

/// <summary>
/// HRP-012 — months PAST DEADLINE, with the employee's court and the leader's court counted
/// SEPARATELY because they are two different people's overdue work.
///
/// <para><b>Employee late</b> = a month the employment window covers whose employee deadline
/// (month-end + 2) has passed and which has NO period row, or a row in DRAFT (which exists only
/// after a leader reopened the month) or REJECTED (the ball is back with the employee). A REJECTED
/// month counts only ONCE month-end + 2 has passed — a leader may reject before that deadline —
/// and is aged from that deadline, not from the rejection.</para>
///
/// <para><b>Approver late</b> = a row in SUBMITTED or EMPLOYEE_APPROVED (both are
/// leader-approvable) whose manager deadline (month-end + 5) has passed.</para>
///
/// <para><b>The two counts, and the leaver-month count, are NOT ADDITIVE.</b> A departing
/// employee's late final month appears BOTH here and on the leaver's-final-month list, because the
/// two lists hand the work to different roles (the employee/HR must get the month sent; HR must
/// close out the leaver). Summing tiles would double-count it.</para>
///
/// <para><b>Bounded by a rolling 12-month floor</b> (<paramref name="LookbackFloor"/>, owner ruling
/// OQ-3 (a)): a never-sent month has no row, so without a bound this list would open on day one
/// with every pre-adoption month of every long-tenured employee. Anything older that still matters
/// reaches payroll through the correction path, not through this list.</para>
/// </summary>
/// <param name="Today">The Copenhagen business day the whole response was computed against (one date per request).</param>
/// <param name="LookbackFloor">The first day of the oldest month considered.</param>
/// <param name="EmployeeLate">Oldest first. NULL when <c>?summary=true</c> — "not requested", never an empty list that would contradict the count.</param>
/// <param name="ApproverLate">Oldest first. NULL when <c>?summary=true</c>.</param>
public sealed record HrPastDeadlineResponse(
    DateOnly Today,
    DateOnly LookbackFloor,
    int EmployeeLateCount,
    DateOnly? OldestEmployeeLateAnchor,
    int ApproverLateCount,
    DateOnly? OldestApproverLateAnchor,
    IReadOnlyList<HrFollowUpMonthItem>? EmployeeLate,
    IReadOnlyList<HrFollowUpMonthItem>? ApproverLate);

/// <summary>
/// HRP-011 — a leaver's FINAL month, when it is missing or not approved.
///
/// <para>For every employee in scope whose employment window has an END date that has passed: the
/// month CONTAINING that end date, listed when it has no period row or a row in DRAFT / SUBMITTED /
/// REJECTED / EMPLOYEE_APPROVED. "Leaver" is the employment window (ADR-040) — never the
/// <c>is_active</c> flag, which governs login only.</para>
///
/// <para>The FINAL month only, per the owner's ruling: a leaver's earlier never-sent months are
/// past-deadline items, not leaver items. Every qualifying month is listed whether or not its
/// manager deadline has passed ("open = all"); <c>daysPastAnchor</c> says which are overdue.</para>
///
/// <para><b>This list and the approved-not-exported list are DISJOINT but NOT complementary, and
/// their counts sum to nothing.</b> No month can appear on both: this list requires a status other
/// than APPROVED, that one requires APPROVED (S140 / TASK-14010 correction — an earlier version of
/// this comment called the two an EXACT partition, which they are not). They are not two halves of
/// one population, because they are drawn over DIFFERENT base populations: this list is restricted
/// to each leaver's FINAL month only, by the owner's ruling, while the approved-not-exported list
/// covers every approved-but-unexported month of active employees and leavers alike. Treat the two
/// counts as two independent facts. And see the note on non-additive counts in
/// <see cref="HrPastDeadlineResponse"/>.</para>
/// </summary>
/// <param name="Items">Oldest first. NULL when <c>?summary=true</c>.</param>
public sealed record HrLeaverFinalMonthResponse(
    DateOnly Today,
    DateOnly LookbackFloor,
    int Count,
    DateOnly? OldestAnchor,
    IReadOnlyList<HrFollowUpMonthItem>? Items);

/// <summary>
/// HRP-022 — months APPROVED but never sent to payroll.
///
/// <para>An approved month reaches payroll only when a person calls the payroll export endpoint;
/// no hosted service does it, and until S140 nothing listed the months waiting. Each item is aged
/// from the ratified EXPORT CUTOFF, which the owner ruled equal to that month's manager deadline
/// (month-end + 5). Every qualifying month is listed whether or not the cutoff has passed.</para>
///
/// <para><b>This read crosses a bounded context, READ-ONLY.</b> <c>payroll_export_records</c> is
/// owned and written SOLELY by the Payroll service; the Backend only ever reads it — permitted
/// explicitly by ADR-034 D4, and already done at three existing sites. The export action itself
/// stays a manual, audited admin call (ADR-013 / ADR-034 unchanged): this list tells HR what is
/// waiting, it does not export anything.</para>
/// </summary>
/// <param name="Items">Oldest first. NULL when <c>?summary=true</c>.</param>
public sealed record HrApprovedNotExportedResponse(
    DateOnly Today,
    DateOnly LookbackFloor,
    int Count,
    DateOnly? OldestAnchor,
    IReadOnlyList<HrFollowUpMonthItem>? Items);

/// <summary>One employee nobody structurally approves (HRP-013). Carries no date — see <see cref="HrUncoveredApproversResponse"/>.</summary>
public sealed record HrOrphanEmployee(
    string EmployeeId,
    string DisplayName,
    string OrgId,
    string? UnitName);

/// <summary>
/// One stand-in (vikar) delegation the expiry sweep closed inside the window (HRP-014a).
/// </summary>
/// <param name="ExpiredOn">The first day no longer covered (the close marker the sweep wrote).</param>
/// <param name="ExpiredAt">The Copenhagen day the closing event was recorded — the age anchor.</param>
/// <param name="ApproverHasActiveCover">
/// TRUE when this absent approver already has an ACTIVE delegation again, i.e. the expiry was
/// followed by a fresh one and the approver is NOT uncovered today. Reported per item so the raw
/// expiry count (a historical fact) cannot be mistaken for "approvers currently uncovered".
/// </param>
public sealed record HrExpiredDelegation(
    Guid VikarId,
    string AbsentApproverId,
    string AbsentApproverName,
    string VikarUserId,
    string? VikarUserName,
    string OrgId,
    string? UnitName,
    [property: AllowedValues("FERIE", "SYGDOM", "ORLOV", "TJENESTEREJSE", "ANDET")] string? Reason,
    DateOnly? UntilDate,
    DateOnly? ExpiredOn,
    DateOnly ExpiredAt,
    int DaysSinceExpiry,
    bool ApproverHasActiveCover);

/// <summary>
/// HRP-013 / HRP-014 — UNCOVERED APPROVERS: two facts on one surface.
///
/// <para><b>Orphans</b> (HRP-013): employees for whom no structural approver resolves, so nobody
/// CAN approve their month. The fact already exists per roster row on the organisation page, but
/// only per organisation, with no cross-organisation roll-up — this is that roll-up. Orphan items
/// carry NO date: there is no moment at which an employee "became" an orphan.</para>
///
/// <para><b>Expired delegations</b> (HRP-014a): stand-ins the expiry sweep closed in the last
/// <paramref name="WindowDays"/> days. Read from the canonical event stream, not from the
/// delegation table: the table has no end-reason column, so a table-only predicate cannot tell an
/// EXPIRY from a late MANUAL close — only the event carries <c>EXPIRED</c>, and only the sweep
/// writes it.</para>
///
/// <para>
/// <b><paramref name="EventSourceLagNote"/> — declared eventual consistency.</b> Those
/// event-sourced items are read from the canonical <c>events</c> table, which the outbox publisher
/// fills ONE POLL CYCLE AFTER the domain transaction commits — not in the same transaction (ADR-018
/// D3 is about the outbox row, not the projected event). So a delegation that expired seconds ago
/// may not be listed yet. Until S140 / TASK-14010 that window was documented HERE but not carried
/// in the response, so no client could tell the list was eventually consistent; this field carries
/// the sentence so the tile can display it — the same shape and field name as
/// <c>PendingSettlementReviewListResponse.EventSourceLagNote</c>, deliberately not a second
/// convention.
/// </para>
///
/// <para><b>No aging colour on this surface.</b> The underlying process is NOT decision-ready: no
/// rule states whether an orphan is tolerable at all, or by when an uncovered approver must be
/// re-covered (register rows HRP-013 / HRP-014). Counts and the oldest item are honest; a
/// red/amber age would assert a deadline nobody has ratified.</para>
/// </summary>
/// <param name="ExpiredDelegationCount">Every expiry in the window — a historical fact.</param>
/// <param name="ExpiredWithoutActiveCoverCount">The subset whose approver has NO active delegation today — the "actually uncovered" number.</param>
/// <param name="Orphans">NULL when <c>?summary=true</c>.</param>
/// <param name="ExpiredDelegations">Oldest first. NULL when <c>?summary=true</c>.</param>
public sealed record HrUncoveredApproversResponse(
    DateOnly Today,
    int OrphanCount,
    IReadOnlyList<HrOrphanEmployee>? Orphans,
    int ExpiredDelegationCount,
    int ExpiredWithoutActiveCoverCount,
    DateOnly? OldestExpiry,
    IReadOnlyList<HrExpiredDelegation>? ExpiredDelegations,
    int WindowDays,
    string EventSourceLagNote);

/// <summary>
/// One employee who cannot register (HRP-015).
/// </summary>
/// <param name="GapSince">
/// The first day of the current uncovered stretch — the day the employee's last agreement-code row
/// stopped covering them. NULL when the gap cannot be dated (the only available anchor is the
/// history-backfill sentinel); reported honestly rather than as an age of two millennia.
/// </param>
/// <param name="DaysSinceGapStart">NULL exactly when <paramref name="GapSince"/> is null.</param>
public sealed record HrCannotRegisterEmployee(
    string EmployeeId,
    string DisplayName,
    string OrgId,
    string? UnitName,
    DateOnly? GapSince,
    int? DaysSinceGapStart);

/// <summary>
/// HRP-015 — employees who CANNOT REGISTER: employed today, with a profile row covering today, but
/// no agreement-code row covering today.
///
/// <para>Such an employee is refused (422) the moment they try to register a pro-rated absence,
/// and today HR is never told — a fail-loud dead end for the employee and silence for the people
/// who could fix it. One row per employee (duplicate gaps suppressed); leavers excluded, since
/// their inability to register is not a data defect.</para>
///
/// <para>No aging colour: no rule states by when a registration-blocking data gap must be fixed
/// (register row HRP-015 is NOT READY). The count and the oldest datable gap are the honest
/// signal.</para>
/// </summary>
/// <param name="Items">Oldest datable gap first; undatable gaps last. NULL when <c>?summary=true</c>.</param>
public sealed record HrCannotRegisterResponse(
    DateOnly Today,
    int Count,
    DateOnly? OldestGapSince,
    IReadOnlyList<HrCannotRegisterEmployee>? Items);
