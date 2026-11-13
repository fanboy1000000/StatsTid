using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// ADR-039 GAP-A — the rule-side rest consumer over midnight-crossing work. The rest checks read
/// the Skema clock intervals (ADR-043 D10), and Skema cannot hold 24:00, so a shift over midnight
/// arrives as two intervals: 23:xx–23:59 on day D and 00:00–xx:xx on D+1. The rule joins them into
/// ONE stint, so the midnight is no false 0-hour rest (QUAL-001's ~29h raw reading never arises) and
/// the post-midnight half does not count as a second worked day. Each rest fact is RED on a rule that
/// keeps the halves apart (mutation M-24, run at the S148 wave-3 gate).
/// </summary>
public class RestPeriodRuleMidnightRestTests
{
    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "STANDARD",
    };

    private static AgreementRuleConfig Config(bool derogation = false, string okVersion = "OK24") => new()
    {
        AgreementCode = "AC",
        OkVersion = okVersion,
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
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
    };

    private static TimeEntry Timed(
        DateOnly date, decimal hours, TimeOnly start, TimeOnly end, Guid? stintId = null) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        StartTime = start,
        EndTime = end,
        AgreementCode = "AC",
        OkVersion = "OK24",
        VoluntaryUnsocialHours = false,
        SourceStintId = stintId,
    };

    private static TimeEntry HoursOnly(DateOnly date, decimal hours) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24",
    };

    private static ComplianceWorkInterval Iv(DateOnly date, TimeOnly start, TimeOnly end) => new(date, start, end);

    // ---------------------------------------------------------------
    // 1. A shift over midnight, as Skema types it (two halves), then the next morning's shift: the
    //    daily rest is the 5h gap to the next morning, NOT the pre-fix bogus ~29h (QUAL-001) and
    //    NOT a one-minute midnight gap.
    // ---------------------------------------------------------------
    /// <summary>
    /// Kind <b>R</b> (the 5 h arithmetic kept). S148 S0-3 (P-12): the old fact fed ONE raw row whose end
    /// was before its start. Intervals cannot express that (Skema keeps only end-after-start, and the
    /// engine ignores any other; the parity row <c>reversed-interval-ignored</c> pins that), so the
    /// crossing is now Skema's two halves, 23:00–23:59 on D and 00:00–02:00 on D+1; the next
    /// morning's shift is an interval; the expected values are unchanged. Red conditions: a rule that
    /// keeps the halves apart finds a 0.0 h rest at midnight, so <c>ActualValue == 5.0m</c> fails.
    /// </summary>
    [Fact]
    public void DailyRest_RawMidnightCrossing_ThenMorningShift_IsFiveHourViolation_Not29h()
    {
        var d = new DateOnly(2026, 3, 16);
        var entries = new List<TimeEntry>
        {
            HoursOnly(d, 0.98m),
            HoursOnly(d.AddDays(1), 10m), // 2h of the night shift + 8h next morning
        };
        // S148 S0-3 (P-12): the crossing is Skema's two halves, as intervals
        var intervals = new[]
        {
            Iv(d, new TimeOnly(23, 0), new TimeOnly(23, 59)),
            Iv(d.AddDays(1), new TimeOnly(0, 0), new TimeOnly(2, 0)),
            Iv(d.AddDays(1), new TimeOnly(7, 0), new TimeOnly(15, 0)), // next morning
        };

        var result = RestPeriodRule.Evaluate(Profile(), entries, d, d.AddDays(6), Config(), intervals);

        // The stint ends d+1 02:00; next stint starts d+1 07:00 → 5h rest < 11h → VIOLATION.
        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            v.ActualValue == 5.0m);
        // And explicitly NOT the pre-fix ~29h (which read no violation at all).
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST && v.ActualValue > 24m);
    }

    // ---------------------------------------------------------------
    // 2. Halves on the OK boundary: no false 0-hour midnight gap; the rest to the next morning is the
    //    true 5h; and each normalized half carries its own OK-version.
    // ---------------------------------------------------------------
    /// <summary>
    /// Kind <b>R</b>. S148 S0-3 (P-12): the two halves and the morning shift are intervals now; the
    /// two OK-version assertions on the normalizer stay as they were (they test the normalizer, not
    /// the rule); expected values unchanged. Red conditions: a rule that keeps the halves apart finds
    /// a 0.0 h rest at midnight, so the 5.0 h <c>Assert.Contains</c> fails and the 0 h
    /// <c>Assert.DoesNotContain</c> fails.
    /// </summary>
    [Fact]
    public void DailyRest_NormalizedBoundaryCrossing_NoFalseMidnightGap_AndPerHalfOkVersion()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var apr01 = new DateOnly(2026, 4, 1);

        var crossing = Timed(mar31, 3m, new TimeOnly(23, 0), new TimeOnly(2, 0), Guid.NewGuid());
        var normalized = MidnightCrossingNormalizer.Normalize(new[] { crossing });

        // D3 — the two halves carry OK24 (pre) and OK26 (post).
        Assert.Equal("OK24", normalized.Single(e => e.Date == mar31).OkVersion);
        Assert.Equal("OK26", normalized.Single(e => e.Date == apr01).OkVersion);

        var entries = normalized.Append(HoursOnly(apr01, 8m)).ToList();
        // S148 S0-3 (P-12): the shifts are intervals; the crossing is its two halves
        var intervals = new[]
        {
            Iv(mar31, new TimeOnly(23, 0), new TimeOnly(23, 59)),
            Iv(apr01, new TimeOnly(0, 0), new TimeOnly(2, 0)),
            Iv(apr01, new TimeOnly(7, 0), new TimeOnly(15, 0)),
        };

        var result = RestPeriodRule.Evaluate(Profile(), entries, mar31, mar31.AddDays(6), Config(), intervals);

        // True rest between the crossing stint's end (01-Apr 02:00) and the morning start
        // (01-Apr 07:00) = 5h → VIOLATION. NOT a false 0-hour (midnight) or 24h gap.
        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            v.ActualValue == 5.0m);
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            (v.ActualValue == 0m || v.ActualValue >= 24m));
    }

    // ---------------------------------------------------------------
    // 3. Weekly rest: a compliant 6-day week whose Saturday shift bleeds past midnight must NOT be
    //    flagged as a 7-day week. The post-half IS dated Sunday, so a naive distinct-date count would
    //    see 7 — the stint-based count sees 6 (the crossing counts under its Saturday start-day only).
    // ---------------------------------------------------------------
    /// <summary>
    /// Kind <b>R</b>. S148 S0-3 (P-12): the Mon–Fri shifts and Saturday's 20:00–23:59 plus Sunday's
    /// 00:00–02:00 are intervals now; expected unchanged (no weekly-rest finding). Red conditions: a
    /// rule that keeps the halves apart counts Sunday as a seventh worked day, so both
    /// <c>Assert.DoesNotContain</c> calls fail (M-24).
    /// </summary>
    [Fact]
    public void WeeklyRest_SixDayWeek_WithSaturdayCrossing_NoFalseSeventhDay()
    {
        var monday = new DateOnly(2026, 4, 6);
        var saturday = monday.AddDays(5);
        var sunday = monday.AddDays(6);

        var entries = new List<TimeEntry>();
        var intervals = new List<ComplianceWorkInterval>();
        for (int i = 0; i < 5; i++) // Mon–Fri day shifts
        {
            entries.Add(HoursOnly(monday.AddDays(i), 7m));
            // S148 S0-3 (P-12): the clock times are intervals now
            intervals.Add(Iv(monday.AddDays(i), new TimeOnly(8, 0), new TimeOnly(15, 0)));
        }

        // Saturday 20:00 → 02:00 Sunday, as Skema's two halves.
        entries.Add(HoursOnly(saturday, 3.98m));
        entries.Add(HoursOnly(sunday, 2m));
        intervals.Add(Iv(saturday, new TimeOnly(20, 0), new TimeOnly(23, 59)));
        intervals.Add(Iv(sunday, new TimeOnly(0, 0), new TimeOnly(2, 0)));

        // Sanity: a Sunday-dated row DOES exist (the post-half) — a naive date count would hit 7.
        Assert.Contains(intervals, i => i.Date == sunday);

        var result = RestPeriodRule.Evaluate(Profile(), entries, monday, sunday, Config(), intervals);

        // 6 worked days (Mon–Sat) → a weekly rest day exists on Sunday → NO violation.
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
    }

    // ---------------------------------------------------------------
    // 4. Hours-summing checks consume the per-day rows AS-IS: the crossing's post-midnight hours
    //    are counted on D+1 (not D), unchanged beyond what normalization already gives them.
    // ---------------------------------------------------------------
    [Fact]
    public void MaxDailyHours_ConsumesNormalizedPerDayRows_PostMidnightHoursCountOnNextDay()
    {
        var mar31 = new DateOnly(2026, 3, 31);
        var apr01 = new DateOnly(2026, 4, 1);

        // 23:00 → 02:00 with Hours == elapsed = 3 → 1h on 31-Mar, 2h on 01-Apr.
        var crossing = Timed(mar31, 3m, new TimeOnly(23, 0), new TimeOnly(2, 0), Guid.NewGuid());
        var normalized = MidnightCrossingNormalizer.Normalize(new[] { crossing });

        Assert.Equal(1m, normalized.Where(e => e.Date == mar31).Sum(e => e.Hours));
        Assert.Equal(2m, normalized.Where(e => e.Date == apr01).Sum(e => e.Hours));
    }

    // ---------------------------------------------------------------
    // 5. BLOCKER-2 — lower-edge crossing. A shift on periodStart-1 (fetched via the GAP-B widen)
    //    that bleeds past midnight into periodStart must NOT falsely mark periodStart as a worked
    //    day and trip a 7-day weekly-rest violation. The crossing counts under its true start day
    //    (periodStart-1, outside the window); periodStart is a genuine rest day.
    // ---------------------------------------------------------------
    /// <summary>
    /// Kind <b>R</b>. S148 S0-3 (P-12): Sunday 3 May 23:00–23:59 and Monday 4 May 00:00–02:00 are
    /// intervals now, as are the other six days' shifts; expected unchanged (no weekly-rest finding).
    /// Red conditions: a rule that keeps the halves apart makes 4 May a worked day (its own 00:00
    /// stint), so all seven days of the window are worked and both <c>Assert.DoesNotContain</c> calls
    /// fail (M-24).
    /// </summary>
    [Fact]
    public void WeeklyRest_LowerEdgeCrossing_DoesNotFalselyMarkPeriodStartAsWorked()
    {
        var periodStart = new DateOnly(2026, 5, 4);   // Monday
        var periodEnd = periodStart.AddDays(6);        // Sunday (7-day window)

        var entries = new List<TimeEntry>();
        var intervals = new List<ComplianceWorkInterval>();

        // Crossing on periodStart-1 (Sun 3 May), 23:00 → 02:00 — as Skema's two halves; the post-half
        // lands on periodStart.
        entries.Add(HoursOnly(periodStart.AddDays(-1), 0.98m));
        entries.Add(HoursOnly(periodStart, 2m));
        // S148 S0-3 (P-12): the crossing and the shifts are intervals now
        intervals.Add(Iv(periodStart.AddDays(-1), new TimeOnly(23, 0), new TimeOnly(23, 59)));
        intervals.Add(Iv(periodStart, new TimeOnly(0, 0), new TimeOnly(2, 0)));

        // Genuine work on the OTHER 6 days of the window (periodStart+1 .. periodStart+6). periodStart
        // itself has NO shift starting on it — only the crossing's bleed.
        for (int i = 1; i <= 6; i++)
        {
            entries.Add(HoursOnly(periodStart.AddDays(i), 7m));
            intervals.Add(Iv(periodStart.AddDays(i), new TimeOnly(8, 0), new TimeOnly(15, 0)));
        }

        // A periodStart-dated interval DOES exist (the post-half) — a naive date count would hit 7.
        Assert.Contains(intervals, i => i.Date == periodStart);

        var result = RestPeriodRule.Evaluate(Profile(), entries, periodStart, periodEnd, Config(), intervals);

        // 6 worked days (periodStart+1..+6); periodStart is the rest day → NO weekly-rest violation.
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
    }
}
