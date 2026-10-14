namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14512 (rulings R5(b), R16, R21, R40) — "calculate the months in order". A month whose
/// opening Monday–Sunday week holds EMPLOYED days of the previous month (M−1) is calculated only when
/// M−1 is APPROVED; otherwise this is thrown, carrying M−1's pay period.
///
/// <para>
/// <b>Plain language.</b> October's first week starts on Monday 28 September, and October pays that
/// week's overtime. Until September is approved its hours can still change, so October would be paid
/// on unsigned hours. The operator can fix this (approve September), so the refusal names September.
/// <c>EMPLOYEE_APPROVED</c> is not enough (the manager has not decided), and a month with no approval
/// row at all (for example one exported through the raw routes, R16) is not approved either.
/// </para>
///
/// <para>
/// <b>Where it is thrown.</b> By the calculation service's admission step, after the planner and the
/// week check and before any rule is evaluated (refusal order N1); and by the export's transaction when
/// its row-locked recheck of M−1 finds the approval withdrawn by a concurrent reopen (TASK-14512 Do 7).
/// Either way nothing has been written. The host maps it to a 409
/// <c>{ success:false, error, kind:"payroll-prior-month-not-approved", periodStart, periodEnd }</c>
/// whose period is M−1's: a pay period, never an employment date. <see cref="Exception.Message"/> is for
/// diagnosis and never reaches a response.
/// </para>
/// </summary>
public sealed class PriorMonthNotApprovedException : Exception
{
    /// <summary>The discriminator of the host's 409 body.</summary>
    public const string KindValue = "payroll-prior-month-not-approved";

    /// <summary>
    /// The fixed sentence of the host's 409 body. It contains no date, no id and no digit, so it can never
    /// leak one; the previous month's pay period travels in the body's <c>periodStart</c> / <c>periodEnd</c>.
    /// </summary>
    public const string ErrorSentence =
        "The previous month must be approved before this month can be calculated: this month's first week " +
        "includes working days of the previous month, and their hours are settled with this month. Approve " +
        "the previous month named by periodStart and periodEnd, then try again. Nothing was calculated, " +
        "written or exported.";

    public PriorMonthNotApprovedException(DateOnly periodStart, DateOnly periodEnd, string message)
        : base(message)
    {
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }

    /// <summary>The first day of the previous month's pay period (M−1).</summary>
    public DateOnly PeriodStart { get; }

    /// <summary>The last day of the previous month's pay period (M−1).</summary>
    public DateOnly PeriodEnd { get; }
}
