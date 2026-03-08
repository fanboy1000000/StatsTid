using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for Sprint 7: ConfigResolutionService — central config lookup,
/// local override validation (protected keys, numeric constraints),
/// and central config correctness per agreement code.
/// </summary>
public class Sprint7ConfigTests
{
    /// <summary>
    /// Creates a ConfigResolutionService instance for testing ValidateLocalOverride.
    /// ValidateLocalOverride is a pure function that doesn't touch the DB or logger,
    /// so we safely pass null! for the repo and NullLogger for the logger.
    /// </summary>
    private static ConfigResolutionService CreateSut()
        => new(null!, null!, null!, NullLogger<ConfigResolutionService>.Instance);

    // ---------------------------------------------------------------
    // 1. GetCentralConfig static method tests
    // ---------------------------------------------------------------

    [Fact]
    public void GetCentralConfig_AC_OK24_ReturnsCorrectConfig()
    {
        var config = ConfigResolutionService.GetCentralConfig("AC", "OK24");

        Assert.NotNull(config);
        Assert.Equal("AC", config!.AgreementCode);
        Assert.Equal("OK24", config.OkVersion);
        Assert.Equal(37.0m, config.WeeklyNormHours);
        Assert.False(config.HasOvertime);
        Assert.True(config.HasMerarbejde);
        Assert.Equal(150.0m, config.MaxFlexBalance);
        Assert.Equal(150.0m, config.FlexCarryoverMax);
    }

    [Fact]
    public void GetCentralConfig_HK_OK24_ReturnsCorrectConfig()
    {
        var config = ConfigResolutionService.GetCentralConfig("HK", "OK24");

        Assert.NotNull(config);
        Assert.Equal("HK", config!.AgreementCode);
        Assert.Equal("OK24", config.OkVersion);
        Assert.Equal(37.0m, config.WeeklyNormHours);
        Assert.True(config.HasOvertime);
        Assert.False(config.HasMerarbejde);
        Assert.Equal(100.0m, config.MaxFlexBalance);
        Assert.Equal(100.0m, config.FlexCarryoverMax);
    }

    [Fact]
    public void GetCentralConfig_PROSA_OK24_ReturnsCorrectConfig()
    {
        var config = ConfigResolutionService.GetCentralConfig("PROSA", "OK24");

        Assert.NotNull(config);
        Assert.Equal("PROSA", config!.AgreementCode);
        Assert.Equal("OK24", config.OkVersion);
        Assert.Equal(37.0m, config.WeeklyNormHours);
        Assert.True(config.HasOvertime);
        Assert.False(config.HasMerarbejde);
        Assert.Equal(120.0m, config.MaxFlexBalance);
        Assert.Equal(120.0m, config.FlexCarryoverMax);
    }

    [Fact]
    public void GetCentralConfig_UnknownAgreement_ReturnsNull()
    {
        var config = ConfigResolutionService.GetCentralConfig("UNKNOWN", "OK24");

        Assert.Null(config);
    }

    [Fact]
    public void HasCentralConfig_KnownPairs_ReturnsTrue()
    {
        Assert.True(ConfigResolutionService.HasCentralConfig("AC", "OK24"));
        Assert.True(ConfigResolutionService.HasCentralConfig("HK", "OK24"));
        Assert.True(ConfigResolutionService.HasCentralConfig("PROSA", "OK24"));
        Assert.True(ConfigResolutionService.HasCentralConfig("AC", "OK26"));
        Assert.True(ConfigResolutionService.HasCentralConfig("HK", "OK26"));
        Assert.True(ConfigResolutionService.HasCentralConfig("PROSA", "OK26"));
    }

    [Fact]
    public void HasCentralConfig_UnknownPair_ReturnsFalse()
    {
        Assert.False(ConfigResolutionService.HasCentralConfig("UNKNOWN", "OK24"));
        Assert.False(ConfigResolutionService.HasCentralConfig("AC", "OK99"));
        Assert.False(ConfigResolutionService.HasCentralConfig("", ""));
    }

    // ---------------------------------------------------------------
    // 2. ValidateLocalOverride — protected key rejection
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateLocalOverride_ProtectedKey_HasOvertime_Rejected()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("HasOvertime", "false", centralConfig);

