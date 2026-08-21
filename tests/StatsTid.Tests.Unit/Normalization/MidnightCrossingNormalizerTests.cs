using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;

namespace StatsTid.Tests.Unit.Normalization;

/// <summary>
/// ADR-039 (S132 TASK-132-1b-1) — midnight-crossing normalization on the calculation/rule
/// INPUT path.
///
/// <para>
/// RED-on-old: every assertion below pins behavior that the BASELINE (no normalization — the
/// crossing shift stays ONE row filed under its start-date/OK24) gets wrong. If
/// <see cref="MidnightCrossingNormalizer.Normalize"/> were the identity function (the baseline),
/// the two-row / D+1 / OK26 / conservation / continuity assertions all fail. The
/// <c>Straddle_OkBoundary_...</c> test additionally shows the concrete numeric bite: the fixture
/// gives OK26 a <c>MaxDailyHours</c> that DIFFERS from OK24 (per the research §5 guidance — with
/// identical placeholder configs the version error is numerically invisible), so the post-midnight
/// hours escaping the OK26 threshold under baseline is observable as a missing violation.
/// </para>
/// </summary>
public class MidnightCrossingNormalizerTests
{
    // The OK24→OK26 boundary is 2026-04-01 (OkVersionResolver). A shift 23:00 on 2026-03-31 →
    // 02:00 on 2026-04-01 is the canonical straddle.
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    // Real monthly periods — the 2-day span [Mar31..Apr01] masked the GAP-B period-edge drop
    // because both halves fell inside it; a whole month exposes it.
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);

    private static TimeEntry Crossing(
        decimal hours,
        TimeOnly start,
        TimeOnly end,
        Guid? sourceStintId = null,
        DateOnly? date = null) => new()
    {
        EmployeeId = "EMP001",
        Date = date ?? Mar31,
        Hours = hours,
        StartTime = start,
        EndTime = end,
        AgreementCode = "AC",
        OkVersion = "OK24", // as filed under the start-date — the baseline (wrong for the D+1 half)
        VoluntaryUnsocialHours = false,
        SourceStintId = sourceStintId,
    };

    // ---------------------------------------------------------------
    // 1. The core split — two rows, correct per-day Date + per-half OK-version, conservation,
    //    shared continuity link. (RED on baseline: baseline yields ONE row, Date=31-Mar, OK24.)
    // ---------------------------------------------------------------
    [Fact]
    public void Crossing_SplitsIntoTwoPerDayRows_WithPerHalfOkVersion()
    {
        var stintId = Guid.NewGuid();
        // 23:00 → 02:00, Hours == elapsed (1h pre + 2h post) = 3h.
        var entry = Crossing(3m, new TimeOnly(23, 0), new TimeOnly(2, 0), stintId);

        var result = MidnightCrossingNormalizer.Normalize(new[] { entry });

        Assert.Equal(2, result.Count);

        var pre = result[0];
        var post = result[1];

        // Per-day attribution.
        Assert.Equal(Mar31, pre.Date);
        Assert.Equal(Apr01, post.Date);

        // Per-half OK-version resolved from each half's OWN date (ADR-003 / D3):
        // the pre-midnight hour stays OK24; the 2 post-midnight hours become OK26.
        Assert.Equal("OK24", pre.OkVersion);
        Assert.Equal("OK26", post.OkVersion);

        // Hours land on the correct day (1h on 31-Mar, 2h on 01-Apr).
        Assert.Equal(1m, pre.Hours);
        Assert.Equal(2m, post.Hours);

        // D2 conservation — halves sum EXACTLY to the original, nothing dropped/doubled.
        Assert.Equal(entry.Hours, pre.Hours + post.Hours);

        // D4 continuity link — both halves carry the SAME source-stint identity.
        Assert.Equal(stintId, pre.SourceStintId);
        Assert.Equal(stintId, post.SourceStintId);
        Assert.Equal(pre.SourceStintId, post.SourceStintId);

        // Split clock encoding (the TASK-1b-3 contract): pre = [Start → 00:00], post = [00:00 → End].
        Assert.Equal(new TimeOnly(23, 0), pre.StartTime);
        Assert.Equal(TimeOnly.MinValue, pre.EndTime);
        Assert.Equal(TimeOnly.MinValue, post.StartTime);
        Assert.Equal(new TimeOnly(2, 0), post.EndTime);
    }

    // ---------------------------------------------------------------
    // 2. D7 — Hours != elapsed wall-clock time is allocated PROPORTIONALLY to each half's
    //    elapsed duration, and still conserves exactly.
    // ---------------------------------------------------------------
    [Fact]
    public void Crossing_HoursDifferFromElapsed_AllocatesProportionally_AndConserves()
    {
        // Elapsed is 1h pre + 2h post = 3h, but Hours is 6 (e.g. a manual/rounded figure).
        // Proportional: pre = 6 * (1/3) = 2, post = 6 - 2 = 4.
        var entry = Crossing(6m, new TimeOnly(23, 0), new TimeOnly(2, 0));

        var result = MidnightCrossingNormalizer.Normalize(new[] { entry });

        Assert.Equal(2, result.Count);
        Assert.Equal(2m, result[0].Hours);
        Assert.Equal(4m, result[1].Hours);
        Assert.Equal(6m, result[0].Hours + result[1].Hours); // exact conservation
    }

    // ---------------------------------------------------------------
    // 3. Non-crossing entries pass through unchanged and in place.
    // ---------------------------------------------------------------
    [Fact]
    public void NonCrossing_PassesThroughUnchanged()
    {
        var normal = Crossing(7m, new TimeOnly(9, 0), new TimeOnly(16, 0)); // end > start
        var result = MidnightCrossingNormalizer.Normalize(new[] { normal });

        Assert.Single(result);
        Assert.Same(normal, result[0]);
        Assert.False(MidnightCrossingNormalizer.IsMidnightCrossing(normal));
    }

    // ---------------------------------------------------------------
    // 3b. A shift ending EXACTLY at midnight (17:00 → 00:00) is NOT a crossing (no next-day
    //     portion) — it stays one row on day D.
    // ---------------------------------------------------------------
    [Fact]
    public void EndsExactlyAtMidnight_IsNotCrossing()
    {
        var entry = Crossing(7m, new TimeOnly(17, 0), TimeOnly.MinValue);
        Assert.False(MidnightCrossingNormalizer.IsMidnightCrossing(entry));

        var result = MidnightCrossingNormalizer.Normalize(new[] { entry });
        Assert.Single(result);
    }

    // ---------------------------------------------------------------
    // 4. Idempotency — normalizing already-normalized rows is a no-op (guards double-application
    //    on any path; the emitted pre-half [Start → 00:00] is not re-detected as a crossing).
    // ---------------------------------------------------------------
    [Fact]
    public void Normalize_IsIdempotent()
    {
        var entry = Crossing(3m, new TimeOnly(23, 0), new TimeOnly(2, 0), Guid.NewGuid());

        var once = MidnightCrossingNormalizer.Normalize(new[] { entry });
        var twice = MidnightCrossingNormalizer.Normalize(once);

        Assert.Equal(once.Count, twice.Count);
        for (int i = 0; i < once.Count; i++)
        {
            Assert.Equal(once[i].Date, twice[i].Date);
            Assert.Equal(once[i].Hours, twice[i].Hours);
            Assert.Equal(once[i].OkVersion, twice[i].OkVersion);
            Assert.Equal(once[i].StartTime, twice[i].StartTime);
            Assert.Equal(once[i].EndTime, twice[i].EndTime);
        }
    }

    // ---------------------------------------------------------------
    // 5. The OK-version bite over REAL MONTHLY periods (GAP-B): the crossing sits on the LAST day
    //    of March. Its 1 pre-midnight hour belongs to March/OK24; its 2 post-midnight hours belong
    //    to 01-Apr/OK26. No hours may be dropped at the boundary, and the 01-Apr hours must be
    //    judged under OK26 (whose MaxDailyHours DIFFERS from OK24 — else the version error is
    //    invisible). The 2-day span used previously masked the drop because both halves fell inside
    //    it; a whole month exposes it.
    // ---------------------------------------------------------------
    [Fact]
    public void Straddle_OkBoundary_OverMonthlyPeriods_NoDroppedHours_PostMidnightUnderOk26()
    {
        // Crossing filed on 31-Mar, 23:00→02:00, 3h (1h pre + 2h post elapsed).
        var entry = Crossing(3m, new TimeOnly(23, 0), new TimeOnly(2, 0), Guid.NewGuid());
        var profile = Profile();
        var ok26 = Config(okVersion: "OK26", maxDaily: 1.5m); // DIFFERS from OK24 (13h)

        var normalized = MidnightCrossingNormalizer.Normalize(new[] { entry });

        // Per-half OK-version (D3): pre stays OK24 on 31-Mar; post becomes OK26 on 01-Apr.
        Assert.Equal("OK24", normalized.Single(e => e.Date == Mar31).OkVersion);
        Assert.Equal("OK26", normalized.Single(e => e.Date == Apr01).OkVersion);

        // --- No dropped hours across the month boundary. Each month's period keeps exactly its
        //     own half; the two sum to the original 3h (D2 conservation across the boundary).
        var marchHours = normalized.Where(e => e.Date >= Mar01 && e.Date <= Mar31).Sum(e => e.Hours);
        var aprilHours = normalized.Where(e => e.Date >= Apr01 && e.Date <= Apr30).Sum(e => e.Hours);
        Assert.Equal(1m, marchHours);
        Assert.Equal(2m, aprilHours);
        Assert.Equal(entry.Hours, marchHours + aprilHours); // nothing dropped, nothing doubled

        // --- BASELINE (RED-on-old): without normalization the raw crossing is filed under 31-Mar,
        //     so an APRIL-period read sees NOTHING — the 2 post-midnight hours are lost from April
        //     entirely (and mis-attributed to March/OK24). This is the drop GAP-B fixes.
        var rawInApril = new[] { entry }.Where(e => e.Date >= Apr01 && e.Date <= Apr30).ToList();
        Assert.Empty(rawInApril);

        // --- FIXED: the 01-Apr half (2h) is judged under OK26 (2h > 1.5h) → VIOLATION on 01-Apr,
        //     within a real April monthly period. The rule's own period filter drops the 31-Mar
        //     pre-half (it belongs to March). This is the outcome the baseline cannot produce.
        var aprilResult = RestPeriodRule.EvaluateMaxDailyHours(profile, normalized, Apr01, Apr30, ok26);
        Assert.Contains(aprilResult.Violations, v =>
            v.ViolationType == ComplianceViolationType.MAX_DAILY_HOURS &&
            v.Date == Apr01 &&
            v.ActualValue == 2m &&
            v.ThresholdValue == 1.5m);
    }

    // ---------------------------------------------------------------
    // 6. BLOCKER-1 — a crossing whose source carries NO SourceStintId still emits two halves that
    //    share a DETERMINISTIC continuity link (so a rest check can rejoin them; not Guid.NewGuid).
    // ---------------------------------------------------------------
    [Fact]
    public void Crossing_NullSourceId_DerivesDeterministicSharedLink()
    {
        var entry = Crossing(3m, new TimeOnly(23, 0), new TimeOnly(2, 0), sourceStintId: null);

        var result = MidnightCrossingNormalizer.Normalize(new[] { entry });

        Assert.Equal(2, result.Count);
        Assert.NotNull(result[0].SourceStintId);                       // a shared id was minted
        Assert.Equal(result[0].SourceStintId, result[1].SourceStintId); // both halves rejoinable

        // Deterministic: an identical source produces the SAME id (pure / replay-stable).
        var again = MidnightCrossingNormalizer.Normalize(
            new[] { Crossing(3m, new TimeOnly(23, 0), new TimeOnly(2, 0), sourceStintId: null) });
        Assert.Equal(result[0].SourceStintId, again[0].SourceStintId);
    }

    // ---------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------
    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "STANDARD",
    };

    private static AgreementRuleConfig Config(string okVersion, decimal maxDaily) => new()
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
        MaxDailyHours = maxDaily,
        MinimumRestHours = 11.0m,
        RestPeriodDerogationAllowed = false,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
    };
}
