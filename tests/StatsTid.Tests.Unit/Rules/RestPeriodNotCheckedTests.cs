using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S147 TASK-14706-T (QUAL-234, refinement T7, AC-234i-1 server half), as narrowed by S148 S0-3
/// (P-12, owner ruling R-22) - the rest check names the days it could not fully measure.
///
/// <para>
/// <b>Plain language.</b> The 11-hour rest check measures a gap between clock times, which Skema
/// stores as work intervals. A day counts as measured only when its clock hours cover its project
/// hours, to the hundredth of an hour. A day with more project hours than clock hours (or project
/// hours and no intervals at all) is listed in <see cref="ComplianceCheckResult.RestNotCheckedDays"/>,
/// so the team overview never shows "no breach" for hours nobody put on the clock. The list is
/// informational only: success, violations and warnings are unchanged by it, and it stays (R-22).
/// </para>
///
/// <para>
/// <b>Expected values come from the refinement (S0-3: "a day is analysed when its interval hours
/// cover its project hours (to the hundredth)"), not from the code.</b> Every fact below is kind
/// <b>N</b> (names the member this sprint creates) except the marker pin (kind R); each carries a
/// POSITIVE date assertion, so the one named mutation "FindRestNotCheckedDays returns an empty
/// list" fails it on an exact-value assertion. The moved value of
/// <c>Evaluate_DayWithIntervalHoursBelowProjectHours_IsListed</c> is proved by mutation M-25
/// (any kept interval makes a day analysed), run at the S148 wave-3 gate. A compile error is not RED.
/// </para>
/// </summary>
public class RestPeriodNotCheckedTests
{
    // March 2026: Sun 1, Mon 2, Tue 3, Wed 4, Thu 5, Fri 6, Sat 7, Sun 8, Mon 9 ...
    private static readonly DateOnly Mon2 = new(2026, 3, 2);
    private static readonly DateOnly Tue3 = new(2026, 3, 3);
    private static readonly DateOnly Wed4 = new(2026, 3, 4);
    private static readonly DateOnly Thu5 = new(2026, 3, 5);
    private static readonly DateOnly Fri6 = new(2026, 3, 6);
    private static readonly DateOnly Sat7 = new(2026, 3, 7);
    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "STANDARD",
    };

    private static AgreementRuleConfig Config() => new()
    {
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        HasOvertime = false,
        HasMerarbejde = true,
        MaxFlexBalance = 150.0m,
        FlexCarryoverMax = 150.0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        MaxDailyHours = 13.0m,
        MinimumRestHours = 11.0m,
        RestPeriodDerogationAllowed = false, // a short gap is a VIOLATION in this fixture
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
    };

    private static TimeEntry HoursOnly(DateOnly date, decimal hours = 7.4m, string? activity = null) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24",
        ActivityType = activity,
    };

    private static ComplianceWorkInterval Interval(DateOnly date, TimeOnly start, TimeOnly end) =>
        new(date, start, end);

    /// <summary>
    /// The brief's shape: two hours-only weekdays (Mon 2, Tue 3) and two clocked weekdays (Thu 5
    /// 14:00-22:00, Fri 6 06:00-14:00, each with 8 project hours) whose gap is exactly 8 hours.
    /// S148 S0-3 (P-12): the clock times are intervals now, the hours are hours-only entries.
    /// </summary>
    private static (List<TimeEntry> Entries, List<ComplianceWorkInterval> Intervals)
        TwoHoursOnlyTwoClockedEightHourGap() => (
        new List<TimeEntry>
        {
            HoursOnly(Mon2),
            HoursOnly(Tue3),
            HoursOnly(Thu5, 8m),
            HoursOnly(Fri6, 8m),
        },
        new List<ComplianceWorkInterval>
        {
            Interval(Thu5, new TimeOnly(14, 0), new TimeOnly(22, 0)),
            Interval(Fri6, new TimeOnly(6, 0), new TimeOnly(14, 0)),
        });

    /// <summary>
    /// Kind <b>N</b>. AC-234i-1 server half. S148 S0-3 (P-12): the two clocked days' clock times moved
    /// from timed entries to intervals (same project hours); expected values unchanged. Red conditions:
    /// <c>Assert.Equal([Mon2, Tue3], RestNotCheckedDays)</c> fails under the mutation "return an empty
    /// list" (actual: empty). The 8-hour gap is still a DAILY_REST VIOLATION with ActualValue 8.0 /
    /// ThresholdValue 11.0: the list must not hide, replace or soften a real breach on clocked days.
    /// </summary>
    [Fact]
    public void Evaluate_TwoHoursOnlyDays_AreReported_AndTheEightHourGapIsStillADailyRestFinding()
    {
        var (entries, intervals) = TwoHoursOnlyTwoClockedEightHourGap();
        var result = RestPeriodRule.Evaluate(
            Profile(), entries, PeriodStart, PeriodEnd, Config(), intervals);

        Assert.Equal(new[] { Mon2, Tue3 }, result.RestNotCheckedDays);

        var rest = Assert.Single(result.Violations, v => v.ViolationType == ComplianceViolationType.DAILY_REST);
        Assert.Equal(8.0m, rest.ActualValue);
        Assert.Equal(11.0m, rest.ThresholdValue);
        Assert.False(result.Success); // the real breach still decides Success
    }

    /// <summary>
    /// Kind <b>N</b>. S148 S0-3 (P-12): the timed days' clock times moved to intervals (8 project
    /// hours = 8 clock hours); expected values unchanged. Red conditions: <c>Assert.Equal([Tue3],
    /// ...)</c> fails under the "empty list" mutation. A fully clocked period reports none (the
    /// negative half) while Tue 3, a lone hours-only day added to it, is reported (the discriminating
    /// half).
    /// </summary>
    [Fact]
    public void Evaluate_FullyTimedPeriod_ReportsNone_AddingOneHoursOnlyDayReportsExactlyThatDay()
    {
        var clocked = new List<TimeEntry> { HoursOnly(Mon2, 8m), HoursOnly(Thu5, 8m) };
        // S148 S0-3 (P-12): the clock times are intervals now
        var intervals = new List<ComplianceWorkInterval>
        {
            Interval(Mon2, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Interval(Thu5, new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };
        Assert.Empty(RestPeriodRule.Evaluate(
            Profile(), clocked, PeriodStart, PeriodEnd, Config(), intervals).RestNotCheckedDays);

        var withOneUnclocked = new List<TimeEntry>(clocked) { HoursOnly(Tue3) };
        var result = RestPeriodRule.Evaluate(
            Profile(), withOneUnclocked, PeriodStart, PeriodEnd, Config(), intervals);

        Assert.Equal(new[] { Tue3 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. Informational only (T7: no payroll figure moves; contract: adds no violation).
    /// Red conditions: <c>Assert.Equal([Mon2, Tue3, Wed4], ...)</c> fails under the "empty list"
    /// mutation. Plus: an hours-only month has Success true, no violations and no warnings, i.e.
    /// the list is the ONLY place the unchecked days appear.
    /// </summary>
    [Fact]
    public void Evaluate_HoursOnlyMonth_ListsTheDays_ButAddsNoViolationOrWarning_AndSuccessStaysTrue()
    {
        var entries = new List<TimeEntry> { HoursOnly(Mon2), HoursOnly(Tue3), HoursOnly(Wed4) };

        var result = RestPeriodRule.Evaluate(Profile(), entries, PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Mon2, Tue3, Wed4 }, result.RestNotCheckedDays);
        Assert.True(result.Success);
        Assert.Empty(result.Violations);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// Kind <b>N</b>. Boundary (T7: "registered work (hours)"): a zero-hours entry is not work.
    /// Red conditions: the positive <c>[Wed4]</c> fails under the "empty list" mutation; Thu 5
    /// (hours 0, no clock times) must NOT be in the list.
    /// </summary>
    [Fact]
    public void Evaluate_ZeroHoursEntryIsNotRegisteredWork_SoItsDayIsNotReported()
    {
        var entries = new List<TimeEntry> { HoursOnly(Wed4), HoursOnly(Thu5, hours: 0m) };

        var result = RestPeriodRule.Evaluate(Profile(), entries, PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Wed4 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. <b>The one intentional moved value.</b> Renamed from
    /// <c>Evaluate_DayWithOneTimedEntryPlusHoursOnlyEntries_CountsAsChecked</c>, which said a day with
    /// one clocked part counts as checked. S148 S0-3 (P-12, owner ruling R-22): a day is analysed only
    /// when its clock hours cover its project hours (to the hundredth), so Mon 2, with an interval
    /// 08:00-12:00 (4.0 h) and project hours 4.0 + 3.4 = 7.4, is now LISTED. Expected:
    /// <c>[Tue3]</c> becomes <c>[Mon2, Tue3]</c>.
    ///
    /// Red conditions: proved by mutation M-25 (any kept interval makes a day analysed, whatever its
    /// hours), run at the S148 wave-3 gate: the list is then <c>[Tue3]</c>, so
    /// <c>Assert.Equal([Mon2, Tue3], ...)</c> fails. Also fails under the "empty list" mutation.
    /// </summary>
    [Fact]
    public void Evaluate_DayWithIntervalHoursBelowProjectHours_IsListed()
    {
        // S148 S0-3 (P-12, R-22): a partly clocked day is listed
        var entries = new List<TimeEntry>
        {
            HoursOnly(Mon2, 4m),
            HoursOnly(Mon2, 3.4m),
            HoursOnly(Tue3),
        };
        var intervals = new List<ComplianceWorkInterval>
        {
            Interval(Mon2, new TimeOnly(8, 0), new TimeOnly(12, 0)),
        };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, PeriodStart, PeriodEnd, Config(), intervals);

        Assert.Equal(new[] { Mon2, Tue3 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. Boundary: a weekend date with registered hours counts like any other date
    /// (the contract says "each date in the period"), and any activity type counts. Dates come back
    /// distinct and ascending even when two entries share a day and the input is unordered.
    /// Red conditions: the exact list <c>[Mon2, Sat7]</c> fails under the "empty list" mutation.
    /// </summary>
    [Fact]
    public void Evaluate_WeekendAndAnyActivityType_AreReported_DistinctAndAscending()
    {
        var entries = new List<TimeEntry>
        {
            HoursOnly(Sat7, 4m),
            HoursOnly(Mon2, 3m, activity: "ON_CALL"),
            HoursOnly(Mon2, 4.4m),
        };

        var result = RestPeriodRule.Evaluate(Profile(), entries, PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Mon2, Sat7 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. Boundary (T7: "in the period"): an hours-only entry on the day BEFORE the
    /// period (the compliance read's one-day lookback) is not reported; the one inside is.
    /// Red conditions: the positive <c>[Mon2]</c> fails under the "empty list" mutation.
    /// </summary>
    [Fact]
    public void Evaluate_HoursOnlyDayBeforeThePeriod_IsNotReported()
    {
        var entries = new List<TimeEntry> { HoursOnly(new DateOnly(2026, 2, 28)), HoursOnly(Mon2) };

        var result = RestPeriodRule.Evaluate(Profile(), entries, PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Mon2 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. Boundary: a night shift typed as Skema's two halves (Mon 2 22:00-23:59 with
    /// project hours 1.98, Tue 3 00:00-02:00 with project hours 2.0) has every hour on the clock on
    /// both days, so neither is listed; Wed 4, hours-only, is reported. S148 S0-3 (P-12): the old
    /// fixture's crossing is now its two halves as intervals, and its separate 3 h hours-only entry
    /// on Tue 3 is gone (it would leave Tue 3 partly clocked, which
    /// <c>Evaluate_DayWithIntervalHoursBelowProjectHours_IsListed</c> covers); expected unchanged.
    /// Red conditions: the positive <c>[Wed4]</c> fails under the "empty list" mutation, and Mon 2 or
    /// Tue 3 would appear if a half's hours were not counted on its own day.
    /// </summary>
    [Fact]
    public void Evaluate_DayReceivingAMidnightCrossingHalf_CountsAsChecked()
    {
        // S148 S0-3 (P-12): the crossing is Skema's two halves, as intervals
        var entries = new List<TimeEntry>
        {
            HoursOnly(Mon2, 1.98m),
            HoursOnly(Tue3, 2m),
            HoursOnly(Wed4),
        };
        var intervals = new List<ComplianceWorkInterval>
        {
            Interval(Mon2, new TimeOnly(22, 0), new TimeOnly(23, 59)),
            Interval(Tue3, new TimeOnly(0, 0), new TimeOnly(2, 0)),
        };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, PeriodStart, PeriodEnd, Config(), intervals);

        Assert.Equal(new[] { Wed4 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>R</b> (regression pin: an existing constant kept; this task did not touch the
    /// arithmetic). TASK-14706 moved no marker: this fact held <c>weekly-timeline-v1</c> green at
    /// its merge (<c>48a1ba25</c>). TASK-14708 then moved the marker to <c>weekly-timeline-v2</c>
    /// in the same wave, as planned (RR-1; its own pin is <c>ConfigFingerprintTests.cs:268</c>), so
    /// the expected value is updated at that merge (Orchestrator merge fix-up). S148's TASK-14811 then
    /// moved it to <c>weekly-timeline-v3</c> (the call-home day is credited; its own pin is
    /// <c>ConfigFingerprintTests.Timeline_EvaluationModel_IsWeeklyTimelineV3</c>); re-pointed here,
    /// kind F (the "v3" string fails on the base). Red conditions: any
    /// change of the constant fails the exact-string assertion. The "a calculated month's lines are
    /// identical" half is carried by the existing golden payroll facts; 14706 changes no payroll code.
    /// </summary>
    [Fact]
    public void ReplayMarker_IsWeeklyTimelineV3_RestCheckMovedNone()
    {
        Assert.Equal("weekly-timeline-v3", WeeklyTimelineCore.EvaluationModel); // S148 S0-2 (R-24): v2 -> v3
    }
}
