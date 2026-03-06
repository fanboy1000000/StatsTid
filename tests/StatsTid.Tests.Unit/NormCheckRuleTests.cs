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

    private static AgreementRuleConfig CreateConfigWithNormWeeks(int normPeriodWeeks) =>
        new()
        {
            AgreementCode = "HK",
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            HasOvertime = true,
            HasMerarbejde = false,
            MaxFlexBalance = 100.0m,
            FlexCarryoverMax = 100.0m,
            EveningSupplementEnabled = true,
            NightSupplementEnabled = true,
            WeekendSupplementEnabled = true,
            HolidaySupplementEnabled = true,
            NormPeriodWeeks = normPeriodWeeks,
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

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), normPeriodWeeks: 1);

        Assert.True(result.NormFulfilled);
        Assert.Equal(37.0m, result.ActualHoursTotal);
        Assert.Equal(37.0m, result.NormHoursTotal);
        Assert.Equal(0.0m, result.Deviation);
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

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), normPeriodWeeks: 1);

        Assert.False(result.NormFulfilled);
        Assert.Equal(22.2m, result.ActualHoursTotal);
        Assert.Equal(-14.8m, result.Deviation);
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

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), normPeriodWeeks: 1);

        Assert.True(result.NormFulfilled);
        Assert.Equal(18.5m, result.ActualHoursTotal);
        Assert.Equal(18.5m, result.NormHoursTotal);
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

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), normPeriodWeeks: 1);

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

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), normPeriodWeeks: 1);

        Assert.Equal(7.4m, result.ActualHoursTotal);
    }

    // --- Multi-week norm period tests (Sprint 10) ---

    [Fact]
    public void Evaluate_NormPeriodWeeks4_FullTime_NormIs148()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormWeeks(4);
        var monday = new DateOnly(2024, 4, 1);
        var periodEnd = monday.AddDays(27); // 4 weeks

        var entries = new List<TimeEntry>();
        for (int week = 0; week < 4; week++)
        {
            for (int day = 0; day < 5; day++)
            {
                entries.Add(CreateEntry(monday.AddDays(week * 7 + day), 7.4m));
            }
        }

        var result = NormCheckRule.Evaluate(profile, entries, monday, periodEnd, config);

        Assert.True(result.Success);
        Assert.Equal(4, result.NormPeriodWeeks);
        Assert.Equal(148.0m, result.NormHoursTotal);
        Assert.Equal(148.0m, result.ActualHoursTotal);
        Assert.Equal(0.0m, result.Deviation);
        Assert.True(result.NormFulfilled);
    }

    [Fact]
    public void Evaluate_NormPeriodWeeks4_PartTime80_NormIs118Point4()
    {
        var profile = CreateProfile(partTimeFraction: 0.8m);
        var config = CreateConfigWithNormWeeks(4);
        var monday = new DateOnly(2024, 4, 1);
        var periodEnd = monday.AddDays(27);

        var entries = new List<TimeEntry>();
        for (int week = 0; week < 4; week++)
        {
            for (int day = 0; day < 5; day++)
            {
                entries.Add(CreateEntry(monday.AddDays(week * 7 + day), 5.92m));
            }
        }

        var result = NormCheckRule.Evaluate(profile, entries, monday, periodEnd, config);

        Assert.True(result.Success);
        Assert.Equal(4, result.NormPeriodWeeks);
        Assert.Equal(118.4m, result.NormHoursTotal);
        Assert.Equal(118.4m, result.ActualHoursTotal);
        Assert.Equal(0.0m, result.Deviation);
        Assert.True(result.NormFulfilled);
    }

    [Fact]
    public void Evaluate_InvalidNormPeriodWeeks_FallsBackTo1()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormWeeks(3); // 3 is not in {1, 2, 4, 8, 12}
        var monday = new DateOnly(2024, 4, 1);

        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 7.4m),
            CreateEntry(monday.AddDays(1), 7.4m),
            CreateEntry(monday.AddDays(2), 7.4m),
            CreateEntry(monday.AddDays(3), 7.4m),
            CreateEntry(monday.AddDays(4), 7.4m),
        };

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), config);

        Assert.True(result.Success);
        Assert.Equal(1, result.NormPeriodWeeks);
        Assert.Equal(37.0m, result.NormHoursTotal);
    }

    [Fact]
    public void Evaluate_ConfigAwareOverload_PassesThroughNormPeriodWeeks()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormWeeks(2);
        var monday = new DateOnly(2024, 4, 1);
        var periodEnd = monday.AddDays(13); // 2 weeks

        var entries = new List<TimeEntry>();
        for (int week = 0; week < 2; week++)
        {
            for (int day = 0; day < 5; day++)
            {
                entries.Add(CreateEntry(monday.AddDays(week * 7 + day), 7.4m));
            }
        }

        var result = NormCheckRule.Evaluate(profile, entries, monday, periodEnd, config);

        Assert.Equal(2, result.NormPeriodWeeks);
        Assert.Equal(74.0m, result.NormHoursTotal);
        Assert.Equal(74.0m, result.ActualHoursTotal);
    }

    [Fact]
    public void Evaluate_ResultIncludesAllNormMetadata()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormWeeks(1);
        var monday = new DateOnly(2024, 4, 1);

        var entries = new List<TimeEntry>
        {
            CreateEntry(monday, 8.0m),
            CreateEntry(monday.AddDays(1), 8.0m),
            CreateEntry(monday.AddDays(2), 8.0m),
            CreateEntry(monday.AddDays(3), 8.0m),
            CreateEntry(monday.AddDays(4), 8.0m),
        };

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), config);

        Assert.True(result.Success);
        Assert.NotNull(result.NormPeriodWeeks);
        Assert.Equal(1, result.NormPeriodWeeks);
        Assert.NotNull(result.NormHoursTotal);
        Assert.Equal(37.0m, result.NormHoursTotal);
        Assert.NotNull(result.ActualHoursTotal);
        Assert.Equal(40.0m, result.ActualHoursTotal);
        Assert.NotNull(result.Deviation);
        Assert.Equal(3.0m, result.Deviation);
        Assert.NotNull(result.NormFulfilled);
        Assert.True(result.NormFulfilled);
    }

    [Fact]
    public void Evaluate_NormPeriodWeeks4_BelowNorm_ShowsDeviation()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormWeeks(4);
        var monday = new DateOnly(2024, 4, 1);
        var periodEnd = monday.AddDays(27);

        var entries = new List<TimeEntry>();
        for (int week = 0; week < 4; week++)
        {
            for (int day = 0; day < 5; day++)
            {
                entries.Add(CreateEntry(monday.AddDays(week * 7 + day), 7.0m));
            }
        }

        var result = NormCheckRule.Evaluate(profile, entries, monday, periodEnd, config);

        Assert.Equal(4, result.NormPeriodWeeks);
        Assert.Equal(148.0m, result.NormHoursTotal);
        Assert.Equal(140.0m, result.ActualHoursTotal);
        Assert.Equal(-8.0m, result.Deviation);
        Assert.False(result.NormFulfilled);
    }
}
