using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S145 / TASK-14503a (A1, QUAL-186) — the "month treated as one week" defect, pinned through the real
/// <see cref="RuleRegistry"/> dispatch. <b>These four facts are RED on purpose</b> until the week-by-week
/// core (TASK-14505, R8) has taken over the legacy dispatch; TASK-14506 re-spawn 1 takes this file
/// UNCHANGED (no value edited) and verifies it green. Authored on TASK-14503a's branch and excluded from
/// that task's merge (R49): a RED Unit file cannot pass the O-1 gate.
///
/// <para>
/// <b>The story, in plain language.</b> A full-time HK employee works exactly their day norm, 7.4 h, every
/// weekday of September 2026 (22 weekdays, no public holiday: 22 x 7.40 = 162.80 h). Nobody worked a minute of
/// overtime. Today the rules take the whole month as if it were ONE week: they compare 162.80 h with the
/// weekly norm of 37 h, and book 125.8 h of "overtime" (HK), 125.8 h of merarbejde (AC), a +125.8 h flex
/// delta and a 25.8 h flex payout. The correct answer is week by week (Monday to Sunday), and every one of
/// these weeks is exactly at norm, so the correct answer for the whole month is: nothing extra.
/// </para>
///
/// <para>
/// <b>Why the legacy dispatch and why no 31 August.</b> The legacy route (<c>/api/rules/evaluate</c>,
/// <c>/evaluate-flex</c>) has no context days (refinement A2: "the request's entries are the only facts;
/// there is no context read"). Its period is the <i>settlement window</i>: a partial opening week
/// (Tue 1 - Sun 6 September) is judged on its in-period days only (norm 29.60, not 37), and the closing partial
/// week (Mon 28 - Wed 30 September) on its three days (norm 22.20), settled on the period's last day. At 7.4 h
/// per weekday every one of those weeks is exactly at its norm. So the entries here start on 1 September.
/// </para>
///
/// <para>
/// <b>Spec sources (not read off the code).</b> Refinement A1 and Stage A "Defect pinned"
/// (REFINEMENT-s145 :225-230, :581): "Before: 3.0 / 122.8 / 37 / +125.8 / payout 25.8. After: no overtime,
/// NormHoursTotal 162.80, flex delta 0, no payout. The AC twin: before 125.8 merarbejde, after 0." Window
/// contract R6 (:243-251): <c>NormHoursTotal</c> = the month's own EMPLOYED days, September 2026 full-time
/// 22 x 7.40 = 162.80. HK tiers (danish-agreements.md): norm 37, OVERTIME_50 37-40, OVERTIME_100 above 40;
/// AC has merarbejde and no overtime.
/// </para>
///
/// <para>
/// <b>Observed RED on today's master (cdd8253), recorded in TASK-14503a's report:</b>
/// OVERTIME_50 = 3.0 and OVERTIME_100 = 122.8 (HK); NormHoursTotal = 37 (Deviation +125.8); flex Delta = +125.8,
/// NewBalance = 100 and ExcessForPayout (FLEX_PAYOUT) = 25.8; MERARBEJDE = 125.8 (AC).
/// </para>
///
/// <para>
/// <b>Surface used.</b> Only <see cref="RuleRegistry.Evaluate(string, EmploymentProfile, IReadOnlyList{TimeEntry}, DateOnly, DateOnly)"/>
/// and <see cref="RuleRegistry.EvaluateFlexBalance"/>, which survive R8 unchanged in signature. Rule ids are
/// string literals, not the rule classes' constants, so the file does not name a member R8 may retire.
/// </para>
///
/// Red conditions (Unit; compiles against today's code, so RED is observed by running it before TASK-14505
/// exists, docs/AGENTS.md RED evidence (a)): each fact fails today on the figure named in its own docstring.
/// After the core merges (14506 re-spawn 1) each is green, and a return to "the month is one week" trips the
/// same assertions.
/// </summary>
public class MonthAsWeekCharacterizationTests
{
    private const string OvertimeRuleId = "OVERTIME_CALC";
    private const string NormRuleId = "NORM_CHECK_37H";

    // September 2026: Tuesday 1 September .. Wednesday 30 September. 22 weekdays, no public holiday.
    private static readonly DateOnly PeriodStart = new(2026, 9, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 9, 30);

    private static EmploymentProfile Profile(string agreementCode) => new()
    {
        EmployeeId = "EMP-MONTH-AS-WEEK",
        AgreementCode = agreementCode,
        OkVersion = "OK26",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
    };

