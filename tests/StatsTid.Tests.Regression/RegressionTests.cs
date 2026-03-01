using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression;

public class RegressionTests
{
    private static EmploymentProfile CreateProfile(string agreement, string okVersion = "OK24") => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = agreement,
        OkVersion = okVersion,
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m
    };

    private static TimeEntry CreateEntry(DateOnly date, decimal hours, string agreement = "AC", string okVersion = "OK24") => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = agreement,
        OkVersion = okVersion
    };

    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    /// <summary>
    /// Regression 1: Same inputs evaluated under OK24 and OK26 must produce structurally
    /// identical results (since OK26 is a placeholder with identical values).
    /// </summary>
    [Fact]
    public void OkVersionTransition_OK24ToOK26_RecalculatesCorrectly()
    {
        var entriesOk24 = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 8m, "HK", "OK24"))
            .ToList();
        var entriesOk26 = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 8m, "HK", "OK26"))
            .ToList();

        var profileOk24 = CreateProfile("HK", "OK24");
        var profileOk26 = CreateProfile("HK", "OK26");

        var configOk24 = AgreementConfigProvider.GetConfig("HK", "OK24");
        var configOk26 = AgreementConfigProvider.GetConfig("HK", "OK26");

        var overtimeOk24 = OvertimeRule.Evaluate(profileOk24, entriesOk24, Monday, Sunday, configOk24);
        var overtimeOk26 = OvertimeRule.Evaluate(profileOk26, entriesOk26, Monday, Sunday, configOk26);

        Assert.Equal(overtimeOk24.LineItems.Count, overtimeOk26.LineItems.Count);
        for (int i = 0; i < overtimeOk24.LineItems.Count; i++)
        {
            Assert.Equal(overtimeOk24.LineItems[i].TimeType, overtimeOk26.LineItems[i].TimeType);
            Assert.Equal(overtimeOk24.LineItems[i].Hours, overtimeOk26.LineItems[i].Hours);
            Assert.Equal(overtimeOk24.LineItems[i].Rate, overtimeOk26.LineItems[i].Rate);
        }
    }

    /// <summary>
    /// Regression 2: Replaying the same inputs must produce identical results (determinism).
    /// </summary>
    [Fact]
    public void HistoricalReplay_ProducesSameResult()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 7.8m))
            .ToList();

        var result1 = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);
        var result2 = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.Equal(result1.LineItems.Count, result2.LineItems.Count);
        for (int i = 0; i < result1.LineItems.Count; i++)
        {
            Assert.Equal(result1.LineItems[i].TimeType, result2.LineItems[i].TimeType);
            Assert.Equal(result1.LineItems[i].Hours, result2.LineItems[i].Hours);
            Assert.Equal(result1.LineItems[i].Rate, result2.LineItems[i].Rate);
        }
    }

    /// <summary>
    /// Regression 3: After a retroactive correction (additional entry), new calculation
    /// produces a different (updated) result.
    /// </summary>
    [Fact]
    public void RetroactiveCorrection_UpdatesCalculation()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");

        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 7.4m))
            .ToList();

        var resultBefore = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);
        Assert.Empty(resultBefore.LineItems); // Exactly at norm

        // Retroactive correction: add 2 extra hours on Monday
        entries.Add(CreateEntry(Monday, 2m));

        var resultAfter = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);
        Assert.NotEmpty(resultAfter.LineItems); // Now has overtime
    }

    /// <summary>
    /// Regression 4: AC employees must NEVER produce OVERTIME_50 or OVERTIME_100.
    /// </summary>
    [Fact]
    public void AC_NeverProducesOvertime()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 12m))
            .ToList();

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.DoesNotContain(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
        Assert.DoesNotContain(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime100);
        Assert.Contains(result.LineItems, li => li.TimeType == OvertimeTypes.Merarbejde);
    }

    /// <summary>
    /// Regression 5: HK employees produce overtime, not merarbejde.
    /// </summary>
    [Fact]
    public void HK_ProducesOvertime_NotMerarbejde()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 9m))
            .ToList();

        var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.DoesNotContain(result.LineItems, li => li.TimeType == OvertimeTypes.Merarbejde);
        Assert.Contains(result.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
    }

    /// <summary>
    /// Regression 6: 100 iterations produce identical results (determinism proof).
    /// </summary>
    [Fact]
    public void DeterminismProof_100Iterations_IdenticalResults()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 8.5m))
            .ToList();

        var referenceResult = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

        for (int run = 0; run < 100; run++)
        {
            var result = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);

            Assert.Equal(referenceResult.LineItems.Count, result.LineItems.Count);
            for (int i = 0; i < referenceResult.LineItems.Count; i++)
            {
                Assert.Equal(referenceResult.LineItems[i].TimeType, result.LineItems[i].TimeType);
                Assert.Equal(referenceResult.LineItems[i].Hours, result.LineItems[i].Hours);
                Assert.Equal(referenceResult.LineItems[i].Rate, result.LineItems[i].Rate);
            }
        }
    }
}
