namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// S145 / TASK-14505 — the calendar of week-by-week settlement: which Monday–Sunday weeks a
/// pay period settles, on which day, and whether an agreement change splits one of them.
///
/// <para>
/// <b>The rule (owner ruling Q3 = F1, 2026-10-01).</b> Weeks run Monday–Sunday (refinement
/// Assumption 1). A week is settled by the period that contains its <i>settlement day</i>: the
/// week's Sunday, or — for a leaver, or a legacy call whose period ends mid-week — the
/// <i>cutoff</i> (the last day evaluated) when that falls inside the week. All of a week's
/// overtime is dated on its settlement day, and a period pays only what is dated inside it, so
/// a week that crosses a month boundary is paid once, by the month holding its Sunday. The
/// days of a settled week that fall before the period are its <i>context days</i>: they count
/// toward that week's total but belong to the previous month for every other figure.
/// </para>
///
/// <para>
/// Pure functions of their arguments (ADR-002): no I/O, no clock. Shared by the Rule Engine's
/// weekly core and payroll's calculability check (TASK-14512), so the two always agree on what
/// a "settled week" is.
/// </para>
/// </summary>
public static class WeeklyContext
{
    /// <summary>The Monday of the Monday–Sunday week containing <paramref name="d"/>.</summary>
    public static DateOnly WeekStart(DateOnly d)
    {
        // DayOfWeek: Sunday = 0 … Saturday = 6. Days since Monday: Monday 0 … Sunday 6.
        var daysSinceMonday = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-daysSinceMonday);
    }

    /// <summary>The Sunday of the Monday–Sunday week containing <paramref name="d"/>.</summary>
    public static DateOnly WeekEnd(DateOnly d) => WeekStart(d).AddDays(6);

    /// <summary>
    /// The day the week containing <paramref name="weekStart"/> is settled and dated: its Sunday,
    /// or <paramref name="cutoff"/> when the cutoff falls inside that Monday–Sunday week.
    /// <paramref name="weekStart"/> is normalised to its Monday, so any day of the week may be passed.
    /// </summary>
    public static DateOnly SettlementDay(DateOnly weekStart, DateOnly? cutoff)
    {
        var monday = WeekStart(weekStart);
        var sunday = monday.AddDays(6);
        if (cutoff is { } c && c >= monday && c <= sunday)
            return c;
        return sunday;
    }

    /// <summary>
    /// The Monday–Sunday weeks whose settlement day (<see cref="SettlementDay"/>) lies in
    /// <c>[periodStart, periodEnd]</c>, in calendar order. Each tuple is the full calendar week
    /// (<c>Start</c> = Monday, <c>End</c> = Sunday), so a week's context days before
    /// <paramref name="periodStart"/> are inside it. Empty when the period is inverted.
    /// </summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> SettledWeeks(
        DateOnly periodStart, DateOnly periodEnd, DateOnly? cutoff)
    {
        var weeks = new List<(DateOnly Start, DateOnly End)>();
        if (periodEnd < periodStart)
            return weeks;

        // A week's settlement day lies inside the week, so only weeks that touch the period can
        // qualify: from the week holding periodStart to the week holding periodEnd.
        for (var monday = WeekStart(periodStart); monday <= periodEnd; monday = monday.AddDays(7))
        {
            var settlementDay = SettlementDay(monday, cutoff);
            if (settlementDay >= periodStart && settlementDay <= periodEnd)
                weeks.Add((monday, monday.AddDays(6)));
        }

        return weeks;
    }

    /// <summary>
    /// True when any date in <paramref name="agreementChangeDates"/> falls on a day other than
    /// a Monday inside any of <paramref name="settledWeeks"/> (bounds inclusive, so context days
    /// before the period count). A change effective on a Monday starts a clean week and is
    /// never a mid-week change. This is the Q5 (c) predicate: a week paid under two agreements
    /// (HK tiered overtime vs AC merarbejde) has no source rule, so such a month is refused.
    ///
    /// <para>
    /// <b>Contract (R43): callers filter hire-dated rows.</b> The predicate is date-only and pure:
    /// it cannot know that a date is an employee's first agreement row (a hire, not a change).
    /// The caller drops any date whose previous day was not employed before calling — payroll's
    /// calculation service does this (TASK-14512 Do 1). A hire on a Thursday is therefore never
    /// a "mid-week agreement change".
    /// </para>
    /// </summary>
    public static bool MidWeekAgreementChange(
        IReadOnlyList<(DateOnly Start, DateOnly End)> settledWeeks,
        IReadOnlyList<DateOnly> agreementChangeDates)
    {
        ArgumentNullException.ThrowIfNull(settledWeeks);
        ArgumentNullException.ThrowIfNull(agreementChangeDates);

        foreach (var change in agreementChangeDates)
        {
            if (change.DayOfWeek == DayOfWeek.Monday)
                continue;

            foreach (var (start, end) in settledWeeks)
            {
                if (change >= start && change <= end)
                    return true;
            }
        }

        return false;
    }
}
