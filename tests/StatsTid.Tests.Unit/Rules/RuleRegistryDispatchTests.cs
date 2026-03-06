using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// Tests for RuleRegistry dispatch of new Sprint 10 rules (CallInWork, TravelTime)
/// and config-aware NormCheck path.
/// </summary>
public class RuleRegistryDispatchTests
{
    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static EmploymentProfile CreateProfile(string agreement = "HK") =>
        new()
        {
            EmployeeId = "EMP001",
            AgreementCode = agreement,
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            EmploymentCategory = "Standard"
        };

    private static TimeEntry CreateEntry(DateOnly date, decimal hours, string? activityType = null) =>
        new()
        {
            EmployeeId = "EMP001",
            Date = date,
            Hours = hours,
            ActivityType = activityType,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

    [Fact]
    public void EvaluateTimeRule_CallInWork_DispatchesCorrectly()
    {
        var registry = new RuleRegistry();
        var profile = CreateProfile("HK");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 1.0m, "CALL_IN"),
        };

        var result = registry.EvaluateTimeRule(
            CallInWorkRule.RuleId,
            profile, entries, Monday, Sunday);

        Assert.True(result.Success);
        Assert.Equal(CallInWorkRule.RuleId, result.RuleId);
        // HK config has CallInWorkEnabled=true, minimum 3h
        Assert.Single(result.LineItems);
        Assert.Equal("CALL_IN_WORK", result.LineItems[0].TimeType);
        Assert.Equal(3.0m, result.LineItems[0].Hours); // minimum guarantee
    }

    [Fact]
    public void EvaluateTimeRule_TravelTime_DispatchesCorrectly()
    {
        var registry = new RuleRegistry();
        var profile = CreateProfile("HK");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 2.0m, "TRAVEL_WORK"),
            CreateEntry(Monday.AddDays(1), 3.0m, "TRAVEL_NON_WORK"),
        };

        var result = registry.EvaluateTimeRule(
            TravelTimeRule.RuleId,
            profile, entries, Monday, Sunday);

        Assert.True(result.Success);
        Assert.Equal(TravelTimeRule.RuleId, result.RuleId);
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal("TRAVEL_WORK", result.LineItems[0].TimeType);
        Assert.Equal(1.0m, result.LineItems[0].Rate);
        Assert.Equal("TRAVEL_NON_WORK", result.LineItems[1].TimeType);
        Assert.Equal(0.5m, result.LineItems[1].Rate);
    }

    [Fact]
    public void Evaluate_NormCheck_DispatchesThroughConfigAwarePath()
    {
        var registry = new RuleRegistry();
        var profile = CreateProfile("HK");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
            CreateEntry(Monday.AddDays(3), 7.4m),
            CreateEntry(Monday.AddDays(4), 7.4m),
        };

        // Use the backward-compatible Evaluate (which routes through EvaluateTimeRule internally)
        var result = registry.Evaluate(
            NormCheckRule.RuleId,
            profile, entries, Monday, Sunday);

        Assert.True(result.Success);
        Assert.Equal(NormCheckRule.RuleId, result.RuleId);
        Assert.Equal(5, result.LineItems.Count);
        Assert.All(result.LineItems, li => Assert.Equal("NORMAL_HOURS", li.TimeType));
    }

    [Fact]
    public void Evaluate_CallInWork_ViaBackwardCompatibleOverload_DispatchesCorrectly()
    {
        var registry = new RuleRegistry();
        var profile = CreateProfile("HK");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 2.0m, "CALL_IN"),
        };

        // Use the backward-compatible Evaluate overload (routes through EvaluateTimeRule)
        var result = registry.Evaluate(
            CallInWorkRule.RuleId,
            profile, entries, Monday, Sunday);

        Assert.True(result.Success);
        Assert.Equal(CallInWorkRule.RuleId, result.RuleId);
        Assert.Single(result.LineItems);
        Assert.Equal("CALL_IN_WORK", result.LineItems[0].TimeType);
    }

    [Fact]
    public void GetAvailableRules_IncludesNewRules()
    {
        var registry = new RuleRegistry();
        var rules = registry.GetAvailableRules("OK24");

        Assert.Contains("CALL_IN_WORK", rules);
        Assert.Contains("TRAVEL_TIME", rules);
        Assert.Contains("NORM_CHECK_37H", rules);
    }
}
