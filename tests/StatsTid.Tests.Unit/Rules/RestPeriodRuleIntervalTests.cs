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

    private static readonly DateOnly AprilStart = new(2026, 4, 1);
    private static readonly DateOnly AprilEnd = new(2026, 4, 30);

    /// <summary>
    /// Kind <b>N</b> (names the interval input this sprint creates), proved red by mutation
    /// <b>M-27</b> (the daily check's upper period guard deleted: <c>|| next.Date &gt; periodEnd</c>),
    /// run at the S148 wave-3 fix-up gate. A rest is judged in the period in which it ENDS (14813 BC-4):
    /// 31 March 14:00-22:00 to 1 April 07:00-15:00 is April's rest (9.0 h), so March, given both days,
    /// does not judge it. Red conditions: under M-27 March reports the 9.0 h finding and
    /// <c>Assert.Empty</c> fails. Contrast arm: April reports exactly that finding, dated 31 March.
    /// </summary>
    [Fact]
    public void RestEndingAfterThePeriod_IsNotJudged()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var intervals = new[] { Iv(mar31, 14, 0, 22, 0), Iv(AprilStart, 7, 0, 15, 0) };

        var march = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals);
        Assert.Empty(DailyRest(march));

        var april = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), AprilStart, AprilEnd, Config(), intervals);
        var rest = Assert.Single(DailyRest(april));
        Assert.Equal(mar31, rest.Date);
        Assert.Equal(9.0m, rest.ActualValue);
    }

    /// <summary>
    /// Kind <b>N</b>, proved red by mutation <b>M-28</b> (the lower period guard deleted:
    /// <c>next.Date &lt; periodStart ||</c>). April, given 30 March 14:00-22:00 and 31 March
    /// 07:00-15:00, does not judge that 9.0 h rest: it ended in March, which owns it. Red conditions:
    /// under M-28 April reports the finding and <c>Assert.Empty</c> fails.
    /// </summary>
    [Fact]
    public void RestEndingBeforeThePeriod_IsNotJudged()
    {
        var intervals = new[]
        {
            Iv(new DateOnly(2026, 3, 30), 14, 0, 22, 0),
            Iv(new DateOnly(2026, 3, 31), 7, 0, 15, 0),
        };

        var april = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), AprilStart, AprilEnd, Config(), intervals);

        Assert.Empty(DailyRest(april));
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; fix-up 1, adjudication ruling 3). A chain cut at the read edge is not
    /// exempt: the read begins on 31 March, so an interval starting at exactly 00:00 there may continue
    /// work from a day the read never saw, and its voluntary status cannot be judged. April's view:
    /// 31 March 00:00-20:00 on a day whose only entry is voluntary, 1 April 05:00-13:00 with a
    /// non-voluntary entry, exemption allowed, derogation off. Expect exactly one VIOLATION, 9.0 h,
    /// dated 31 March. Red conditions: with fix-up 1's engine change reverted (<c>cutAtReadEdge</c>
    /// removed) the stint counts as wholly voluntary, the pair is skipped and there is no finding, so
    /// <c>Assert.Single</c> fails.
    /// </summary>
    [Fact]
    public void ChainCutAtTheReadEdge_IsNotExempt()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var entries = new List<TimeEntry> { Entry(mar31, 8m, voluntary: true), Entry(AprilStart, 8m) };
        var intervals = new[] { Iv(mar31, 0, 0, 20, 0), Iv(AprilStart, 5, 0, 13, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, AprilStart, AprilEnd, Config(), intervals);

        var rest = Assert.Single(DailyRest(result));
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        Assert.Equal(mar31, rest.Date);
        Assert.Equal(9.0m, rest.ActualValue);
    }

    /// <summary>
    /// Kind <b>R</b> (control for <see cref="ChainCutAtTheReadEdge_IsNotExempt"/>): the same shape with
    /// the read-edge day starting at 00:01, not 00:00, stays exempt: its beginning is seen, so the rule
    /// does not apply. Red conditions: a rule that cuts at any early start reports the finding and
    /// <c>Assert.Empty</c> fails.
    /// </summary>
    [Fact]
    public void VoluntaryDayStartingAt0001BeforeThePeriod_StaysExempt()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var entries = new List<TimeEntry> { Entry(mar31, 8m, voluntary: true), Entry(AprilStart, 8m) };
        var intervals = new[] { Iv(mar31, 0, 1, 20, 0), Iv(AprilStart, 5, 0, 13, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, AprilStart, AprilEnd, Config(), intervals);

        Assert.Empty(DailyRest(result));
    }

    /// <summary>
    /// Kind <b>R</b> (control): a voluntary day starting at 00:00 on <c>periodStart</c> itself (not
    /// before it) stays exempt. 1 April 00:00-20:00 on a voluntary day, 2 April 05:00-13:00 with a
    /// non-voluntary entry: the pair is skipped. Red conditions: a rule that cuts at every 00:00 start,
    /// not only those before the period, reports the 9.0 h rest and <c>Assert.Empty</c> fails.
    /// </summary>
    [Fact]
    public void VoluntaryDayStartingAt0000OnPeriodStart_StaysExempt()
    {
        var apr2 = AprilStart.AddDays(1);
        var entries = new List<TimeEntry> { Entry(AprilStart, 8m, voluntary: true), Entry(apr2, 8m) };
        var intervals = new[] { Iv(AprilStart, 0, 0, 20, 0), Iv(apr2, 5, 0, 13, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, AprilStart, AprilEnd, Config(), intervals);

        Assert.Empty(DailyRest(result));
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; fix-up 2, adjudication ruling 1). A rest is shown to one decimal with halves
    /// rounded up, and the finding's message says the same figure. Mon 16 March 14:00-22:00 and Tue 17
    /// March 06:27-14:00 leave 8 h 27 min = 8.45 h: <c>ActualValue</c> 8.5. The expected message is built
    /// in the test from the value (this machine's culture prints a decimal comma, so no literal).
    /// Red conditions: with fix-up 2 reverted the value is rounded to even, 8.4, so
    /// <c>Assert.Equal(8.5m, ...)</c> fails (the message still says 8.5).
    /// </summary>
    [Fact]
    public void MidpointRest_ValueAndMessageAgree()
    {
        var mon = new DateOnly(2026, 3, 16);
        var tue = new DateOnly(2026, 3, 17);
        var intervals = new[] { Iv(mon, 14, 0, 22, 0), Iv(tue, 6, 27, 14, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals);

        var rest = Assert.Single(DailyRest(result));
        Assert.Equal(8.5m, rest.ActualValue);
        Assert.Contains($"er {rest.ActualValue:F1}t", rest.Message);
    }
}
