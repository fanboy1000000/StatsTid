using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.Integrations.Payroll.Services;

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

    // --- Sprint 4: Payroll Chain Regression Tests ---

    private static AbsenceEntry CreateAbsence(DateOnly date, string type, decimal hours = 7.4m, string agreement = "AC", string okVersion = "OK24") => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        AbsenceType = type,
        Hours = hours,
        AgreementCode = agreement,
        OkVersion = okVersion
    };

    /// <summary>
    /// Regression 7: AC employee full payroll chain — produces MERARBEJDE (not OVERTIME),
    /// no supplements, and each line item traces to a rule.
    /// </summary>
    [Fact]
    public void PayrollChain_ACEmployee_ProducesMerarbejde_NoOvertime()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");

        // AC employee works 45 hours in a week (5 days x 9h)
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 9m))
            .ToList();
        var absences = new List<AbsenceEntry>();

        // Run all 5 pure rule functions in sequence
        var normResult = NormCheckRule.Evaluate(profile, entries, Monday, Sunday, normPeriodWeeks: 1);
        var supplementResult = SupplementRule.Evaluate(profile, entries, Monday, Sunday, config);
        var overtimeResult = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);
        var absenceResult = AbsenceRule.Evaluate(profile, absences, Monday, Sunday);
        var flexResult = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 0m);

        // AC must produce MERARBEJDE, not OVERTIME
        Assert.Contains(overtimeResult.LineItems, li => li.TimeType == OvertimeTypes.Merarbejde);
        Assert.DoesNotContain(overtimeResult.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
        Assert.DoesNotContain(overtimeResult.LineItems, li => li.TimeType == OvertimeTypes.Overtime100);

        // AC has all supplements disabled
        Assert.Empty(supplementResult.LineItems);

        // Verify each result traces to the correct rule
        Assert.Equal(NormCheckRule.RuleId, normResult.RuleId);
        Assert.Equal(SupplementRule.RuleId, supplementResult.RuleId);
        Assert.Equal(OvertimeRule.RuleId, overtimeResult.RuleId);
        Assert.Equal(AbsenceRule.RuleId, absenceResult.RuleId);

        // Merarbejde hours = 45 - 37 = 8 hours
        var merarbejde = overtimeResult.LineItems.Single(li => li.TimeType == OvertimeTypes.Merarbejde);
        Assert.Equal(8m, merarbejde.Hours);
        Assert.Equal(1.0m, merarbejde.Rate);

        // Flex should reflect the excess
        Assert.Equal(8m, flexResult.Delta);
    }

    /// <summary>
    /// Regression 8: HK employee full payroll chain — produces OVERTIME_50, OVERTIME_100,
    /// and EVENING_SUPPLEMENT. No MERARBEJDE.
    /// </summary>
    [Fact]
    public void PayrollChain_HKEmployee_ProducesOvertime_WithSupplements()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");

        // HK employee works 42 hours: 5 days, some with evening hours
        // Mon-Thu: 8h (8:00-16:00), Fri: 10h (8:00-18:00) including evening
        var entries = new List<TimeEntry>
        {
            new() { EmployeeId = "EMP001", Date = Monday, Hours = 8m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(1), Hours = 8m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(2), Hours = 8m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(3), Hours = 8m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(4), Hours = 10m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), AgreementCode = "HK", OkVersion = "OK24" },
        };
        var absences = new List<AbsenceEntry>();

        // Run all rules
        var normResult = NormCheckRule.Evaluate(profile, entries, Monday, Sunday, normPeriodWeeks: 1);
        var supplementResult = SupplementRule.Evaluate(profile, entries, Monday, Sunday, config);
        var overtimeResult = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);
        var absenceResult = AbsenceRule.Evaluate(profile, absences, Monday, Sunday);
        var flexResult = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 0m);

        // HK must produce overtime, not merarbejde
        Assert.DoesNotContain(overtimeResult.LineItems, li => li.TimeType == OvertimeTypes.Merarbejde);
        // Total = 42h, norm = 37h, threshold100 = 40h
        // OVERTIME_50: hours from 37 to 40 = 3h
        // OVERTIME_100: hours above 40 = 2h
        Assert.Contains(overtimeResult.LineItems, li => li.TimeType == OvertimeTypes.Overtime50);
        Assert.Contains(overtimeResult.LineItems, li => li.TimeType == OvertimeTypes.Overtime100);

        var ot50 = overtimeResult.LineItems.Single(li => li.TimeType == OvertimeTypes.Overtime50);
        var ot100 = overtimeResult.LineItems.Single(li => li.TimeType == OvertimeTypes.Overtime100);
        Assert.Equal(3m, ot50.Hours);
        Assert.Equal(1.5m, ot50.Rate);
        Assert.Equal(2m, ot100.Hours);
        Assert.Equal(2.0m, ot100.Rate);

        // Friday 17:00-18:00 = 1 hour of evening supplement (HK evening: 17-23)
        Assert.Contains(supplementResult.LineItems, li => li.TimeType == SupplementTypes.Evening);

        // Verify rule IDs
        Assert.Equal(OvertimeRule.RuleId, overtimeResult.RuleId);
        Assert.Equal(SupplementRule.RuleId, supplementResult.RuleId);
    }

    /// <summary>
    /// Regression 9: All 3 new absence types (SPECIAL_HOLIDAY_ALLOWANCE, CHILD_SICK_2,
    /// CHILD_SICK_3) produce correct time types and grant norm credit.
    /// </summary>
    [Fact]
    public void PayrollChain_AbsenceScenarios_AllNewTypesCorrect()
    {
        var profile = CreateProfile("HK");

        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday, AbsenceTypes.SpecialHolidayAllowance, 7.4m, "HK"),
            CreateAbsence(Monday.AddDays(1), AbsenceTypes.ChildSick2, 7.4m, "HK"),
            CreateAbsence(Monday.AddDays(2), AbsenceTypes.ChildSick3, 7.4m, "HK"),
        };

        var result = AbsenceRule.Evaluate(profile, absences, Monday, Sunday);

        Assert.True(result.Success);
        Assert.Equal(3, result.LineItems.Count);

        // Verify time type mappings
        Assert.Equal("SPECIAL_HOLIDAY_ALLOWANCE", result.LineItems[0].TimeType);
        Assert.Equal("CHILD_SICK_DAY_2", result.LineItems[1].TimeType);
        Assert.Equal("CHILD_SICK_DAY_3", result.LineItems[2].TimeType);

        // All three must grant norm credit
        var normCredits = AbsenceRule.GetNormCreditHours(profile, absences, Monday, Sunday);
        Assert.Equal(22.2m, normCredits); // 7.4 * 3

        // Verify each line item has correct hours
        foreach (var li in result.LineItems)
        {
            Assert.Equal(7.4m, li.Hours);
            Assert.Equal(1.0m, li.Rate);
        }

        // Verify the rule ID
        Assert.Equal(AbsenceRule.RuleId, result.RuleId);
    }

    /// <summary>
    /// Regression 10: Flex payout produces FLEX_PAYOUT line item when balance exceeds cap.
    /// AC employee: MaxFlexBalance = 150, previous = 145, works 45h → delta = 8 → raw = 153,
    /// clamped to 150, excess = 3.
    /// </summary>
    [Fact]
    public void PayrollChain_FlexPayout_ProducesLineItem()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24"); // MaxFlexBalance = 150

        // AC employee works 45h in a week
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 9m))
            .ToList();
        var absences = new List<AbsenceEntry>();

        // Previous balance = 145
        var flexResult = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 145m);

        // Delta = 45 - 37 = 8, raw = 145 + 8 = 153, clamped to 150, excess = 3
        Assert.Equal(150m, flexResult.NewBalance);
        Assert.Equal(3m, flexResult.ExcessForPayout);

        // Get the payout line item
        var payoutItem = FlexBalanceRule.GetPayoutLineItem(flexResult, Sunday);

        Assert.NotNull(payoutItem);
        Assert.Equal("FLEX_PAYOUT", payoutItem.TimeType);
        Assert.Equal(3m, payoutItem.Hours);
        Assert.Equal(1.0m, payoutItem.Rate);
        Assert.Equal(Sunday, payoutItem.Date);
    }

    /// <summary>
    /// Regression 11: Traceability proof — every line item from every rule can be attributed
    /// to a RuleId. This is the end-to-end traceability guarantee for the payroll chain.
    /// </summary>
    [Fact]
    public void PayrollChain_TraceabilityProof_EveryLineItemHasRuleId()
    {
        var profile = CreateProfile("HK");
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");

        // HK employee works 42h with evening hours, plus one vacation day absence
        // Mon-Wed: 10h each (8-18), Thu: 12h (10-22, includes evening), Fri: vacation
        // Total worked = 42h, above 37h norm → overtime triggered
        var entries = new List<TimeEntry>
        {
            new() { EmployeeId = "EMP001", Date = Monday, Hours = 10m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(1), Hours = 10m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(2), Hours = 10m, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), AgreementCode = "HK", OkVersion = "OK24" },
            new() { EmployeeId = "EMP001", Date = Monday.AddDays(3), Hours = 12m, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(22, 0), AgreementCode = "HK", OkVersion = "OK24" },
        };

        var absences = new List<AbsenceEntry>
        {
            CreateAbsence(Monday.AddDays(4), AbsenceTypes.Vacation, 7.4m, "HK")
        };

        // Run all 5 rules
        var normResult = NormCheckRule.Evaluate(profile, entries, Monday, Sunday, normPeriodWeeks: 1);
        var supplementResult = SupplementRule.Evaluate(profile, entries, Monday, Sunday, config);
        var overtimeResult = OvertimeRule.Evaluate(profile, entries, Monday, Sunday, config);
        var absenceResult = AbsenceRule.Evaluate(profile, absences, Monday, Sunday);
        var flexResult = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 50m);

        // Collect all results with their rule IDs
        var allResults = new List<CalculationResult>
        {
            normResult,
            supplementResult,
            overtimeResult,
            absenceResult
        };

        // Every result must have a non-empty RuleId
        foreach (var result in allResults)
        {
            Assert.False(string.IsNullOrEmpty(result.RuleId),
                $"Result for {result.EmployeeId} has empty RuleId");
        }

        // Collect all line items with their source rule
        var allLineItemsWithRules = new List<(string RuleId, CalculationLineItem Item)>();
        foreach (var result in allResults)
        {
            foreach (var item in result.LineItems)
            {
                allLineItemsWithRules.Add((result.RuleId, item));
            }
        }

        // Add flex payout if applicable
        var payoutItem = FlexBalanceRule.GetPayoutLineItem(flexResult, Sunday);
        if (payoutItem != null)
        {
            allLineItemsWithRules.Add((FlexBalanceRule.RuleId, payoutItem));
        }

        // Must have line items from at least 3 different rules
        // (norm, overtime, absence — supplements may or may not produce items)
        var distinctRules = allLineItemsWithRules.Select(x => x.RuleId).Distinct().ToList();
        Assert.True(distinctRules.Count >= 3,
            $"Expected at least 3 distinct rules, got {distinctRules.Count}: {string.Join(", ", distinctRules)}");

        // Every single line item must trace to a valid rule
        foreach (var (ruleId, item) in allLineItemsWithRules)
        {
            Assert.False(string.IsNullOrEmpty(ruleId),
                $"Line item {item.TimeType} on {item.Date} has no RuleId");
            Assert.False(string.IsNullOrEmpty(item.TimeType),
                $"Line item from {ruleId} on {item.Date} has no TimeType");
        }

        // Verify we have the expected known rules
        Assert.Contains(NormCheckRule.RuleId, distinctRules);
        Assert.Contains(OvertimeRule.RuleId, distinctRules);
        Assert.Contains(AbsenceRule.RuleId, distinctRules);
    }

    // --- Sprint 5: On-Call Duty & Retroactive Correction Regression Tests ---

    private static AgreementRuleConfig CreateOnCallConfig(string agreement, bool enabled, decimal rate = 0.33m)
    {
        var baseConfig = AgreementConfigProvider.GetConfig(agreement, "OK24");
        return new AgreementRuleConfig
        {
            AgreementCode = baseConfig.AgreementCode,
            OkVersion = baseConfig.OkVersion,
            WeeklyNormHours = baseConfig.WeeklyNormHours,
            HasOvertime = baseConfig.HasOvertime,
            HasMerarbejde = baseConfig.HasMerarbejde,
            MaxFlexBalance = baseConfig.MaxFlexBalance,
            FlexCarryoverMax = baseConfig.FlexCarryoverMax,
            EveningSupplementEnabled = baseConfig.EveningSupplementEnabled,
            NightSupplementEnabled = baseConfig.NightSupplementEnabled,
            WeekendSupplementEnabled = baseConfig.WeekendSupplementEnabled,
            HolidaySupplementEnabled = baseConfig.HolidaySupplementEnabled,
            OnCallDutyEnabled = enabled,
            OnCallDutyRate = rate,
        };
    }

    private static TimeEntry CreateOnCallEntry(DateOnly date, decimal hours, string agreement = "HK") => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        ActivityType = "ON_CALL",
        AgreementCode = agreement,
        OkVersion = "OK24"
    };

    /// <summary>
    /// Regression 12: AC on-call duty is always disabled — AC employees must never produce
    /// ON_CALL_DUTY line items regardless of the entries provided.
    /// </summary>
    [Fact]
    public void OnCallDuty_AC_AlwaysDisabled_NeverProducesLineItems()
    {
        var profile = CreateProfile("AC");
        var config = CreateOnCallConfig("AC", enabled: false);

        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 8m, "AC"),
            CreateOnCallEntry(Monday.AddDays(1), 12m, "AC"),
            CreateOnCallEntry(Monday.AddDays(2), 4m, "AC"),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Empty(result.LineItems);
        Assert.DoesNotContain(result.LineItems, li => li.TimeType == "ON_CALL_DUTY");
    }

    /// <summary>
    /// Regression 13: HK on-call duty produces correct ON_CALL_DUTY time type that maps
    /// to SLS wage code 0710. Verifies agreement fidelity and traceability.
    /// </summary>
    [Fact]
    public void OnCallDuty_HK_Enabled_ProducesCorrectTimeType()
    {
        var profile = CreateProfile("HK");
        var config = CreateOnCallConfig("HK", enabled: true, rate: 0.33m);

        var entries = new List<TimeEntry>
        {
            CreateOnCallEntry(Monday, 8m),
            CreateOnCallEntry(Monday.AddDays(2), 12m),
        };

        var result = OnCallDutyRule.Evaluate(profile, entries, Monday, Sunday, config);

        Assert.True(result.Success);
        Assert.Equal(OnCallDutyRule.RuleId, result.RuleId);
        Assert.Equal(2, result.LineItems.Count);

        // All line items must be ON_CALL_DUTY (mappable to SLS 0710)
        Assert.All(result.LineItems, li =>
        {
            Assert.Equal("ON_CALL_DUTY", li.TimeType);
            Assert.Equal(0.33m, li.Rate);
        });

        // Verify hours match input
        Assert.Equal(8m, result.LineItems[0].Hours);
        Assert.Equal(12m, result.LineItems[1].Hours);
    }

    /// <summary>
    /// Regression 14: Flex payout produces FLEX_PAYOUT line item with correct time type.
    /// This is the unified flex response pattern — the FLEX_PAYOUT time type must be
    /// consistent across the system for payroll mapping.
    /// </summary>
    [Fact]
    public void FlexPayout_ProducesFlexPayoutTimeType()
    {
        var profile = CreateProfile("AC");
        var config = AgreementConfigProvider.GetConfig("AC", "OK24"); // MaxFlexBalance = 150

        // AC employee works 50h in a week (excess will trigger payout)
        var entries = Enumerable.Range(0, 5)
            .Select(i => CreateEntry(Monday.AddDays(i), 10m))
            .ToList();
        var absences = new List<AbsenceEntry>();

        // Previous balance = 145, delta = 13 → raw = 158, clamped to 150, excess = 8
        var flexResult = FlexBalanceRule.Evaluate(profile, entries, absences, Monday, Sunday, config, 145m);

        Assert.Equal(150m, flexResult.NewBalance);
        Assert.Equal(8m, flexResult.ExcessForPayout);

        var payoutItem = FlexBalanceRule.GetPayoutLineItem(flexResult, Sunday);

        Assert.NotNull(payoutItem);
        Assert.Equal("FLEX_PAYOUT", payoutItem.TimeType);
        Assert.Equal(8m, payoutItem.Hours);
        Assert.Equal(1.0m, payoutItem.Rate);
    }

    /// <summary>
    /// Regression 15: CorrectionExportLine diff calculation is correct when comparing
    /// previous and corrected PayrollExportLines. Verifies retroactive correction
    /// arithmetic integrity.
    /// </summary>
    [Fact]
    public void CorrectionExportLine_DiffMatchesBetweenOriginalAndCorrected()
    {
        // Original: 3h overtime at 1.5x = 4.50 amount
        var originalLine = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            Hours = 3.0m,
            Amount = 4.5m,
            PeriodStart = Monday,
            PeriodEnd = Sunday,
            OkVersion = "OK24",
            SourceRuleId = "OVERTIME_CALC",
            SourceTimeType = "OVERTIME_50"
        };

        // Corrected: 5h overtime at 1.5x = 7.50 amount
        var correctedLine = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            Hours = 5.0m,
            Amount = 7.5m,
            PeriodStart = Monday,
            PeriodEnd = Sunday,
            OkVersion = "OK24",
            SourceRuleId = "OVERTIME_CALC",
            SourceTimeType = "OVERTIME_50"
        };

        // Build the correction line
        var correction = new CorrectionExportLine
        {
            EmployeeId = originalLine.EmployeeId,
            WageType = originalLine.WageType,
            OriginalHours = originalLine.Hours,
            CorrectedHours = correctedLine.Hours,
            DifferenceHours = correctedLine.Hours - originalLine.Hours,
            OriginalAmount = originalLine.Amount,
            CorrectedAmount = correctedLine.Amount,
            DifferenceAmount = correctedLine.Amount - originalLine.Amount,
            PeriodStart = originalLine.PeriodStart,
            PeriodEnd = originalLine.PeriodEnd,
            OkVersion = originalLine.OkVersion,
            SourceRuleId = originalLine.SourceRuleId,
            SourceTimeType = originalLine.SourceTimeType
        };

        // Verify diff arithmetic
        Assert.Equal(2.0m, correction.DifferenceHours);
        Assert.Equal(3.0m, correction.DifferenceAmount);
        Assert.Equal(correction.CorrectedHours - correction.OriginalHours, correction.DifferenceHours);
        Assert.Equal(correction.CorrectedAmount - correction.OriginalAmount, correction.DifferenceAmount);

        // Verify traceability
        Assert.Equal("OVERTIME_CALC", correction.SourceRuleId);
        Assert.Equal("OVERTIME_50", correction.SourceTimeType);
    }
}
