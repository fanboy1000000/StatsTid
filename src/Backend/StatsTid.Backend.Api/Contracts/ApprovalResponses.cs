using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S116 / TASK-11600 (Fork B retrofit Pass 3, PAT-010/PAT-012) — named response records for the
// approval family (ApprovalEndpoints). Each record is an EXACT shape-copy of the anonymous object
// its handler previously returned: same member NAMES, same ORDER, same nullability — serialized
// camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON.
//
// Enum authorities (OQ-3, owner-ruled S116): period status = the approval_periods status CHECK
// (init.sql:867, superseded by the 5-state re-CHECK at init.sql:1103 — DRAFT / EMPLOYEE_APPROVED /
// SUBMITTED / APPROVED / REJECTED; the team-overview row's synthetic "DRAFT" is a member); period
// type = the approval_periods period_type CHECK (init.sql:866 — WEEKLY / MONTHLY).

/// <summary>The shared <c>{ periodId, status }</c> action receipt — serialized by
/// <c>POST /api/approval/submit</c> (SUBMITTED), <c>POST .../{periodId}/approve</c> (APPROVED),
/// <c>POST .../{periodId}/employee-approve</c> (EMPLOYEE_APPROVED) and
/// <c>POST .../{periodId}/reopen</c> (DRAFT). Sibling handlers emitting the identical shape share
/// ONE record (PAT-012 paved road step 1). Reject is NOT a member — it adds <c>reason</c>
/// (<see cref="PeriodRejectResponse"/>).</summary>
public sealed record PeriodActionResponse(
    Guid PeriodId,
    // Authority: approval_periods status CHECK, init.sql:1103 (5-state).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status);

/// <summary>The <c>POST /api/approval/{periodId}/reject</c> 200 body — the
/// <see cref="PeriodActionResponse"/> shape PLUS the non-null <c>reason</c> echoed from the
/// request (<c>RejectPeriodRequest.Reason</c> is <c>required</c>). A SEPARATE sibling record —
/// sharing an optional member with the action receipt would ADD <c>reason: null</c> to the other
/// four ops' wire (forbidden).</summary>
public sealed record PeriodRejectResponse(
    Guid PeriodId,
    // Authority: approval_periods status CHECK, init.sql:1103 (5-state).
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
    // Authority: approval_periods period_type CHECK, init.sql:866.
    [property: AllowedValues("WEEKLY", "MONTHLY")]
    string PeriodType,
    // Authority: approval_periods status CHECK, init.sql:1103 (5-state).
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
    // Authority: approval_periods period_type CHECK, init.sql:866.
    [property: AllowedValues("WEEKLY", "MONTHLY")]
    string PeriodType,
    // Authority: approval_periods status CHECK, init.sql:1103 (5-state).
    [property: AllowedValues("DRAFT", "SUBMITTED", "EMPLOYEE_APPROVED", "APPROVED", "REJECTED")]
    string Status,
    string AgreementCode,
    string OkVersion,
    DateTime? SubmittedAt,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    string? RejectionReason,
    DateTime CreatedAt);

/// <summary>One row of the <c>GET /api/approval/team-overview</c> envelope — the S87 handler-
/// assembled 18-field Teamoversigt row. <paramref name="PeriodId"/> is null on a zero-period
/// DRAFT row (the roster LEFT JOIN); <paramref name="SubmittedAt"/>/<paramref name="DecisionAt"/>/
/// <paramref name="RejectionReason"/>/<paramref name="PayrollExportedAt"/> are state-dependent
/// nullable scalars. <paramref name="Status"/> includes the SYNTHETIC "DRAFT" the handler emits
/// for zero-period rows — still inside the init.sql:1103 closed set.
/// <para>S124 / TASK-12402: <paramref name="NormRegistered"/>, <paramref name="Overtime"/> and
/// <paramref name="HasWarning"/> are ALSO null on a non-submitted row — the manager-visibility rule
/// (an un-submitted timesheet is not the manager's to read). See their inline note below.</para>
/// </summary>
public sealed record TeamOverviewEmployeeRow(
    Guid? PeriodId,
    string EmployeeId,
    string DisplayName,
    string Agreement,
    // Authority: approval_periods status CHECK, init.sql:1103 (5-state; the zero-period row's
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
    //   • NormExpected  — (weekdays / 5) × the agreement's weekly norm. A CONTRACT fact; no
    //                     projection or registration input. It is the honest denominator that
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
    // FlexBalance is withheld too. It has NO emission site anywhere in src/ today, so it cannot
    // currently carry draft data — but that makes showing it worthless (always 0), and the moment
    // flex gains a registration-time writer it would silently become a leak. Withholding is the
    // fail-safe reading of the ruling and costs nothing.
    decimal? FlexBalance,
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
