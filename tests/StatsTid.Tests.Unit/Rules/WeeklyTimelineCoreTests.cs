using System.Reflection;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S145 / TASK-14506 — the pure week-by-week core (<c>WeeklyTimelineCore.Evaluate</c>, TASK-14505),
/// pinned from the SPEC: refinement A2 (:232-272), the precision rule (:548), Q2 and Q3 (:482-502),
/// the Stage A criteria "Weekly judging" through "Flex" (:581-620).
///
/// <para>
/// <b>Plain language — the defect these pins exist for.</b> Before S145 the overtime, norm and flex
/// rules compared a whole <i>period's</i> hours with <i>one week's</i> norm. A month of full-time
/// hours (22 weekdays × 7.40 = 162.80 h) was judged against 37 h, so every ordinary September
/// "earned" about 126 h of overtime and flex that nobody worked. The core judges <b>week by week and
/// day by day</b> instead: each weekday carries its own norm from that day's contract; a Monday–Sunday
/// week is settled by the month that contains its <b>Sunday</b> and its overtime is dated that
/// Sunday (a leaver's last week is settled on their last day); a month pays only what is dated
/// inside it. Days in the month's first week that belong to the previous month ("context days") are
/// judged but never counted toward the month's own norm, hours or lines.
/// </para>
///
/// <para>
/// <b>Calendar facts used throughout</b> (verified, 2026): Tuesday 1 September; Monday 31 August is
/// the context day of September's opening week; Sundays 6, 13, 20, 27 September and 4 October;
/// Wednesday 30 September; September has 22 weekdays and no public holiday; 1 April is a Wednesday
/// (the OK24→OK26 boundary), so the week 30 March – 5 April crosses it. A full-time HK weekday is
/// 7.40 h (norm), 7.40 h (50% threshold) and 8.00 h (100% threshold); a Monday–Sunday week is
/// 37.00 / 37.00 / 40.00. Overtime: hours up to the norm pay nothing, hours from the 50% to the
/// 100% threshold pay OVERTIME_50, hours above pay OVERTIME_100; AC pays the whole excess as
/// MERARBEJDE. All expected figures below are hand-derived from those rules.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate — these pins name types TASK-14505 creates,
/// so a compile error is not RED; <c>docs/AGENTS.md</c> § "RED evidence"). Each fact names its
/// mutation: MU-01 (the whole window as one week) · MU-02 (lines dated on the Monday) · MU-03 (week
/// settled by the month of its Monday) · MU-04 (<c>SettlementCutoff</c> ignored) · MU-05 (norm for
/// uncovered weekdays) · MU-06 (one config per week, the Sunday's) · MU-07 (one fraction per week,
/// the Sunday's) · MU-08 (NORMAL_HOURS for context entries) · MU-09 (mode from the week's first
/// covered day) · MU-10 (annual mode through weekday norms) · MU-11 (flex clamped per week) · MU-12
/// (cap from the first covered day) · MU-13 (default credit <c>7.4 × fraction</c>, unrounded) · MU-41 (pieces beyond <c>PeriodEnd</c> governing) · MU-42 (default credit takes the settlement day's fraction, not the absence day's).
/// </para>
/// </summary>
public sealed class WeeklyTimelineCoreTests
{
    private const string Emp = "EMP-S145";

