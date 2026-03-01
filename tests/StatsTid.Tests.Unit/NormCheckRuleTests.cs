using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class NormCheckRuleTests
{
    private static EmploymentProfile CreateProfile(
        decimal weeklyNorm = 37m,
        decimal partTimeFraction = 1.0m) => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = weeklyNorm,
        EmploymentCategory = "Standard",
        PartTimeFraction = partTimeFraction
    };

    private static TimeEntry CreateEntry(DateOnly date, decimal hours) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24"
    };

    [Fact]
    public void FullWeek_37Hours_NormFulfilled()
    {
        var profile = CreateProfile();
        var monday = new DateOnly(2024, 4, 1);
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 7.4m),
            CreateEntry(monday.AddDays(1), 7.4m),
            CreateEntry(monday.AddDays(2), 7.4m),
            CreateEntry(monday.AddDays(3), 7.4m),
            CreateEntry(monday.AddDays(4), 7.4m),
        };

        var summary = NormCheckRule.Summarize(profile, entries, monday, monday.AddDays(6));

        Assert.True(summary.NormFulfilled);
        Assert.Equal(37.0m, summary.ActualHours);
        Assert.Equal(37.0m, summary.NormHours);
        Assert.Equal(0.0m, summary.Deviation);
    }

    [Fact]
    public void PartialWeek_BelowNorm_NotFulfilled()
    {
        var profile = CreateProfile();
        var monday = new DateOnly(2024, 4, 1);
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 7.4m),
            CreateEntry(monday.AddDays(1), 7.4m),
            CreateEntry(monday.AddDays(2), 7.4m),
        };

        var summary = NormCheckRule.Summarize(profile, entries, monday, monday.AddDays(6));

        Assert.False(summary.NormFulfilled);
        Assert.Equal(22.2m, summary.ActualHours);
        Assert.Equal(-14.8m, summary.Deviation);
    }

    [Fact]
    public void PartTime_50Percent_ReducedNorm()
    {
        var profile = CreateProfile(partTimeFraction: 0.5m);
        var monday = new DateOnly(2024, 4, 1);
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 3.7m),
            CreateEntry(monday.AddDays(1), 3.7m),
            CreateEntry(monday.AddDays(2), 3.7m),
            CreateEntry(monday.AddDays(3), 3.7m),
            CreateEntry(monday.AddDays(4), 3.7m),
        };

        var summary = NormCheckRule.Summarize(profile, entries, monday, monday.AddDays(6));

        Assert.True(summary.NormFulfilled);
        Assert.Equal(18.5m, summary.ActualHours);
        Assert.Equal(18.5m, summary.NormHours);
    }

    [Fact]
    public void Evaluate_ReturnsLineItems_ForEachEntry()
    {
        var profile = CreateProfile();
        var monday = new DateOnly(2024, 4, 1);
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 7.4m),
            CreateEntry(monday.AddDays(1), 7.4m),
        };

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6));

        Assert.True(result.Success);
        Assert.Equal(2, result.LineItems.Count);
        Assert.All(result.LineItems, li => Assert.Equal("NORMAL_HOURS", li.TimeType));
    }

    [Fact]
    public void EntriesOutsidePeriod_AreExcluded()
    {
        var profile = CreateProfile();
        var monday = new DateOnly(2024, 4, 1);
        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 7.4m),
            CreateEntry(monday.AddDays(-7), 7.4m), // Previous week
        };

        var summary = NormCheckRule.Summarize(profile, entries, monday, monday.AddDays(6));

        Assert.Equal(7.4m, summary.ActualHours);
    }
}
