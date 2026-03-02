using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for the OnCallDutyRule (Sprint 5).
/// Verifies AC disabled, HK/PROSA enabled, filtering by activity type, period filtering,
/// multiple entries, empty entries, and rate application.
/// </summary>
public class OnCallDutyRuleTests
{
    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static AgreementRuleConfig CreateConfig(bool enabled, decimal rate = 0.33m, string agreement = "HK") =>
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
            OnCallDutyEnabled = enabled,
            OnCallDutyRate = rate,
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

    private static TimeEntry CreateOnCallEntry(DateOnly date, decimal hours) =>
        new()
        {
            EmployeeId = "EMP001",
            Date = date,
            Hours = hours,
            ActivityType = "ON_CALL",
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
    public void AC_Disabled_ReturnsEmptyLineItems()
    {
        var profile = CreateProfile("AC");
        var config = CreateConfig(enabled: false, agreement: "AC");
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 8m),
            CreateOnCallEntry(Monday.AddDays(1), 4m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(OnCallDutyRule.RuleId, result.RuleId);
        Assert.Equal("EMP001", result.EmployeeId);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void HK_Enabled_ReturnsOnCallDutyLineItems()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, rate: 0.33m);
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 8m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal("ON_CALL_DUTY", result.LineItems[0].TimeType);
        Assert.Equal(8m, result.LineItems[0].Hours);
        Assert.Equal(0.33m, result.LineItems[0].Rate);
    }

    [Fact]
    public void PROSA_Enabled_AppliesRateCorrectly()
    {
        var profile = CreateProfile("PROSA");
        var config = CreateConfig(enabled: true, rate: 0.33m, agreement: "PROSA");
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 12m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal("ON_CALL_DUTY", result.LineItems[0].TimeType);
        Assert.Equal(12m, result.LineItems[0].Hours);
        Assert.Equal(0.33m, result.LineItems[0].Rate);
    }

    [Fact]
    public void FiltersOnlyOnCallActivityType()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true);
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 8m),
            CreateNormalEntry(Monday.AddDays(1), 7.4m),
            CreateOnCallEntry(Monday.AddDays(2), 4m),
            CreateNormalEntry(Monday.AddDays(3), 8m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(2, result.LineItems.Count);
        Assert.All(result.LineItems, li => Assert.Equal("ON_CALL_DUTY", li.TimeType));
        Assert.Equal(8m, result.LineItems[0].Hours);
        Assert.Equal(4m, result.LineItems[1].Hours);
    }

    [Fact]
    public void PeriodFiltering_IgnoresEntriesOutsidePeriod()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true);
        var beforePeriod = Monday.AddDays(-3);
        var afterPeriod = Sunday.AddDays(2);
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(beforePeriod, 6m),    // Before period — ignored
            CreateOnCallEntry(Monday, 8m),           // In period
            CreateOnCallEntry(afterPeriod, 10m),     // After period — ignored
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(8m, result.LineItems[0].Hours);
        Assert.Equal(Monday, result.LineItems[0].Date);
    }

    [Fact]
    public void MultipleOnCallEntries_ProducesOneLineItemPerEntry()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, rate: 0.33m);
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 4m),
            CreateOnCallEntry(Monday.AddDays(1), 8m),
            CreateOnCallEntry(Monday.AddDays(2), 6m),
            CreateOnCallEntry(Monday.AddDays(3), 12m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(4, result.LineItems.Count);
        Assert.Equal(4m, result.LineItems[0].Hours);
        Assert.Equal(8m, result.LineItems[1].Hours);
        Assert.Equal(6m, result.LineItems[2].Hours);
        Assert.Equal(12m, result.LineItems[3].Hours);
        Assert.All(result.LineItems, li => Assert.Equal(0.33m, li.Rate));
    }

    [Fact]
    public void EmptyEntries_ReturnsSuccessWithEmptyLineItems()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true);
        var entries = new List<TimeEntry>();

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
        Assert.Equal(OnCallDutyRule.RuleId, result.RuleId);
    }

    [Fact]
    public void RateApplication_CustomRate_AppliedToAllLineItems()
    {
        var profile = CreateProfile("HK");
        var customRate = 0.50m;
        var config = CreateConfig(enabled: true, rate: customRate);
        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 10m),
            CreateOnCallEntry(Monday.AddDays(1), 6m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(2, result.LineItems.Count);
        Assert.All(result.LineItems, li =>
        {
            Assert.Equal("ON_CALL_DUTY", li.TimeType);
            Assert.Equal(customRate, li.Rate);
        });
        // Verify hours * rate conceptually correct
        Assert.Equal(10m, result.LineItems[0].Hours);
        Assert.Equal(6m, result.LineItems[1].Hours);
    }
}