    // ---- the days the specs talk about ---------------------------------------------------------
    private static readonly DateOnly Mar30 = new(2026, 3, 30);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);
    private static readonly DateOnly Apr3 = new(2026, 4, 3);
    private static readonly DateOnly Apr5 = new(2026, 4, 5);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep2 = new(2026, 9, 2);
    private static readonly DateOnly Sep3 = new(2026, 9, 3);
    private static readonly DateOnly Sep4 = new(2026, 9, 4);
    private static readonly DateOnly Sep6 = new(2026, 9, 6);
    private static readonly DateOnly Sep7 = new(2026, 9, 7);
    private static readonly DateOnly Sep8 = new(2026, 9, 8);
    private static readonly DateOnly Sep9 = new(2026, 9, 9);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private static readonly DateOnly Sep11 = new(2026, 9, 11);
    private static readonly DateOnly Sep14 = new(2026, 9, 14);
    private static readonly DateOnly Sep15 = new(2026, 9, 15);
    private static readonly DateOnly Sep16 = new(2026, 9, 16);
    private static readonly DateOnly Sep18 = new(2026, 9, 18);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);
    private static readonly DateOnly Sep21 = new(2026, 9, 21);
    private static readonly DateOnly Sep25 = new(2026, 9, 25);
    private static readonly DateOnly Sep28 = new(2026, 9, 28);
    private static readonly DateOnly Sep29 = new(2026, 9, 29);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct2 = new(2026, 10, 2);
    private static readonly DateOnly Oct4 = new(2026, 10, 4);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    // ---- the live configs the facts run on -----------------------------------------------------
    private static readonly AgreementRuleConfig Hk24 = CentralAgreementConfigs.GetConfig("HK", "OK24");
    private static readonly AgreementRuleConfig Hk26 = CentralAgreementConfigs.GetConfig("HK", "OK26");
    private static readonly AgreementRuleConfig Ac26 = CentralAgreementConfigs.GetConfig("AC", "OK26");
    private static readonly AgreementRuleConfig AcDepartmentHead26 = CentralAgreementConfigs.GetConfig("AC", "OK26", "DEPARTMENT_HEAD");
    private static readonly AgreementRuleConfig AcResearch26 = CentralAgreementConfigs.GetConfig("AC_RESEARCH", "OK26");

    // =============================================================================================
    // helpers
    // =============================================================================================

    /// <summary>Init-only properties and no <c>with</c>: a synthetic config is a reflection copy with edits.</summary>
    private static AgreementRuleConfig With(AgreementRuleConfig source, params (string Property, object Value)[] changes)
    {
        var edits = changes.ToDictionary(c => c.Property, c => c.Value);
        var clone = (AgreementRuleConfig)Activator.CreateInstance(typeof(AgreementRuleConfig))!;
        foreach (var property in typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            property.SetValue(clone, edits.TryGetValue(property.Name, out var value) ? value : property.GetValue(source));
        return clone;
    }

    private static ResolvedPiece Piece(DateOnly from, DateOnly to, decimal fraction, AgreementRuleConfig config) =>
        new(from, to, fraction, config);

    private static TimeEntry Entry(DateOnly day, decimal hours) => new()
    {
        EmployeeId = Emp,
        Date = day,
        Hours = hours,
        AgreementCode = "HK",
        OkVersion = "OK26",
    };

    /// <summary>One entry per WEEKDAY in [from, to] at <paramref name="perDay"/> hours, with bands overriding ranges.</summary>
    private static List<TimeEntry> WeekdayHours(
        DateOnly from, DateOnly to, decimal perDay, params (DateOnly From, DateOnly To, decimal Hours)[] bands)
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
            entries.Add(Entry(day, hours));
        }
        return entries;
    }

    private static TimelineCoreResult Run(
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly? settlementCutoff,
        IReadOnlyList<ResolvedPiece> pieces,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry>? absences = null,
        decimal previousFlexBalance = 0m) =>
        WeeklyTimelineCore.Evaluate(new TimelineCoreInput(
            Emp, periodStart, periodEnd, settlementCutoff, pieces, entries,
            absences ?? Array.Empty<AbsenceEntry>(), previousFlexBalance));

    private static decimal Val(decimal? value)
    {
        Assert.NotNull(value);
        return value!.Value;
    }

    /// <summary>Exact line set, order-insensitive (the spec fixes the lines, not their order).</summary>
    private static void AssertLines(CalculationResult result, params (string TimeType, decimal Hours, decimal Rate, DateOnly Date)[] expected)
    {
        static List<(string, decimal, decimal, DateOnly)> Sorted(IEnumerable<(string TimeType, decimal Hours, decimal Rate, DateOnly Date)> lines) =>
            lines.OrderBy(l => l.Date).ThenBy(l => l.TimeType, StringComparer.Ordinal)
                 .Select(l => (l.TimeType, l.Hours, l.Rate, l.Date)).ToList();

        var actual = Sorted(result.LineItems.Select(l => (l.TimeType, l.Hours, l.Rate, l.Date)));
        Assert.Equal(Sorted(expected), actual);
    }

    /// <summary>No overtime/merarbejde line at all, and no flex payout either.</summary>
    private static void AssertNoOvertimeLinesAndNoFlexPayout(TimelineCoreResult result)
    {
        Assert.Equal("OVERTIME_CALC", result.Overtime.RuleId);
        Assert.True(result.Overtime.Success);
        Assert.Empty(result.Overtime.LineItems);
        Assert.Null(result.FlexPayout);
    }

    // =============================================================================================
    // A1 — the defect pinned, then fixed (refinement :581)
    // =============================================================================================

    /// <summary>
    /// September 2026, full-time HK, 7.40 h every weekday INCLUDING Monday 31 August (a context day),
    /// opening flex balance 0. Today: OVERTIME_50 3.0, OVERTIME_100 122.8, norm 37, flex +125.8.
    /// After: no overtime, norm 162.80 (22 × 7.40), flex delta 0, no payout. RED by MU-01.
    /// </summary>
    [Fact]
    public void Core_September2026_FullTimeHK_AtNorm_With31AugContext_NoOvertime_NormTotal16280_FlexZero()
    {
        var entries = WeekdayHours(Aug31, Sep30, 7.40m);

        var result = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Hk26)], entries);

        AssertNoOvertimeLinesAndNoFlexPayout(result);

        Assert.Equal("NORM_CHECK_37H", result.Norm.RuleId);
        Assert.Equal(162.80m, Val(result.Norm.NormHoursTotal));        // 22 weekdays × 7.40, the month's own days only
        Assert.Equal(162.80m, Val(result.Norm.ActualHoursTotal));      // 31 August's 7.40 is context: not counted
        Assert.Equal(0m, Val(result.Norm.Deviation));
        Assert.True(result.Norm.NormFulfilled);

        Assert.Equal(0m, result.Flex.PreviousBalance);
        Assert.Equal(0m, result.Flex.Delta);
        Assert.Equal(0m, result.Flex.NewBalance);
        Assert.Equal(0m, result.Flex.ExcessForPayout);
        Assert.Equal(162.80m, result.Flex.WorkedHours);
        Assert.Equal(162.80m, result.Flex.NormHours);
    }

    /// <summary>The AC twin of the defect (refinement :581): before 125.8 merarbejde, after 0. RED by MU-01.</summary>
    [Fact]
    public void Core_September2026_FullTimeAC_AtNorm_With31AugContext_NoMerarbejde()
    {
        var entries = WeekdayHours(Aug31, Sep30, 7.40m);

        var result = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Ac26)], entries);

        AssertNoOvertimeLinesAndNoFlexPayout(result);
        Assert.Equal(162.80m, Val(result.Norm.NormHoursTotal));
        Assert.Equal(0m, result.Flex.Delta);
    }

    // =============================================================================================
    // Weekly judging, F1 settlement (refinement :582-587)
    // =============================================================================================

    /// <summary>
    /// "Weekly judging, stated tail": the week 14–20 September worked at 8 h per weekday = 40.00 h
    /// against norm 37.00 (5 × 7.40) and 100% threshold 40.00 (5 × 8.00): excess 3.00, all of it
    /// inside the 50% band. September pays exactly OVERTIME_50 3.00, dated SUNDAY 20 September, and
    /// nothing else. RED by MU-02 (dated Monday) and MU-01.
    /// </summary>
    [Fact]
    public void Core_StatedTail_Week14To20At8h_PaysOvertime50_300_DatedSunday20Sep_Only()
    {
        var entries = WeekdayHours(Aug31, Sep30, 7.40m, (Sep14, Sep18, 8.00m));

        var result = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Hk26)], entries);

        AssertLines(result.Overtime, ("OVERTIME_50", 3.00m, 1.5m, Sep20));
        // The same 3.00 h is also this month's flex surplus (worked 165.80 vs norm 162.80), and 3.00 is
        // far below the HK cap of 100, so nothing is paid out.
        Assert.Equal(3.00m, result.Flex.Delta);
        Assert.Equal(3.00m, result.Flex.NewBalance);
        Assert.Null(result.FlexPayout);
    }

    /// <summary>
    /// F1 closing week (fixtures per R18): 28–30 September worked 9 h each, 1–2 October 7.40 h each =
    /// 41.80 h in the week 28 Sep – 4 Oct (norm 37.00, thresholds 37.00 / 40.00): OVERTIME_50 3.00 +
    /// OVERTIME_100 1.80. That week's SUNDAY is 4 October, so OCTOBER settles it, dated 4 October;
    /// September pays nothing for it (its 3 × 1.60 = 4.80 h of extra hours are flex, not overtime).
    /// RED by MU-03 (week settled by the month of its Monday).
    /// </summary>
    [Fact]
    public void Core_ClosingWeek_SettledByOctober_Dated4Oct_SeptemberPaysNothing()
    {
        var entries = WeekdayHours(Aug31, Oct31, 7.40m, (Sep28, Sep30, 9.00m));

        // September: closing week is NOT settled here.
        var september = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Hk26)], entries.Where(e => e.Date <= Sep30).ToList());
        AssertNoOvertimeLinesAndNoFlexPayout(september);
        Assert.Equal(4.80m, september.Flex.Delta); // 3 days × (9.00 − 7.40)

        // October: opening week starts Monday 28 September (three context days), pieces start there.
        var october = Run(Oct1, Oct31, null, [Piece(Sep28, Oct31, 1.0m, Hk26)], entries.Where(e => e.Date >= Sep28).ToList());
        AssertLines(
            october.Overtime,
            ("OVERTIME_50", 3.00m, 1.5m, Oct4),
            ("OVERTIME_100", 1.80m, 2.0m, Oct4));

        // October's own figures never include the three context days: 22 weekdays × 7.40, flex delta 0
        // (the context days' 27.00 h are NOT October's flex), and no NORMAL_HOURS line dated in September.
        Assert.Equal(162.80m, Val(october.Norm.NormHoursTotal));
        Assert.Equal(162.80m, Val(october.Norm.ActualHoursTotal));
        Assert.Equal(0m, october.Flex.Delta);
        Assert.DoesNotContain(october.Norm.LineItems, l => l.Date < Oct1);
    }

    /// <summary>
    /// F1 tail crossing: 28 September – 2 October worked 8 h each = 40.00 h → OVERTIME_50 3.00, dated
    /// 4 October, paid by OCTOBER. September pays nothing. RED by MU-03.
    /// </summary>
    [Fact]
    public void Core_TailCrossing_OctoberPays300_Dated4Oct()
    {
        var entries = WeekdayHours(Aug31, Oct31, 7.40m, (Sep28, Oct2, 8.00m));

        var september = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Hk26)], entries.Where(e => e.Date <= Sep30).ToList());
        AssertNoOvertimeLinesAndNoFlexPayout(september);

        var october = Run(Oct1, Oct31, null, [Piece(Sep28, Oct31, 1.0m, Hk26)], entries.Where(e => e.Date >= Sep28).ToList());
        AssertLines(october.Overtime, ("OVERTIME_50", 3.00m, 1.5m, Oct4));
    }

    // =============================================================================================
    // Employment edges (refinement :597-601)
    // =============================================================================================

    /// <summary>
    /// Leaver whose last day is Tuesday 29 September, working 9 h on 28 and 29 September. The week
    /// 28 Sep – 4 Oct is settled by the LAST EMPLOYED DAY (the cutoff), i.e. in September, dated 29
    /// September, and judged on its two employed days only: norm 14.80 (2 × 7.40), 50% threshold
    /// 14.80, 100% threshold 16.00 (2 × 8.00); 18.00 h worked → OVERTIME_50 1.20 (14.80→16.00) +
    /// OVERTIME_100 2.00 (16.00→18.00). RED by MU-04 (cutoff ignored: the week then belongs to October
    /// and September pays nothing).
    /// </summary>
    [Fact]
    public void Core_Leaver_LastDayTue29Sep_Pays120And200_Dated29Sep()
    {
        var entries = WeekdayHours(Aug31, Sep29, 7.40m, (Sep28, Sep29, 9.00m));

        var result = Run(Sep1, Sep30, Sep29, [Piece(Aug31, Sep29, 1.0m, Hk26)], entries);

        AssertLines(
            result.Overtime,
            ("OVERTIME_50", 1.20m, 1.5m, Sep29),
            ("OVERTIME_100", 2.00m, 2.0m, Sep29));

        // Wednesday 30 September is not employed, so it carries no norm: 21 weekdays × 7.40.
        Assert.Equal(155.40m, Val(result.Norm.NormHoursTotal));
    }

    /// <summary>
    /// Starter hired Thursday 3 September: the opening week 31 Aug – 6 Sep counts norm only for the two
    /// employed days, 3 and 4 September (2 × 7.40 = 14.80; 50% threshold 14.80; 100% threshold 16.00),
    /// not for the uncovered Monday–Wednesday. Worked 8 h on both = 16.00 h → OVERTIME_50 1.20, dated
    /// Sunday 6 September (had the week been judged against 37, nothing would be paid). The month's own
    /// norm is the 20 employed weekdays: 148.00. RED by MU-05.
    /// </summary>
    [Fact]
    public void Core_Starter_Thu3Sep_OpeningWeekNormOnlyEmployedDays_1480()
    {
        var entries = WeekdayHours(Sep3, Sep30, 7.40m, (Sep3, Sep4, 8.00m));

        var result = Run(Sep1, Sep30, null, [Piece(Sep3, Sep30, 1.0m, Hk26)], entries);

        AssertLines(result.Overtime, ("OVERTIME_50", 1.20m, 1.5m, Sep6));
        Assert.Equal(148.00m, Val(result.Norm.NormHoursTotal));
    }

    // =============================================================================================
    // OK-boundary week and opening-week fraction change (refinement :602-607)
    // =============================================================================================

    /// <summary>
    /// The week 30 March – 5 April 2026 crosses the OK24→OK26 boundary (1 April). With SYNTHETIC OK26
    /// settings (<c>WeeklyNormHours</c> and <c>OvertimeThreshold50</c> both 36; the live OK26 values are
    /// identical placeholders) each weekday uses its own date's config: Mon–Tue 7.40 (OK24), Wed–Fri 7.20
    /// (OK26) → norm 36.40, 50% threshold 36.40, 100% threshold 40.00. At 8 h per weekday (40.00 h) it
    /// pays OVERTIME_50 3.60, dated Sunday 5 April, and nothing above. The pieces' 30–31 March are
    /// context days: April's own norm is 22 weekdays × 7.20 = 158.40. RED by MU-06 (one config per
    /// week, the Sunday's: norm 36.00 and a different excess).
    /// </summary>
    [Fact]
    public void Core_OkBoundaryWeek_SyntheticOk26_PerDayConfig_Norm3640_Overtime50_360()
    {
        var syntheticOk26 = With(Hk26, ("WeeklyNormHours", 36.0m), ("OvertimeThreshold50", 36.0m));
        var entries = WeekdayHours(Mar30, Apr30, 7.20m, (Mar30, Apr3, 8.00m));

        var result = Run(
            Apr1, Apr30, null,
            [Piece(Mar30, Mar31, 1.0m, Hk24), Piece(Apr1, Apr30, 1.0m, syntheticOk26)],
            entries);

        AssertLines(result.Overtime, ("OVERTIME_50", 3.60m, 1.5m, Apr5));
        Assert.Equal(158.40m, Val(result.Norm.NormHoursTotal));
    }

    /// <summary>
    /// Opening-week fraction change (Q2 = A): 37 → 30 h effective Tuesday 1 September (stored fraction
    /// 0.811). The week 31 Aug – 6 Sep is worked at 8 h per weekday. Per day: Monday 7.40 / 8.00
    /// (fraction 1.0), Tuesday–Friday 6.00 / 6.49 (fraction 0.811: 37 × 0.811 ÷ 5 = 6.0014 → 6.00;
    /// 40 × 0.811 ÷ 5 = 6.488 → 6.49). Norm 7.40 + 4 × 6.00 = 31.40; 100% threshold 8.00 + 4 × 6.49 =
    /// 33.96; worked 40.00 → excess 8.60 → OVERTIME_50 2.56 (31.40→33.96) + OVERTIME_100 6.04
    /// (33.96→40.00), dated Sunday 6 September. September's own norm: 22 × 6.00 = 132.00. RED by MU-07
    /// (one fraction per week: the Sunday's 0.811 gives 30.00 / 32.45 and 2.45 / 7.55).
    /// </summary>
    [Fact]
    public void Core_OpeningWeekFractionChange_Tue1Sep_Norm3140_Thr3396_HK256And604()
    {
        var entries = WeekdayHours(Aug31, Sep30, 6.00m, (Aug31, Sep4, 8.00m));

        var result = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Aug31, 1.0m, Hk26), Piece(Sep1, Sep30, 0.811m, Hk26)],
            entries);

        AssertLines(
            result.Overtime,
            ("OVERTIME_50", 2.56m, 1.5m, Sep6),
            ("OVERTIME_100", 6.04m, 2.0m, Sep6));
        Assert.Equal(132.00m, Val(result.Norm.NormHoursTotal));
    }

    /// <summary>The AC twin of the fraction change: same week, same norm 31.40, excess 8.60, all MERARBEJDE at 1.0. RED by MU-07.</summary>
    [Fact]
    public void Core_OpeningWeekFractionChange_Tue1Sep_AC_Merarbejde860()
    {
        var entries = WeekdayHours(Aug31, Sep30, 6.00m, (Aug31, Sep4, 8.00m));

        var result = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Aug31, 1.0m, Ac26), Piece(Sep1, Sep30, 0.811m, Ac26)],
            entries);

        AssertLines(result.Overtime, ("MERARBEJDE", 8.60m, 1.0m, Sep6));
    }

    /// <summary>
    /// Q2 = A for a change in the MIDDLE of a settled week (refinement :491; no context day involved):
    /// 37 → 30 h effective Wednesday 16 September, the week 14–20 September worked at 8 h per weekday.
    /// Monday–Tuesday: norm 2 × 7.40 = 14.80, 100% threshold 2 × 8.00 = 16.00. Wednesday–Friday (fraction
    /// 0.811): norm 3 × 6.00 = 18.00, 100% threshold 3 × 6.49 = 19.47. Week: norm 32.80, 100% threshold
    /// 35.47, worked 40.00 → excess 7.20 → HK OVERTIME_50 2.67 (32.80→35.47) + OVERTIME_100 4.53
    /// (35.47→40.00); AC MERARBEJDE 7.20. All dated Sunday 20 September. The naive split — a full
    /// week's norm for each part — would pay nothing. RED by MU-07 (one fraction per week) and MU-01.
    /// </summary>
    [Fact]
    public void Core_MidWeekFractionChange_Wed16Sep_Norm3280_Thr3547_HK267And453_AC720()
    {
        // Every other week sits exactly at its own norm (7.40 per day before the change, 6.00 after).
        var entries = WeekdayHours(Aug31, Sep30, 7.40m, (Sep14, Sep18, 8.00m), (Sep21, Sep30, 6.00m));

        var hk = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Sep15, 1.0m, Hk26), Piece(Sep16, Sep30, 0.811m, Hk26)],
            entries);
        AssertLines(
            hk.Overtime,
            ("OVERTIME_50", 2.67m, 1.5m, Sep20),
            ("OVERTIME_100", 4.53m, 2.0m, Sep20));

        var ac = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Sep15, 1.0m, Ac26), Piece(Sep16, Sep30, 0.811m, Ac26)],
            entries);
        AssertLines(ac.Overtime, ("MERARBEJDE", 7.20m, 1.0m, Sep20));
    }

    // =============================================================================================
    // Window contract, norm modes (refinement :608-614, R6)
    // =============================================================================================

    /// <summary>
    /// R6: <c>NORMAL_HOURS</c> lines are one per entry dated INSIDE the month, and never for a context
    /// day. September full-time: <c>NormHoursTotal</c> 162.80; <c>ActualHoursTotal</c> equals the
    /// in-month entries only; one line per in-month entry (a day split in two entries gives two lines)
    /// and none for 31 August. RED by MU-08.
    /// </summary>
    [Fact]
    public void Core_WindowContract_NoNormalHoursForContextDays()
    {
        var entries = WeekdayHours(Aug31, Sep30, 7.40m);
        // Split Tuesday 8 September into two 3.70 h entries: "one line per ENTRY", not per day.
        entries.RemoveAll(e => e.Date == Sep8);
        entries.Add(Entry(Sep8, 3.70m));
        entries.Add(Entry(Sep8, 3.70m));

        var result = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Hk26)], entries);

        Assert.Equal(162.80m, Val(result.Norm.NormHoursTotal));
        Assert.Equal(162.80m, Val(result.Norm.ActualHoursTotal));

        var inMonth = entries.Where(e => e.Date >= Sep1 && e.Date <= Sep30).ToList();
        Assert.Equal(23, inMonth.Count); // 22 weekdays, one of them as two entries

        Assert.All(result.Norm.LineItems, l =>
        {
            Assert.Equal("NORMAL_HOURS", l.TimeType);
            Assert.Equal(1.0m, l.Rate);
        });
        Assert.DoesNotContain(result.Norm.LineItems, l => l.Date == Aug31);
        Assert.Equal(
            inMonth.Select(e => (e.Date, e.Hours)).OrderBy(x => x.Date).ThenBy(x => x.Hours).ToList(),
            result.Norm.LineItems.Select(l => (l.Date, l.Hours)).OrderBy(x => x.Date).ThenBy(x => x.Hours).ToList());
    }

    /// <summary>
    /// R6: a week whose norm mode changes mid-week is governed by the mode in force on its SUNDAY (the
    /// last covered day of the week). Only <c>NormPeriodWeeks</c> can differ inside a week today
    /// (<c>NormModel</c> is per agreement), so the pin uses it: AC base (1) versus AC
    /// DEPARTMENT_HEAD (4). Leg A: Monday on the base config, Tuesday–Sunday on DEPARTMENT_HEAD → the
    /// result reports 4. Leg B (the reverse): Monday–Wednesday on DEPARTMENT_HEAD, Thursday–Sunday on
    /// the base → 1 (so "always the larger" and "always 4" also fail). Both legs run the week
    /// 31 Aug – 6 Sep as a one-week period, the only shape in which the opening week IS the last week.
    /// Leg C is the refinement's whole-month case: a position change effective Tuesday 1 September →
    /// the opening week reports under the Sunday's mode (4) and the total is unchanged (162.80).
    /// The total is the per-day sum in every leg (29.60 = 4 × 7.40 for 1–6 Sep). RED by MU-09.
    /// </summary>
    [Fact]
    public void Core_WeekWithNormPeriodWeeksChange_SundayModeGoverns()
    {
        // Leg A.
        var entriesWeek = WeekdayHours(Aug31, Sep4, 7.40m);
        var legA = Run(
            Sep1, Sep6, null,
            [Piece(Aug31, Aug31, 1.0m, Ac26), Piece(Sep1, Sep6, 1.0m, AcDepartmentHead26)],
            entriesWeek);
        Assert.Equal(4, legA.Norm.NormPeriodWeeks);
        Assert.Equal(29.60m, Val(legA.Norm.NormHoursTotal));
        AssertNoOvertimeLinesAndNoFlexPayout(legA);

        // Leg B.
        var legB = Run(
            Sep1, Sep6, null,
            [Piece(Aug31, Sep2, 1.0m, AcDepartmentHead26), Piece(Sep3, Sep6, 1.0m, Ac26)],
            entriesWeek);
        Assert.Equal(1, legB.Norm.NormPeriodWeeks);
        Assert.Equal(29.60m, Val(legB.Norm.NormHoursTotal));
        AssertNoOvertimeLinesAndNoFlexPayout(legB);

        // Leg C: the whole month.
        var legC = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Aug31, 1.0m, Ac26), Piece(Sep1, Sep30, 1.0m, AcDepartmentHead26)],
            WeekdayHours(Aug31, Sep30, 7.40m));
        Assert.Equal(4, legC.Norm.NormPeriodWeeks);
        Assert.Equal(162.80m, Val(legC.Norm.NormHoursTotal));
        AssertNoOvertimeLinesAndNoFlexPayout(legC);
    }

    /// <summary>
    /// A piece that starts after <c>PeriodEnd</c> must not govern the closing week (TASK-14505 Step 5a,
    /// W1). September 2026 ends on Wednesday 30 September, so the closing week Mon 28 Sep – Sun 4 Oct
    /// runs past the period end; a piece covering Thu 1 – Sun 4 Oct would otherwise be the week's
    /// Sunday piece and decide its mode and its reported norm-period length. Here that second piece
    /// differs from September's HK OK26 config in <c>NormPeriodWeeks</c> (4) AND in
    /// <c>NormModel</c> (ANNUAL_ACTIVITY, the field <c>ModeFor</c> reads), so BOTH the mode and the
    /// label would move if it governed. Called directly on the core with two resolved configs (the
    /// endpoint resolves config from the agreement code and cannot be given such a piece). Leg 1 has
    /// <c>SettlementCutoff</c> null — with a cutoff at 30 Sep the cutoff check fires first and the
    /// pin would pass with or without the bound, i.e. it would be vacuous. Wed 30 Sep is a weekday, so
    /// the closing week really does run past the end. Expected: the same result as the one-piece
    /// reference call (September's <c>NormPeriodWeeks</c> 1, same norm total, same NORMAL_HOURS lines).
    /// Leg 2: a cutoff of 4 Oct is past <c>PeriodEnd</c> and must not widen evaluation. RED by MU-41
    /// (the <c>day &gt; _periodEnd → -1</c> bound in <c>PieceIndexOn</c> removed, fix-up 2259cc1).
    /// </summary>
    [Fact]
    public void Core_PiecesBeyondPeriodEnd_DoNotGovern()
    {
        // Premise: the pin is vacuous unless the period end is a weekday (the closing week must run past it).
        Assert.Equal(DayOfWeek.Wednesday, Sep30.DayOfWeek);
        var entries = WeekdayHours(Aug31, Sep30, 7.40m);
        var october = With(Hk26,
            (nameof(AgreementRuleConfig.NormPeriodWeeks), 4),
            (nameof(AgreementRuleConfig.NormModel), NormModel.ANNUAL_ACTIVITY));

        // Reference: one piece, to the period end only.
        var reference = Run(Sep1, Sep30, null, [Piece(Aug31, Sep30, 1.0m, Hk26)], entries);
        Assert.Equal(1, reference.Norm.NormPeriodWeeks);
        Assert.Equal(162.80m, Val(reference.Norm.NormHoursTotal));
        Assert.NotEmpty(reference.Norm.LineItems.Where(l => l.TimeType == "NORMAL_HOURS"));

        var pieces = new[] { Piece(Aug31, Sep30, 1.0m, Hk26), Piece(Oct1, Oct4, 1.0m, october) };

        foreach (DateOnly? cutoff in new DateOnly?[] { null, Oct4 })
        {
            var result = Run(Sep1, Sep30, cutoff, pieces, entries);

            Assert.Equal(1, result.Norm.NormPeriodWeeks);
            Assert.Equal("NORM_CHECK_37H", result.Norm.RuleId);
            Assert.Equal(Val(reference.Norm.NormHoursTotal), Val(result.Norm.NormHoursTotal));
            Assert.Equal(Val(reference.Norm.ActualHoursTotal), Val(result.Norm.ActualHoursTotal));
            Assert.Equal(
                reference.Norm.LineItems.Where(l => l.TimeType == "NORMAL_HOURS")
                    .Select(l => (l.Date, l.Hours, l.Rate)).OrderBy(l => l.Date).ToList(),
                result.Norm.LineItems.Where(l => l.TimeType == "NORMAL_HOURS")
                    .Select(l => (l.Date, l.Hours, l.Rate)).OrderBy(l => l.Date).ToList());
            AssertNoOvertimeLinesAndNoFlexPayout(result);
        }
    }

    /// <summary>
    /// Annual mode (AC_RESEARCH, <c>ANNUAL_ACTIVITY</c>) keeps its days ÷ 365 total, PER PIECE and
    /// summed: <c>AnnualNormHours</c> 1924 × fraction × days ÷ 365. One piece, September (30 days):
    /// 1924 × 30 ÷ 365 = 158.136986…; two pieces, 15 days at 1.0 and 15 days at 0.5:
    /// 1924 × 15 ÷ 365 + 1924 × 0.5 × 15 ÷ 365 = 118.602739… (compared to 6 decimals — the spec does not
    /// fix the division order). The result reports no <c>NormPeriodWeeks</c>. Overtime (merarbejde)
    /// still uses the weekday per-day norms (entries here sit exactly at them, so none is paid). RED by
    /// MU-10: annual mode through weekday norms gives 162.80 and 122.10 instead.
    /// </summary>
    [Fact]
    public void Core_AnnualActivity_DaysOver365PerPiece()
    {
        // One piece.
        var one = Run(
            Sep1, Sep30, null,
            [Piece(Sep1, Sep30, 1.0m, AcResearch26)],
            WeekdayHours(Sep1, Sep30, 7.40m));
        Assert.Null(one.Norm.NormPeriodWeeks);
        Assert.Equal(158.136986m, Math.Round(Val(one.Norm.NormHoursTotal), 6));
        Assert.Equal(162.80m, Val(one.Norm.ActualHoursTotal));
        AssertNoOvertimeLinesAndNoFlexPayout(one);

        // Two pieces with different fractions: the per-piece sum, not one fraction for the month.
        var entries = WeekdayHours(Sep1, Sep15, 7.40m);
        entries.AddRange(WeekdayHours(Sep16, Sep30, 3.70m));
        var two = Run(
            Sep1, Sep30, null,
            [Piece(Sep1, Sep15, 1.0m, AcResearch26), Piece(Sep16, Sep30, 0.5m, AcResearch26)],
            entries);
        Assert.Null(two.Norm.NormPeriodWeeks);
        Assert.Equal(118.602740m, Math.Round(Val(two.Norm.NormHoursTotal), 6));
        Assert.Equal(122.10m, Val(two.Norm.ActualHoursTotal)); // 11 × 7.40 + 11 × 3.70
        AssertNoOvertimeLinesAndNoFlexPayout(two);
    }

    // =============================================================================================
    // Flex (refinement :615-620, R9)
    // =============================================================================================

    /// <summary>
    /// Flex is settled ONCE for the month, not clamped week by week. HK cap 100, opening balance 95;
    /// the week 7–11 September is +10.00 h over norm (5 × 9.40), the week 21–25 September is −8.00 h
    /// (5 × 5.80). Settled once: 95 + 10 − 8 = 97, below the cap, no payout. Clamped per week it would
    /// be 95 + 10 → capped at 100 (5 paid out) → 100 − 8 = 92. RED by MU-11.
    /// </summary>
    [Fact]
    public void Core_Flex_95Plus10Minus8_ClosesAt97_NoPayout()
    {
        var entries = WeekdayHours(Sep1, Sep30, 7.40m, (Sep7, Sep11, 9.40m), (Sep21, Sep25, 5.80m));

        var result = Run(Sep1, Sep30, null, [Piece(Sep1, Sep30, 1.0m, Hk26)], entries, previousFlexBalance: 95m);

        Assert.Equal(95m, result.Flex.PreviousBalance);
        Assert.Equal(2.00m, result.Flex.Delta);
        Assert.Equal(97.00m, result.Flex.NewBalance);
        Assert.Equal(0m, result.Flex.ExcessForPayout);
        Assert.Null(result.FlexPayout);
    }

    /// <summary>
    /// The cap in force ON THE SETTLEMENT DAY (the last covered day of the period, 30 September)
    /// applies, and the one <c>FLEX_PAYOUT</c> line is dated that day. Opening balance 110, worked
    /// exactly at norm (delta 0). Leg 1: the cap is 120 for 31 Aug – 15 Sep and 100 from 16 Sep → the
    /// settlement-day cap 100 applies: new balance 100, payout 10.00 dated 30 September. Leg 2 (the
    /// reverse caps): 100 until 15 Sep and 120 from 16 Sep → cap 120, balance 110, no payout. RED by
    /// MU-12 (cap taken from the first covered day: both legs invert).
    /// </summary>
    [Fact]
    public void Core_Flex_CapInForceOnSettlementDay()
    {
        var cap120 = With(Hk26, ("MaxFlexBalance", 120.0m));
        var entries = WeekdayHours(Aug31, Sep30, 7.40m);

        var leg1 = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Sep15, 1.0m, cap120), Piece(Sep16, Sep30, 1.0m, Hk26)],
            entries, previousFlexBalance: 110m);
        Assert.Equal(0m, leg1.Flex.Delta);
        Assert.Equal(100.00m, leg1.Flex.NewBalance);
        Assert.Equal(10.00m, leg1.Flex.ExcessForPayout);
        Assert.NotNull(leg1.FlexPayout);
        Assert.Equal("FLEX_PAYOUT", leg1.FlexPayout!.TimeType);
        Assert.Equal(10.00m, leg1.FlexPayout.Hours);
        Assert.Equal(1.0m, leg1.FlexPayout.Rate);
        Assert.Equal(Sep30, leg1.FlexPayout.Date);

        var leg2 = Run(
            Sep1, Sep30, null,
            [Piece(Aug31, Sep15, 1.0m, Hk26), Piece(Sep16, Sep30, 1.0m, cap120)],
            entries, previousFlexBalance: 110m);
        Assert.Equal(110.00m, leg2.Flex.NewBalance);
        Assert.Equal(0m, leg2.Flex.ExcessForPayout);
        Assert.Null(leg2.FlexPayout);
    }

    /// <summary>
    /// A 0.811 employee (30 h/week) books absences carrying <b>0 hours</b> — the ONLY branch where the
    /// default credit applies (an absence with hours uses its own; brief contradiction 12: R24 makes a
    /// 0-hour Skema absence unreachable, so this is a unit-level fact). The default is
    /// <c>DayNorm.ForWeekday(37.0, fraction)</c> = 6.00 (R35), not 7.4 × 0.811 = 6.0014. Wed 9 September
    /// VACATION 0 h credits 6.00; Thu 10 September LEAVE_WITHOUT_PAY 0 h reduces the norm by 6.00;
    /// Fri 11 September VACATION 4.00 h credits exactly its own 4.00. Credits 10.00; effective norm
    /// 22 × 6.00 − 6.00 = 126.00; worked 19 × 6.00 = 114.00; delta 114 + 10 − 126 = −2.00. RED by MU-13.
    /// </summary>
    [Fact]
    public void Core_Flex_Fraction0811_ZeroHourVacationCredits600_UnpaidReduces600()
    {
        var entries = WeekdayHours(Sep1, Sep30, 6.00m)
            .Where(e => e.Date != Sep9 && e.Date != Sep10 && e.Date != Sep11)
            .ToList();
        var absences = new List<AbsenceEntry>
        {
            Absence(Sep9, AbsenceTypes.Vacation, 0m),
            Absence(Sep10, AbsenceTypes.LeaveWithoutPay, 0m),
            Absence(Sep11, AbsenceTypes.Vacation, 4.00m),
        };

        var result = Run(Sep1, Sep30, null, [Piece(Sep1, Sep30, 0.811m, Hk26)], entries, absences);

        Assert.Equal(10.00m, result.Flex.AbsenceNormCredits);   // 6.00 (default) + 4.00 (own hours)
        Assert.Equal(126.00m, result.Flex.NormHours);           // 132.00 − 6.00
        Assert.Equal(114.00m, result.Flex.WorkedHours);
        Assert.Equal(-2.00m, result.Flex.Delta);
        Assert.Equal(-2.00m, result.Flex.NewBalance);
        Assert.Null(result.FlexPayout);
    }

    /// <summary>
    /// Refinement :616 — "the per-day default credit uses THAT day's fraction" (R35, precision rule :548).
    /// A part-timer drops from 37 to 30 h (fraction 0.811) effective Wednesday 16 September and books a
    /// 0-hour VACATION on each side of the change, both inside the one week that straddles it (Mon 14 –
    /// Sun 20 Sep). 0 hours is the only branch where the default credit applies (brief contradiction 12).
    /// Mon 14 Sep is on the old contract: credit 7.4 × 1.0 = 7.40. Thu 17 Sep is on the new one:
    /// 7.4 × 0.811 = 6.0014 → 6.00 (rounded per day). Credits 13.40. September has 11 weekdays in each
    /// piece (1–15 Sep: 4 + 5 + 2; 16–30 Sep: 3 + 5 + 3): norm 11 × 7.40 + 11 × 6.00 = 147.40 (no unpaid
    /// absence, so the norm is not reduced); worked 10 × 7.40 + 10 × 6.00 = 134.00 (the two absence days
    /// carry no entry); delta 134.00 + 13.40 − 147.40 = 0. The straddle week is worked 19.40 against a
    /// norm of 32.80, so no overtime arises anywhere. RED by MU-42 (the default credit takes its fraction
    /// from the piece in force on the settlement day instead of the absence day's piece): credits 12.00
    /// (0.811 for both), delta −1.40. A first-piece regression gives 14.80 (1.0 for both), delta +1.40.
    /// Because both days share one week, a week-level regression (the credit at the fraction of the
    /// piece governing the week, i.e. the Sunday's) also gives 12.00, and one at the Monday's gives
    /// 14.80 (Step 5a cycle 2 of 14506, NOTE). The existing single-fraction fact cannot tell any of
    /// these apart.
    /// </summary>
    [Fact]
    public void Core_Flex_MidMonthFractionChange_ZeroHourCredits_UseEachDaysFraction_1340()
    {
        // Weekday premises: the absences and the change are on weekdays, so none is skipped as zero-norm,
        // and both absences fall in the straddle week Mon 14 – Sun 20 Sep.
        var sep17 = new DateOnly(2026, 9, 17);
        Assert.Equal(DayOfWeek.Monday, Sep14.DayOfWeek);
        Assert.Equal(DayOfWeek.Tuesday, Sep15.DayOfWeek);
        Assert.Equal(DayOfWeek.Wednesday, Sep16.DayOfWeek);
        Assert.Equal(DayOfWeek.Thursday, sep17.DayOfWeek);

        var entries = WeekdayHours(Sep1, Sep15, 7.40m)
            .Concat(WeekdayHours(Sep16, Sep30, 6.00m))
            .Where(e => e.Date != Sep14 && e.Date != sep17)
            .ToList();
        var absences = new List<AbsenceEntry>
        {
            Absence(Sep14, AbsenceTypes.Vacation, 0m),
            Absence(sep17, AbsenceTypes.Vacation, 0m),
        };

        var result = Run(
            Sep1, Sep30, null,
            [Piece(Sep1, Sep15, 1.0m, Hk26), Piece(Sep16, Sep30, 0.811m, Hk26)],
            entries, absences);

        Assert.Equal(13.40m, result.Flex.AbsenceNormCredits);   // 7.40 (old day) + 6.00 (new day)
        Assert.Equal(147.40m, result.Flex.NormHours);
        Assert.Equal(134.00m, result.Flex.WorkedHours);
        Assert.Equal(0m, result.Flex.Delta);
        Assert.Equal(0m, result.Flex.NewBalance);
        Assert.Null(result.FlexPayout);
    }

    private static AbsenceEntry Absence(DateOnly day, string type, decimal hours) => new()
    {
        EmployeeId = Emp,
        Date = day,
        AbsenceType = type,
        Hours = hours,
        AgreementCode = "HK",
        OkVersion = "OK26",
    };
}
