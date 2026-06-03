using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S61 / TASK-6101 / ADR-030 — direct unit matrix for the SharedKernel monthly-accrual math
/// <see cref="AccrualMath.EarnedToDate"/> (Ferieloven <em>samtidighedsferie</em>).
///
/// <para>
/// This suite locks the SOURCE of truth itself, calling <see cref="AccrualMath"/> directly rather
/// than through the <c>AccrualCalculator</c> delegator (the parity between delegator and source is
/// pinned separately by <c>AccrualCalculatorTests.RuleEngineEarnedToDate_DelegatesTo_SharedKernelAccrualMath</c>).
/// It pins the exact-fractional earning curve (NO rounding in the calc — rounding is a display
/// concern), part-time scaling, mid-ferieår-hire pro-ration (accrue from
/// <c>max(ferieaarStart, employmentStart)</c>), the null-employmentStart full-ferieår fallback
/// (deliberately NOT fail-closed — a missing hire date must not deny already-earned vacation),
/// the asOf-before-ferieår ⇒ 0 and employmentStart-after-asOf ⇒ 0 edges, and repeat-call
/// determinism (priority #2/#4: pure function of its arguments).
/// </para>
/// </summary>
public class AccrualMathTests
{
    // VACATION ferieår: 1 Sep. Annual 25 d full-time ⇒ 25/12 ≈ 2,0833 d/md.
    private static readonly DateOnly FerieaarStart = new(2025, 9, 1);

    [Theory]
    // monthsElapsed is inclusive of the start month, clamped [0,12]. asOf in Sep (month 1) ⇒ 25*1/12.
    [InlineData(2025, 9, 1)]   // Sep — 1 month
    [InlineData(2025, 10, 2)]  // Oct — 2 months
    [InlineData(2025, 11, 3)]
    [InlineData(2025, 12, 4)]
    [InlineData(2026, 1, 5)]
    [InlineData(2026, 2, 6)]
    [InlineData(2026, 3, 7)]
    [InlineData(2026, 4, 8)]
    [InlineData(2026, 5, 9)]
    [InlineData(2026, 6, 10)]
    [InlineData(2026, 7, 11)]
    [InlineData(2026, 8, 12)]  // Aug — full ferieår, 12 months
    public void EarnedToDate_Vacation_FullTime_ProgressesMonthByMonth_ExactFractional(
        int asOfYear, int asOfMonth, int expectedMonths)
    {
        var asOf = new DateOnly(asOfYear, asOfMonth, 15);
        var earned = AccrualMath.EarnedToDate(
            annualQuota: 25m, partTimeFraction: 1.0m,
            ferieaarStart: FerieaarStart, employmentStart: null, asOf: asOf);

        var expected = 25m * expectedMonths / 12m; // exact fractional — NOT rounded
        Assert.Equal(expected, earned);
    }

