using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// ADR-039 GAP-A (S132 TASK-1a + 1b-3) — the rule-side rest consumer over midnight-crossing
/// stints. These tests are RED-on-old: the pre-fix day-bucketing rest checks read a bogus ~29h
/// rest for a raw crossing (QUAL-001), a false 0-hour gap at midnight for normalized halves, and a
/// false extra worked-day for a shift bleeding past midnight. Each is now correct because both
/// checks reconstruct one continuous stint from absolute instants (raw crossings via
/// <c>EndTime ≤ StartTime</c>; normalized halves via the shared <c>SourceStintId</c>).
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

    // ---------------------------------------------------------------
    // 1. RAW midnight-crossing entry (one row, un-normalized) reaching the rule: daily rest must
    //    be the 5h gap to the next morning, NOT the pre-fix bogus ~29h (QUAL-001). This is the
    //    absolute-instant (TASK-1a) reconstruction working directly on a crossing row.
    // ---------------------------------------------------------------
    [Fact]
    public void DailyRest_RawMidnightCrossing_ThenMorningShift_IsFiveHourViolation_Not29h()
    {
        var d = new DateOnly(2026, 3, 16);
        var entries = new List<TimeEntry>
        {
            Timed(d, 3m, new TimeOnly(23, 0), new TimeOnly(2, 0)),          // crosses into d+1 02:00
            Timed(d.AddDays(1), 8m, new TimeOnly(7, 0), new TimeOnly(15, 0)), // next morning
        };

        var result = RestPeriodRule.Evaluate(Profile(), entries, d, d.AddDays(6), Config());

        // The stint ends d+1 02:00; next stint starts d+1 07:00 → 5h rest < 11h → VIOLATION.
        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            v.ActualValue == 5.0m);
        // And explicitly NOT the pre-fix ~29h (which read no violation at all).
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST && v.ActualValue > 24m);
    }

    // ---------------------------------------------------------------
    // 2. NORMALIZED halves on the OK boundary: no false 0-hour midnight gap; the rest to the next
    //    morning is the true 5h; and each half carries its own OK-version.
    // ---------------------------------------------------------------
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

        var entries = normalized.Append(
            Timed(apr01, 8m, new TimeOnly(7, 0), new TimeOnly(15, 0))).ToList();

        var result = RestPeriodRule.Evaluate(Profile(), entries, mar31, mar31.AddDays(6), Config());

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
    //    flagged as a 7-day week. The normalized post-half IS dated Sunday, so a naive
    //    distinct-date count would see 7 — the stint-based count sees 6 (the crossing counts under
    //    its Saturday start-day only).
    // ---------------------------------------------------------------
    [Fact]
    public void WeeklyRest_SixDayWeek_WithSaturdayCrossing_NoFalseSeventhDay()
    {
        var monday = new DateOnly(2026, 4, 6);
        var saturday = monday.AddDays(5);
        var sunday = monday.AddDays(6);

        var entries = new List<TimeEntry>();
        for (int i = 0; i < 5; i++) // Mon–Fri day shifts
            entries.Add(Timed(monday.AddDays(i), 7m, new TimeOnly(8, 0), new TimeOnly(15, 0)));

        // Saturday 20:00 → 02:00 Sunday, normalized into two halves sharing a stint id.
        var satCrossing = Timed(saturday, 6m, new TimeOnly(20, 0), new TimeOnly(2, 0), Guid.NewGuid());
        entries.AddRange(MidnightCrossingNormalizer.Normalize(new[] { satCrossing }));

        // Sanity: a Sunday-dated row DOES exist (the post-half) — a naive date count would hit 7.
        Assert.Contains(entries, e => e.Date == sunday);

        var result = RestPeriodRule.Evaluate(Profile(), entries, monday, sunday, Config());

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
    [Fact]
    public void WeeklyRest_LowerEdgeCrossing_DoesNotFalselyMarkPeriodStartAsWorked()
    {
        var periodStart = new DateOnly(2026, 5, 4);   // Monday
        var periodEnd = periodStart.AddDays(6);        // Sunday (7-day window)

        var entries = new List<TimeEntry>();

        // Crossing on periodStart-1 (Sun 3 May), 23:00 → 02:00 — its post-half lands on periodStart.
        var lowerEdge = Timed(periodStart.AddDays(-1), 3m, new TimeOnly(23, 0), new TimeOnly(2, 0), Guid.NewGuid());
        entries.AddRange(MidnightCrossingNormalizer.Normalize(new[] { lowerEdge }));

        // Genuine work on the OTHER 6 days of the window (periodStart+1 .. periodStart+6). periodStart
        // itself has NO shift starting on it — only the crossing's bleed.
        for (int i = 1; i <= 6; i++)
            entries.Add(Timed(periodStart.AddDays(i), 7m, new TimeOnly(8, 0), new TimeOnly(15, 0)));

        // A periodStart-dated row DOES exist (the post-half) — a naive date count would hit 7.
        Assert.Contains(entries, e => e.Date == periodStart);

        var result = RestPeriodRule.Evaluate(Profile(), entries, periodStart, periodEnd, Config());

        // 6 worked days (periodStart+1..+6); periodStart is the rest day → NO weekly-rest violation.
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
    }
}
