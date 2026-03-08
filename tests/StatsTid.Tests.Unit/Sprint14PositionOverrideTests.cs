using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit;

public class Sprint14PositionOverrideTests
{
    // ========================================================================
    // 1. PositionOverrideConfigs.TryGetOverride — lookup correctness
    // ========================================================================

    [Fact]
    public void TryGetOverride_AC_OK24_DepartmentHead_ReturnsOverride()
    {
        var result = PositionOverrideConfigs.TryGetOverride("AC", "OK24", "DEPARTMENT_HEAD");

        Assert.NotNull(result);
        Assert.Equal(200.0m, result!.MaxFlexBalance);
        Assert.Equal(4, result.NormPeriodWeeks);
        Assert.Null(result.FlexCarryoverMax);
        Assert.Null(result.WeeklyNormHours);
    }

    [Fact]
    public void TryGetOverride_AC_OK24_Researcher_ReturnsPartialOverride()
    {
        var result = PositionOverrideConfigs.TryGetOverride("AC", "OK24", "RESEARCHER");

        Assert.NotNull(result);
        Assert.Equal(4, result!.NormPeriodWeeks);
        Assert.Null(result.MaxFlexBalance);
        Assert.Null(result.FlexCarryoverMax);
        Assert.Null(result.WeeklyNormHours);
    }

    [Fact]
    public void TryGetOverride_UnknownPosition_ReturnsNull()
    {
        var result = PositionOverrideConfigs.TryGetOverride("AC", "OK24", "UNKNOWN_POSITION");
        Assert.Null(result);
    }

    // ========================================================================
    // 2. PositionOverrideConfigs.ApplyOverride — merge behavior
    // ========================================================================

    [Fact]
    public void ApplyOverride_WithMaxFlexBalance_ChangesConfigMaxFlexBalance()
    {
        var baseConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");
        var posOverride = new PositionOverrideConfigs.PositionConfigOverride
        {
            MaxFlexBalance = 200.0m,
        };

        var result = PositionOverrideConfigs.ApplyOverride(baseConfig, posOverride);

        Assert.Equal(200.0m, result.MaxFlexBalance);
        // Other fields remain unchanged
        Assert.Equal(baseConfig.AgreementCode, result.AgreementCode);
        Assert.Equal(baseConfig.OkVersion, result.OkVersion);
        Assert.Equal(baseConfig.WeeklyNormHours, result.WeeklyNormHours);
        Assert.Equal(baseConfig.HasOvertime, result.HasOvertime);
        Assert.Equal(baseConfig.HasMerarbejde, result.HasMerarbejde);
    }

    [Fact]
    public void ApplyOverride_WithNullMaxFlexBalance_LeavesConfigMaxFlexBalanceUnchanged()
    {
        var baseConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");
        var posOverride = new PositionOverrideConfigs.PositionConfigOverride
        {
            MaxFlexBalance = null,
            NormPeriodWeeks = 4,
        };

        var result = PositionOverrideConfigs.ApplyOverride(baseConfig, posOverride);

        Assert.Equal(baseConfig.MaxFlexBalance, result.MaxFlexBalance);
        Assert.Equal(4, result.NormPeriodWeeks);
    }

    [Fact]
    public void ApplyOverride_WithNormPeriodWeeks4_ChangesConfigNormPeriodWeeks()
    {
        var baseConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");
        Assert.Equal(1, baseConfig.NormPeriodWeeks); // precondition

        var posOverride = new PositionOverrideConfigs.PositionConfigOverride
        {
            NormPeriodWeeks = 4,
        };

        var result = PositionOverrideConfigs.ApplyOverride(baseConfig, posOverride);

        Assert.Equal(4, result.NormPeriodWeeks);
        Assert.Equal(baseConfig.MaxFlexBalance, result.MaxFlexBalance);
        Assert.Equal(baseConfig.FlexCarryoverMax, result.FlexCarryoverMax);
    }

    // ========================================================================
    // 3. ConfigResolutionService static helpers
    // ========================================================================

    [Theory]
    [InlineData("AC", "OK24")]
    [InlineData("HK", "OK24")]
    [InlineData("PROSA", "OK24")]
    [InlineData("AC", "OK26")]
    public void GetCentralConfig_ReturnsCorrectConfig(string code, string version)
    {
        var config = ConfigResolutionService.GetCentralConfig(code, version);

        Assert.NotNull(config);
        Assert.Equal(code, config!.AgreementCode);
        Assert.Equal(version, config.OkVersion);
    }

    [Theory]
    [InlineData("AC", "OK24", true)]
    [InlineData("HK", "OK24", true)]
    [InlineData("PROSA", "OK24", true)]
    [InlineData("UNKNOWN", "OK24", false)]
    [InlineData("AC", "OK99", false)]
    public void HasCentralConfig_ReturnsExpected(string code, string version, bool expected)
    {
        var result = ConfigResolutionService.HasCentralConfig(code, version);
        Assert.Equal(expected, result);
    }

    // ========================================================================
    // 4. CentralAgreementConfigs.GetConfig with position — end-to-end
    // ========================================================================

    [Fact]
    public void GetConfig_WithPosition_DepartmentHead_AppliesOverride()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24", "DEPARTMENT_HEAD");

        Assert.Equal(200.0m, config.MaxFlexBalance);
        Assert.Equal(4, config.NormPeriodWeeks);
        Assert.Equal("AC", config.AgreementCode);
    }

    [Fact]
    public void GetConfig_WithNullPosition_ReturnsBaseConfig()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24", null);
        var baseConfig = CentralAgreementConfigs.GetConfig("AC", "OK24");

        Assert.Equal(baseConfig.MaxFlexBalance, config.MaxFlexBalance);
        Assert.Equal(baseConfig.NormPeriodWeeks, config.NormPeriodWeeks);
    }

    // ========================================================================
    // 5. WageTypeMapping model
    // ========================================================================

    [Fact]
    public void WageTypeMapping_FullyPopulated_HasAllFieldsSet()
    {
        var mapping = new WageTypeMapping
        {
            TimeType = "NORMAL_WORK",
            WageType = "1010",
            OkVersion = "OK24",
            AgreementCode = "AC",
            Description = "Normal working hours",
        };

        Assert.Equal("NORMAL_WORK", mapping.TimeType);
        Assert.Equal("1010", mapping.WageType);
        Assert.Equal("OK24", mapping.OkVersion);
        Assert.Equal("AC", mapping.AgreementCode);
        Assert.Equal("Normal working hours", mapping.Description);
    }

    [Fact]
    public void WageTypeMapping_Description_CanBeNull()
    {
        var mapping = new WageTypeMapping
        {
            TimeType = "OVERTIME",
            WageType = "2010",
            OkVersion = "OK24",
            AgreementCode = "HK",
        };

        Assert.Null(mapping.Description);
    }
}
