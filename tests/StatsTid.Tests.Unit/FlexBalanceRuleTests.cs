using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class FlexBalanceRuleTests
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

    private static TimeEntry CreateEntry(DateOnly date, decimal hours) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "HK",
        OkVersion = "OK24"
    };

    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    [Fact]
    public void ExactNorm_ZeroDelta()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
            CreateEntry(Monday.AddDays(3), 7.4m),
            CreateEntry(Monday.AddDays(4), 7.4m),
        };

        var result = FlexBalanceRule.Evaluate(profile, entries, new List<AbsenceEntry>(), Monday, Sunday, config, 0m);

        Assert.True(result.Success);
        Assert.Equal(0m, result.Delta);
        Assert.Equal(0m, result.NewBalance);
    }

    [Fact]
    public void OverNorm_PositiveDelta()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 8m),
            CreateEntry(Monday.AddDays(1), 8m),
            CreateEntry(Monday.AddDays(2), 8m),
            CreateEntry(Monday.AddDays(3), 8m),
            CreateEntry(Monday.AddDays(4), 8m),
        };

        var result = FlexBalanceRule.Evaluate(profile, entries, new List<AbsenceEntry>(), Monday, Sunday, config, 0m);

        Assert.Equal(3.0m, result.Delta);
        Assert.Equal(3.0m, result.NewBalance);
    }

    [Fact]
    public void UnderNorm_NegativeDelta()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7m),
            CreateEntry(Monday.AddDays(1), 7m),
            CreateEntry(Monday.AddDays(2), 7m),
            CreateEntry(Monday.AddDays(3), 7m),
            CreateEntry(Monday.AddDays(4), 7m),
        };

        var result = FlexBalanceRule.Evaluate(profile, entries, new List<AbsenceEntry>(), Monday, Sunday, config, 10m);

        Assert.Equal(-2.0m, result.Delta);
        Assert.Equal(8.0m, result.NewBalance);
    }

    [Fact]
    public void VacationDay_GrantsNormCredit()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        // 4 work days + 1 vacation day
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
            CreateEntry(Monday.AddDays(3), 7.4m),
        };
        var absences = new List<AbsenceEntry>
        {
            new()
            {
                EmployeeId = "EMP001", Date = Monday.AddDays(4),
                AbsenceType = AbsenceTypes.Vacation, Hours = 7.4m,
                AgreementCode = "HK", OkVersion = "OK24"
            }
        };

        var result = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 0m);

        Assert.Equal(0m, result.Delta); // 29.6 + 7.4 = 37
    }

    [Fact]
    public void ClampToMaxBalance()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24"); // Max = 100
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 10m),
            CreateEntry(Monday.AddDays(1), 10m),
            CreateEntry(Monday.AddDays(2), 10m),
            CreateEntry(Monday.AddDays(3), 10m),
            CreateEntry(Monday.AddDays(4), 10m),
        };

        var result = FlexBalanceRule.Evaluate(profile, entries, new List<AbsenceEntry>(), Monday, Sunday, config, 95m);

        Assert.Equal(100m, result.NewBalance);
        Assert.True(result.ExcessForPayout > 0);
    }

    [Fact]
    public void CarryoverPreviousBalance()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
            CreateEntry(Monday.AddDays(3), 7.4m),
            CreateEntry(Monday.AddDays(4), 7.4m),
        };

        var result = FlexBalanceRule.Evaluate(profile, entries, new List<AbsenceEntry>(), Monday, Sunday, config, 25.5m);

        Assert.Equal(0m, result.Delta);
        Assert.Equal(25.5m, result.NewBalance);
        Assert.Equal(25.5m, result.PreviousBalance);
    }

    [Fact]
    public void LeaveWithoutPay_ReducesNorm()
    {
        var profile = CreateProfile();
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        // 3 work days + 1 day leave without pay
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 7.4m),
            CreateEntry(Monday.AddDays(1), 7.4m),
            CreateEntry(Monday.AddDays(2), 7.4m),
        };
        var absences = new List<AbsenceEntry>
        {
            new()
            {
                EmployeeId = "EMP001", Date = Monday.AddDays(3),
                AbsenceType = AbsenceTypes.LeaveWithoutPay, Hours = 7.4m,
                AgreementCode = "HK", OkVersion = "OK24"
            }
        };

        // Norm = 37 - 7.4 = 29.6, Worked = 22.2, No norm credit from unpaid leave
        var result = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 0m);

        Assert.Equal(-7.4m, result.Delta); // 22.2 - 29.6
    }

    [Fact]
    public void AutoPayoutExcess_FlaggedCorrectly()
    {
        var profile = CreateProfile("AC"); // Max 150
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");
        var entries = new List<TimeEntry>
        {
            CreateEntry(Monday, 12m),
            CreateEntry(Monday.AddDays(1), 12m),
            CreateEntry(Monday.AddDays(2), 12m),
            CreateEntry(Monday.AddDays(3), 12m),
            CreateEntry(Monday.AddDays(4), 12m),
        };

        // Previous = 148, delta = 23 → raw = 171, clamped to 150, excess = 21
        var result = FlexBalanceRule.Evaluate(profile, entries, new List<AbsenceEntry>(), Monday, Sunday, config, 148m);

        Assert.Equal(150m, result.NewBalance);
        Assert.Equal(21m, result.ExcessForPayout);
    }

    // --- Sprint 4: GetPayoutLineItem tests ---

    [Fact]
    public void GetPayoutLineItem_NoExcess_ReturnsNull()
    {
        var result = new FlexBalanceResult
        {
            EmployeeId = "EMP001",
            PreviousBalance = 50m,
            NewBalance = 53m,
            Delta = 3m,
            WorkedHours = 40m,
            AbsenceNormCredits = 0m,
            NormHours = 37m,
            ExcessForPayout = 0m,
            Success = true
        };

        var payout = FlexBalanceRule.GetPayoutLineItem(result, Sunday);

        Assert.Null(payout);
    }

    [Fact]
    public void GetPayoutLineItem_WithExcess_ReturnsFlexPayoutItem()
    {
        var result = new FlexBalanceResult
        {
            EmployeeId = "EMP001",
            PreviousBalance = 140m,
            NewBalance = 150m,
            Delta = 25.5m,
            WorkedHours = 62.5m,
            AbsenceNormCredits = 0m,
            NormHours = 37m,
            ExcessForPayout = 15.5m,
            Success = true
        };

        var payout = FlexBalanceRule.GetPayoutLineItem(result, Sunday);

        Assert.NotNull(payout);
        Assert.Equal("FLEX_PAYOUT", payout.TimeType);
        Assert.Equal(15.5m, payout.Hours);
        Assert.Equal(1.0m, payout.Rate);
    }

    [Fact]
    public void GetPayoutLineItem_NegativeExcess_ReturnsNull()
    {
        var result = new FlexBalanceResult
        {
            EmployeeId = "EMP001",
            PreviousBalance = 50m,
            NewBalance = 45m,
            Delta = -5m,
            WorkedHours = 32m,
            AbsenceNormCredits = 0m,
            NormHours = 37m,
            ExcessForPayout = -5m,
            Success = true
        };

        var payout = FlexBalanceRule.GetPayoutLineItem(result, Sunday);

        Assert.Null(payout);
    }

    [Fact]
    public void GetPayoutLineItem_DateMatchesPeriodEnd()
    {
        var periodEnd = new DateOnly(2024, 6, 30);
        var result = new FlexBalanceResult
        {
            EmployeeId = "EMP001",
            PreviousBalance = 140m,
            NewBalance = 150m,
            Delta = 20m,
            WorkedHours = 57m,
            AbsenceNormCredits = 0m,
            NormHours = 37m,
            ExcessForPayout = 10m,
            Success = true
        };

        var payout = FlexBalanceRule.GetPayoutLineItem(result, periodEnd);

        Assert.NotNull(payout);
        Assert.Equal(periodEnd, payout.Date);
    }
}
