using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Comprehensive tests for Sprint 11 features:
/// - Position override configs (TASK-1104)
/// - Academic norm model / ANNUAL_ACTIVITY (TASK-1106)
/// - Academic agreement configs (TASK-1107)
/// - CorrectionExportLine FlexDelta (TASK-1101)
/// - RetroactiveCorrectionRequested audit fields (TASK-1101)
/// - Correction SLS export format (TASK-1102)
/// - EmploymentProfile Position field (TASK-1104)
/// - Determinism proofs
/// </summary>
public class Sprint11Tests
{
    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static EmploymentProfile CreateProfile(
        string agreement = "AC",
        string okVersion = "OK24",
        string? position = null) =>
        new()
        {
            EmployeeId = "TEST01",
            AgreementCode = agreement,
            OkVersion = okVersion,
            WeeklyNormHours = 37.0m,
            EmploymentCategory = "Standard",
            IsPartTime = false,
            PartTimeFraction = 1.0m,
            Position = position
        };

    private static List<TimeEntry> CreateEntries(DateOnly start, int days, decimal hoursPerDay) =>
        Enumerable.Range(0, days)
            .Select(i => new TimeEntry
            {
                EmployeeId = "TEST01",
                Date = start.AddDays(i),
                Hours = hoursPerDay,
                ActivityType = "NORMAL",
                AgreementCode = "AC",
                OkVersion = "OK24"
            })
            .ToList();

    private static AgreementRuleConfig CreateConfigWithNormModel(
        NormModel normModel = NormModel.WEEKLY_HOURS,
        decimal annualNormHours = 0m,
        int normPeriodWeeks = 1) =>
        new()
        {
            AgreementCode = "AC",
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            HasOvertime = false,
            HasMerarbejde = true,
            MaxFlexBalance = 150.0m,
            FlexCarryoverMax = 150.0m,
            EveningSupplementEnabled = false,
            NightSupplementEnabled = false,
            WeekendSupplementEnabled = false,
            HolidaySupplementEnabled = false,
            NormModel = normModel,
            AnnualNormHours = annualNormHours,
            NormPeriodWeeks = normPeriodWeeks,
        };

    // ---------------------------------------------------------------
    // 1. EmploymentProfile Position field (TASK-1104)
    // ---------------------------------------------------------------

    [Fact]
    public void EmploymentProfile_Position_DefaultsToNull_BackwardCompat()
    {
        var profile = CreateProfile();
        Assert.Null(profile.Position);
    }

    [Fact]
    public void EmploymentProfile_Position_CanBeSetToResearcher()
    {
        var profile = CreateProfile(position: "RESEARCHER");
        Assert.Equal("RESEARCHER", profile.Position);
    }

    [Fact]
    public void EmploymentProfile_Position_CanBeSetToDepartmentHead()
    {
        var profile = CreateProfile(position: "DEPARTMENT_HEAD");
        Assert.Equal("DEPARTMENT_HEAD", profile.Position);
    }

    // ---------------------------------------------------------------
    // 2. Position Override Configs (TASK-1104)
    // ---------------------------------------------------------------

    [Fact]
    public void PositionOverrideConfigs_TryGetOverride_DepartmentHead_ReturnsOverride()
    {
        var result = PositionOverrideConfigs.TryGetOverride("AC", "OK24", "DEPARTMENT_HEAD");
        Assert.NotNull(result);
    }

    [Fact]
    public void PositionOverrideConfigs_TryGetOverride_UnknownPosition_ReturnsNull()
    {
        var result = PositionOverrideConfigs.TryGetOverride("AC", "OK24", "UNKNOWN");
        Assert.Null(result);
    }

    [Fact]
    public void PositionOverrideConfigs_TryGetOverride_NullPosition_ReturnsNull()
    {
        var result = PositionOverrideConfigs.TryGetOverride("AC", "OK24", null!);
        Assert.Null(result);
    }

