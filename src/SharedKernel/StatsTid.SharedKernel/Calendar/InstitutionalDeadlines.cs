namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// The two deadlines a monthly time-registration period carries — the single source of truth for
/// "when should this month have been sent, and when should it have been approved?".
///
/// <para><b>Plain language.</b> An employee registers a month and SENDS it; their leader then
/// APPROVES it; only an approved month may go to payroll (SYSTEM_TARGET §H). Each period row is
/// stamped with two dates when it is created: <c>employee_deadline</c> = the month's last day plus
/// <see cref="SubmitDays"/> days, and <c>manager_deadline</c> = the month's last day plus
/// <see cref="ApproveDays"/> days. Before S140 those offsets were hard-coded literally in two
/// unrelated endpoint files — the Skema month read (period creation) and the send flow (the only
/// production writer of period rows) — which meant nothing in the system could state the rule, and
/// the organisation page's "efter frist" ("past deadline") tile therefore counted every PENDING
/// month rather than the late ones. This type is that rule, named once.</para>
///
/// <para><b>PROVISIONAL institutional defaults — awaiting per-institution configuration.</b> The
/// owner ratified +2 / +5 on 2026-09-08 as the institutional defaults, explicitly provisionally:
/// <c>SYSTEM_TARGET.md</c> § G "Local Configuration" (item 5) names approval flows and <em>cutoff
/// dates</em> as operational configuration each institution sets, and that configuration surface
/// does not exist yet. S140 deliberately builds NO configuration here — it makes the two values
/// one named, testable constant so that when the per-institution surface lands there is exactly one
/// place to route it through, instead of a grep for the literal offsets. The ratification is also
/// what makes the derived reads honest: the export cutoff for an approved month is the LEADER's
/// deadline (owner ruling, same date), and a period row created before the deadline columns existed
/// gets these defaults COMPUTED and flagged as computed rather than silently treated as on time.
/// The derivation is month-end + 2 (<c>monthEnd.AddDays(2)</c>) and month-end + 5
/// (<c>monthEnd.AddDays(5)</c>); those two expressions appear NOWHERE else in <c>src/</c>.</para>
///
/// <para>It lives in SharedKernel next to <see cref="CopenhagenBusinessDate"/> and
/// <see cref="OkVersionResolver"/> for the same reason they do: BOTH the Backend.Api write paths
/// (which stamp the columns) and the Infrastructure read repositories (which compute the fallback
/// for rows whose columns are NULL) must reach it, and Infrastructure cannot reference Backend.Api
/// (PAT-005 / the dependency rules). It is dependency-free and pure — no clock, no I/O — because
/// the deadlines are a function of the MONTH, not of today.</para>
/// </summary>
public static class InstitutionalDeadlines
{
    /// <summary>
    /// Days after the month's last day by which the EMPLOYEE should have sent the month
    /// (<c>approval_periods.employee_deadline</c>). Provisional institutional default — see the
    /// type summary.
    /// </summary>
    public const int SubmitDays = 2;

    /// <summary>
    /// Days after the month's last day by which the LEADER should have approved the month
    /// (<c>approval_periods.manager_deadline</c>). Also the ratified payroll EXPORT CUTOFF for an
    /// approved month (owner ruling 2026-09-08). Provisional institutional default — see the type
    /// summary.
    /// </summary>
    public const int ApproveDays = 5;

    /// <summary>
    /// The employee's send deadline for the month whose LAST DAY is <paramref name="monthEnd"/>.
    /// </summary>
    public static DateOnly EmployeeDeadlineFor(DateOnly monthEnd) => monthEnd.AddDays(SubmitDays);

    /// <summary>
    /// The leader's approval deadline for the month whose LAST DAY is <paramref name="monthEnd"/>
    /// — and, per the owner's 2026-09-08 ruling, the payroll export cutoff for that month.
    /// </summary>
    public static DateOnly ManagerDeadlineFor(DateOnly monthEnd) => monthEnd.AddDays(ApproveDays);

    /// <summary>
    /// Both deadlines for the month whose LAST DAY is <paramref name="monthEnd"/> — the shape the
    /// period-creation sites want, so a caller cannot stamp one deadline from this rule and the
    /// other from somewhere else.
    /// </summary>
    public static (DateOnly EmployeeDeadline, DateOnly ManagerDeadline) ForMonthEnd(DateOnly monthEnd)
        => (EmployeeDeadlineFor(monthEnd), ManagerDeadlineFor(monthEnd));
}
