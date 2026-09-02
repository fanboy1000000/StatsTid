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

    // ════════════════════════════════════════════════════════════════════════
    // S137 / ADR-040 D9 — the employment END-cap (accrual stops at the last employed day).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The marquee D9 case: a leaver whose last employed day is 15 Mar 2026, read at the ferieår's
    /// end (31 Aug 2026). Without the cap the read would show the full 12 months (25 d); with it
    /// the as-of is clamped to 15 Mar, so Sep..Mar = 7 accrual months ⇒ 25 × 7/12. The running
    /// balance stops rising at the leave date.
    /// </summary>
    [Fact]
    public void EarnedToDate_AsOfAfterEmploymentEnd_ClampsToEmploymentEnd()
    {
        var employmentEnd = new DateOnly(2026, 3, 15);
        var asOf = new DateOnly(2026, 8, 31);

        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf, employmentEnd);

        Assert.Equal(25m * 7 / 12m, earned);
        // And the uncapped read really would have been the full year — the cap is load-bearing.
        Assert.Equal(25m, AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf));
    }

    /// <summary>
    /// A leaver's curve PLATEAUS: every month-end AFTER the leave date yields the same earned as the
    /// leave month itself (the Balance /series and year-overview matrix pin this end-to-end).
    /// </summary>
    [Theory]
    [InlineData(2026, 3, 31)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 6, 30)]
    [InlineData(2026, 8, 31)]
    [InlineData(2027, 1, 31)]
    public void EarnedToDate_EveryAsOfAfterEmploymentEnd_EqualsTheLeaveMonthValue(int y, int m, int d)
    {
        var employmentEnd = new DateOnly(2026, 3, 15);
        var atLeaveDate = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, employmentEnd, employmentEnd);

        var later = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, new DateOnly(y, m, d), employmentEnd);

        Assert.Equal(atLeaveDate, later);
        Assert.Equal(25m * 7 / 12m, later);
    }

    /// <summary>
    /// Idempotence pin (the sites-9/10 argument): when asOf is ON or BEFORE the employment end, the
    /// cap is a no-op — the 6-arg call equals the 5-arg call exactly. This is why the ADR-033
    /// settlement rails that already valuate AT the end date are unaffected by opting in.
    /// </summary>
    [Theory]
    [InlineData(2025, 9, 30)]   // well before the end
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 3, 14)]   // day before the end
    [InlineData(2026, 3, 15)]   // ON the end date (inclusive — the last employed day counts)
    public void EarnedToDate_AsOfOnOrBeforeEmploymentEnd_IsIdentity(int y, int m, int d)
    {
        var employmentEnd = new DateOnly(2026, 3, 15);
        var asOf = new DateOnly(y, m, d);

        var capped = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf, employmentEnd);
        var uncapped = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);

        Assert.Equal(uncapped, capped);
    }

    /// <summary>
    /// An employment end BEFORE the ferieår start ⇒ 0: the person had already left when this
    /// ferieår began, so nothing of it was earned (the mirror of "start after asOf ⇒ 0").
    /// </summary>
    [Fact]
    public void EarnedToDate_EmploymentEndBeforeFerieaarStart_ReturnsZero()
    {
        var employmentEnd = new DateOnly(2025, 6, 30); // left before 1 Sep 2025
        var asOf = new DateOnly(2026, 2, 28);

        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf, employmentEnd);

        Assert.Equal(0m, earned);
    }

    /// <summary>
    /// End-cap and start pro-ration compose: hired 1 Nov 2025, left 15 Feb 2026, read in August ⇒
    /// Nov,Dec,Jan,Feb = 4 months.
    /// </summary>
    [Fact]
    public void EarnedToDate_MidFerieaarHireAndLeaver_ComposesStartAndEnd()
    {
        var employmentStart = new DateOnly(2025, 11, 1);
        var employmentEnd = new DateOnly(2026, 2, 15);
        var asOf = new DateOnly(2026, 8, 31);

        var earned = AccrualMath.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf, employmentEnd);

        Assert.Equal(25m * 4 / 12m, earned);
    }

    /// <summary>
    /// An end date before the hire date (inconsistent data) yields 0 rather than a negative or a
    /// throw — the clamp moves asOf before the accrual start, and the existing "before start ⇒ 0"
    /// edge absorbs it.
    /// </summary>
    [Fact]
    public void EarnedToDate_EmploymentEndBeforeEmploymentStart_ReturnsZero_NeverNegative()
    {
        var earned = AccrualMath.EarnedToDate(
            25m, 1.0m, FerieaarStart,
            employmentStart: new DateOnly(2026, 2, 1),
            asOf: new DateOnly(2026, 8, 31),
            employmentEnd: new DateOnly(2025, 12, 31));

        Assert.Equal(0m, earned);
    }

    /// <summary>
    /// Byte-identical legacy behaviour: a null employmentEnd equals the 5-arg call across a matrix
    /// of quotas / fractions / hire dates / as-ofs — every pre-S137 caller that has not opted in
    /// computes exactly what it did before.
    /// </summary>
    [Theory]
    [InlineData(25, 1.0, null, 2025, 9, 30)]
    [InlineData(25, 1.0, null, 2026, 8, 31)]
    [InlineData(25, 1.0, null, 2027, 3, 1)]     // past the ferieår (clamps to 12)
    [InlineData(25, 0.8, "2025-10-01", 2025, 12, 31)]
    [InlineData(5, 1.0, null, 2025, 9, 1)]      // SPECIAL_HOLIDAY first month
    [InlineData(5, 0.5, "2025-11-01", 2026, 2, 1)]
    [InlineData(25, 1.0, "2026-06-01", 2026, 2, 1)] // hire after asOf ⇒ 0 in both
    [InlineData(25, 1.0, null, 2025, 8, 31)]    // before the ferieår ⇒ 0 in both
    public void EarnedToDate_NullEmploymentEnd_EqualsFiveArgCall_ByteIdentical(
        int quota, double fraction, string? hire, int y, int m, int d)
    {
        DateOnly? employmentStart = hire is null ? null : DateOnly.Parse(hire);
        var asOf = new DateOnly(y, m, d);

        var legacy = AccrualMath.EarnedToDate((decimal)quota, (decimal)fraction, FerieaarStart, employmentStart, asOf);
        var withNullEnd = AccrualMath.EarnedToDate((decimal)quota, (decimal)fraction, FerieaarStart, employmentStart, asOf, employmentEnd: null);

        Assert.Equal(legacy, withNullEnd);
    }
}
