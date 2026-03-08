using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class Sprint13BalanceTests
{
    // ========================================================================
    // 1. CentralAgreementConfigs — WeeklyNormHours per agreement
    // ========================================================================

    [Theory]
    [InlineData("AC", "OK24", 37.0)]
    [InlineData("HK", "OK24", 37.0)]
    [InlineData("PROSA", "OK24", 37.0)]
    public void CentralConfig_WeeklyNormHours_IsCorrect(string code, string version, decimal expected)
    {
        var config = CentralAgreementConfigs.TryGetConfig(code, version);
        Assert.NotNull(config);
        Assert.Equal(expected, config!.WeeklyNormHours);
    }

    // ========================================================================
    // 2. CentralAgreementConfigs — HasMerarbejde per agreement
    // ========================================================================

    [Theory]
    [InlineData("AC", "OK24", true)]
    [InlineData("HK", "OK24", false)]
    [InlineData("PROSA", "OK24", false)]
    public void CentralConfig_HasMerarbejde_IsCorrect(string code, string version, bool expected)
    {
        var config = CentralAgreementConfigs.TryGetConfig(code, version);
        Assert.NotNull(config);
        Assert.Equal(expected, config!.HasMerarbejde);
    }

    // ========================================================================
    // 3. TryGetConfig returns null for unknown agreement
    // ========================================================================

    [Fact]
    public void CentralConfig_TryGetConfig_ReturnsNull_ForUnknown()
    {
        var config = CentralAgreementConfigs.TryGetConfig("UNKNOWN", "OK24");
        Assert.Null(config);
    }

    // ========================================================================
    // 4. Working days calculation for known months
    // ========================================================================

    [Theory]
    [InlineData(2026, 3, 22)]  // March 2026 has 22 weekdays
    [InlineData(2026, 2, 20)]  // February 2026 has 20 weekdays
    [InlineData(2026, 1, 22)]  // January 2026 has 22 weekdays
    public void WeekdayCount_IsCorrect(int year, int month, int expectedWeekdays)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, daysInMonth);

        var weekdays = 0;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                weekdays++;
        }

        Assert.Equal(expectedWeekdays, weekdays);
    }

    // ========================================================================
    // 5. Norm hours expected calculation
    // ========================================================================

    [Theory]
    [InlineData(22, 37.0, 162.8)]  // 22 weekdays * (37/5) = 162.8
    [InlineData(20, 37.0, 148.0)]  // 20 weekdays * (37/5) = 148.0
    public void NormHoursExpected_CalculatesCorrectly(int weekdays, decimal weeklyNorm, decimal expected)
    {
        var normHoursExpected = (weekdays / 5.0m) * weeklyNorm;
        Assert.Equal(expected, normHoursExpected);
    }

    // ========================================================================
    // 6. Overtime calculation
    // ========================================================================

    [Theory]
    [InlineData(170.0, 162.8, 7.2)]   // overtime = 170 - 162.8 = 7.2
    [InlineData(150.0, 162.8, 0.0)]   // no overtime if under norm
    [InlineData(162.8, 162.8, 0.0)]   // no overtime if exactly at norm
    public void OvertimeHours_CalculatesCorrectly(decimal actual, decimal expected, decimal overtime)
    {
        var result = Math.Max(0m, actual - expected);
        Assert.Equal(overtime, result);
    }
}
