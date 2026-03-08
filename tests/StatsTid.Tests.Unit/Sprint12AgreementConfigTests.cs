using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class Sprint12AgreementConfigTests
{
    private static ConfigResolutionService CreateSut()
        => new(null!, null!, null!, NullLogger<ConfigResolutionService>.Instance);

    // ========================================================================
    // 1. AgreementConfigEntity.ToRuleConfig() mapping
    // ========================================================================

    [Fact]
    public void ToRuleConfig_MapsAllFields_FromAcConfig()
    {
        var entity = CreateAcEntity();
        var config = entity.ToRuleConfig();

        Assert.Equal("AC", config.AgreementCode);
        Assert.Equal("OK24", config.OkVersion);
        Assert.Equal(37.0m, config.WeeklyNormHours);
        Assert.Equal(1, config.NormPeriodWeeks);
        Assert.Equal(NormModel.WEEKLY_HOURS, config.NormModel);
        Assert.Equal(1924m, config.AnnualNormHours);
        Assert.Equal(150.0m, config.MaxFlexBalance);
        Assert.Equal(150.0m, config.FlexCarryoverMax);
        Assert.False(config.HasOvertime);
        Assert.True(config.HasMerarbejde);
        Assert.Equal(37.0m, config.OvertimeThreshold50);
        Assert.Equal(40.0m, config.OvertimeThreshold100);
        Assert.False(config.EveningSupplementEnabled);
        Assert.False(config.NightSupplementEnabled);
        Assert.False(config.WeekendSupplementEnabled);
        Assert.False(config.HolidaySupplementEnabled);
        Assert.Equal(17, config.EveningStart);
        Assert.Equal(23, config.EveningEnd);
        Assert.Equal(23, config.NightStart);
        Assert.Equal(6, config.NightEnd);
        Assert.Equal(1.25m, config.EveningRate);
        Assert.Equal(1.50m, config.NightRate);
        Assert.Equal(1.50m, config.WeekendSaturdayRate);
        Assert.Equal(2.0m, config.WeekendSundayRate);
        Assert.Equal(2.0m, config.HolidayRate);
        Assert.False(config.OnCallDutyEnabled);
        Assert.Equal(0.0m, config.OnCallDutyRate);
        Assert.False(config.CallInWorkEnabled);
        Assert.Equal(0.0m, config.CallInMinimumHours);
        Assert.Equal(0.0m, config.CallInRate);
        Assert.True(config.TravelTimeEnabled);
        Assert.Equal(1.0m, config.WorkingTravelRate);
        Assert.Equal(0.5m, config.NonWorkingTravelRate);
    }

    [Fact]
    public void ToRuleConfig_MapsHkConfig_WithOvertimeAndSupplements()
    {
        var entity = CreateHkEntity();
        var config = entity.ToRuleConfig();

        Assert.Equal("HK", config.AgreementCode);
        Assert.Equal("OK24", config.OkVersion);
        Assert.True(config.HasOvertime);
        Assert.False(config.HasMerarbejde);
        Assert.True(config.EveningSupplementEnabled);
        Assert.True(config.NightSupplementEnabled);
        Assert.True(config.WeekendSupplementEnabled);
        Assert.True(config.HolidaySupplementEnabled);
        Assert.True(config.OnCallDutyEnabled);
        Assert.Equal(0.33m, config.OnCallDutyRate);
        Assert.True(config.CallInWorkEnabled);
        Assert.Equal(3.0m, config.CallInMinimumHours);
        Assert.Equal(100.0m, config.MaxFlexBalance);
        Assert.Equal(100.0m, config.FlexCarryoverMax);
    }

    [Fact]
    public void ToRuleConfig_MapsNormModel_AnnualActivityForResearch()
    {
        var entity = CreateResearchEntity();
        var config = entity.ToRuleConfig();

        Assert.Equal(NormModel.ANNUAL_ACTIVITY, config.NormModel);
        Assert.Equal(1924m, config.AnnualNormHours);
        Assert.Equal("AC_RESEARCH", config.AgreementCode);
    }

    // ========================================================================
    // 2. ConfigResolutionService.ValidateLocalOverride
    // ========================================================================

    [Theory]
    [InlineData("HasOvertime")]
    [InlineData("HasMerarbejde")]
    [InlineData("EveningSupplementEnabled")]
    [InlineData("OnCallDutyEnabled")]
    [InlineData("OvertimeThreshold50")]
    public void ValidateLocalOverride_RejectsProtectedKeys(string protectedKey)
    {
        var sut = CreateSut();
        var centralConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");

        var (valid, error) = sut.ValidateLocalOverride(protectedKey, "true", centralConfig);

        Assert.False(valid);
        Assert.Contains("centrally negotiated", error!);
    }

    [Fact]
    public void ValidateLocalOverride_MaxFlexBalance_ValidWithinBounds()
    {
        var sut = CreateSut();
        var centralConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");
        // AC central MaxFlexBalance = 150

        var (valid, error) = sut.ValidateLocalOverride("MaxFlexBalance", "100", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLocalOverride_MaxFlexBalance_InvalidExceedsCentral()
    {
        var sut = CreateSut();
        var centralConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");
        // AC central MaxFlexBalance = 150

        var (valid, error) = sut.ValidateLocalOverride("MaxFlexBalance", "200", centralConfig);

        Assert.False(valid);
        Assert.Contains("exceeds central maximum", error!);
    }

    [Fact]
    public void ValidateLocalOverride_WeeklyNormHours_ValidWithinRange()
    {
        var sut = CreateSut();
        var centralConfig = CentralAgreementConfigs.GetConfig("HK", "OK24");

        var (valid, error) = sut.ValidateLocalOverride("WeeklyNormHours", "30", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLocalOverride_WeeklyNormHours_InvalidExceeds40()
    {
        var sut = CreateSut();
        var centralConfig = CentralAgreementConfigs.GetConfig("HK", "OK24");

        var (valid, error) = sut.ValidateLocalOverride("WeeklyNormHours", "45", centralConfig);

        Assert.False(valid);
        Assert.Contains("exceeds maximum of 40", error!);
    }

    [Fact]
    public void ValidateLocalOverride_FlexCarryoverMax_BoundaryAtCentralMax()
    {
        var sut = CreateSut();
        var centralConfig = CentralAgreementConfigs.GetConfig("HK", "OK24");
        // HK central FlexCarryoverMax = 100

        var (valid, error) = sut.ValidateLocalOverride("FlexCarryoverMax", "100", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    // ========================================================================
    // 3. ConfigResolutionService.GetCentralConfig static method
    // ========================================================================

    [Fact]
    public void GetCentralConfig_ReturnsConfig_ForKnownPair()
    {
        var config = ConfigResolutionService.GetCentralConfig("AC", "OK24");

        Assert.NotNull(config);
        Assert.Equal("AC", config!.AgreementCode);
        Assert.Equal("OK24", config.OkVersion);
    }

    [Fact]
    public void GetCentralConfig_ReturnsNull_ForUnknownPair()
    {
        var config = ConfigResolutionService.GetCentralConfig("UNKNOWN", "OK24");

        Assert.Null(config);
    }

    [Fact]
    public void GetCentralConfig_ValuesMatchExpected_AcOk24()
    {
        var config = ConfigResolutionService.GetCentralConfig("AC", "OK24");

        Assert.NotNull(config);
        Assert.Equal(37.0m, config!.WeeklyNormHours);
        Assert.False(config.HasOvertime);
        Assert.True(config.HasMerarbejde);
        Assert.Equal(150.0m, config.MaxFlexBalance);
        Assert.False(config.OnCallDutyEnabled);
        Assert.True(config.TravelTimeEnabled);
    }

    // ========================================================================
    // 4. CentralAgreementConfigs coverage — all 10 configs resolvable
    // ========================================================================

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("AC", "OK26")]
    [InlineData("HK", "OK24")]
    [InlineData("HK", "OK26")]
    [InlineData("PROSA", "OK24")]
    [InlineData("PROSA", "OK26")]
    [InlineData("AC_RESEARCH", "OK24")]
    [InlineData("AC_RESEARCH", "OK26")]
    [InlineData("AC_TEACHING", "OK24")]
    [InlineData("AC_TEACHING", "OK26")]
    public void CentralAgreementConfigs_HasAllExpectedConfigs(string code, string version)
    {
        Assert.True(CentralAgreementConfigs.HasConfig(code, version));

        var config = CentralAgreementConfigs.TryGetConfig(code, version);
        Assert.NotNull(config);
        Assert.Equal(code, config!.AgreementCode);
        Assert.Equal(version, config.OkVersion);
    }

    [Fact]
    public void CentralAgreementConfigs_BothVersionsExist_ForAllAgreements()
    {
        var codes = new[] { "AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING" };
        foreach (var code in codes)
        {
            Assert.True(CentralAgreementConfigs.HasConfig(code, "OK24"),
                $"Missing OK24 for {code}");
            Assert.True(CentralAgreementConfigs.HasConfig(code, "OK26"),
                $"Missing OK26 for {code}");
        }
    }

    // ========================================================================
    // 5. AgreementConfigStatus enum values
    // ========================================================================

    [Fact]
    public void AgreementConfigStatus_HasExpectedValues()
    {
        Assert.Equal(0, (int)AgreementConfigStatus.DRAFT);
        Assert.Equal(1, (int)AgreementConfigStatus.ACTIVE);
        Assert.Equal(2, (int)AgreementConfigStatus.ARCHIVED);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static AgreementConfigEntity CreateAcEntity() => new()
    {
        ConfigId = Guid.NewGuid(),
        AgreementCode = "AC",
        OkVersion = "OK24",
        Status = AgreementConfigStatus.ACTIVE,
        WeeklyNormHours = 37.0m,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 150.0m,
        FlexCarryoverMax = 150.0m,
        HasOvertime = false,
        HasMerarbejde = true,
        OvertimeThreshold50 = 37.0m,
        OvertimeThreshold100 = 40.0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.50m,
        WeekendSaturdayRate = 1.50m,
        WeekendSundayRate = 2.0m,
        HolidayRate = 2.0m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.0m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 0.0m,
        CallInRate = 0.0m,
        TravelTimeEnabled = true,
        WorkingTravelRate = 1.0m,
        NonWorkingTravelRate = 0.5m,
        CreatedBy = "test",
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static AgreementConfigEntity CreateResearchEntity() => new()
    {
        ConfigId = Guid.NewGuid(),
        AgreementCode = "AC_RESEARCH",
        OkVersion = "OK24",
        Status = AgreementConfigStatus.ACTIVE,
        WeeklyNormHours = 37.0m,
        NormPeriodWeeks = 1,
        NormModel = NormModel.ANNUAL_ACTIVITY,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 150.0m,
        FlexCarryoverMax = 150.0m,
        HasOvertime = false,
        HasMerarbejde = true,
        OvertimeThreshold50 = 37.0m,
        OvertimeThreshold100 = 40.0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.50m,
        WeekendSaturdayRate = 1.50m,
        WeekendSundayRate = 2.0m,
        HolidayRate = 2.0m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.0m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 0.0m,
        CallInRate = 0.0m,
        TravelTimeEnabled = true,
        WorkingTravelRate = 1.0m,
        NonWorkingTravelRate = 0.5m,
        CreatedBy = "test",
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static AgreementConfigEntity CreateHkEntity() => new()
    {
        ConfigId = Guid.NewGuid(),
        AgreementCode = "HK",
        OkVersion = "OK24",
        Status = AgreementConfigStatus.ACTIVE,
        WeeklyNormHours = 37.0m,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 100.0m,
        FlexCarryoverMax = 100.0m,
        HasOvertime = true,
        HasMerarbejde = false,
        OvertimeThreshold50 = 37.0m,
        OvertimeThreshold100 = 40.0m,
        EveningSupplementEnabled = true,
        NightSupplementEnabled = true,
        WeekendSupplementEnabled = true,
        HolidaySupplementEnabled = true,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.50m,
        WeekendSaturdayRate = 1.50m,
        WeekendSundayRate = 2.0m,
        HolidayRate = 2.0m,
        OnCallDutyEnabled = true,
        OnCallDutyRate = 0.33m,
        CallInWorkEnabled = true,
        CallInMinimumHours = 3.0m,
        CallInRate = 1.0m,
        TravelTimeEnabled = true,
        WorkingTravelRate = 1.0m,
        NonWorkingTravelRate = 0.5m,
        CreatedBy = "test",
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