    [Fact]
    public void EarnedToDate_FirstMonth_IsExactTwentyFiveTwelfths_NotRounded()
    {
        // Explicitly pin the "~2,08" first-month value as the exact unrounded fraction.
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, new DateOnly(2025, 9, 30));
        Assert.Equal(25m / 12m, earned);
        // Guard against an accidental Math.Round creeping into the calc: the exact value is not 2.08.
        Assert.NotEqual(2.08m, earned);
    }

    [Fact]
    public void EarnedToDate_ClampsAtTwelveMonths_NeverExceedsAnnual()
    {
        // asOf well past the ferieår end ⇒ clamps to 12 months = full annual quota.
        var asOf = new DateOnly(2027, 3, 1);
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(25m, earned);
    }

    [Fact]
    public void EarnedToDate_SpecialHoliday_FullTime_UsesGenericQuota()
    {
        // SPECIAL_HOLIDAY annual 5 d ⇒ 5/12 ≈ 0,4167 d/md. Falls out of the generic quota.
        var asOf = new DateOnly(2025, 9, 1); // 1 month
        var earned = AccrualMath.EarnedToDate(5m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(5m * 1 / 12m, earned);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(0.6)]
    [InlineData(0.375)]
    public void EarnedToDate_PartTime_ScalesLinearly_ExactFractional(double fraction)
    {
        var pt = (decimal)fraction;
        var asOf = new DateOnly(2026, 2, 1); // 6 months elapsed
        var earned = AccrualMath.EarnedToDate(25m, pt, FerieaarStart, null, asOf);
        Assert.Equal(25m * pt * 6 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_MidFerieaarHire_AccruesFromEmploymentStart()
    {
        // Hired 1 Nov (3rd month of the ferieår). At Feb (month 6 of ferieår), accrual months
        // count from Nov ⇒ Nov,Dec,Jan,Feb = 4 months. Pro-rated, not the full 6.
        var employmentStart = new DateOnly(2025, 11, 1);
        var asOf = new DateOnly(2026, 2, 1);
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(25m * 4 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_MidFerieaarHire_PartTime_AccrualStartIsMaxOfDates_ThenScaled()
    {
        // Accrual start = max(ferieaarStart=Sep, employmentStart=Oct) = Oct. At Dec, elapsed =
        // Oct,Nov,Dec = 3 months. Part-time 0.8 scales linearly on top.
        var employmentStart = new DateOnly(2025, 10, 1);
        var asOf = new DateOnly(2025, 12, 31);
        var earned = AccrualMath.EarnedToDate(25m, 0.8m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(25m * 0.8m * 3 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_EmploymentStartBeforeFerieaar_UsesFerieaarStart()
    {
        // Hired before the ferieår started ⇒ accrual begins at ferieårStart (the max of the two).
        var employmentStart = new DateOnly(2024, 3, 1);
        var asOf = new DateOnly(2025, 9, 1); // 1 month into ferieår
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(25m * 1 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_NullEmploymentStart_AssumesFullFerieaar_DoesNotFailClosed()
    {
        var asOf = new DateOnly(2026, 8, 1); // 12 months
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(25m, earned); // full annual — a missing hire date must not deny earned vacation
    }

    [Fact]
    public void EarnedToDate_EmploymentStartAfterAsOf_ReturnsZero()
    {
        // Hired in the future relative to asOf ⇒ nothing earned yet.
        var employmentStart = new DateOnly(2026, 6, 1);
        var asOf = new DateOnly(2026, 2, 1);
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void EarnedToDate_AsOfBeforeFerieaarStart_ReturnsZero()
    {
        var asOf = new DateOnly(2025, 8, 31); // day before the ferieår starts
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void EarnedToDate_AsOfInSameMonthAsAccrualStart_CountsThatMonth()
    {
        // asOf earlier in the SAME calendar month as the ferieår start still counts month 1
        // (MonthIndex is month-granular: day-of-month does not change the elapsed-month count).
        var asOf = new DateOnly(2025, 9, 1);
        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(25m * 1 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_IsPureAndDeterministic_OnRepeatedCalls()
    {
        var asOf = new DateOnly(2026, 1, 10);
        var a = AccrualMath.EarnedToDate(25m, 0.75m, FerieaarStart, new DateOnly(2025, 10, 1), asOf);
        var b = AccrualMath.EarnedToDate(25m, 0.75m, FerieaarStart, new DateOnly(2025, 10, 1), asOf);
        var c = AccrualMath.EarnedToDate(25m, 0.75m, FerieaarStart, new DateOnly(2025, 10, 1), asOf);
        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // EarnedToDatePiecewise — ADR-030 D8 cumulative samtidighedsferie (each elapsed accrual month
    // earns at the fraction in effect that month, summed). The single-fraction EarnedToDate above is
    // its byte-equality oracle for the constant-fraction case. Anchors are month-START (whole month).
    // ──────────────────────────────────────────────────────────────────────────────────────────

    // A single open-ended period spanning the whole window ⇒ a constant fraction; the short-circuit
    // must return EXACTLY the legacy EarnedToDate value. Min From so it covers every in-window anchor.
    private static IReadOnlyList<FractionPeriod> WholeWindow(decimal fraction) =>
        new[] { new FractionPeriod(DateOnly.MinValue, null, fraction) };

    [Theory]
    // (annualQuota, fraction, asOf) covering VACATION 25 & SPECIAL_HOLIDAY 5; fractions storable at scale 3.
    [InlineData(25, 1.0, 2025, 9, 1)]    // VACATION full-time, 1 month
    [InlineData(25, 1.0, 2026, 8, 15)]   // VACATION full-time, 12 months (clamped)
    [InlineData(25, 0.5, 2026, 2, 1)]    // VACATION 0.5, 6 months
    [InlineData(25, 0.8, 2026, 1, 1)]    // VACATION 0.8, 5 months
    [InlineData(5, 1.0, 2026, 2, 1)]     // SPECIAL_HOLIDAY full-time, 6 months
    [InlineData(5, 0.5, 2025, 12, 1)]    // SPECIAL_HOLIDAY 0.5, 4 months
    [InlineData(5, 0.8, 2026, 8, 1)]     // SPECIAL_HOLIDAY 0.8, 12 months
    public void EarnedToDatePiecewise_ConstantFraction_IsBitIdenticalToLegacyEarnedToDate(
        int annualQuota, double fraction, int asOfYear, int asOfMonth, int asOfDay)
    {
        var quota = (decimal)annualQuota;
        var frac = (decimal)fraction;
        var asOf = new DateOnly(asOfYear, asOfMonth, asOfDay);

        var legacy = AccrualMath.EarnedToDate(quota, frac, FerieaarStart, null, asOf);
        var piecewise = AccrualMath.EarnedToDatePiecewise(
            quota, FerieaarStart, employmentStart: null, asOf, WholeWindow(frac));

        Assert.Equal(legacy, piecewise); // EXACT — same decimal, same scale (no rounding)
    }

    [Fact]
    public void EarnedToDatePiecewise_MidYearDrop_SumsPerMonthFractions()
    {
        // Ferieår Sep 1 2025. Full-time Sep–Dec (4 months), 0.5 from Jan 1 2026 (8 months) to year end.
        var history = new[]
        {
            new FractionPeriod(DateOnly.MinValue, new DateOnly(2026, 1, 1), 1.0m),
            new FractionPeriod(new DateOnly(2026, 1, 1), null, 0.5m),
        };
        var asOf = new DateOnly(2026, 8, 31); // ferieår end
        var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);

        Assert.Equal(25m * (4 * 1.0m + 8 * 0.5m) / 12m, earned); // = 25 * 8 / 12, exact
    }

    [Fact]
    public void EarnedToDatePiecewise_MidYearRise_IsSymmetric_AndLessThanFlatFullTime()
    {
        // 0.5 Sep–Dec (4 months), full-time from Jan (8 months). Mirror image of the drop case.
        var history = new[]
        {
            new FractionPeriod(DateOnly.MinValue, new DateOnly(2026, 1, 1), 0.5m),
            new FractionPeriod(new DateOnly(2026, 1, 1), null, 1.0m),
        };
        var asOf = new DateOnly(2026, 8, 31);
        var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);

        Assert.Equal(25m * (4 * 0.5m + 8 * 1.0m) / 12m, earned); // = 25 * 10 / 12, exact

        // Strictly less than a flat full-time ferieår (the 4 part-time months earn less).
        var flatFullTime = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.True(earned < flatFullTime);
    }

    [Fact]
    public void EarnedToDatePiecewise_MidFerieaarHire_PlusFractionChange_CreditsFromHireMonth()
    {
        // Hired 15 Nov 2025 ⇒ accrual starts Nov (month-START anchor); Sep & Oct contribute 0.
        // Full-time Nov–Dec, then 0.8 from 1 Feb 2026. asOf 28 Feb 2026.
        var employmentStart = new DateOnly(2025, 11, 15);
        var history = new[]
        {
            new FractionPeriod(DateOnly.MinValue, new DateOnly(2026, 2, 1), 1.0m),
            new FractionPeriod(new DateOnly(2026, 2, 1), null, 0.8m),
        };
        var asOf = new DateOnly(2026, 2, 28);

        var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, employmentStart, asOf, history);

        // Accrual months: Nov,Dec (1.0) + Jan (1.0) + Feb (0.8) = 3*1.0 + 0.8 = 3.8. Sep/Oct = 0.
        Assert.Equal(25m * (3 * 1.0m + 0.8m) / 12m, earned);

        // Sanity: strictly less than if accrual had (wrongly) started at ferieår Sep.
        var fromFerieaarStart = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);
        Assert.True(earned < fromFerieaarStart);
    }

    [Fact]
    public void EarnedToDatePiecewise_ClampsAtTwelveMonths_SummationDoesNotOverShoot()
    {
        // Mirror EarnedToDate_ClampsAtTwelveMonths: a fraction change deep past the ferieår end and
        // an asOf well past it must STILL cap at the 12 accruable months — no over-sum.
        var history = new[]
        {
            new FractionPeriod(DateOnly.MinValue, new DateOnly(2026, 9, 1), 1.0m), // covers all 12 in-window months
            new FractionPeriod(new DateOnly(2026, 9, 1), null, 0.5m),             // month 13+ — outside the clamp
        };
        var asOf = new DateOnly(2027, 3, 1); // same far-future asOf as the legacy clamp test
        var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);

        Assert.Equal(25m, earned); // full annual — the 0.5 month-13 change must not be summed in
    }

    [Fact]
    public void EarnedToDatePiecewise_FrontGap_CarriesEarliestFractionBackward_NotFullTime()
    {
        // Earliest period starts 1 Nov 2025 — AFTER the in-window Sep & Oct anchors (a front gap).
        // Those pre-history months must carry the EARLIEST fraction (0.5) backward, never 1.0.
        var history = new[]
        {
            new FractionPeriod(new DateOnly(2025, 11, 1), new DateOnly(2026, 2, 1), 0.5m),
            new FractionPeriod(new DateOnly(2026, 2, 1), null, 1.0m),
        };
        var asOf = new DateOnly(2026, 2, 28);
        var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);

        // Sep,Oct (front gap → 0.5) + Nov,Dec,Jan (0.5) + Feb (1.0) = 5*0.5 + 1.0 = 3.5.
        Assert.Equal(25m * (5 * 0.5m + 1.0m) / 12m, earned);

        // If the front gap had WRONGLY inflated to full-time: Sep,Oct=1.0 ⇒ 2*1.0 + 3*0.5 + 1.0 = 4.5.
        var wrongIfFullTime = 25m * (2 * 1.0m + 3 * 0.5m + 1.0m) / 12m;
        Assert.True(earned < wrongIfFullTime);

        // And clearly not a flat full-time ferieår-to-date either.
        var flatFullTime = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.NotEqual(flatFullTime, earned);
    }

    [Fact]
    public void EarnedToDatePiecewise_EmptyHistory_EqualsLegacyFullTime()
    {
        // Empty history is the ONLY case treated as full-time (1.0) ⇒ identical to EarnedToDate(1.0).
        var asOf = new DateOnly(2026, 2, 1); // 6 months
        var earned = AccrualMath.EarnedToDatePiecewise(
            25m, FerieaarStart, null, asOf, Array.Empty<FractionPeriod>());

        var legacyFullTime = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(legacyFullTime, earned);
    }

    [Fact]
    public void EarnedToDatePiecewise_IsMonotonicAcrossTheFerieaar_NeverDrops()
    {
        // The S61 /series scenario: full-time Sep–Dec then 0.5 from Jan. Walking month-END across the
        // whole ferieår, cumulative earned must be NON-DECREASING (the bug was a mid-year drop).
        var history = new[]
        {
            new FractionPeriod(DateOnly.MinValue, new DateOnly(2026, 1, 1), 1.0m),
            new FractionPeriod(new DateOnly(2026, 1, 1), null, 0.5m),
        };

        // Month-end as-of dates Sep 2025 … Aug 2026 (last day of each accrual month).
        var monthEnds = new[]
        {
            new DateOnly(2025, 9, 30), new DateOnly(2025, 10, 31), new DateOnly(2025, 11, 30),
            new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 31),
            new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 31),
        };

        decimal previous = -1m;
        foreach (var asOf in monthEnds)
        {
            var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);
            Assert.True(earned >= previous,
                $"earned dropped at {asOf:yyyy-MM}: {earned} < {previous} (non-monotonic accrual curve)");
            previous = earned;
        }
    }

    [Fact]
    public void EarnedToDatePiecewise_AsOfBeforeAccrualStart_ReturnsZero()
    {
        // Window guard survives the piecewise path too: asOf before the ferieår ⇒ 0, no summation.
        var asOf = new DateOnly(2025, 8, 31);
        var earned = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, WholeWindow(0.5m));
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void EarnedToDatePiecewise_IsPureAndDeterministic_OnRepeatedCalls()
    {
        var history = new[]
        {
            new FractionPeriod(DateOnly.MinValue, new DateOnly(2026, 1, 1), 1.0m),
            new FractionPeriod(new DateOnly(2026, 1, 1), null, 0.5m),
        };
        var asOf = new DateOnly(2026, 3, 10);
        var a = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);
        var b = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);
        var c = AccrualMath.EarnedToDatePiecewise(25m, FerieaarStart, null, asOf, history);
        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }
}