    /// <summary>7.4 h (the full-time day norm, 37 / 5) on every weekday 1-30 September 2026: 22 entries.</summary>
    private static List<TimeEntry> SeptemberAtNorm(string agreementCode)
    {
        var entries = new List<TimeEntry>();
        for (var d = PeriodStart; d <= PeriodEnd; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            entries.Add(new TimeEntry
            {
                EmployeeId = "EMP-MONTH-AS-WEEK",
                Date = d,
                Hours = 7.4m,
                AgreementCode = agreementCode,
                OkVersion = "OK26",
            });
        }
        return entries;
    }

    private static decimal HoursOf(CalculationResult result, string timeType) =>
        result.LineItems.Where(li => li.TimeType == timeType).Sum(li => li.Hours);

    /// <summary>
    /// Full-time HK, 7.4 h every weekday of September: no overtime at all.
    ///
    /// <para>Before (today): OVERTIME_50 = 3.0 and OVERTIME_100 = 122.8 — the month's 162.80 h judged as one
    /// 37 h week (excess 125.8: the first 3 h at 50 %, everything above 40 h at 100 %).</para>
    /// </summary>
    [Fact]
    public void September2026_FullTimeHK_7_4PerWeekday_NoOvertime()
    {
        var entries = SeptemberAtNorm("HK");
        Assert.Equal(22, entries.Count);
        Assert.Equal(162.80m, entries.Sum(e => e.Hours));

        var result = new RuleRegistry().Evaluate(OvertimeRuleId, Profile("HK"), entries, PeriodStart, PeriodEnd);

        Assert.True(result.Success, $"OVERTIME_CALC failed: {result.ErrorMessage}");
        Assert.Multiple(
            () => Assert.Equal(0m, HoursOf(result, "OVERTIME_50")),
            () => Assert.Equal(0m, HoursOf(result, "OVERTIME_100")),
            () => Assert.Empty(result.LineItems));
    }

    /// <summary>
    /// Full-time HK, 7.4 h every weekday of September: the month's norm is the sum of its own days' norms,
    /// 22 x 7.40 = 162.80 h (window contract R6), so the deviation is zero.
    ///
    /// <para>Before (today): NormHoursTotal = 37 (one weekly norm for the whole month), Deviation = +125.8.</para>
    /// </summary>
    [Fact]
    public void September2026_FullTimeHK_NormHoursTotal_16280()
    {
        var result = new RuleRegistry().Evaluate(NormRuleId, Profile("HK"), SeptemberAtNorm("HK"), PeriodStart, PeriodEnd);

        Assert.True(result.Success, $"NORM_CHECK_37H failed: {result.ErrorMessage}");
        Assert.Multiple(
            () => Assert.Equal(162.80m, result.NormHoursTotal),
            () => Assert.Equal(162.80m, result.ActualHoursTotal),
            () => Assert.Equal(0m, result.Deviation));
    }

    /// <summary>
    /// Full-time HK, 7.4 h every weekday of September, opening flex balance 0: the flex delta is 0 and nothing
    /// is paid out. Worked hours equal the norm, so the balance stays at 0.
    ///
    /// <para>Before (today): Delta = +125.8 (162.80 worked - 37 norm), NewBalance = 100 (clamped at the HK cap)
    /// and ExcessForPayout = 25.8 — the amount the endpoint turns into a FLEX_PAYOUT line.</para>
    /// </summary>
    [Fact]
    public void September2026_FullTimeHK_FlexDeltaZero_NoPayout()
    {
        var result = new RuleRegistry().EvaluateFlexBalance(
            Profile("HK"), SeptemberAtNorm("HK"), Array.Empty<AbsenceEntry>(), PeriodStart, PeriodEnd, previousBalance: 0m);

        Assert.True(result.Success);
        Assert.Multiple(
            () => Assert.Equal(0m, result.Delta),
            () => Assert.Equal(0m, result.NewBalance),
            () => Assert.Equal(0m, result.ExcessForPayout));
    }

    /// <summary>
    /// Full-time AC, 7.4 h every weekday of September: no merarbejde (AC has merarbejde, not overtime).
    ///
    /// <para>Before (today): MERARBEJDE = 125.8 — the month's 162.80 h judged as one 37 h week.</para>
    /// </summary>
    [Fact]
    public void September2026_FullTimeAC_NoMerarbejde()
    {
        var result = new RuleRegistry().Evaluate(OvertimeRuleId, Profile("AC"), SeptemberAtNorm("AC"), PeriodStart, PeriodEnd);

        Assert.True(result.Success, $"OVERTIME_CALC failed: {result.ErrorMessage}");
        Assert.Multiple(
            () => Assert.Equal(0m, HoursOf(result, "MERARBEJDE")),
            () => Assert.Empty(result.LineItems));
    }
}
