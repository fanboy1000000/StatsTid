using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S56 / TASK-5603 per-day norm (dailyNorm) contract. The Skema month GET computes,
/// for each calendar day in the month, a per-day norm that the grid renders as the
/// "Diff. fra normtid" reference. The rules (mirrored from SkemaEndpoints.cs:253-291):
///
/// <list type="bullet">
///   <item>Weekends → 0.</item>
///   <item>ANNUAL_ACTIVITY (academic) → null (a per-weekday split is not meaningful;
///   do NOT approximate).</item>
///   <item>Otherwise: <c>Math.Round(WeeklyNormHours × partTimeFraction / 5, 2)</c>.</item>
///   <item>OK version is resolved PER DAY via <see cref="OkVersionResolver"/> — NOT
///   year&gt;=2026 — because the OK24→OK26 switch is 2026-04-01.</item>
/// </list>
///
/// These tests pin the pure, deterministic arithmetic + branching. The DB-backed
/// config/profile resolution is exercised at the integration boundary; here we feed
/// the resolved inputs directly so the formula and per-day OK resolution are the
/// units under test.
/// </summary>
public class Sprint56DailyNormTests
{
    // Verbatim mirror of the endpoint's per-day norm decision.
    private static decimal? DailyNorm(
        DateOnly day, decimal weeklyNormHours, decimal partTimeFraction, NormModel normModel)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return 0m;
        if (normModel == NormModel.ANNUAL_ACTIVITY)
            return null;
        return Math.Round(weeklyNormHours * partTimeFraction / 5m, 2);
    }

    [Fact]
    public void FullTime_Weekday_Is7point4()
    {
        // 37h/week full-time → 37 × 1.0 / 5 = 7.4.
        var norm = DailyNorm(new DateOnly(2026, 3, 4), 37m, 1.0m, NormModel.WEEKLY_HOURS);
        Assert.Equal(7.4m, norm);
    }

    [Fact]
    public void PartTime_Fraction0point8_IsLowerThanFullTime()
    {
        // 37 × 0.8 / 5 = 5.92 — strictly below the 7.4 full-time norm.
        var norm = DailyNorm(new DateOnly(2026, 3, 4), 37m, 0.8m, NormModel.WEEKLY_HOURS);
        Assert.Equal(5.92m, norm);
        Assert.True(norm < 7.4m);
    }

    [Fact]
    public void Weekend_IsZero()
    {
        var saturday = new DateOnly(2026, 3, 7);
        var sunday = new DateOnly(2026, 3, 8);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);
        Assert.Equal(0m, DailyNorm(saturday, 37m, 1.0m, NormModel.WEEKLY_HOURS));
        Assert.Equal(0m, DailyNorm(sunday, 37m, 1.0m, NormModel.WEEKLY_HOURS));
    }

    [Fact]
    public void AnnualActivity_Weekday_IsNull()
    {
        var norm = DailyNorm(new DateOnly(2026, 3, 4), 1924m, 1.0m, NormModel.ANNUAL_ACTIVITY);
        Assert.Null(norm);
    }

    [Fact]
    public void AnnualActivity_Weekend_StillZero()
    {
        // Weekend short-circuits BEFORE the ANNUAL_ACTIVITY null branch.
        var saturday = new DateOnly(2026, 3, 7);
        Assert.Equal(0m, DailyNorm(saturday, 1924m, 1.0m, NormModel.ANNUAL_ACTIVITY));
    }

    [Theory]
    [InlineData("2026-01-15", "OK24")]
    [InlineData("2026-02-28", "OK24")]
    [InlineData("2026-03-31", "OK24")] // last OK24 day
    [InlineData("2026-04-01", "OK26")] // first OK26 day — the switch boundary
    public void OkVersion_ResolvedPerDay_AcrossThe2026_04_01Switch(string dateText, string expectedOk)
    {
        var day = DateOnly.Parse(dateText);
        Assert.Equal(expectedOk, OkVersionResolver.ResolveVersion(day));
    }

    [Fact]
    public void JanThroughMar2026_AllResolveToOK24_NotByYear()
    {
        // A naive year>=2026 → OK26 rule would be WRONG: Jan–Mar 2026 is still OK24.
        for (var day = new DateOnly(2026, 1, 1); day <= new DateOnly(2026, 3, 31); day = day.AddDays(1))
            Assert.Equal("OK24", OkVersionResolver.ResolveVersion(day));
    }
}
