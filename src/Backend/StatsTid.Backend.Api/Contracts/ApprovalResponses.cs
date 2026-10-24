using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S116 / TASK-11600 (Fork B retrofit Pass 3, PAT-010/PAT-012) — named response records for the
// approval family (ApprovalEndpoints). Each record is an EXACT shape-copy of the anonymous object
// its handler previously returned: same member NAMES, same ORDER, same nullability — serialized
// camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON.
//
// Enum authorities (OQ-3, owner-ruled S116): period status = the approval_periods status CHECK
// (init.sql:883, superseded by the 5-state re-CHECK at init.sql:1119 — DRAFT / EMPLOYEE_APPROVED /
// SUBMITTED / APPROVED / REJECTED; the team-overview row's synthetic "DRAFT" is a member); period
// type = the approval_periods period_type CHECK (init.sql:882 — WEEKLY / MONTHLY).

/// <summary>The shared <c>{ periodId, status }</c> action receipt — serialized by
/// <c>POST /api/approval/send</c> and <c>POST .../{periodId}/employee-approve</c> (both
/// EMPLOYEE_APPROVED — the two adapters over the S127 send command),
/// <c>POST .../{periodId}/approve</c> (APPROVED) and <c>POST .../{periodId}/reopen</c> (DRAFT).
/// Sibling handlers emitting the identical shape share ONE record (PAT-012 paved road step 1).
/// Reject is NOT a member — it adds <c>reason</c> (<see cref="PeriodRejectResponse"/>).
/// <para>S127 / TASK-12703: <c>POST /api/approval/submit</c> is RETIRED, so no production route
/// serializes <c>"SUBMITTED"</c> into this record any more. The literal stays in the
/// <c>[AllowedValues]</c> set below because it stays a legal <c>approval_periods.status</c> value —
/// the enum's authority is the DB CHECK, not the set of statuses a handler happens to emit — and the
/// 138 legacy rows carrying it are still served by every read op that shares this vocabulary.</para>
/// </summary>
public sealed record PeriodActionResponse(
    Guid PeriodId,
    // Authority: approval_periods status CHECK, init.sql:1119 (5-state).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status);

/// <summary>The <c>POST /api/approval/{periodId}/reject</c> 200 body — the
/// <see cref="PeriodActionResponse"/> shape PLUS the non-null <c>reason</c> echoed from the
/// request (<c>RejectPeriodRequest.Reason</c> is <c>required</c>). A SEPARATE sibling record —
/// sharing an optional member with the action receipt would ADD <c>reason: null</c> to the other
/// four ops' wire (forbidden).</summary>
public sealed record PeriodRejectResponse(
    Guid PeriodId,
    // Authority: approval_periods status CHECK, init.sql:1119 (5-state).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status,
    string Reason);

/// <summary>One element of the BARE-ARRAY responses of <c>GET /api/approval/pending</c> and
/// <c>GET /api/approval/by-month</c> (declared
/// <c>.Produces&lt;IEnumerable&lt;ApprovalPeriodListItem&gt;&gt;</c>). The two ops' elements are
/// byte-identical (9 fields), and each op has TWO return sites (the my-reports branch and the
/// scope-aggregate branch) — ALL FOUR sites serialize this ONE record.
/// <paramref name="SubmittedAt"/> is null for a not-yet-submitted period.</summary>
public sealed record ApprovalPeriodListItem(
    Guid PeriodId,
    string EmployeeId,
    string OrgId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    // Authority: approval_periods period_type CHECK, init.sql:882.
    [property: AllowedValues("WEEKLY", "MONTHLY")]
    string PeriodType,
    // Authority: approval_periods status CHECK, init.sql:1119 (5-state).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status,
    DateTime? SubmittedAt,
    string AgreementCode);

/// <summary>One element of the <c>GET /api/approval/{employeeId}</c> BARE ARRAY (declared
/// <c>.Produces&lt;IEnumerable&lt;EmployeePeriodItem&gt;&gt;</c>) — the WIDER 14-field per-employee
/// period row (NOT shared with <see cref="ApprovalPeriodListItem"/>: it adds okVersion /
/// approvedBy / approvedAt / rejectionReason / createdAt and orders agreementCode differently).
/// The nullable scalars mirror the <c>ApprovalPeriod</c> model members the handler projects.</summary>
public sealed record EmployeePeriodItem(
    Guid PeriodId,
    string EmployeeId,
    string OrgId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    // Authority: approval_periods period_type CHECK, init.sql:882.
    [property: AllowedValues("WEEKLY", "MONTHLY")]
    string PeriodType,
    // Authority: approval_periods status CHECK, init.sql:1119 (5-state).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status,
    string AgreementCode,
    string OkVersion,
    DateTime? SubmittedAt,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    string? RejectionReason,
    DateTime CreatedAt);

/// <summary>
/// S146 / TASK-14607 (owner ruling OQ-5, plan ruling R-33 item 5) — the closed set of
/// <see cref="TeamOverviewEmployeeRow.FlexBalanceState"/>: WHICH state the row's flex column is in,
/// so a client never has to read meaning into a null. Two of the three states carry a null
/// balance, and they mean different things to a leader ("not yours to see yet" vs. "nothing
/// settled yet"), so the null alone cannot tell them apart.
/// </summary>
public static class TeamOverviewFlexBalanceState
{
    /// <summary>The month is not submitted (S124 rule, OQ-5): balance and month are null.</summary>
    public const string Withheld = "withheld";

    /// <summary>Submitted, but the employee has no recorded (settled) month yet — "ingen afregnet
    /// måned": balance and month are null.</summary>
    public const string None = "none";

    /// <summary>Submitted, and the balance is the latest recorded closing, the month its
    /// <c>"YYYY-MM"</c>: both non-null.</summary>
    public const string Recorded = "recorded";
}

/// <summary>One row of the <c>GET /api/approval/team-overview</c> envelope — the S87 handler-
/// assembled Teamoversigt row (18 fields; 20 since S146 added <paramref name="FlexBalanceMonth"/>
/// and <paramref name="FlexBalanceState"/>). <paramref name="PeriodId"/> is null on a zero-period
/// DRAFT row (the roster LEFT JOIN); <paramref name="SubmittedAt"/>/<paramref name="DecisionAt"/>/
/// <paramref name="RejectionReason"/>/<paramref name="PayrollExportedAt"/> are state-dependent
/// nullable scalars. <paramref name="Status"/> includes the SYNTHETIC "DRAFT" the handler emits
/// for zero-period rows — still inside the init.sql:1119 closed set.
/// <para>S124 / TASK-12402: <paramref name="NormRegistered"/>, <paramref name="Overtime"/> and
/// <paramref name="HasWarning"/> are ALSO null on a non-submitted row — the manager-visibility rule
/// (an un-submitted timesheet is not the manager's to read). See their inline note below.</para>
/// <para>S146 / TASK-14607 (OQ-5): <paramref name="FlexBalance"/> and
/// <paramref name="FlexBalanceMonth"/> are null on a non-submitted row too, and on a submitted row
/// with no recorded month; <paramref name="FlexBalanceState"/> says which (see the inline note).</para>
/// </summary>
public sealed record TeamOverviewEmployeeRow(
    Guid? PeriodId,
    string EmployeeId,
    string DisplayName,
    string Agreement,
    // Authority: approval_periods status CHECK, init.sql:1119 (5-state; the zero-period row's
    // synthetic "DRAFT" is a set member).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status,
    DateTime? SubmittedAt,
    DateTime? DecisionAt,
    string? RejectionReason,
    decimal NormExpected,
    // ── S124 / TASK-12402 — THE WITHHELD SET ────────────────────────────────────────────────
    // NULL until the employee SENDS the month. Owner ruling (2026-07-30): "a manager cannot see
    // anything before a month is submitted", so every figure that can carry the employee's own
    // registrations is withheld — not just the obvious hours.
    //
    // NULLABLE rather than 0: "registered 0,0 t" / "0 feriedage brugt" would be a LIE about
    // someone who may have registered 140 t or booked a week off, and a contract that asserts
    // something false is the exact failure class PAT-012 exists to prevent.
    //
    // What deliberately STAYS visible, and why it cannot leak the month:
    //   • NormExpected  — weekdays × the employee's own per-day norm (agreement weekly norm ×
    //                     part-time fraction at month end ÷ 5) (S146 / QUAL-198). A CONTRACT fact;
    //                     no projection or registration input. It is the honest denominator that
    //                     makes "— / 155,4 t" readable.
    //   • FerieTotal    — entitlement_balances.total_quota, the standing annual quota. Only
    //                     `used` moves when the employee registers absence, so only `used` is
    //                     withheld; the quota is the denominator, same shape as NormExpected.
    //   • AwayToday     — is this person absent TODAY. A present-tense operational fact, and the
    //                     reason the row is kept at all (the leader must staff the day and chase
    //                     the missing submission before the lederfrist).
    // Non-withheld rows are UNCHANGED: a manager must keep seeing everything about what they are
    // asked to approve, and everything they already decided on.
    decimal? NormRegistered,
    // FlexBalance (with its FlexBalanceMonth) is withheld too — by the owner's STRICT reading of
    // the S124 ruling (S146 OQ-5, 2026-10-23). Since S146 the figure is the employee's latest
    // RECORDED flex closing: the balance on the newest export record that carries one
    // (payroll_export_records, written only by the Payroll service, read here read-only per
    // ADR-034 D4), labelled with that record's month. That figure is a payroll fact, not the
    // employee's in-progress registrations — so withholding it is not about a leak. It is withheld
    // because "a manager cannot see anything before a month is submitted" is read as NOTHING on
    // the row's figure columns, not merely nothing that moves with the draft. (Before S146 the
    // reason given here was that the column had no writer at all and always read 0; S146 gave it
    // one, so that premise no longer holds.)
    //
    // FlexBalanceState tells the client WHICH null it is looking at — a closed set
    // (TeamOverviewFlexBalanceState), never an overloaded null:
    //   withheld — the month is not submitted: FlexBalance and FlexBalanceMonth are null;
    //   none     — submitted, but no recorded month yet ("ingen afregnet måned"): both null;
    //   recorded — submitted: FlexBalance is the latest recorded closing and FlexBalanceMonth its
    //              "YYYY-MM" (which may be any earlier settled month, not necessarily this one).
    decimal? FlexBalance,
    string? FlexBalanceMonth,
    // Authority: S146 owner ruling OQ-5 + plan ruling R-33 item 5 (a handler-derived closed set;
    // no DB column) — the constants in TeamOverviewFlexBalanceState.
    [property: AllowedValues(
        TeamOverviewFlexBalanceState.Withheld,
        TeamOverviewFlexBalanceState.None,
        TeamOverviewFlexBalanceState.Recorded)]
    string FlexBalanceState,
    decimal? Overtime,
    // entitlement_balances.used is incremented INSIDE the employee's own Skema save transaction
    // (SkemaEndpoints.cs → EntitlementBalanceRepository.CheckAndAdjustAsync) with NO approval
    // gate — so a drafted vacation day moved the manager's Ferie column. That was a real leak.
    decimal? FerieUsed,
    decimal FerieTotal,
    bool AwayToday,
    bool? HasWarning,
    bool PayrollExported,
    DateTime? PayrollExportedAt);

/// <summary>The <c>GET /api/approval/team-overview</c> 200 envelope — <c>{ employees: [...] }</c>
/// (NOT a bare array). Serialized by BOTH return sites: the empty-roster early return
/// (<c>employees: []</c>) and the assembled roster.</summary>
public sealed record TeamOverviewResponse(
    IReadOnlyList<TeamOverviewEmployeeRow> Employees);

/// <summary>One element of <see cref="AllocationBreakdownResponse.Allocations"/> — the month-sum
/// NORMAL + non-null-TaskId hours for one task.</summary>
public sealed record AllocationBreakdownItem(
    string TaskId,
    decimal Hours);

/// <summary>The <c>GET /api/approval/{employeeId}/allocation-breakdown</c> 200 envelope — the S88
/// per-employee Fordeling detail the team-overview row lazy-fetches on expand.</summary>
public sealed record AllocationBreakdownResponse(
    IReadOnlyList<AllocationBreakdownItem> Allocations,
    decimal Worked,
    decimal Allocated,
    decimal UnderAllocated,
    decimal OverAllocated,
    bool HasAllocationImbalance);
