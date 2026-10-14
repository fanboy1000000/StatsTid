using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S145 / TASK-14506 — the LEGACY single-period entry points (<c>RuleRegistry.Evaluate</c>, behind
/// <c>POST /api/rules/evaluate</c>) now run on the week-by-week core (R8, R34) — one arithmetic path,
/// not two.
///
/// <para>
/// <b>Plain language.</b> A legacy call hands the engine one period and the hours for it, with no
/// "context" before the period. The core treats that period as the <i>settlement window</i>: it settles
/// the weeks whose settlement day (the week's Sunday, or the period's LAST day when the period ends
/// mid-week) lies inside it, and it judges a partial week on the in-period days only. So a call for
/// 1–30 September is no longer "37 hours for the whole month": its opening partial week, Tuesday 1 –
/// Sunday 6 September, has four weekdays and a norm of 4 × 7.40 = <b>29.60</b>, not 37. That is a
/// labelled, intentional behaviour change (refinement :626).
/// </para>
///
/// <para>
/// R34: every norm id goes through the core, but the three decomposed ids must keep their meaning even
/// when the employee's own config says otherwise. <c>NORM_CHECK_WEEKLY</c> always judges a weekly norm
/// (reports <c>NormPeriodWeeks</c> 1) even on a four-week config; <c>NORM_CHECK_ANNUAL</c> always uses
/// days ÷ 365 even on a weekly config; each result keeps its OWN rule id. <c>NORM_CHECK_37H</c> alone
/// follows the config.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate): MU-20 judges the legacy opening partial week
/// against a full 37 h norm and trips the 29.60 fact (and its closing-week sibling); MU-21 dates legacy
/// lines on the period's first day and trips both date assertions; MU-37 makes the legacy dispatch pass
/// <c>ForcedNormMode = null</c> for every norm id (R34) and trips both forced-mode facts.
/// </para>
/// </summary>
public sealed class LegacyDispatchOnCoreTests
{
    private const string Emp = "EMP-LEGACY";

    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep4 = new(2026, 9, 4);
    private static readonly DateOnly Sep6 = new(2026, 9, 6);
    private static readonly DateOnly Sep7 = new(2026, 9, 7);
    private static readonly DateOnly Sep13 = new(2026, 9, 13);
    private static readonly DateOnly Sep28 = new(2026, 9, 28);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    private static EmploymentProfile Profile(string agreement, string? position = null) => new()
    {
        EmployeeId = Emp,
        AgreementCode = agreement,
        OkVersion = "OK26",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        Position = position,
    };

    private static List<TimeEntry> WeekdayHours(
        string agreement, DateOnly from, DateOnly to, decimal perDay, params (DateOnly From, DateOnly To, decimal Hours)[] bands)
    {
        var entries = new List<TimeEntry>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var current = day;
            var hours = bands.Any(b => current >= b.From && current <= b.To)
                ? bands.First(b => current >= b.From && current <= b.To).Hours
                : perDay;
            entries.Add(new TimeEntry
            {
                EmployeeId = Emp,
                Date = day,
                Hours = hours,
                AgreementCode = agreement,
                OkVersion = "OK26",
            });
        }
        return entries;
    }

    /// <summary>
    /// A NON-WEEK-ALIGNED September call (1–30 September 2026, a Tuesday to a Wednesday), full-time HK.
    /// The opening partial week 1–6 September has four employed weekdays: norm 4 × 7.40 = 29.60, 50%
    /// threshold 29.60, 100% threshold 4 × 8.00 = 32.00. Worked 8 h on each of the four = 32.00 h →
    /// excess 2.40, all inside the 50% band → OVERTIME_50 2.40, dated Sunday 6 September (the week's
    /// settlement day), and nothing else — every other week is exactly at its norm. Judged against 37 h
    /// (MU-20) nothing would be paid; dated on the period's first day (MU-21) it would be 1 September.
    /// </summary>
    [Fact]
    public void Legacy_Evaluate_OvertimeCalc_NonAlignedSeptember_OpeningPartialWeekNorm2960()
    {
        var entries = WeekdayHours("HK", Sep1, Sep30, 7.40m, (Sep1, Sep4, 8.00m));

        var result = new RuleRegistry().Evaluate("OVERTIME_CALC", Profile("HK"), entries, Sep1, Sep30);

        Assert.True(result.Success);
        Assert.Equal("OVERTIME_CALC", result.RuleId);
        var line = Assert.Single(result.LineItems);
        Assert.Equal("OVERTIME_50", line.TimeType);
        Assert.Equal(2.40m, line.Hours);
        Assert.Equal(1.5m, line.Rate);
        Assert.Equal(Sep6, line.Date);
    }

    /// <summary>
    /// The closing partial week of the same call: the period ends on Wednesday 30 September, so the week
    /// 28 Sep – 4 Oct is settled ON THE PERIOD'S LAST DAY (refinement :268), judged on its three in-period
    /// weekdays only: norm 3 × 7.40 = 22.20, 100% threshold 24.00. Worked 8 h on each = 24.00 h → excess
    /// 1.80 → OVERTIME_50 1.80, dated 30 September. (Together with the opening week: two lines, 6 and 30
    /// September.) RED by MU-21 (dated on the period's first day) and by any change that drops the
    /// closing partial week.
    /// </summary>
    [Fact]
    public void Legacy_Evaluate_OvertimeCalc_NonAlignedSeptember_ClosingPartialWeekSettledOnPeriodEnd_Dated30Sep()
    {
        var entries = WeekdayHours("HK", Sep1, Sep30, 7.40m, (Sep1, Sep4, 8.00m), (Sep28, Sep30, 8.00m));

        var result = new RuleRegistry().Evaluate("OVERTIME_CALC", Profile("HK"), entries, Sep1, Sep30);

        Assert.True(result.Success);
        var lines = result.LineItems.OrderBy(l => l.Date).ToList();
        Assert.Equal(2, lines.Count);

        Assert.Equal(("OVERTIME_50", 2.40m, 1.5m, Sep6), (lines[0].TimeType, lines[0].Hours, lines[0].Rate, lines[0].Date));
        Assert.Equal(("OVERTIME_50", 1.80m, 1.5m, Sep30), (lines[1].TimeType, lines[1].Hours, lines[1].Rate, lines[1].Date));
    }

    /// <summary>
    /// R34 — a <c>NormPeriodWeeks</c> = 4 config (AC DEPARTMENT_HEAD) called as <c>NORM_CHECK_WEEKLY</c>
    /// is forced into weekly mode: it reports <c>NormPeriodWeeks</c> 1 and tags the result
    /// <c>NORM_CHECK_WEEKLY</c>. Called as <c>NORM_CHECK_MULTIWEEK</c> it reports the config's 4 and
    /// tags <c>NORM_CHECK_MULTIWEEK</c>; the legacy <c>NORM_CHECK_37H</c> alone follows the config (4,
    /// tagged <c>NORM_CHECK_37H</c>). One Monday–Sunday week of 7.40 h weekdays, so the weekly total is
    /// 37.00 under every reading. RED by MU-37 (a null forced mode for every id reports 4 for WEEKLY).
    /// </summary>
    [Fact]
    public void Legacy_NormCheckWeekly_OnFourWeekConfig_ForcesWeeklyMode_TagsOwnId()
    {
        var profile = Profile("AC", "DEPARTMENT_HEAD");
        var entries = WeekdayHours("AC", Sep7, Sep13, 7.40m);
        var registry = new RuleRegistry();

        var weekly = registry.Evaluate("NORM_CHECK_WEEKLY", profile, entries, Sep7, Sep13);
        Assert.True(weekly.Success);
        Assert.Equal("NORM_CHECK_WEEKLY", weekly.RuleId);
        Assert.Equal(1, weekly.NormPeriodWeeks);
        Assert.Equal(37.00m, weekly.NormHoursTotal);
        Assert.Equal(37.00m, weekly.ActualHoursTotal);
        Assert.Equal(5, weekly.LineItems.Count(l => l.TimeType == "NORMAL_HOURS"));

        var multiWeek = registry.Evaluate("NORM_CHECK_MULTIWEEK", profile, entries, Sep7, Sep13);
        Assert.True(multiWeek.Success);
        Assert.Equal("NORM_CHECK_MULTIWEEK", multiWeek.RuleId);
        Assert.Equal(4, multiWeek.NormPeriodWeeks);
        Assert.Equal(37.00m, multiWeek.NormHoursTotal);
        Assert.Equal(37.00m, multiWeek.ActualHoursTotal);

        var legacy = registry.Evaluate("NORM_CHECK_37H", profile, entries, Sep7, Sep13);
        Assert.True(legacy.Success);
        Assert.Equal("NORM_CHECK_37H", legacy.RuleId);
        Assert.Equal(4, legacy.NormPeriodWeeks);
        Assert.Equal(37.00m, legacy.NormHoursTotal);
        Assert.Equal(37.00m, legacy.ActualHoursTotal);
    }

    /// <summary>
    /// R34, the twin — a WEEKLY config (HK) called as <c>NORM_CHECK_ANNUAL</c> is forced into annual
    /// mode: days ÷ 365 of the annual norm (1924 × 73 ÷ 365 = 384.80 for the 73 days 1 September –
    /// 12 November; 73 = 365 ÷ 5, so the figure is exact), no <c>NormPeriodWeeks</c>, tagged
    /// <c>NORM_CHECK_ANNUAL</c> (the annual result reports none, as <c>NormCheckRule.cs:92-98</c> does
    /// today). The same HK profile under <c>NORM_CHECK_37H</c> stays a weekly norm (reports 1). RED by
    /// MU-37.
    /// </summary>
    [Fact]
    public void Legacy_NormCheckAnnual_OnWeeklyConfig_ForcesAnnualMode_TagsOwnId()
    {
        var profile = Profile("HK");
        var periodStart = Sep1;
        var periodEnd = new DateOnly(2026, 11, 12);
        Assert.Equal(73, periodEnd.DayNumber - periodStart.DayNumber + 1);
        var entries = WeekdayHours("HK", Sep1, Sep4, 7.40m);
        var registry = new RuleRegistry();

        var annual = registry.Evaluate("NORM_CHECK_ANNUAL", profile, entries, periodStart, periodEnd);
        Assert.True(annual.Success);
        Assert.Equal("NORM_CHECK_ANNUAL", annual.RuleId);
        Assert.Null(annual.NormPeriodWeeks);
        Assert.Equal(384.80m, annual.NormHoursTotal);
        Assert.Equal(29.60m, annual.ActualHoursTotal); // the four entries, nothing else

        var legacy = registry.Evaluate("NORM_CHECK_37H", profile, entries, periodStart, periodEnd);
        Assert.True(legacy.Success);
        Assert.Equal("NORM_CHECK_37H", legacy.RuleId);
        Assert.Equal(1, legacy.NormPeriodWeeks);
    }
}