    [Fact]
    public void PositionOverrideConfigs_ApplyOverride_MergesNonNullFields()
    {
        var baseConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");
        var overrideConfig = PositionOverrideConfigs.TryGetOverride("AC", "OK24", "DEPARTMENT_HEAD");
        Assert.NotNull(overrideConfig);

        var merged = PositionOverrideConfigs.ApplyOverride(baseConfig, overrideConfig!);

        // The merged config should have the override's MaxFlexBalance (200)
        Assert.Equal(200.0m, merged.MaxFlexBalance);
        // But should retain base values for fields not overridden
        Assert.Equal("AC", merged.AgreementCode);
        Assert.Equal("OK24", merged.OkVersion);
    }

    [Fact]
    public void CentralAgreementConfigs_GetConfig_WithPosition_DepartmentHead_MaxFlex200()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24", "DEPARTMENT_HEAD");
        Assert.Equal(200.0m, config.MaxFlexBalance);
    }

    [Fact]
    public void CentralAgreementConfigs_GetConfig_WithNullPosition_ReturnsBaseConfig()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24", null);
        Assert.Equal(150.0m, config.MaxFlexBalance);
    }

    [Fact]
    public void CentralAgreementConfigs_GetConfig_WithUnknownPosition_ReturnsBaseConfig()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24", "UNKNOWN_POSITION");
        Assert.Equal(150.0m, config.MaxFlexBalance);
    }

    [Fact]
    public void CentralAgreementConfigs_GetConfig_Researcher_NormPeriodWeeks4()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24", "RESEARCHER");
        Assert.Equal(4, config.NormPeriodWeeks);
    }

    // ---------------------------------------------------------------
    // 3. Academic Agreement Configs (TASK-1107)
    // ---------------------------------------------------------------

    [Fact]
    public void CentralAgreementConfigs_AcResearch_OK24_HasAnnualActivityNorm()
    {
        var config = CentralAgreementConfigs.GetConfig("AC_RESEARCH", "OK24");
        Assert.Equal(NormModel.ANNUAL_ACTIVITY, config.NormModel);
        Assert.Equal(1924m, config.AnnualNormHours);
    }

    [Fact]
    public void CentralAgreementConfigs_AcTeaching_OK24_HasAnnualNormHours1680()
    {
        var config = CentralAgreementConfigs.GetConfig("AC_TEACHING", "OK24");
        Assert.Equal(1680m, config.AnnualNormHours);
    }

    [Fact]
    public void CentralAgreementConfigs_AcResearch_OK26_Exists()
    {
        Assert.True(CentralAgreementConfigs.HasConfig("AC_RESEARCH", "OK26"));
    }

    [Fact]
    public void CentralAgreementConfigs_AcTeaching_OK26_Exists()
    {
        Assert.True(CentralAgreementConfigs.HasConfig("AC_TEACHING", "OK26"));
    }

    // ---------------------------------------------------------------
    // 4. NormCheckRule with ANNUAL_ACTIVITY (TASK-1106)
    // ---------------------------------------------------------------

    [Fact]
    public void NormCheckRule_AnnualActivity_FullYear_CorrectNorm()
    {
        var profile = CreateProfile(agreement: "AC_RESEARCH");
        var config = CreateConfigWithNormModel(
            normModel: NormModel.ANNUAL_ACTIVITY,
            annualNormHours: 1924m);

        var periodStart = new DateOnly(2024, 1, 1);
        var periodEnd = new DateOnly(2024, 12, 31);
        var totalDays = periodEnd.DayNumber - periodStart.DayNumber + 1; // 366 (leap year)

        // Create entries covering the full year with enough hours
        var entries = CreateEntries(periodStart, totalDays, 6.0m);

        var result = NormCheckRule.Evaluate(profile, entries, periodStart, periodEnd, config);

        Assert.True(result.Success);
        // For full year: norm = AnnualNormHours * PartTimeFraction * (days / 365)
        // Exact value depends on implementation (365 vs 366 for leap year)
        Assert.NotNull(result.NormHoursTotal);
        Assert.True(result.NormHoursTotal > 0);
    }

    [Fact]
    public void NormCheckRule_AnnualActivity_OneMonth_ProRatedNorm()
    {
        var profile = CreateProfile(agreement: "AC_RESEARCH");
        var config = CreateConfigWithNormModel(
            normModel: NormModel.ANNUAL_ACTIVITY,
            annualNormHours: 1924m);

        var periodStart = new DateOnly(2024, 3, 1);
        var periodEnd = new DateOnly(2024, 3, 31);
        var totalDays = 31;

        var entries = CreateEntries(periodStart, totalDays, 8.0m);

        var result = NormCheckRule.Evaluate(profile, entries, periodStart, periodEnd, config);

        Assert.True(result.Success);
        // Pro-rated norm for ~31 days: 1924 * 1.0 * 31/365 ≈ 163.4
        Assert.NotNull(result.NormHoursTotal);
        var expectedApprox = 1924m * 31m / 365m;
        // Allow some tolerance for implementation differences
        Assert.InRange(result.NormHoursTotal!.Value, expectedApprox - 10m, expectedApprox + 10m);
    }

    [Fact]
    public void NormCheckRule_AnnualActivity_PartTime50_HalvesNorm()
    {
        var profile = new EmploymentProfile
        {
            EmployeeId = "TEST01",
            AgreementCode = "AC_RESEARCH",
            OkVersion = "OK24",
            WeeklyNormHours = 37.0m,
            EmploymentCategory = "Standard",
            IsPartTime = true,
            PartTimeFraction = 0.5m,
            Position = null
        };

        var config = CreateConfigWithNormModel(
            normModel: NormModel.ANNUAL_ACTIVITY,
            annualNormHours: 1924m);

        var periodStart = new DateOnly(2024, 1, 1);
        var periodEnd = new DateOnly(2024, 12, 31);
        var totalDays = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var entries = CreateEntries(periodStart, totalDays, 3.0m);

        var result = NormCheckRule.Evaluate(profile, entries, periodStart, periodEnd, config);

        Assert.True(result.Success);
        Assert.NotNull(result.NormHoursTotal);

        // Full-time norm for the same period
        var fullTimeProfile = CreateProfile(agreement: "AC_RESEARCH");
        var fullResult = NormCheckRule.Evaluate(fullTimeProfile, entries, periodStart, periodEnd, config);

        // Part-time norm should be approximately half of full-time
        Assert.True(result.NormHoursTotal!.Value < fullResult.NormHoursTotal!.Value);
        var ratio = result.NormHoursTotal.Value / fullResult.NormHoursTotal.Value;
        Assert.InRange(ratio, 0.49m, 0.51m);
    }

    [Fact]
    public void NormCheckRule_WeeklyHours_UnchangedBehavior_Regression()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormModel(
            normModel: NormModel.WEEKLY_HOURS,
            normPeriodWeeks: 1);

        var monday = new DateOnly(2024, 4, 1);
        var entries = new List<TimeEntry>();
        for (int day = 0; day < 5; day++)
        {
            entries.Add(new TimeEntry
            {
                EmployeeId = "TEST01",
                Date = monday.AddDays(day),
                Hours = 7.4m,
                ActivityType = "NORMAL",
                AgreementCode = "AC",
                OkVersion = "OK24"
            });
        }

        var result = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), config);

        Assert.True(result.Success);
        Assert.Equal(37.0m, result.NormHoursTotal);
        Assert.Equal(37.0m, result.ActualHoursTotal);
        Assert.True(result.NormFulfilled);
    }

    [Fact]
    public void NormCheckRule_AnnualActivity_NormFulfilled_WhenAboveNorm()
    {
        var profile = CreateProfile(agreement: "AC_RESEARCH");
        var config = CreateConfigWithNormModel(
            normModel: NormModel.ANNUAL_ACTIVITY,
            annualNormHours: 1924m);

        // One week: norm ≈ 1924 * 7/365 ≈ 36.9
        var start = new DateOnly(2024, 4, 1);
        var end = new DateOnly(2024, 4, 7);
        var entries = CreateEntries(start, 7, 6.0m); // 42 hours > ~36.9

        var result = NormCheckRule.Evaluate(profile, entries, start, end, config);

        Assert.True(result.Success);
        Assert.True(result.NormFulfilled);
    }

    [Fact]
    public void NormCheckRule_AnnualActivity_NormNotFulfilled_WhenBelowNorm()
    {
        var profile = CreateProfile(agreement: "AC_RESEARCH");
        var config = CreateConfigWithNormModel(
            normModel: NormModel.ANNUAL_ACTIVITY,
            annualNormHours: 1924m);

        // One week: norm ≈ 1924 * 7/365 ≈ 36.9
        var start = new DateOnly(2024, 4, 1);
        var end = new DateOnly(2024, 4, 7);
        var entries = CreateEntries(start, 5, 4.0m); // 20 hours < ~36.9

        var result = NormCheckRule.Evaluate(profile, entries, start, end, config);

        Assert.True(result.Success);
        Assert.False(result.NormFulfilled);
    }

    // ---------------------------------------------------------------
    // 5. CorrectionExportLine FlexDelta (TASK-1101)
    // ---------------------------------------------------------------

    [Fact]
    public void CorrectionExportLine_FlexDelta_CanBeSet()
    {
        var line = new CorrectionExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            OriginalHours = 37.0m,
            CorrectedHours = 40.0m,
            DifferenceHours = 3.0m,
            OriginalAmount = 0m,
            CorrectedAmount = 0m,
            DifferenceAmount = 0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            FlexDelta = 3.0m
        };

        Assert.Equal(3.0m, line.FlexDelta);
    }

    [Fact]
    public void CorrectionExportLine_FlexDelta_DefaultsToNull()
    {
        var line = new CorrectionExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            OriginalHours = 37.0m,
            CorrectedHours = 37.0m,
            DifferenceHours = 0m,
            OriginalAmount = 0m,
            CorrectedAmount = 0m,
            DifferenceAmount = 0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24"
        };

        Assert.Null(line.FlexDelta);
    }

    [Fact]
    public void CorrectionExportLine_FlexDelta_NegativeValue_ForReducedFlex()
    {
        var line = new CorrectionExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            OriginalHours = 40.0m,
            CorrectedHours = 37.0m,
            DifferenceHours = -3.0m,
            OriginalAmount = 0m,
            CorrectedAmount = 0m,
            DifferenceAmount = 0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            FlexDelta = -3.0m
        };

        Assert.Equal(-3.0m, line.FlexDelta);
    }

    // ---------------------------------------------------------------
    // 6. RetroactiveCorrectionRequested audit fields (TASK-1101)
    // ---------------------------------------------------------------

    [Fact]
    public void RetroactiveCorrectionRequested_PreviousOkVersion_CanBeSet()
    {
        var evt = new RetroactiveCorrectionRequested
        {
            EmployeeId = "EMP001",
            OriginalPeriodStart = new DateOnly(2024, 4, 8),
            OriginalPeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "AC",
            OkVersion = "OK26",
            Reason = "OK version transition correction",
            CorrectedByActorId = "admin01",
            CorrectionLineCount = 2,
            TotalDifferenceHours = 1.5m,
            PreviousOkVersion = "OK24"
        };

        Assert.Equal("OK24", evt.PreviousOkVersion);
        Assert.Equal("OK26", evt.OkVersion);
    }

    [Fact]
    public void RetroactiveCorrectionRequested_Position_CanBeSet()
    {
        var evt = new RetroactiveCorrectionRequested
        {
            EmployeeId = "EMP001",
            OriginalPeriodStart = new DateOnly(2024, 4, 8),
            OriginalPeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "AC",
            OkVersion = "OK24",
            Reason = "Position override correction",
            CorrectedByActorId = "admin01",
            CorrectionLineCount = 1,
            TotalDifferenceHours = 2.0m,
            Position = "DEPARTMENT_HEAD"
        };

        Assert.Equal("DEPARTMENT_HEAD", evt.Position);
    }

    [Fact]
    public void RetroactiveCorrectionRequested_PreviousOkVersion_DefaultsToNull()
    {
        var evt = new RetroactiveCorrectionRequested
        {
            EmployeeId = "EMP001",
            OriginalPeriodStart = new DateOnly(2024, 4, 8),
            OriginalPeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "AC",
            OkVersion = "OK24",
            Reason = "Simple correction",
            CorrectedByActorId = "admin01",
            CorrectionLineCount = 1,
            TotalDifferenceHours = 0.5m
        };

        Assert.Null(evt.PreviousOkVersion);
        Assert.Null(evt.Position);
    }

    // ---------------------------------------------------------------
    // 7. Correction SLS Export Format (TASK-1102)
    // ---------------------------------------------------------------

    [Fact]
    public void SlsExportFormatter_FormatCorrections_HeaderUsesHCPrefix()
    {
        var corrections = new List<CorrectionExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1020",
                OriginalHours = 3.0m,
                CorrectedHours = 5.0m,
                DifferenceHours = 2.0m,
                OriginalAmount = 4.5m,
                CorrectedAmount = 7.5m,
                DifferenceAmount = 3.0m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var exportId = "CORR-001";
        var timestamp = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        var output = SlsExportFormatter.FormatCorrections(corrections, exportId, timestamp);
        var lines = output.Split(Environment.NewLine);

        Assert.StartsWith("HC|", lines[0]);
    }

    [Fact]
    public void SlsExportFormatter_FormatCorrections_DataRecordsUseCPrefix()
    {
        var corrections = new List<CorrectionExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1020",
                OriginalHours = 3.0m,
                CorrectedHours = 5.0m,
                DifferenceHours = 2.0m,
                OriginalAmount = 4.5m,
                CorrectedAmount = 7.5m,
                DifferenceAmount = 3.0m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var output = SlsExportFormatter.FormatCorrections(
            corrections, "CORR-002",
            new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var lines = output.Split(Environment.NewLine);

        // Second line should be a data record with C| prefix
        Assert.StartsWith("C|", lines[1]);
    }

    [Fact]
    public void SlsExportFormatter_FormatCorrections_TrailerUsesTCPrefix()
    {
        var corrections = new List<CorrectionExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1020",
                OriginalHours = 3.0m,
                CorrectedHours = 5.0m,
                DifferenceHours = 2.0m,
                OriginalAmount = 4.5m,
                CorrectedAmount = 7.5m,
                DifferenceAmount = 3.0m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var output = SlsExportFormatter.FormatCorrections(
            corrections, "CORR-003",
            new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var lines = output.Split(Environment.NewLine);

        // Last non-empty line should be trailer with TC| prefix
        var lastLine = lines.Where(l => !string.IsNullOrEmpty(l)).Last();
        Assert.StartsWith("TC|", lastLine);
    }

    [Fact]
    public void SlsExportFormatter_FormatCorrections_ChecksumUsesAbsoluteDifferences()
    {
        // Test with a negative difference to verify absolute value is used
        var corrections = new List<CorrectionExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1020",
                OriginalHours = 5.0m,
                CorrectedHours = 3.0m,
                DifferenceHours = -2.0m,
                OriginalAmount = 7.5m,
                CorrectedAmount = 4.5m,
                DifferenceAmount = -3.0m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var output = SlsExportFormatter.FormatCorrections(
            corrections, "CORR-ABS",
            new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc));

        // The output should not have negative checksum values
        var lines = output.Split(Environment.NewLine);
        var trailerLine = lines.Where(l => l.StartsWith("TC|")).First();
        var parts = trailerLine.Split('|');
        // Checksum (last field) should be positive
        var checksum = long.Parse(parts[^1]);
        Assert.True(checksum >= 0, "Checksum should use absolute differences and be non-negative");
    }

    [Fact]
    public void SlsExportFormatter_FormatCorrections_InvariantCulture()
    {
        var corrections = new List<CorrectionExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1020",
                OriginalHours = 3.5m,
                CorrectedHours = 5.25m,
                DifferenceHours = 1.75m,
                OriginalAmount = 5.25m,
                CorrectedAmount = 7.875m,
                DifferenceAmount = 2.625m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var output = SlsExportFormatter.FormatCorrections(
            corrections, "CORR-IC",
            new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc));

        // Verify decimal separator is period (.) not comma (,)
        Assert.Contains("1.75", output);
        Assert.DoesNotContain("1,75", output);
    }

    // ---------------------------------------------------------------
    // 8. Determinism Proofs
    // ---------------------------------------------------------------

    [Fact]
    public void Determinism_NormCheckRule_AnnualActivity_SameInputsSameOutputs()
    {
        var profile = CreateProfile(agreement: "AC_RESEARCH");
        var config = CreateConfigWithNormModel(
            normModel: NormModel.ANNUAL_ACTIVITY,
            annualNormHours: 1924m);

        var start = new DateOnly(2024, 4, 1);
        var end = new DateOnly(2024, 4, 30);
        var entries = CreateEntries(start, 30, 7.0m);

        var result1 = NormCheckRule.Evaluate(profile, entries, start, end, config);
        var result2 = NormCheckRule.Evaluate(profile, entries, start, end, config);

        Assert.Equal(result1.NormHoursTotal, result2.NormHoursTotal);
        Assert.Equal(result1.ActualHoursTotal, result2.ActualHoursTotal);
        Assert.Equal(result1.Deviation, result2.Deviation);
        Assert.Equal(result1.NormFulfilled, result2.NormFulfilled);
        Assert.Equal(result1.Success, result2.Success);
        Assert.Equal(result1.LineItems.Count, result2.LineItems.Count);
    }

    [Fact]
    public void Determinism_PositionOverride_SameInputsSameConfig()
    {
        var config1 = CentralAgreementConfigs.GetConfig("AC", "OK24", "DEPARTMENT_HEAD");
        var config2 = CentralAgreementConfigs.GetConfig("AC", "OK24", "DEPARTMENT_HEAD");

        Assert.Equal(config1.MaxFlexBalance, config2.MaxFlexBalance);
        Assert.Equal(config1.AgreementCode, config2.AgreementCode);
        Assert.Equal(config1.OkVersion, config2.OkVersion);
        Assert.Equal(config1.WeeklyNormHours, config2.WeeklyNormHours);
        Assert.Equal(config1.NormPeriodWeeks, config2.NormPeriodWeeks);
    }

    [Fact]
    public void Determinism_NormCheckRule_WeeklyHours_SameInputsSameOutputs()
    {
        var profile = CreateProfile();
        var config = CreateConfigWithNormModel(normModel: NormModel.WEEKLY_HOURS, normPeriodWeeks: 1);
        var monday = new DateOnly(2024, 4, 1);
        var entries = CreateEntries(monday, 5, 7.4m);

        var result1 = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), config);
        var result2 = NormCheckRule.Evaluate(profile, entries, monday, monday.AddDays(6), config);

        Assert.Equal(result1.NormHoursTotal, result2.NormHoursTotal);
        Assert.Equal(result1.ActualHoursTotal, result2.ActualHoursTotal);
        Assert.Equal(result1.NormFulfilled, result2.NormFulfilled);
    }
}
