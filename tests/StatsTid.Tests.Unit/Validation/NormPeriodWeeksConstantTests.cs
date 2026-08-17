using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Validation;

/// <summary>
/// SEC-033 — pins the relocation of the valid-norm-period set from the Rule Engine's
/// <c>NormCheckRule</c> to the SharedKernel <see cref="AgreementRuleConfig.ValidNormPeriodWeeks"/>
/// constant, and proves NormCheckRule's fallback behaviour is PRESERVED byte-for-byte after the
/// move (values outside the set fall back to a 1-week norm; values inside the set are honoured).
/// The relocation is what lets Backend.Api validate norm-period input WITHOUT referencing
/// RuleEngine.Api (ARCHITECTURE.md hard rule #2).
/// </summary>
public class NormPeriodWeeksConstantTests
{
    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
    };

    private static AgreementRuleConfig ConfigWithNormWeeks(int normPeriodWeeks) => new()
    {
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        HasOvertime = false,
        HasMerarbejde = true,
        MaxFlexBalance = 150m,
        FlexCarryoverMax = 150m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        NormPeriodWeeks = normPeriodWeeks,
    };

    [Fact]
    public void ValidNormPeriodWeeks_IsExactlyTheDomainSet()
    {
        Assert.Equal(new[] { 1, 2, 4, 8, 12 }, AgreementRuleConfig.ValidNormPeriodWeeks.OrderBy(w => w));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void InSetValues_AreHonoured(int weeks)
    {
        var result = NormCheckRule.EvaluateMultiWeek(
            Profile(), new List<TimeEntry>(), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11),
            ConfigWithNormWeeks(weeks));

        // The rule tags the result with the effective (honoured) norm-period length.
        Assert.Equal(weeks, result.NormPeriodWeeks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(52)]
    [InlineData(-4)]
    public void OutOfSetValues_FallBackToOneWeek_BehaviourPreserved(int weeks)
    {
        var result = NormCheckRule.EvaluateMultiWeek(
            Profile(), new List<TimeEntry>(), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11),
            ConfigWithNormWeeks(weeks));

        Assert.Equal(1, result.NormPeriodWeeks);
    }
}
