using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 TASK-14813-T - the rest check on Skema's clock intervals, called directly on the engine.
///
/// <para>
/// <b>Plain language.</b> Skema cannot hold 24:00, so a night shift is typed as 22:00-23:59 on one day
/// and 00:00-06:00 on the next. Read literally that is a one-minute "rest" at midnight. The engine
/// must read it as one shift, and measure the rest from 06:00. A day whose single non-voluntary entry
/// sits beside a voluntary one is not exempt from the rest check.
/// </para>
/// </summary>
public class RestPeriodRuleIntervalTests
{
    private static readonly DateOnly MarchStart = new(2026, 3, 1);
    private static readonly DateOnly MarchEnd = new(2026, 3, 31);

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
        RestPeriodDerogationAllowed = false,
        VoluntaryUnsocialHoursAllowed = true,
    };

    private static ComplianceWorkInterval Iv(DateOnly date, int startH, int startM, int endH, int endM) =>
        new(date, new TimeOnly(startH, startM), new TimeOnly(endH, endM));

    private static TimeEntry Entry(DateOnly date, decimal hours, bool voluntary = false) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24",
        VoluntaryUnsocialHours = voluntary,
    };

    private static List<ComplianceViolation> DailyRest(ComplianceCheckResult result) =>
        result.Violations.Concat(result.Warnings)
            .Where(f => f.ViolationType == ComplianceViolationType.DAILY_REST)
            .ToList();

    /// <summary>
    /// Kind <b>N</b> (AC-S0-3b; names the interval input this sprint creates), proved red by
    /// mutation <b>M-24</b> (midnight continuity dropped: an interval ending at 23:59 or later and
    /// one starting at 00:00 the next day stay two stints), run at the wave gate in a detached
    /// worktree. Red conditions: under M-24 (i) finds one DAILY_REST finding with ActualValue 0.0
    /// (the one-minute midnight "rest"), failing "no DAILY_REST finding"; (ii) finds the 0.0 finding
    /// instead of the 10.0 one, failing "exactly one, 10.0".
    ///
    /// Expected values from the refinement (REF:310): 22:00-23:59 plus 00:00-06:00 is one stint; the
    /// rest is measured before 22:00 and after 06:00, never as a one-minute gap. (i) alone: no
    /// finding. (ii) with Thu 12 March 16:00-20:00: 06:00 to 16:00 = 10.0 hours, one VIOLATION dated
    /// the day the night shift started, 11 March.
    /// </summary>
    [Fact]
    public void MidnightHalves_FormOneStint_NoOneMinuteRest()
    {
        var wed = new DateOnly(2026, 3, 11);
        var thu = new DateOnly(2026, 3, 12);
        var halves = new[] { Iv(wed, 22, 0, 23, 59), Iv(thu, 0, 0, 6, 0) };

        // (i) the halves alone
        var alone = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), halves);
        Assert.Empty(DailyRest(alone));

        // (ii) the halves plus the same Thursday's afternoon work
        var withAfternoon = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(),
            halves.Append(Iv(thu, 16, 0, 20, 0)).ToList());

        var rest = Assert.Single(DailyRest(withAfternoon));
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        Assert.Equal(wed, rest.Date);
        Assert.Equal(10.0m, rest.ActualValue);
        Assert.Contains("2026-03-12", rest.Message);
        Assert.DoesNotContain(DailyRest(withAfternoon), f => f.ActualValue == 0.0m);
    }

    /// <summary>
    /// Kind <b>R</b> (a regression pin: the voluntary-unsocial-hours exemption is kept, at day level,
    /// the stricter reading ruled in 14813 BC-3; <c>RestPeriodRuleTests.DailyRest_VoluntaryUnsocial_
    /// SkipsRestCheck</c> pins the exempt side). Census row 2's fixture: Mon 8 April 2024 15:00-23:00
    /// with its 8 h entry, Tue 9 April 2024 05:00-09:00 with a voluntary 4 h entry, the rest 6 h. Tue 9
    /// also carries one NON-voluntary 1 h entry, so the day is not wholly voluntary and is not exempt.
    ///
    /// Discriminating assertion: the 6.0 DAILY_REST VIOLATION dated 8 April is present. It fails if the
    /// exemption is read as "any entry of the day is voluntary" (the finding disappears).
    /// </summary>
    [Fact]
    public void VoluntaryDay_WithOneNonVoluntaryEntry_IsNotExempt()
    {
        var mon = new DateOnly(2024, 4, 8);
        var tue = new DateOnly(2024, 4, 9);
        var entries = new List<TimeEntry>
        {
            Entry(mon, 8m),
            Entry(tue, 4m, voluntary: true),
            Entry(tue, 1m),
        };
        var intervals = new[] { Iv(mon, 15, 0, 23, 0), Iv(tue, 5, 0, 9, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, mon, mon.AddDays(6), Config(), intervals);

        var rest = Assert.Single(DailyRest(result));
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        Assert.Equal(mon, rest.Date);
        Assert.Equal(6.0m, rest.ActualValue);
    }
}
