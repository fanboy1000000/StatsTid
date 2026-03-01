using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

public class DanishPublicHolidaysTests
{
    [Fact]
    public void Easter2024_IsCorrect()
    {
        var easter = DanishPublicHolidays.ComputeEasterSunday(2024);
        Assert.Equal(new DateOnly(2024, 3, 31), easter);
    }

    [Fact]
    public void Easter2025_IsCorrect()
    {
        var easter = DanishPublicHolidays.ComputeEasterSunday(2025);
        Assert.Equal(new DateOnly(2025, 4, 20), easter);
    }

    [Fact]
    public void Easter2026_IsCorrect()
    {
        var easter = DanishPublicHolidays.ComputeEasterSunday(2026);
        Assert.Equal(new DateOnly(2026, 4, 5), easter);
    }

    [Fact]
    public void StoreBededag_RemovedUnderOK24()
    {
        // Store Bededag (4th Friday after Easter 2024) = April 26, 2024
        var storeBededag = new DateOnly(2024, 4, 26);
        Assert.False(DanishPublicHolidays.IsPublicHoliday(storeBededag, "OK24"));
    }

    [Fact]
    public void StoreBededag_PresentBeforeOK24()
    {
        // Under OK21, Store Bededag should be present
        var easter2024 = DanishPublicHolidays.ComputeEasterSunday(2024);
        var storeBededag = easter2024.AddDays(26);
        Assert.True(DanishPublicHolidays.IsPublicHoliday(storeBededag, "OK21"));
    }

    [Fact]
    public void Juledag_IsHoliday()
    {
        Assert.True(DanishPublicHolidays.IsPublicHoliday(new DateOnly(2024, 12, 25), "OK24"));
    }

    [Fact]
    public void Weekend_IsCorrectlyIdentified()
    {
        Assert.True(DanishPublicHolidays.IsWeekend(new DateOnly(2024, 4, 6))); // Saturday
        Assert.True(DanishPublicHolidays.IsWeekend(new DateOnly(2024, 4, 7))); // Sunday
        Assert.False(DanishPublicHolidays.IsWeekend(new DateOnly(2024, 4, 8))); // Monday
    }

    [Fact]
    public void CountWorkingDays_FullWeek_Returns5()
    {
        // Mon Apr 8 - Sun Apr 14, 2024 (no holidays)
        var count = DanishPublicHolidays.CountWorkingDays(
            new DateOnly(2024, 4, 8), new DateOnly(2024, 4, 14), "OK24");
        Assert.Equal(5, count);
    }

    [Fact]
    public void GetHolidays_OK24_Has11Holidays()
    {
        var holidays = DanishPublicHolidays.GetHolidays(2024, "OK24");
        Assert.Equal(11, holidays.Count);
    }

    [Fact]
    public void GetHolidays_OK21_Has12Holidays_IncludesStoreBededag()
    {
        var holidays = DanishPublicHolidays.GetHolidays(2024, "OK21");
        Assert.Equal(12, holidays.Count);
        Assert.Contains(holidays, h => h.Name == "Store Bededag");
    }
}
