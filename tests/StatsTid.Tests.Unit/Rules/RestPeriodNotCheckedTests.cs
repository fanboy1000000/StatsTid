using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S147 TASK-14706-T (QUAL-234 interim, refinement T7, AC-234i-1 server half) - the rest check names
/// the days it could not measure.
///
/// <para>
/// <b>Plain language.</b> The 11-hour rest check measures a gap between two clock times. A day
/// registered in Skema has hours but no clock times, so nothing can be measured. Before S147 such a
/// day passed silently, so the team overview showed "no breach" for days nobody checked. Now the
/// result also carries <see cref="ComplianceCheckResult.RestNotCheckedDays"/>: the dates with
/// registered hours and no entry carrying both clock times. It is informational only: success,
/// violations and warnings are unchanged by it.
/// </para>
///
/// <para>
/// <b>Expected values come from the refinement (T7: "each date in the period on which the employee
/// has registered work (hours) but no entry with both clock times"), not from the code.</b>
/// Every fact below is kind <b>N</b> (names the member this sprint creates) except the marker pin
/// (kind R), and every N fact carries at least one POSITIVE date assertion, so the one named
/// mutation - "FindRestNotCheckedDays returns an empty list" - fails each of them on an
/// exact-value assertion (RED evidence is in the task report). A compile error is not RED.
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

    private static TimeEntry Timed(DateOnly date, decimal hours, TimeOnly start, TimeOnly end) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        StartTime = start,
        EndTime = end,
        AgreementCode = "AC",
        OkVersion = "OK24",
    };

    /// <summary>
    /// The brief's shape: two hours-only weekdays (Mon 2, Tue 3) and two timed weekdays (Thu 5
    /// 14:00-22:00, Fri 6 06:00-14:00) whose gap is exactly 8 hours.
    /// </summary>
    private static List<TimeEntry> TwoHoursOnlyTwoTimedEightHourGap() => new()
    {
        HoursOnly(Mon2),
        HoursOnly(Tue3),
        Timed(Thu5, 8m, new TimeOnly(14, 0), new TimeOnly(22, 0)),
        Timed(Fri6, 8m, new TimeOnly(6, 0), new TimeOnly(14, 0)),
    };

    /// <summary>
    /// Kind <b>N</b>. AC-234i-1 server half. Red conditions: <c>Assert.Equal([Mon2, Tue3],
    /// RestNotCheckedDays)</c> fails under the mutation "return an empty list" (actual: empty).
    /// The 8-hour gap is still a DAILY_REST VIOLATION with ActualValue 8.0 / ThresholdValue 11.0:
    /// the new member must not hide, replace or soften a real breach on timed days.
    /// </summary>
    [Fact]
    public void Evaluate_TwoHoursOnlyDays_AreReported_AndTheEightHourGapIsStillADailyRestFinding()
    {
        var result = RestPeriodRule.Evaluate(
            Profile(), TwoHoursOnlyTwoTimedEightHourGap(), PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Mon2, Tue3 }, result.RestNotCheckedDays);

        var rest = Assert.Single(result.Violations, v => v.ViolationType == ComplianceViolationType.DAILY_REST);
        Assert.Equal(8.0m, rest.ActualValue);
        Assert.Equal(11.0m, rest.ThresholdValue);
        Assert.False(result.Success); // the real breach still decides Success
    }

    /// <summary>
    /// Kind <b>N</b>. Red conditions: the positive <c>[Mon2, Tue3]</c> from the single daily-rest
    /// entry point (<see cref="RestPeriodRule.EvaluateDailyRest"/>, the registry's per-check path)
    /// fails under the "empty list" mutation. EvaluateWeeklyRest keeps an empty list (a different
    /// check; the implementer's report) - asserted as the contrast.
    /// </summary>
    [Fact]
    public void EvaluateDailyRest_FillsTheList_EvaluateWeeklyRest_KeepsItEmpty()
    {
        var entries = TwoHoursOnlyTwoTimedEightHourGap();

        var daily = RestPeriodRule.EvaluateDailyRest(Profile(), entries, PeriodStart, PeriodEnd, Config());
        var weekly = RestPeriodRule.EvaluateWeeklyRest(Profile(), entries, PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Mon2, Tue3 }, daily.RestNotCheckedDays);
        Assert.Empty(weekly.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. Red conditions: <c>Assert.Equal([Tue3], ...)</c> fails under the "empty list"
    /// mutation. A fully timed period reports none (the negative half) while Tue 3, a lone
    /// hours-only day added to it, is reported (the discriminating half).
    /// </summary>
    [Fact]
    public void Evaluate_FullyTimedPeriod_ReportsNone_AddingOneHoursOnlyDayReportsExactlyThatDay()
    {
        var timed = new List<TimeEntry>
        {
            Timed(Mon2, 8m, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Timed(Thu5, 8m, new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };
        Assert.Empty(RestPeriodRule.Evaluate(Profile(), timed, PeriodStart, PeriodEnd, Config()).RestNotCheckedDays);

        var withOneUntimed = new List<TimeEntry>(timed) { HoursOnly(Tue3) };
        var result = RestPeriodRule.Evaluate(Profile(), withOneUntimed, PeriodStart, PeriodEnd, Config());

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
    /// Kind <b>N</b>. Boundary: a day with one timed entry plus an hours-only entry counts as
    /// checked (the timed entry bounds the day's gap); a day with only an hours-only entry does not.
    /// Red conditions: the positive <c>[Tue3]</c> fails under the "empty list" mutation; Mon 2
    /// (timed + hours-only) must not be listed.
    /// </summary>
    [Fact]
    public void Evaluate_DayWithOneTimedEntryPlusHoursOnlyEntries_CountsAsChecked()
    {
        var entries = new List<TimeEntry>
        {
            Timed(Mon2, 4m, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            HoursOnly(Mon2, 3.4m),
            HoursOnly(Tue3),
        };

        var result = RestPeriodRule.Evaluate(Profile(), entries, PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Tue3 }, result.RestNotCheckedDays);
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
            HoursOnly(Mon2, 3m, activity: "VACATION"),
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
    /// Kind <b>N</b>. Boundary: a midnight-crossing TIMED shift (Mon 2 22:00 to 02:00), split by the
    /// shared normalizer as the Backend does before calling the rule, puts a timed post-midnight half on Tue 3, so Tue 3 is checked even though an hours-only entry also sits on
    /// it; Wed 4, hours-only, is reported. Red conditions: the positive <c>[Wed4]</c> fails under
    /// the "empty list" mutation.
    /// </summary>
    [Fact]
    public void Evaluate_DayReceivingAMidnightCrossingHalf_CountsAsChecked()
    {
        var entries = new List<TimeEntry>
        {
            Timed(Mon2, 4m, new TimeOnly(22, 0), new TimeOnly(2, 0)),
            HoursOnly(Tue3, 3m),
            HoursOnly(Wed4),
        };

        // The Backend splits a crossing shift BEFORE the rule sees it (ADR-039; the rule itself does
        // not split), so feed the rule the real normalized view.
        var result = RestPeriodRule.Evaluate(
            Profile(), MidnightCrossingNormalizer.Normalize(entries), PeriodStart, PeriodEnd, Config());

        Assert.Equal(new[] { Wed4 }, result.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>R</b> (regression pin: an existing constant kept; this task did not touch the
    /// arithmetic). TASK-14706 moved no marker: this fact held <c>weekly-timeline-v1</c> green at
    /// its merge (<c>48a1ba25</c>). TASK-14708 then moved the marker to <c>weekly-timeline-v2</c>
    /// in the same wave, as planned (RR-1; its own pin is <c>ConfigFingerprintTests.cs:268</c>), so
    /// the expected value is updated at that merge (Orchestrator merge fix-up). Red conditions: any
    /// change of the constant fails the exact-string assertion. The "a calculated month's lines are
    /// identical" half is carried by the existing golden payroll facts; 14706 changes no payroll code.
    /// </summary>
    [Fact]
    public void ReplayMarker_IsWeeklyTimelineV2_RestCheckMovedNone()
    {
        Assert.Equal("weekly-timeline-v2", WeeklyTimelineCore.EvaluationModel);
    }
}
