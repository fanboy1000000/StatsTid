using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class AbsenceRuleTests
{
    private static EmploymentProfile CreateProfile(decimal partTime = 1.0m) => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "Standard",
        PartTimeFraction = partTime
    };

    private static AbsenceEntry CreateAbsence(DateOnly date, string type, decimal hours = 7.4m) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        AbsenceType = type,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24"
    };

    private static readonly DateOnly Monday = new(2024, 4, 8);

    [Fact]
    public void Vacation_GrantsNormCredit()
    {
        Assert.True(AbsenceRule.GrantsNormCredit(AbsenceTypes.Vacation));
    }

    [Fact]
    public void LeaveWithoutPay_DoesNotGrantNormCredit()
    {
        Assert.False(AbsenceRule.GrantsNormCredit(AbsenceTypes.LeaveWithoutPay));
    }

    [Fact]
    public void Vacation_ProducesVacationTimeType()
    {
        var profile = CreateProfile();
        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.Vacation, 7.4m)
        };

        var result = AbsenceRule.Evaluate(profile, absences, Monday, Monday.AddDays(6));

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal("VACATION", result.LineItems[0].TimeType);
        Assert.Equal(7.4m, result.LineItems[0].Hours);
    }

    [Fact]
    public void ChildSick_ProducesCorrectTimeType()
    {
        var profile = CreateProfile();
        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.ChildSick1, 7.4m)
        };

        var result = AbsenceRule.Evaluate(profile, absences, Monday, Monday.AddDays(6));

        Assert.Single(result.LineItems);
        Assert.Equal("CHILD_SICK_DAY", result.LineItems[0].TimeType);
    }

    [Fact]
    public void NormCreditHours_IncludesVacationAndCareDay()
    {
        var profile = CreateProfile();
        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.Vacation, 7.4m),
            CreateAbsence(Monday.AddDays(1), AbsenceTypes.CareDay, 7.4m),
        };

        var credits = AbsenceRule.GetNormCreditHours(profile, absences, Monday, Monday.AddDays(6));

        Assert.Equal(14.8m, credits);
    }

    [Fact]
    public void NormCreditHours_ExcludesLeaveWithoutPay()
    {
        var profile = CreateProfile();
        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.Vacation, 7.4m),
            CreateAbsence(Monday.AddDays(1), AbsenceTypes.LeaveWithoutPay, 7.4m),
        };

        var credits = AbsenceRule.GetNormCreditHours(profile, absences, Monday, Monday.AddDays(6));

        Assert.Equal(7.4m, credits);
    }

    [Fact]
    public void NormReduction_FromLeaveWithoutPay()
    {
        var profile = CreateProfile();
        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.LeaveWithoutPay, 7.4m),
        };

        var reduction = AbsenceRule.GetNormReductionHours(profile, absences, Monday, Monday.AddDays(6));

        Assert.Equal(7.4m, reduction);
    }

    [Fact]
    public void PartTime_ProRatedAbsenceHours()
    {
        var profile = CreateProfile(partTime: 0.5m);
        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.Vacation, 0m) // 0 = use default
        };

        var result = AbsenceRule.Evaluate(profile, absences, Monday, Monday.AddDays(6));

        Assert.True(result.Success);
        Assert.Single(result.LineItems);
        Assert.Equal(3.7m, result.LineItems[0].Hours); // 7.4 * 0.5
    }
}
