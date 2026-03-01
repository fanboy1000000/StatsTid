using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class OvertimeRuleTests
{
    private static EmploymentProfile CreateProfile(string agreement, decimal partTime = 1.0m) => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = agreement,
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "Standard",
        PartTimeFraction = partTime
    };

    private static TimeEntry CreateEntry(DateOnly date, decimal hours) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24"
    };

    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Friday = Monday.AddDays(4);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    [Fact]
    public void AC_ExcessHours_ProducesMerarbejde_NotOvertime()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");
        // 40 hours in one week = 3 hours excess
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 8m),
            CreateEntry(Monday.AddDays(1), 8m),
            CreateEntry(Monday.AddDays(2), 8m),
            CreateEntry(Monday.AddDays(3), 8m),
            CreateEntry(Friday, 8m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(OvertimeTypes.Merarbejde, result.LineItems[0].TimeType);
        Assert.Equal(3.0m, result.LineItems[0].Hours);
        Assert.Equal(1.0m, result.LineItems[0].Rate);
    }

    [Fact]
    public void AC_NeverProducesOvertime50()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 10m),
            CreateEntry(Monday.AddDays(1), 10m),
            CreateEntry(Monday.AddDays(2), 10m),
            CreateEntry(Monday.AddDays(3), 10m),
            CreateEntry(Friday, 10m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.DoesNotContain(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
        Assert.DoesNotContain(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime100);
    }

    [Fact]
    public void HK_38Hours_ProducesOvertime50Only()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.6m),
            CreateEntry(Monday.AddDays(1), 7.6m),
            CreateEntry(Monday.AddDays(2), 7.6m),
            CreateEntry(Monday.AddDays(3), 7.6m),
            CreateEntry(Friday, 7.6m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(OvertimeTypes.Overtime50, result.LineItems[0].TimeType);
        Assert.Equal(1.0m, result.LineItems[0].Hours);
        Assert.Equal(1.5m, result.LineItems[0].Rate);
    }

    [Fact]
    public void HK_42Hours_ProducesOvertime50_And_Overtime100()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 8.4m),
            CreateEntry(Monday.AddDays(1), 8.4m),
            CreateEntry(Monday.AddDays(2), 8.4m),
            CreateEntry(Monday.AddDays(3), 8.4m),
            CreateEntry(Friday, 8.4m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(2, result.LineItems.Count);

        var ot50 = result.LineItems.First(li => li.TimeType == OvertimeTypes.Overtime50);
        var ot100 = result.LineItems.First(li => li.TimeType == OvertimeTypes.Overtime100);

        Assert.Equal(3.0m, ot50.Hours); // 37-40
        Assert.Equal(1.5m, ot50.Rate);
        Assert.Equal(2.0m, ot100.Hours); // >40
        Assert.Equal(2.0m, ot100.Rate);
    }

    [Fact]
    public void HK_BelowNorm_NoOvertime()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.0m),
            CreateEntry(Monday.AddDays(1), 7.0m),
            CreateEntry(Monday.AddDays(2), 7.0m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void PROSA_Overtime_SameAsHK()
    {
        var profile = CreateProfile("PROSA");
        var config = AgreementConfigProvider.GetConfig("PROSA", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.8m),
            CreateEntry(Monday.AddDays(1), 7.8m),
            CreateEntry(Monday.AddDays(2), 7.8m),
            CreateEntry(Monday.AddDays(3), 7.8m),
            CreateEntry(Friday, 7.8m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Contains(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
    }

    [Fact]
    public void PartTime50_ProRatedThresholds()
    {
        var profile = CreateProfile("HK", partTime: 0.5m);
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        // Part-time norm = 18.5h, threshold50=18.5, threshold100=20
        // 20h work = 1.5h overtime50
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 4m),
            CreateEntry(Monday.AddDays(1), 4m),
            CreateEntry(Monday.AddDays(2), 4m),
            CreateEntry(Monday.AddDays(3), 4m),
            CreateEntry(Friday, 4m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Contains(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
    }

    [Fact]
    public void ExactlyAtNorm_NoOvertime()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
            CreateEntry(Monday.AddDays(3), 7.4m),
            CreateEntry(Friday, 7.4m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void HK_NeverProducesMerarbejde()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 10m),
            CreateEntry(Monday.AddDays(1), 10m),
            CreateEntry(Monday.AddDays(2), 10m),
            CreateEntry(Monday.AddDays(3), 10m),
            CreateEntry(Friday, 10m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.DoesNotContain(result.LineItems, li => li.TimeType == OvertimeTypes.Merarbejde);
    }

    [Fact]
    public void AC_ExactlyAtNorm_NoMerarbejde()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
            CreateEntry(Monday.AddDays(3), 7.4m),
            CreateEntry(Friday, 7.4m),
        };

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
    }
}
