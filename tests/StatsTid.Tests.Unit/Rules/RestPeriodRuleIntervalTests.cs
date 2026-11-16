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

    private static AgreementRuleConfig Config(bool derogation = false) => new()
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
        RestPeriodDerogationAllowed = derogation,
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
    /// worktree. Under the look-back rule (RR-1) arms (i) and (ii) give the same verdict with or without
    /// the join, so the join is proved by arm (iii), added at 3b: red conditions: under M-24 the
    /// unjoined 23:59 end is a work end of its own on 11 March (window rests 06:00-08:00 and 16:00-22:00,
    /// 8.0), so (iii) finds two findings and "exactly one" fails (actual also 11 March 8.0).
    ///
    /// Expected values from the refinement (REF:310) and RR-1: 22:00-23:59 plus 00:00-06:00 is one
    /// stint; the rest is measured before 22:00 and after 06:00, never as a one-minute gap. (i) alone:
    /// no finding. (ii) with Thu 12 March 16:00-20:00: the work end Thu 20:00, window from Wed 20:00,
    /// rests Wed 20:00-22:00 (2 h) and Thu 06:00-16:00 (10 h): 10.0, one VIOLATION dated the day the
    /// work ends, 12 March. (iii) Wed 11 March 08:00-16:00 plus the halves: the stint ends Thu 06:00,
    /// window from Wed 06:00 holds rests 06:00-08:00 and 16:00-22:00: 6.0, dated 12 March, and no
    /// finding dated 11 March.
    /// </summary>
    [Fact]
    public void MidnightHalves_FormOneStint_NoOneMinuteRest()
    {
        var wed = new DateOnly(2026, 3, 11);
        var thu = new DateOnly(2026, 3, 12);
        var halves = new[] { Iv(wed, 22, 0, 23, 59), Iv(thu, 0, 0, 6, 0) };

        // (i) the halves alone
        var alone = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), halves, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);
        Assert.Empty(DailyRest(alone));

        // (ii) the halves plus the same Thursday's afternoon work
        var withAfternoon = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(),
            halves.Append(Iv(thu, 16, 0, 20, 0)).ToList(), StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var rest = Assert.Single(DailyRest(withAfternoon));
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        // S148 3b (RR-1): dated the day the work ends, Thu 12 March (was Wed 11 March).
        Assert.Equal(thu, rest.Date);
        Assert.Equal(10.0m, rest.ActualValue);
        Assert.Contains("2026-03-12", rest.Message);
        Assert.DoesNotContain(DailyRest(withAfternoon), f => f.ActualValue == 0.0m);

        // (iii) a day shift the same Wednesday: the join decides which ends are judged (N, M-24).
        var withDayShift = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(),
            halves.Append(Iv(wed, 8, 0, 16, 0)).ToList(), StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var joined = Assert.Single(DailyRest(withDayShift));
        Assert.Equal(thu, joined.Date);
        Assert.Equal(6.0m, joined.ActualValue);
        Assert.DoesNotContain(DailyRest(withDayShift), f => f.Date == wed);
    }

    /// <summary>
    /// Kind <b>R</b> (a regression pin: the voluntary-unsocial-hours exemption is kept, at day level,
    /// the stricter reading ruled in 14813 BC-3; <c>RestPeriodRuleTests.DailyRest_VoluntaryUnsocial_
    /// SkipsRestCheck</c> pins the exempt side). Census row 2's fixture: Mon 8 April 2024 15:00-23:00
    /// with its 8 h entry, Tue 9 April 2024 05:00-09:00 with a voluntary 4 h entry, the rest 6 h. Tue 9
    /// also carries one NON-voluntary 1 h entry, so the day is not wholly voluntary and is not exempt.
    ///
    /// Discriminating assertion: the 6.0 DAILY_REST VIOLATION dated 9 April (the day the work ends) is present. It fails if the
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
            Profile(), entries, mon, mon.AddDays(6), Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var rest = Assert.Single(DailyRest(result));
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        // S148 3b (RR-1): dated the day the work ends (Tue 9 April): end 09:00, window [Mon 09:00, Tue 09:00],
        // rests Mon 09:00-15:00 (6 h) and 23:00-05:00 (6 h); the longest is 6.0 (was dated Mon 8 April).
        Assert.Equal(tue, rest.Date);
        Assert.Equal(6.0m, rest.ActualValue);
    }

    private static readonly DateOnly AprilStart = new(2026, 4, 1);
    private static readonly DateOnly AprilEnd = new(2026, 4, 30);

    /// <summary>
    /// Kind <b>N</b> (names the interval input this sprint creates), proved red by mutation
    /// <b>M-27</b> (the daily check's upper period guard deleted: <c>|| next.Date &gt; periodEnd</c>),
    /// run at the S148 wave-3 fix-up gate. A rest is judged in the period in which it ENDS (14813 BC-4):
    /// 31 March 14:00-22:00 to 1 April 07:00-15:00 is April's rest (9.0 h), so March, given both days,
    /// does not judge it. Red conditions: under M-27 (work ends dated after <c>periodEnd</c> are judged too)
    /// March reports the 9.0 h finding dated 1 April and <c>Assert.Empty</c> fails. Contrast arm: April
    /// reports exactly that finding, dated 1 April (the day the work ends).
    /// </summary>
    [Fact]
    public void RestEndingAfterThePeriod_IsNotJudged()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var intervals = new[] { Iv(mar31, 14, 0, 22, 0), Iv(AprilStart, 7, 0, 15, 0) };

        var march = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);
        Assert.Empty(DailyRest(march));

        var april = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), AprilStart, AprilEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);
        var rest = Assert.Single(DailyRest(april));
        // S148 3b (RR-1): dated the day the work ends: end Wed 1 Apr 15:00, window from Tue 31 Mar 15:00,
        // rest 22:00-07:00 is 9.0 (was dated 31 March).
        Assert.Equal(AprilStart, rest.Date);
        Assert.Equal(9.0m, rest.ActualValue);
    }

    /// <summary>
    /// Kind <b>N</b>, proved red by mutation <b>M-28</b> (work ends dated before <c>periodStart</c> are
    /// judged too). April, given 30 March 14:00-22:00 and 31 March
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
            Profile(), new List<TimeEntry>(), AprilStart, AprilEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        Assert.Empty(DailyRest(april));
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; fix-up 1, adjudication ruling 3). A chain cut at the read edge is not
    /// exempt: the read begins on 31 March, so an interval starting at exactly 00:00 there may continue
    /// work from a day the read never saw, and its voluntary status cannot be judged. April's view:
    /// 31 March 00:00-20:00 on a day whose only entry is voluntary, 1 April 05:00-13:00 with a
    /// non-voluntary entry, exemption allowed, derogation off. Expect exactly one VIOLATION, 9.0 h,
    /// dated 1 April (the day the work ends; end 13:00, window from 31 March 13:00, rest 20:00-05:00).
    /// Red conditions: with fix-up 1's engine change reverted (<c>cutAtReadEdge</c> removed) the stint
    /// counts as wholly voluntary, it is not judged and there is no finding, so <c>Assert.Single</c>
    /// fails.
    /// </summary>
    [Fact]
    public void ChainCutAtTheReadEdge_IsNotExempt()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var entries = new List<TimeEntry> { Entry(mar31, 8m, voluntary: true), Entry(AprilStart, 8m) };
        var intervals = new[] { Iv(mar31, 0, 0, 20, 0), Iv(AprilStart, 5, 0, 13, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, AprilStart, AprilEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var rest = Assert.Single(DailyRest(result));
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        // S148 3b (RR-1): dated 1 April, the day the work ends (was 31 March).
        Assert.Equal(AprilStart, rest.Date);
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
            Profile(), entries, AprilStart, AprilEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

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
            Profile(), entries, AprilStart, AprilEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        Assert.Empty(DailyRest(result));
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; wave-3b brief adjudication item 9). A rest is shown to one decimal rounded
    /// DOWN, and the finding's message says the same figure, so a breach never shows the minimum itself.
    /// Mon 16 March 14:00-22:00 and Tue 17 March 06:27-14:00: the work end Tue 14:00 looks back to Mon
    /// 14:00, the rest 22:00-06:27 is 8 h 27 min = 8.45 h, shown <c>ActualValue</c> 8.4. The expected
    /// message is built in the test from the value (this machine's culture prints a decimal comma, so no
    /// literal). Red conditions: on the wave-3 engine (half up) the value is 8.5 dated 16 March, so
    /// <c>Assert.Equal(8.4m, ...)</c> fails.
    /// </summary>
    [Fact]
    public void MidpointRest_ValueAndMessageAgree()
    {
        var mon = new DateOnly(2026, 3, 16);
        var tue = new DateOnly(2026, 3, 17);
        var intervals = new[] { Iv(mon, 14, 0, 22, 0), Iv(tue, 6, 27, 14, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var rest = Assert.Single(DailyRest(result));
        // S148 3b (RR-1, item 9): 8.5 -> 8.4 (rounded down), dated the day the work ends.
        Assert.Equal(tue, rest.Date);
        Assert.Equal(8.4m, rest.ActualValue);
        Assert.Contains($"er {rest.ActualValue:F1}t", rest.Message);
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; AC-S0-3h, RR-1 "one finding per day, the lowest value" and item 10's tie).
    /// March 2026, no derogation. <b>(a)</b> Tue 10 Mar 14:00-22:00; Wed 11 Mar 06:00-10:00 and
    /// 13:00-23:30. Wed's 10:00 end: window from Tue 10:00, rests 10:00-14:00 and 22:00-06:00: 8.0
    /// (fails); Wed's 23:30 end: window from Tue 23:30, rests 23:30-06:00 (6.5) and 10:00-13:00 (3):
    /// 6.5 (fails, lower, later). Exactly one finding: 11 March, 6.5, VIOLATION; its message names
    /// "2026-03-11" and "23:30", not "10:00". <b>(b) the tie:</b> Sun 8 Mar 08:00-12:00 and
    /// 13:00-22:30; Mon 9 Mar 07:00-11:00 and 12:00-16:00. Both 9 March ends give 8.5 (rest 22:30-07:00
    /// in each window); the 9 March finding is 8.5 and its message contains "11:00", not "16:00" (equal
    /// lowest values name the earliest end). Red conditions: the wave-3 engine reports (a) one finding
    /// dated 10 March, 8.0, and (b) one finding dated 8 March, 8.5, so the date and value assertions fail.
    /// </summary>
    [Fact]
    public void OneFindingPerDay_AtTheLowestValue_MessageNamesTheJudgedEnd()
    {
        var tue = new DateOnly(2026, 3, 10);
        var wed = new DateOnly(2026, 3, 11);
        var intervals = new[] { Iv(tue, 14, 0, 22, 0), Iv(wed, 6, 0, 10, 0), Iv(wed, 13, 0, 23, 30) };

        var result = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var rest = Assert.Single(DailyRest(result));
        Assert.Equal(wed, rest.Date);
        Assert.Equal(6.5m, rest.ActualValue);
        Assert.Equal(ComplianceSeverity.VIOLATION, rest.Severity);
        Assert.Contains("2026-03-11", rest.Message);
        Assert.Contains("23:30", rest.Message);
        Assert.DoesNotContain("10:00", rest.Message);

        // (b) the tie: equal lowest values name the earliest end
        var sun = new DateOnly(2026, 3, 8);
        var mon = new DateOnly(2026, 3, 9);
        var tie = new[] { Iv(sun, 8, 0, 12, 0), Iv(sun, 13, 0, 22, 30), Iv(mon, 7, 0, 11, 0), Iv(mon, 12, 0, 16, 0) };

        var tieResult = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), tie, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var monday = Assert.Single(DailyRest(tieResult), f => f.Date == mon);
        Assert.Equal(8.5m, monday.ActualValue);
        Assert.Contains("11:00", monday.Message);
        Assert.DoesNotContain("16:00", monday.Message);
    }

    /// <summary>
    /// Kind <b>F</b> (Unit; AC-S0-3i, RR-2 and item 9). With the derogation allowed, a rest of 8.0 or more
    /// is a WARNING and a lower one a VIOLATION, on the true value; the shown value is rounded down.
    /// Mon 2 March 2026, one shift from 06:00, the window from Sun 1 Mar at the end time: <b>(a)</b> to
    /// 22:00: rest 22:00-06:00 = 8.0, one WARNING; <b>(b)</b> to 22:30: 7.5, one VIOLATION; <b>(c)</b> to
    /// 22:03: 7 h 57 min, shown 7.9 (not 8.0), one VIOLATION. Red conditions: the wave-3 engine reports
    /// no finding for a single shift, so each <c>Assert.Single</c> fails. M-31 (floor compared strictly)
    /// turns (a) into a VIOLATION.
    /// </summary>
    [Fact]
    public void DerogationFloor_EightHoursWarns_BelowIsAViolation()
    {
        var mon = new DateOnly(2026, 3, 2);

        var a = Assert.Single(DailyRest(Derogating(Iv(mon, 6, 0, 22, 0))));
        Assert.Equal(mon, a.Date);
        Assert.Equal(8.0m, a.ActualValue);
        Assert.Equal(ComplianceSeverity.WARNING, a.Severity);

        var b = Assert.Single(DailyRest(Derogating(Iv(mon, 6, 0, 22, 30))));
        Assert.Equal(7.5m, b.ActualValue);
        Assert.Equal(ComplianceSeverity.VIOLATION, b.Severity);

        var c = Assert.Single(DailyRest(Derogating(Iv(mon, 6, 0, 22, 3))));
        Assert.Equal(7.9m, c.ActualValue);
        Assert.Equal(ComplianceSeverity.VIOLATION, c.Severity);
    }

    private static ComplianceCheckResult Derogating(ComplianceWorkInterval interval) =>
        RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(derogation: true), new[] { interval },
            StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

    /// <summary>
    /// Kind <b>F</b> (Unit; AC-S0-3i, item 17). A WARNING under the derogation says what StatsTid does
    /// not check: its message contains, word for word, the sentence below (built here from the ruling's
    /// text, not from the engine's constant); a VIOLATION's message does not. Arm (a) Mon 2 March
    /// 06:00-22:00 (8.0, WARNING); arm (b) 06:00-22:30 (7.5, VIOLATION). Red conditions: the wave-3
    /// engine reports no finding for a single shift, so <c>Assert.Single</c> fails.
    /// </summary>
    [Fact]
    public void DerogationWarning_SaysWhatIsNotChecked()
    {
        const string notChecked =
            "Hvilen kan undtagelsesvis nedsættes til 8 timer, højst 1 gang pr. 7 døgn (2 gange efter lokal aftale) "
            + "og kun med tilsvarende kompenserende hvile umiddelbart efter. StatsTid kontrollerer ingen af delene.";
        var mon = new DateOnly(2026, 3, 2);

        var warning = Assert.Single(DailyRest(Derogating(Iv(mon, 6, 0, 22, 0))));
        Assert.Equal(ComplianceSeverity.WARNING, warning.Severity);
        Assert.Contains(notChecked, warning.Message);

        var violation = Assert.Single(DailyRest(Derogating(Iv(mon, 6, 0, 22, 30))));
        Assert.Equal(ComplianceSeverity.VIOLATION, violation.Severity);
        Assert.DoesNotContain("Hvilen kan", violation.Message);
        Assert.DoesNotContain("StatsTid kontrollerer", violation.Message);
    }

    /// <summary>
    /// Kind <b>R</b> (Unit; AC-S0-3j; M-32). A wholly voluntary stint is neither judged nor work in
    /// another work end's window. Sat 7 March 06:00-22:00 with one voluntary 16 h entry (the config
    /// allows it); Sun 8 March 06:00-14:00 with a non-voluntary 8 h entry. No DAILY_REST finding:
    /// Saturday is not judged (its own window would give 8.0), and it is rest in Sunday's window (rest
    /// from Sat 14:00 to Sun 06:00, 16 h). Discriminating: under M-32 (voluntary stints count as work)
    /// Sunday gets 8.0 (8 March); under "voluntary stints judged" Saturday gets 8.0. Green on the
    /// wave-3 engine (the pair is skipped), so R.
    /// </summary>
    [Fact]
    public void VoluntaryStint_IsNeitherJudgedNorWorkInAnotherWindow()
    {
        var sat = new DateOnly(2026, 3, 7);
        var sun = new DateOnly(2026, 3, 8);
        var entries = new List<TimeEntry> { Entry(sat, 16m, voluntary: true), Entry(sun, 8m) };
        var intervals = new[] { Iv(sat, 6, 0, 22, 0), Iv(sun, 6, 0, 14, 0) };

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, MarchStart, MarchEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        Assert.Empty(DailyRest(result));
    }

    /// <summary>
    /// Kind <b>N</b> (Unit; AC-S0-3o, item 5; M-33). The elapsed time comes from the zone the host
    /// passes in, not from a lookup inside the rule. Sat 28 March 2026 12:00-20:00 and Sun 29 March
    /// 07:30-15:30 (clocks forward that night): <b>(a)</b> with the Copenhagen zone the night is 10.5
    /// elapsed hours, one finding dated 29 March; <b>(b)</b> with a fixed +01:00 zone (no daylight
    /// saving) it is 11.5 hours and there is no finding. Red conditions: under M-33 (the helper reads
    /// <c>CopenhagenBusinessDate.Zone</c> instead of the zone passed in) arm (b) finds 29 March 10.5, so
    /// <c>Assert.Empty</c> fails.
    /// </summary>
    [Fact]
    public void ElapsedTime_ComesFromTheZonePassedIn()
    {
        var sat = new DateOnly(2026, 3, 28);
        var sun = new DateOnly(2026, 3, 29);
        var intervals = new[] { Iv(sat, 12, 0, 20, 0), Iv(sun, 7, 30, 15, 30) };

        var copenhagen = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);
        var rest = Assert.Single(DailyRest(copenhagen));
        Assert.Equal(sun, rest.Date);
        Assert.Equal(10.5m, rest.ActualValue);

        var fixedZone = TimeZoneInfo.CreateCustomTimeZone("Fixed+01", TimeSpan.FromHours(1), "Fixed+01", "Fixed+01");
        var fixedResult = RestPeriodRule.Evaluate(
            Profile(), new List<TimeEntry>(), MarchStart, MarchEnd, Config(), intervals, fixedZone);
        Assert.Empty(DailyRest(fixedResult));
    }
}
