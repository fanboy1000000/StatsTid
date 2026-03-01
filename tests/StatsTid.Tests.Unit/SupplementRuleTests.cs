using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class SupplementRuleTests
{
    private static EmploymentProfile CreateProfile(string agreement = "HK") => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = agreement,
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m
    };

    private static TimeEntry CreateEntry(DateOnly date, decimal hours, TimeOnly? start = null, TimeOnly? end = null) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        StartTime = start,
        EndTime = end,
        AgreementCode = "HK",
        OkVersion = "OK24"
    };

    [Fact]
    public void AC_AllSupplementsDisabled_ReturnsEmpty()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");
        var monday = new DateOnly(2024, 4, 6); // Saturday
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 8m, new TimeOnly(9, 0), new TimeOnly(17, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, monday, monday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void HK_EveningWork_ReturnsEveningSupplement()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var tuesday = new DateOnly(2024, 4, 9);
        var entries = new List<TimeEntry>
        {
            CreateEntry(tuesday, 4m, new TimeOnly(18, 0), new TimeOnly(22, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, tuesday, tuesday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Evening, result.LineItems[0].TimeType);
        Assert.Equal(4m, result.LineItems[0].Hours);
        Assert.Equal(1.25m, result.LineItems[0].Rate);
    }

    [Fact]
    public void HK_NightWork_ReturnsNightSupplement()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var wednesday = new DateOnly(2024, 4, 10);
        // 23:00 to 02:00 = 3 hours night work (crosses midnight)
        var entries = new List<TimeEntry>
        {
            CreateEntry(wednesday, 3m, new TimeOnly(23, 0), new TimeOnly(2, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, wednesday, wednesday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Night, result.LineItems[0].TimeType);
        Assert.Equal(3m, result.LineItems[0].Hours);
        Assert.Equal(1.50m, result.LineItems[0].Rate);
    }

    [Fact]
    public void HK_SaturdayWork_ReturnsWeekendSupplement_AtSaturdayRate()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var saturday = new DateOnly(2024, 4, 6);
        var entries = new List<TimeEntry>
        {
            CreateEntry(saturday, 8m, new TimeOnly(9, 0), new TimeOnly(17, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, saturday, saturday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Weekend, result.LineItems[0].TimeType);
        Assert.Equal(1.50m, result.LineItems[0].Rate);
    }

    [Fact]
    public void HK_SundayWork_ReturnsWeekendSupplement_AtSundayRate()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var sunday = new DateOnly(2024, 4, 7);
        var entries = new List<TimeEntry>
        {
            CreateEntry(sunday, 6m, new TimeOnly(10, 0), new TimeOnly(16, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, sunday, sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Weekend, result.LineItems[0].TimeType);
        Assert.Equal(2.0m, result.LineItems[0].Rate);
    }

    [Fact]
    public void HK_HolidayWork_ReturnsHolidaySupplement()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        // Juledag 2024 = Dec 25, Wednesday
        var holiday = new DateOnly(2024, 12, 25);
        var entries = new List<TimeEntry>
        {
            CreateEntry(holiday, 7.4m, new TimeOnly(8, 0), new TimeOnly(15, 24))
        };

        var result = SupplementRule.Evaluate(profile, entries, holiday, holiday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Holiday, result.LineItems[0].TimeType);
        Assert.Equal(2.0m, result.LineItems[0].Rate);
    }

    [Fact]
    public void Holiday_TakesPrecedence_OverWeekend()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        // Nytaarsdag 2028 = Jan 1 is a Saturday
        var nytaarsdag = new DateOnly(2028, 1, 1);
        var entries = new List<TimeEntry>
        {
            CreateEntry(nytaarsdag, 5m, new TimeOnly(9, 0), new TimeOnly(14, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, nytaarsdag, nytaarsdag, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Holiday, result.LineItems[0].TimeType);
    }

    [Fact]
    public void NoStartEndTime_NoSupplements()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var monday = new DateOnly(2024, 4, 8);
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 7.4m) // No start/end time
        };

        var result = SupplementRule.Evaluate(profile, entries, monday, monday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void MidnightCrossingOverlap_CalculatesCorrectly()
    {
        // 22:00 to 01:00 = 3 hours total
        // Evening window 17-23: 22:00-23:00 = 1 hour
        // Night window 23-06: 23:00-01:00 = 2 hours
        var eveningHours = SupplementRule.CalculateOverlapHours(
            new TimeOnly(22, 0), new TimeOnly(1, 0), 17, 23);
        var nightHours = SupplementRule.CalculateOverlapHours(
            new TimeOnly(22, 0), new TimeOnly(1, 0), 23, 6);

        Assert.Equal(1m, eveningHours);
        Assert.Equal(2m, nightHours);
    }

    [Fact]
    public void PROSA_EveningWork_SameAsHK()
    {
        var profile = CreateProfile("PROSA");
        var config = AgreementConfigProvider.GetConfig("PROSA", "OK24");
        var tuesday = new DateOnly(2024, 4, 9);
        var entries = new List<TimeEntry>
        {
            CreateEntry(tuesday, 2m, new TimeOnly(19, 0), new TimeOnly(21, 0))
        };

        var result = SupplementRule.Evaluate(profile, entries, tuesday, tuesday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(SupplementTypes.Evening, result.LineItems[0].TimeType);
    }
}
