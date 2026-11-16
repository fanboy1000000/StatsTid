using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for Sprint 7: ConfigResolutionService — central config lookup
/// and central config correctness per agreement code.
/// </summary>
public class Sprint7ConfigTests
{
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
