using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S80 / TASK-8001 (ADR-033 Slice 2, R1/R2/R3/R10) — the shared
/// <see cref="EntitlementPeriodResolver"/> period-geometry matrix.
///
/// <para>Pins the THREE geometries the resolver hoists:</para>
/// <list type="bullet">
///   <item><description><b>VACATION (reset_month 9)</b> — the EXACT pre-S80 ferieår keying
///   ("Month ≥ reset_month"), the 1-Sep accrual/taking start, and the §21 31-Dec-(E+1) boundary.
///   This is the HARD VACATION-unchanged invariant.</description></item>
///   <item><description><b>Calendar types (reset_month 1)</b> — Jan-1 accrual/taking start, 31-Dec-E
///   boundary, "Month ≥ 1" ⇒ always the same calendar year.</description></item>
///   <item><description><b>SPECIAL_HOLIDAY</b> — calendar accrual (1 Jan), the 1-May-(Y+1) taking
///   window start, the 30-Apr-(Y+2) boundary, and the TWO-calendar-year usage→accrual-year mapping
///   (May–Dec T → accrual T−1; Jan–Apr T → accrual T−2). reset_month is IGNORED for SPECIAL_HOLIDAY
///   (its geometry is fixed by Cirkulære 021-24 §12).</description></item>
/// </list>
///
/// <para>These FAIL on the unfixed code: the pre-S80 sites keyed SPECIAL_HOLIDAY off raw reset_month
/// (Sep–Aug geometry), so a May–Dec / Jan–Apr accrual-year split and a 30-Apr-(Y+2) boundary were
/// not expressible at all.</para>
/// </summary>
public class EntitlementPeriodResolverTests
{
    // ── VACATION (reset_month 9): the hard pre-S80 invariant ────────────────

    [Theory]
    // Sep–Dec of T ⇒ ferieår T (Month ≥ 9). Jan–Aug of T ⇒ ferieår T−1.
    [InlineData(2025, 9, 1, 2025)]
    [InlineData(2025, 12, 31, 2025)]
    [InlineData(2026, 1, 1, 2025)]
    [InlineData(2026, 8, 31, 2025)]
    [InlineData(2026, 9, 1, 2026)]
    public void Vacation_ResetMonth9_KeysEntitlementYear_LikePreS80(
        int y, int m, int d, int expectedYear)
    {
        var p = EntitlementPeriodResolver.Resolve("VACATION", 9, new DateOnly(y, m, d));
        Assert.Equal(expectedYear, p.EntitlementYear);
        // Accrual + taking both start 1 Sep of the entitlement year; the two coincide for VACATION.
        Assert.Equal(new DateOnly(expectedYear, 9, 1), p.AccrualStart);
        Assert.Equal(new DateOnly(expectedYear, 9, 1), p.TakingStart);
        // §21 31-Dec deadline of the ferieår-END year (Sep E .. Aug E+1 ⇒ 31 Dec E+1).
        Assert.Equal(new DateOnly(expectedYear + 1, 12, 31), p.Boundary);
    }

    [Fact]
    public void Vacation_ResolveForYear_MatchesResolveGeometry()
    {
        // The settlement-service entry point (known entitlementYear) yields the same geometry.
        var p = EntitlementPeriodResolver.ResolveForYear("VACATION", 9, 2024);
        Assert.Equal(2024, p.EntitlementYear);
        Assert.Equal(new DateOnly(2024, 9, 1), p.AccrualStart);
        Assert.Equal(new DateOnly(2025, 8, 31), p.AccrualEnd); // S80/8001 BLOCKER-1: ferieår END (31 Aug E+1) — the EarnedToDate asOf, distinct from the §21 31-Dec Boundary
        Assert.Equal(new DateOnly(2025, 12, 31), p.Boundary); // 31 Dec E+1
    }

    // ── Calendar types (reset_month 1) — CARE_DAY / SENIOR_DAY ──────────────

