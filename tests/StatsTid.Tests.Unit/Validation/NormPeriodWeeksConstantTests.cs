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

    // S145 TASK-14505-T: NormCheckRule.EvaluateMultiWeek was retired by R8/R34. Its equivalent is the
    // weekly core's Norm result with the multi-week mode forced and the result tagged with the
    // NORM_CHECK_MULTIWEEK id: a one-piece timeline [periodStart, periodEnd] under the given config,
    // cutoff on the period's last day. The core reports the config's NormPeriodWeeks through
    // NormCheckRule.EffectiveNormPeriodWeeks (in the valid set: honoured; outside it: 1), so the
    // fallback these tests pin is unchanged. The expected values below do not change.
    private static CalculationResult EvaluateMultiWeek(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config) =>
        WeeklyTimelineCore.Evaluate(new TimelineCoreInput(
            profile.EmployeeId,
            periodStart,
            periodEnd,
            periodEnd,
            [new ResolvedPiece(periodStart, periodEnd, profile.PartTimeFraction, config)],
            entries,
            [],
            0m,
            TimelineNormMode.MultiWeek,
            NormCheckRule.MultiWeekRuleId)).Norm;

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
        var result = EvaluateMultiWeek(
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
        var result = EvaluateMultiWeek(
            Profile(), new List<TimeEntry>(), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11),
            ConfigWithNormWeeks(weeks));

        Assert.Equal(1, result.NormPeriodWeeks);
    }
}