        Assert.False(valid);
        Assert.Contains("centrally negotiated", error!);
        Assert.Contains("HasOvertime", error);
    }

    [Fact]
    public void ValidateLocalOverride_ProtectedKey_OnCallDutyRate_Rejected()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("OnCallDutyRate", "0.5", centralConfig);

        Assert.False(valid);
        Assert.Contains("centrally negotiated", error!);
        Assert.Contains("OnCallDutyRate", error);
    }

    // ---------------------------------------------------------------
    // 3. ValidateLocalOverride — MaxFlexBalance constraints
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateLocalOverride_MaxFlexBalance_WithinCentralLimit_Accepted()
    {
        var sut = CreateSut();
        // HK central MaxFlexBalance = 100
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("MaxFlexBalance", "80", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLocalOverride_MaxFlexBalance_ExceedingCentralLimit_Rejected()
    {
        var sut = CreateSut();
        // HK central MaxFlexBalance = 100
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("MaxFlexBalance", "150", centralConfig);

        Assert.False(valid);
        Assert.Contains("exceeds central maximum", error!);
    }

    [Fact]
    public void ValidateLocalOverride_MaxFlexBalance_Zero_Rejected()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("MaxFlexBalance", "0", centralConfig);

        Assert.False(valid);
        Assert.Contains("greater than 0", error!);
    }

    [Fact]
    public void ValidateLocalOverride_MaxFlexBalance_Negative_Rejected()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("MaxFlexBalance", "-10", centralConfig);

        Assert.False(valid);
        Assert.Contains("greater than 0", error!);
    }

    // ---------------------------------------------------------------
    // 4. ValidateLocalOverride — FlexCarryoverMax constraints
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateLocalOverride_FlexCarryoverMax_WithinLimit_Accepted()
    {
        var sut = CreateSut();
        // HK central FlexCarryoverMax = 100
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("FlexCarryoverMax", "75", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLocalOverride_FlexCarryoverMax_ExceedingLimit_Rejected()
    {
        var sut = CreateSut();
        // HK central FlexCarryoverMax = 100
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("FlexCarryoverMax", "120", centralConfig);

        Assert.False(valid);
        Assert.Contains("exceeds central maximum", error!);
    }

    // ---------------------------------------------------------------
    // 5. ValidateLocalOverride — WeeklyNormHours constraints
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateLocalOverride_WeeklyNormHours_WithinLimit_Accepted()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("WeeklyNormHours", "35", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLocalOverride_WeeklyNormHours_ExceedingForty_Rejected()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("WeeklyNormHours", "42", centralConfig);

        Assert.False(valid);
        Assert.Contains("exceeds maximum of 40", error!);
    }

    // ---------------------------------------------------------------
    // 6. ValidateLocalOverride — informational & unknown keys
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateLocalOverride_PlanningStartDay_Accepted()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("PlanningStartDay", "Monday", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLocalOverride_UnknownKey_Accepted()
    {
        var sut = CreateSut();
        var centralConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        var (valid, error) = sut.ValidateLocalOverride("SomeCustomLocalKey", "any-value", centralConfig);

        Assert.True(valid);
        Assert.Null(error);
    }

    // ---------------------------------------------------------------
    // 7. Central config correctness — AC vs HK behavioral separation
    // ---------------------------------------------------------------

    [Fact]
    public void CentralConfig_AC_HasOvertimeFalse_HasMerarbejdeTrue()
    {
        var acConfig = ConfigResolutionService.GetCentralConfig("AC", "OK24")!;

        Assert.False(acConfig.HasOvertime);
        Assert.True(acConfig.HasMerarbejde);
        Assert.False(acConfig.EveningSupplementEnabled);
        Assert.False(acConfig.NightSupplementEnabled);
        Assert.False(acConfig.WeekendSupplementEnabled);
        Assert.False(acConfig.HolidaySupplementEnabled);
        Assert.False(acConfig.OnCallDutyEnabled);
    }

    [Fact]
    public void CentralConfig_HK_HasOvertimeTrue_HasMerarbejdeFalse_SupplementsEnabled()
    {
        var hkConfig = ConfigResolutionService.GetCentralConfig("HK", "OK24")!;

        Assert.True(hkConfig.HasOvertime);
        Assert.False(hkConfig.HasMerarbejde);
        Assert.True(hkConfig.EveningSupplementEnabled);
        Assert.True(hkConfig.NightSupplementEnabled);
        Assert.True(hkConfig.WeekendSupplementEnabled);
        Assert.True(hkConfig.HolidaySupplementEnabled);
        Assert.True(hkConfig.OnCallDutyEnabled);
        Assert.Equal(0.33m, hkConfig.OnCallDutyRate);
    }
}