    [Theory]
    [InlineData(2026, 1, 1, 2026)]
    [InlineData(2026, 6, 15, 2026)]
    [InlineData(2026, 12, 31, 2026)]
    public void CalendarType_ResetMonth1_KeysToSameCalendarYear(int y, int m, int d, int expectedYear)
    {
        var p = EntitlementPeriodResolver.Resolve("CARE_DAY", 1, new DateOnly(y, m, d));
        Assert.Equal(expectedYear, p.EntitlementYear);
        Assert.Equal(new DateOnly(expectedYear, 1, 1), p.AccrualStart);
        Assert.Equal(new DateOnly(expectedYear, 1, 1), p.TakingStart);
        Assert.Equal(new DateOnly(expectedYear, 12, 31), p.AccrualEnd); // calendar-year end (== Boundary for these types)
        Assert.Equal(new DateOnly(expectedYear, 12, 31), p.Boundary); // 31 Dec E
    }

    // ── SPECIAL_HOLIDAY (R2) — BOTH halves of the two-calendar-year mapping ──

    [Theory]
    // May–Dec T → accrual year T−1 (the taking period that opened THIS May).
    [InlineData(2024, 5, 1, 2023)]   // first day of the taking window
    [InlineData(2024, 6, 15, 2023)]
    [InlineData(2024, 11, 30, 2023)] // the Nov example
    [InlineData(2024, 12, 31, 2023)]
    public void SpecialHoliday_MayToDec_KeysToAccrualYearTMinus1(int y, int m, int d, int expected)
    {
        var p = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(y, m, d));
        Assert.Equal(expected, p.EntitlementYear);
    }

    [Theory]
    // Jan–Apr T → accrual year T−2 (the taking period that opened LAST May).
    [InlineData(2024, 1, 1, 2022)]
    [InlineData(2024, 2, 15, 2022)]
    [InlineData(2024, 4, 30, 2022)]  // last day of the taking window for accrual 2022
    public void SpecialHoliday_JanToApr_KeysToAccrualYearTMinus2(int y, int m, int d, int expected)
    {
        var p = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(y, m, d));
        Assert.Equal(expected, p.EntitlementYear);
    }

    [Fact]
    public void SpecialHoliday_AccrualAnchoredTo1Jan_TakingOpens1May_BoundaryIs30Apr()
    {
        // A May–Dec usage (1 Jun 2024) keys to accrual year 2023.
        var p = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(2024, 6, 1));
        Assert.Equal(2023, p.EntitlementYear);
        Assert.Equal(new DateOnly(2023, 1, 1), p.AccrualStart);   // R1: calendar accrual
        Assert.Equal(new DateOnly(2024, 5, 1), p.TakingStart);    // R2: 1 May (Y+1)
        Assert.Equal(new DateOnly(2023, 12, 31), p.AccrualEnd);   // S80/8001 BLOCKER-1: calendar accrual-year END (31 Dec Y), DISTINCT from the 30 Apr settlement Boundary
        Assert.Equal(new DateOnly(2025, 4, 30), p.Boundary);      // R3: 30 Apr (Y+2)
    }

    [Fact]
    public void SpecialHoliday_WorkedExample_Accrual2022_TakingStart2023May_Boundary2024Apr30()
    {
        // The law worked example (SPRINT-80 §source-verified, Cirkulære 021-24 §12):
        // accrual 2022 → taken 1 May 2023 – 30 Apr 2024 → godtgørelse at 30 Apr 2024.
        var p = EntitlementPeriodResolver.ResolveForYear("SPECIAL_HOLIDAY", 1, 2022);
        Assert.Equal(2022, p.EntitlementYear);
        Assert.Equal(new DateOnly(2022, 1, 1), p.AccrualStart);
        Assert.Equal(new DateOnly(2023, 5, 1), p.TakingStart);
        Assert.Equal(new DateOnly(2024, 4, 30), p.Boundary);
    }

    [Fact]
    public void SpecialHoliday_IgnoresResetMonth_GeometryFixedByLaw()
    {
        // Whatever reset_month is stored, SPECIAL_HOLIDAY uses its statutory geometry. A May–Dec
        // usage keys to T−1 regardless of reset_month (9 = the OLD mis-modeled value; 1 = the new).
        var withOld = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 9, new DateOnly(2024, 6, 1));
        var withNew = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(2024, 6, 1));
        Assert.Equal(withNew, withOld);
        Assert.Equal(2023, withNew.EntitlementYear);
        Assert.Equal(new DateOnly(2025, 4, 30), withNew.Boundary);
    }

    [Fact]
    public void SpecialHoliday_TakingWindowTransitionAtMay_AdjacentMonthsKeyToAdjacentAccrualYears()
    {
        // The discriminating R2 pin: 30 Apr 2024 and 1 May 2024 are one day apart yet key to
        // DIFFERENT accrual years (the taking window rolls on 1 May). The unfixed reset_month-9
        // geometry would key both to the same Sep-anchored ferieår.
        var apr30 = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(2024, 4, 30));
        var may01 = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(2024, 5, 1));
        Assert.Equal(2022, apr30.EntitlementYear); // Jan–Apr → T−2
        Assert.Equal(2023, may01.EntitlementYear); // May–Dec → T−1
        Assert.NotEqual(apr30.EntitlementYear, may01.EntitlementYear);
    }

    [Fact]
    public void IsSpecialHoliday_OnlyTrueForExactCanonicalValue()
    {
        Assert.True(EntitlementPeriodResolver.IsSpecialHoliday("SPECIAL_HOLIDAY"));
        Assert.False(EntitlementPeriodResolver.IsSpecialHoliday("VACATION"));
        Assert.False(EntitlementPeriodResolver.IsSpecialHoliday("special_holiday")); // ordinal, case-sensitive
    }

    [Fact]
    public void Resolve_IsPureAndDeterministic_OnRepeatedCalls()
    {
        var a = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(2024, 6, 1));
        var b = EntitlementPeriodResolver.Resolve("SPECIAL_HOLIDAY", 1, new DateOnly(2024, 6, 1));
        Assert.Equal(a, b);
    }

    // ── R1 — accrual ANCHORED to 1 Jan (the resolver + AccrualMath composition) ──

    [Fact]
    public void SpecialHoliday_AccrualAnchoredTo1Jan_NovAsOf_Earns11Twelfths_KeyedToAccrualYear()
    {
        // R1: SPECIAL_HOLIDAY accrues on the CALENDAR year from 1 Jan. A Nov (month 11) asOf in
        // accrual year Y earns 11 elapsed months = 5 × 11/12 ≈ 0.4167 × 11 ≈ 4.58 days (NOT the full
        // 5). Feeding AccrualMath the resolver's AccrualStart (1 Jan Y) is what makes this true —
        // the OLD reset_month-9 anchor (1 Sep Y) would count only Sep,Oct,Nov = 3 months at the same
        // asOf, the mis-model this slice corrects. The accrual is keyed to accrual year Y.
        //
        // We accrue WITHIN accrual year Y, so asOf is a date in Y. The resolver's Resolve() keys a
        // November date to the TAKING year (Y is taken the FOLLOWING May), so for the accrual-year
        // composition we use ResolveForYear(Y) to get the 1-Jan-Y anchor directly.
        var accrualYear = 2023;
        var period = EntitlementPeriodResolver.ResolveForYear("SPECIAL_HOLIDAY", 1, accrualYear);
        Assert.Equal(new DateOnly(2023, 1, 1), period.AccrualStart);

        var asOfNov = new DateOnly(2023, 11, 30); // 11 elapsed accrual months (Jan..Nov)
        var earned = AccrualMath.EarnedToDate(
            annualQuota: 5m, partTimeFraction: 1.0m,
            ferieaarStart: period.AccrualStart, employmentStart: null, asOf: asOfNov);

        Assert.Equal(5m * 11 / 12m, earned);          // ≈ 4.58, the calendar-anchored accrual
        Assert.NotEqual(5m * 3 / 12m, earned);        // NOT the old Sep-anchored 3-month figure
    }

    [Fact]
    public void SpecialHoliday_FullyAccruedByDecember_FiveDays()
    {
        // By 31 Dec of the accrual year all 12 months have elapsed ⇒ the full 5 days.
        var period = EntitlementPeriodResolver.ResolveForYear("SPECIAL_HOLIDAY", 1, 2023);
        var earned = AccrualMath.EarnedToDate(5m, 1.0m, period.AccrualStart, null, new DateOnly(2023, 12, 31));
        Assert.Equal(5m, earned);
    }
}
