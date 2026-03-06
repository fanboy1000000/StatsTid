using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// Tests for the CallInWorkRule (Sprint 10).
/// Verifies AC disabled, HK/PROSA minimum hours guarantee,
/// actual > minimum passthrough, period filtering, activity type filtering,
/// and multiple entry handling.
/// </summary>
public class CallInWorkRuleTests
{
    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static AgreementRuleConfig CreateConfig(
        bool enabled,
        decimal minimumHours = 3.0m,
        decimal rate = 1.0m,
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
            CallInWorkEnabled = enabled,
            CallInMinimumHours = minimumHours,
            CallInRate = rate,
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

    private static TimeEntry CreateCallInEntry(DateOnly date, decimal hours) =>
        new()
        {
            EmployeeId = "EMP001",
            Date = date,
            Hours = hours,
            ActivityType = "CALL_IN",
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
        var profile = CreateProfile("AC");
        var config = CreateConfig(enabled: false, agreement: "AC");
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 1m),
            CreateCallInEntry(Monday.AddDays(1), 2m),
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(CallInWorkRule.RuleId, result.RuleId);
        Assert.Equal("EMP001", result.EmployeeId);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void Evaluate_Enabled_ActualBelowMinimum_CreditsMinimumHours()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, minimumHours: 3.0m);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 1.0m),
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal("CALL_IN_WORK", result.LineItems[0].TimeType);
        Assert.Equal(3.0m, result.LineItems[0].Hours); // Math.Max(1.0, 3.0) = 3.0
        Assert.Equal(1.0m, result.LineItems[0].Rate);
        Assert.Equal(Monday, result.LineItems[0].Date);
    }

    [Fact]
    public void Evaluate_Enabled_ActualAboveMinimum_CreditsActualHours()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, minimumHours: 3.0m);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 4.0m),
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(4.0m, result.LineItems[0].Hours); // Math.Max(4.0, 3.0) = 4.0
    }

    [Fact]
    public void Evaluate_Enabled_ActualEqualsMinimum_CreditsExactHours()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, minimumHours: 3.0m);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 3.0m),
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(3.0m, result.LineItems[0].Hours); // Math.Max(3.0, 3.0) = 3.0
    }

    [Fact]
    public void Evaluate_MultipleCallInEntries_ProducesOneLineItemPerEntry()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, minimumHours: 3.0m);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 1.0m),          // below minimum → 3.0
            CreateCallInEntry(Monday.AddDays(1), 4.0m), // above minimum → 4.0
            CreateCallInEntry(Monday.AddDays(2), 2.5m), // below minimum → 3.0
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(3, result.LineItems.Count);
        Assert.Equal(3.0m, result.LineItems[0].Hours);
        Assert.Equal(4.0m, result.LineItems[1].Hours);
        Assert.Equal(3.0m, result.LineItems[2].Hours);
        Assert.All(result.LineItems, li => Assert.Equal("CALL_IN_WORK", li.TimeType));
        Assert.All(result.LineItems, li => Assert.Equal(1.0m, li.Rate));
    }

    [Fact]
    public void Evaluate_NonCallInEntries_AreIgnored()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 2.0m),
            CreateNormalEntry(Monday.AddDays(1), 7.4m),
            CreateCallInEntry(Monday.AddDays(2), 1.5m),
            new()
            {
                EmployeeId = "EMP001", Date = Monday.AddDays(3), Hours = 8m,
                ActivityType = "ON_CALL", AgreementCode = "HK", OkVersion = "OK24"
            },
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal(3.0m, result.LineItems[0].Hours); // 2.0 → min 3.0
        Assert.Equal(3.0m, result.LineItems[1].Hours); // 1.5 → min 3.0
    }

    [Fact]
    public void Evaluate_EntriesOutsidePeriod_AreIgnored()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, minimumHours: 3.0m);
        var beforePeriod = Monday.AddDays(-3);
        var afterPeriod = Sunday.AddDays(2);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(beforePeriod, 2.0m),   // before period — ignored
            CreateCallInEntry(Monday, 1.0m),           // in period
            CreateCallInEntry(afterPeriod, 5.0m),      // after period — ignored
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(3.0m, result.LineItems[0].Hours);
        Assert.Equal(Monday, result.LineItems[0].Date);
    }

    [Fact]
    public void Evaluate_EmptyEntries_ReturnsSuccessWithEmptyLineItems()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true);
        var entries = new List<TimeEntry>();

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
        Assert.Equal(CallInWorkRule.RuleId, result.RuleId);
    }

    [Fact]
    public void Evaluate_CustomRate_AppliedToAllLineItems()
    {
        var profile = CreateProfile("HK");
        var config = CreateConfig(enabled: true, rate: 1.5m);
        var entries = new List<TimeEntry>
        {
            CreateCallInEntry(Monday, 2.0m),
        };

        var result = CallInWorkRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(1.5m, result.LineItems[0].Rate);
        Assert.Equal(3.0m, result.LineItems[0].Hours); // minimum guarantee still applies
    }

    [Fact]
    public void RuleId_IsCallInWork()
    {
        Assert.Equal("CALL_IN_WORK", CallInWorkRule.RuleId);
    }
}
