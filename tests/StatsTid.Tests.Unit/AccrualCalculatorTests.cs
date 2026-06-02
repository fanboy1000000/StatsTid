using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S60 / TASK-6009 / ADR-030 — unit matrix for the pure monthly-accrual math
/// <see cref="AccrualCalculator.EarnedToDate"/> (Ferieloven samtidighedsferie).
///
/// Pins the exact-fractional earning curve (no rounding in the calculator), part-time scaling,
/// mid-ferieår-hire pro-ration (accrue from max(ferieårStart, employmentStart)), the
/// null-employmentStart full-ferieår fallback, and the employment-start-after-asOf ⇒ 0 case.
///
/// S61 / TASK-6101 — the formula was consolidated into the SharedKernel
/// <see cref="AccrualMath.EarnedToDate"/>; <see cref="AccrualCalculator"/> now delegates to it,
/// as do the (formerly duplicated) Backend Balance/Skema endpoints. The reconciliation test below
/// now pins <see cref="AccrualMath"/> ↔ <see cref="AccrualCalculator"/> parity directly (no
/// reflection into Backend types — those local mirrors no longer exist).
/// </summary>
public class AccrualCalculatorTests
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
        var earned = AccrualCalculator.EarnedToDate(
            annualQuota: 25m, partTimeFraction: 1.0m,
            ferieaarStart: FerieaarStart, employmentStart: null, asOf: asOf);

        var expected = 25m * expectedMonths / 12m; // exact fractional — NOT rounded
        Assert.Equal(expected, earned);
    }

    [Fact]
    public void EarnedToDate_ClampsAtTwelveMonths_NeverExceedsAnnual()
    {
        // asOf well past the ferieår end ⇒ clamps to 12 months = full annual quota.
        var asOf = new DateOnly(2027, 3, 1);
        var earned = AccrualCalculator.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(25m, earned);
    }

    [Fact]
    public void EarnedToDate_SpecialHoliday_FullTime_UsesGenericQuota()
    {
        // SPECIAL_HOLIDAY annual 5 d ⇒ 5/12 ≈ 0,4167 d/md. Falls out of the generic quota.
        var asOf = new DateOnly(2025, 9, 1); // 1 month
        var earned = AccrualCalculator.EarnedToDate(5m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(5m * 1 / 12m, earned);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(0.6)]
    public void EarnedToDate_PartTime_ScalesLinearly(double fraction)
    {
        var pt = (decimal)fraction;
        var asOf = new DateOnly(2026, 2, 1); // 6 months elapsed
        var earned = AccrualCalculator.EarnedToDate(25m, pt, FerieaarStart, null, asOf);
        Assert.Equal(25m * pt * 6 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_MidFerieaarHire_AccruesFromEmploymentStart()
    {
        // Hired 1 Nov (3rd month of the ferieår). At Feb (month 6 of ferieår), accrual months
        // are counted from Nov ⇒ Nov,Dec,Jan,Feb = 4 months. Pro-rated, not full 6.
        var employmentStart = new DateOnly(2025, 11, 1);
        var asOf = new DateOnly(2026, 2, 1);
        var earned = AccrualCalculator.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(25m * 4 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_EmploymentStartBeforeFerieaar_UsesFerieaarStart()
    {
        // Hired before the ferieår started ⇒ accrual begins at ferieårStart (max of the two).
        var employmentStart = new DateOnly(2024, 3, 1);
        var asOf = new DateOnly(2025, 9, 1); // 1 month into ferieår
        var earned = AccrualCalculator.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(25m * 1 / 12m, earned);
    }

    [Fact]
    public void EarnedToDate_NullEmploymentStart_AssumesFullFerieaar()
    {
        var asOf = new DateOnly(2026, 8, 1); // 12 months
        var earned = AccrualCalculator.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(25m, earned); // full annual — does NOT fail-closed on missing hire date
    }

    [Fact]
    public void EarnedToDate_EmploymentStartAfterAsOf_ReturnsZero()
    {
        // Hired in the future relative to asOf ⇒ nothing earned yet.
        var employmentStart = new DateOnly(2026, 6, 1);
        var asOf = new DateOnly(2026, 2, 1);
        var earned = AccrualCalculator.EarnedToDate(25m, 1.0m, FerieaarStart, employmentStart, asOf);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void EarnedToDate_AsOfBeforeFerieaarStart_ReturnsZero()
    {
        var asOf = new DateOnly(2025, 8, 31); // day before the ferieår starts
        var earned = AccrualCalculator.EarnedToDate(25m, 1.0m, FerieaarStart, null, asOf);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void EarnedToDate_IsPureAndDeterministic_OnRepeatedCalls()
    {
        var asOf = new DateOnly(2026, 1, 10);
        var a = AccrualCalculator.EarnedToDate(25m, 0.75m, FerieaarStart, new DateOnly(2025, 10, 1), asOf);
        var b = AccrualCalculator.EarnedToDate(25m, 0.75m, FerieaarStart, new DateOnly(2025, 10, 1), asOf);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// S61 / TASK-6101 consolidation guard: <see cref="AccrualCalculator.EarnedToDate"/> is now a
    /// thin delegator to the single SharedKernel copy <see cref="AccrualMath.EarnedToDate"/>, and
    /// the (formerly triplicated) Backend Balance/Skema endpoints delegate to the SAME SharedKernel
    /// fn. This reconciliation pins the delegator ↔ shared-source parity across the same value
    /// matrix that previously guarded the now-removed Backend mirrors, so the public rule-engine
    /// surface can never diverge from the shared math.
    /// </summary>
    [Theory]
    [InlineData(25, 1.0, 2025, 9, 1, null, null, 2025, 9, 1)]      // 1 month
    [InlineData(25, 1.0, 2025, 9, 1, null, null, 2026, 8, 1)]      // full year
    [InlineData(5, 0.5, 2025, 9, 1, null, null, 2026, 2, 1)]       // special holiday, part-time
    [InlineData(25, 0.8, 2025, 9, 1, 2025, 11, 2026, 1, 10)]       // mid-year hire, part-time
    [InlineData(25, 1.0, 2025, 9, 1, 2026, 6, 2026, 2, 1)]         // hire after asOf ⇒ 0
    [InlineData(25, 1.0, 2025, 9, 1, null, null, 2025, 8, 31)]     // before ferieår ⇒ 0
    public void RuleEngineEarnedToDate_DelegatesTo_SharedKernelAccrualMath(
        double annualQuota, double partTimeFraction,
        int faYear, int faMonth, int faDay,
        int? esYear, int? esMonth,
        int asOfYear, int asOfMonth, int asOfDay)
    {
        var ferieaarStart = new DateOnly(faYear, faMonth, faDay);
        DateOnly? employmentStart = esYear.HasValue
            ? new DateOnly(esYear.Value, esMonth!.Value, 1)
            : null;
        var asOf = new DateOnly(asOfYear, asOfMonth, asOfDay);

        var ruleEngine = AccrualCalculator.EarnedToDate(
            (decimal)annualQuota, (decimal)partTimeFraction, ferieaarStart, employmentStart, asOf);
        var sharedKernel = AccrualMath.EarnedToDate(
            (decimal)annualQuota, (decimal)partTimeFraction, ferieaarStart, employmentStart, asOf);

        Assert.Equal(sharedKernel, ruleEngine);
    }
}
