using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// Tests for the TravelTimeRule (Sprint 10).
/// Verifies disabled config, working travel rate, non-working travel rate,
/// mixed travel types, period filtering, and non-travel entry filtering.
/// </summary>
public class TravelTimeRuleTests
{
    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static AgreementRuleConfig CreateConfig(
        bool enabled,
        decimal workingRate = 1.0m,
        decimal nonWorkingRate = 0.5m,
        string agreement = "HK") =>
        new()
        {
            AgreementCode = agreement,
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            HasOvertime = agreement != "AC",
            HasMerarbejde = agreement == "AC",
            MaxFlexBalance = 100.0m,
            FlexCarryoverMax = 100.0m,
            EveningSupplementEnabled = agreement != "AC",
            NightSupplementEnabled = agreement != "AC",
            WeekendSupplementEnabled = agreement != "AC",
            HolidaySupplementEnabled = agreement != "AC",
            TravelTimeEnabled = enabled,
            WorkingTravelRate = workingRate,
            NonWorkingTravelRate = nonWorkingRate,
        };

    private static EmploymentProfile CreateProfile(string agreement = "HK") =>
        new()
        {
            EmployeeId = "EMP001",
            AgreementCode = agreement,
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            EmploymentCategory = "Standard"
        };

    private static TimeEntry CreateTravelWorkEntry(DateOnly date, decimal hours) =>
        new()
        {
            EmployeeId = "EMP001",
            Date = date,
            Hours = hours,
            ActivityType = "TRAVEL_WORK",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

    private static TimeEntry CreateTravelNonWorkEntry(DateOnly date, decimal hours) =>
        new()
        {
            EmployeeId = "EMP001",
            Date = date,
            Hours = hours,
            ActivityType = "TRAVEL_NON_WORK",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

    private static TimeEntry CreateNormalEntry(DateOnly date, decimal hours) =>
        new()
        {
            EmployeeId = "EMP001",
            Date = date,
            Hours = hours,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

    [Fact]
    public void Evaluate_DisabledConfig_ReturnsEmptyLineItemsAndSuccess()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: false);
        var entries = new List<TimeEntry>
        {
            CreateTravelWorkEntry(Monday, 3.0m),
            CreateTravelNonWorkEntry(Monday.AddDays(1), 2.0m),
        };

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(TravelTimeRule.RuleId, result.RuleId);
        Assert.Equal("EMP001", result.EmployeeId);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void Evaluate_TravelWork_CreditedAtWorkingRate()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: true, workingRate: 1.0m);
        var entries = new List<TimeEntry>
        {
            CreateTravelWorkEntry(Monday, 3.0m),
        };

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal("TRAVEL_WORK", result.LineItems[0].TimeType);
        Assert.Equal(3.0m, result.LineItems[0].Hours);
        Assert.Equal(1.0m, result.LineItems[0].Rate);
        Assert.Equal(Monday, result.LineItems[0].Date);
    }

    [Fact]
    public void Evaluate_TravelNonWork_CreditedAtNonWorkingRate()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: true, nonWorkingRate: 0.5m);
        var entries = new List<TimeEntry>
        {
            CreateTravelNonWorkEntry(Monday, 4.0m),
        };

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal("TRAVEL_NON_WORK", result.LineItems[0].TimeType);
        Assert.Equal(4.0m, result.LineItems[0].Hours);
        Assert.Equal(0.5m, result.LineItems[0].Rate);
        Assert.Equal(Monday, result.LineItems[0].Date);
    }

    [Fact]
    public void Evaluate_MixedTravelTypes_CorrectRatesApplied()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: true, workingRate: 1.0m, nonWorkingRate: 0.5m);
        var entries = new List<TimeEntry>
        {
            CreateTravelWorkEntry(Monday, 2.0m),
            CreateTravelNonWorkEntry(Monday.AddDays(1), 3.0m),
            CreateTravelWorkEntry(Monday.AddDays(2), 1.5m),
            CreateTravelNonWorkEntry(Monday.AddDays(3), 4.0m),
        };

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(4, result.LineItems.Count);

        Assert.Equal("TRAVEL_WORK", result.LineItems[0].TimeType);
        Assert.Equal(2.0m, result.LineItems[0].Hours);
        Assert.Equal(1.0m, result.LineItems[0].Rate);

        Assert.Equal("TRAVEL_NON_WORK", result.LineItems[1].TimeType);
        Assert.Equal(3.0m, result.LineItems[1].Hours);
        Assert.Equal(0.5m, result.LineItems[1].Rate);

        Assert.Equal("TRAVEL_WORK", result.LineItems[2].TimeType);
        Assert.Equal(1.5m, result.LineItems[2].Hours);
        Assert.Equal(1.0m, result.LineItems[2].Rate);

        Assert.Equal("TRAVEL_NON_WORK", result.LineItems[3].TimeType);
        Assert.Equal(4.0m, result.LineItems[3].Hours);
        Assert.Equal(0.5m, result.LineItems[3].Rate);
    }

    [Fact]
    public void Evaluate_EntriesOutsidePeriod_AreIgnored()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: true);
        var beforePeriod = Monday.AddDays(-3);
        var afterPeriod = Sunday.AddDays(2);
        var entries = new List<TimeEntry>
        {
            CreateTravelWorkEntry(beforePeriod, 2.0m),   // before period — ignored
            CreateTravelWorkEntry(Monday, 3.0m),           // in period
            CreateTravelNonWorkEntry(afterPeriod, 4.0m),   // after period — ignored
        };

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(3.0m, result.LineItems[0].Hours);
        Assert.Equal(Monday, result.LineItems[0].Date);
    }

    [Fact]
    public void Evaluate_NonTravelEntries_AreIgnored()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: true);
        var entries = new List<TimeEntry>
        {
            CreateTravelWorkEntry(Monday, 2.0m),
            CreateNormalEntry(Monday.AddDays(1), 7.4m),
            new()
            {
                EmployeeId = "EMP001", Date = Monday.AddDays(2), Hours = 8m,
                ActivityType = "ON_CALL", AgreementCode = "HK", OkVersion = "OK24"
            },
            new()
            {
                EmployeeId = "EMP001", Date = Monday.AddDays(3), Hours = 1.5m,
                ActivityType = "CALL_IN", AgreementCode = "HK", OkVersion = "OK24"
            },
            CreateTravelNonWorkEntry(Monday.AddDays(4), 3.0m),
        };

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal("TRAVEL_WORK", result.LineItems[0].TimeType);
        Assert.Equal("TRAVEL_NON_WORK", result.LineItems[1].TimeType);
    }

    [Fact]
    public void Evaluate_EmptyEntries_ReturnsSuccessWithEmptyLineItems()
    {
        var profile = CreateProfile();
        var config = CreateConfig(enabled: true);
        var entries = new List<TimeEntry>();

        var result = TravelTimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
        Assert.Equal(TravelTimeRule.RuleId, result.RuleId);
    }

    [Fact]
    public void RuleId_IsTravelTime()
    {
        Assert.Equal("TRAVEL_TIME", TravelTimeRule.RuleId);
    }
}
