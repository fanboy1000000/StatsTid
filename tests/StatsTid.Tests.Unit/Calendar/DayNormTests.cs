using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit.Calendar;

/// <summary>
/// S145 / TASK-14506 — pins for <c>StatsTid.SharedKernel.Calendar.DayNorm</c>, the ONE place that
/// turns "weekly hours × part-time fraction" into "this weekday's norm" (TASK-14505 contract,
/// R40; precision rule at refinement :548; ruling Q2 = A).
///
/// <para>
/// <b>Plain language.</b> Overtime, norm and flex all need to know what one working day is worth.
/// The screen (<c>DailyNormCalculator</c>) and the payslip rules must agree on it to the hundredth
/// of an hour, otherwise an employee sees 30.00 h on the screen and is paid against 30.007 h. So
/// both now call one helper, and this file pins what that helper returns: a weekday is
/// <c>weekly × fraction ÷ 5</c> rounded to 2 decimals, half to even (the .NET default), and a
/// weekend day is 0. The figures below are hand-derived from the spec, never read back from the
/// code.
/// </para>
///
/// <para>
/// <b>Why 0.811.</b> A 30-hour contract is stored as 30 ÷ 37 = 0.811 (NUMERIC(4,3),
/// <c>init.sql:559</c>). 37 × 0.811 ÷ 5 = 6.0014 → <b>6.00</b>; the 100% overtime threshold
/// 40 × 0.811 ÷ 5 = 6.488 → <b>6.49</b>. A week of five such days is therefore 30.00 / 32.45, not
/// the 30.007 / 32.44 that a week-level multiplication produces.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate, never by reverting — a compile error is
/// not RED; <c>docs/AGENTS.md</c> § "RED evidence"): MU-14 swaps the rounding to
/// <c>MidpointRounding.AwayFromZero</c> and trips <c>DayNorm_HalfToEven_AtMidpoint</c> on its first
/// four rows; MU-15 gives weekends the weekday norm and trips <c>DayNorm_Weekend_Zero</c>. The
/// 0.811 fact has no named MU of its own: it fails under any of those two edits to the rounding
/// call, and under a week-level (unrounded-per-day) implementation.
/// </para>
/// </summary>
public sealed class DayNormTests
{
    // Monday 7 September 2026 … Sunday 13 September 2026: a plain week with no public holiday.
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Fact]
    public void DayNorm_Fraction0811_Norm600_Threshold100_649()
    {
        // 37.0 × 0.811 / 5 = 6.0014 → 6.00 ; 40.0 × 0.811 / 5 = 6.488 → 6.49 (refinement :548).
        Assert.Equal(6.00m, DayNorm.ForWeekday(37.0m, 0.811m));
        Assert.Equal(6.49m, DayNorm.ForWeekday(40.0m, 0.811m));

        // (The 50% threshold of every live config equals the weekly norm, 37.0, so it is 6.00 too.)

        // ForDay agrees with ForWeekday on a weekday (Tuesday 8 September).
        Assert.Equal(6.00m, DayNorm.ForDay(Monday.AddDays(1), 37.0m, 0.811m));
        Assert.Equal(6.49m, DayNorm.ForDay(Monday.AddDays(1), 40.0m, 0.811m));

        // A Monday–Sunday week is the SUM of per-day amounts (precision rule): 30.00 / 32.45, NOT
        // the week-level 37 × 0.811 = 30.007 and 40 × 0.811 = 32.44.
        var weekNorm = Enumerable.Range(0, 7).Sum(i => DayNorm.ForDay(Monday.AddDays(i), 37.0m, 0.811m));
        var weekThreshold100 = Enumerable.Range(0, 7).Sum(i => DayNorm.ForDay(Monday.AddDays(i), 40.0m, 0.811m));
        Assert.Equal(30.00m, weekNorm);
        Assert.Equal(32.45m, weekThreshold100);
    }

    [Fact]
    public void DayNorm_FullTimeAndHalfTime_ExactFigures()
    {
        // The exact (no rounding needed) anchors every other figure in the sprint is built on.
        Assert.Equal(7.40m, DayNorm.ForWeekday(37.0m, 1.0m));
        Assert.Equal(8.00m, DayNorm.ForWeekday(40.0m, 1.0m));
        Assert.Equal(3.70m, DayNorm.ForWeekday(37.0m, 0.5m));
        Assert.Equal(7.20m, DayNorm.ForWeekday(36.0m, 1.0m)); // the synthetic OK26 week of 36 h
    }

    public static TheoryData<decimal, decimal, decimal> MidpointRows => new()
    {
        // weekly, fraction, expected (2 decimals, half to even). weekly × fraction / 5 is an EXACT
        // x.xx5 in every row, so the only thing deciding the result is the midpoint rule.
        { 37.0m, 0.625m, 4.62m },  // 4.625 → even 4.62   (away-from-zero would give 4.63)
        { 37.0m, 0.125m, 0.92m },  // 0.925 → even 0.92   (away-from-zero would give 0.93)
        { 0.125m, 1.0m, 0.02m },   // 0.025 → even 0.02   (away-from-zero would give 0.03)
        { 0.225m, 1.0m, 0.04m },   // 0.045 → even 0.04   (away-from-zero would give 0.05)
        // Control row where "to even" and "away from zero" AGREE (7 is odd, so even rounds UP):
        // proves the helper is round-half-to-EVEN, not "round half down".
        { 0.375m, 1.0m, 0.08m },   // 0.075 → even 0.08
    };

    [Theory]
    [MemberData(nameof(MidpointRows))]
    public void DayNorm_HalfToEven_AtMidpoint(decimal weekly, decimal fraction, decimal expected)
    {
        Assert.Equal(expected, DayNorm.ForWeekday(weekly, fraction));
    }

    [Fact]
    public void DayNorm_Weekend_Zero()
    {
        var saturday = Monday.AddDays(5);
        var sunday = Monday.AddDays(6);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);

        foreach (var (weekly, fraction) in new[] { (37.0m, 1.0m), (37.0m, 0.811m), (40.0m, 1.0m), (36.0m, 0.5m) })
        {
            Assert.Equal(0m, DayNorm.ForDay(saturday, weekly, fraction));
            Assert.Equal(0m, DayNorm.ForDay(sunday, weekly, fraction));
        }

        // …and the five weekdays carry the whole week: a full-time week sums to exactly 37.00.
        for (var i = 0; i < 5; i++)
            Assert.Equal(7.40m, DayNorm.ForDay(Monday.AddDays(i), 37.0m, 1.0m));
        Assert.Equal(37.00m, Enumerable.Range(0, 7).Sum(i => DayNorm.ForDay(Monday.AddDays(i), 37.0m, 1.0m)));
    }

    [Fact]
    public void DayNorm_PublicHolidayWeekday_StillCarriesTheNorm()
    {
        // Assumption 3 (refinement :533): public holidays do not reduce the norm, in payroll or on
        // the screen. 2 and 3 April 2026 (Maundy Thursday, Good Friday) are the holidays the
        // refinement cites (DanishPublicHolidays.cs:42-43); both are weekdays and carry 7.40.
        Assert.Equal(DayOfWeek.Thursday, new DateOnly(2026, 4, 2).DayOfWeek);
        Assert.Equal(7.40m, DayNorm.ForDay(new DateOnly(2026, 4, 2), 37.0m, 1.0m));
        Assert.Equal(7.40m, DayNorm.ForDay(new DateOnly(2026, 4, 3), 37.0m, 1.0m));
    }
}
