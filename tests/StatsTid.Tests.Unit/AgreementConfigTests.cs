using StatsTid.RuleEngine.Api.Config;

namespace StatsTid.Tests.Unit;

public class AgreementConfigTests
{
    [Fact]
    public void AC_OK24_HasMerarbejde_NoOvertime()
    {
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");

        Assert.True(config.HasMerarbejde);
        Assert.False(config.HasOvertime);
        Assert.Equal(150m, config.MaxFlexBalance);
    }

    [Fact]
    public void HK_OK24_HasOvertime_NoMerarbejde()
    {
        var config = AgreementConfigProvider.GetConfig("HK", "OK24");

        Assert.True(config.HasOvertime);
        Assert.False(config.HasMerarbejde);
        Assert.Equal(100m, config.MaxFlexBalance);
    }

    [Fact]
    public void PROSA_OK24_HasOvertime_SupplementsEnabled()
    {
        var config = AgreementConfigProvider.GetConfig("PROSA", "OK24");

        Assert.True(config.HasOvertime);
        Assert.True(config.EveningSupplementEnabled);
        Assert.True(config.NightSupplementEnabled);
        Assert.Equal(120m, config.MaxFlexBalance);
    }

    [Fact]
    public void AC_OK24_SupplementsDisabled()
    {
        var config = AgreementConfigProvider.GetConfig("AC", "OK24");

        Assert.False(config.EveningSupplementEnabled);
        Assert.False(config.NightSupplementEnabled);
        Assert.False(config.WeekendSupplementEnabled);
        Assert.False(config.HolidaySupplementEnabled);
    }

    [Fact]
    public void OK26_Exists_ForAllAgreements()
    {
        Assert.True(AgreementConfigProvider.HasConfig("AC", "OK26"));
        Assert.True(AgreementConfigProvider.HasConfig("HK", "OK26"));
        Assert.True(AgreementConfigProvider.HasConfig("PROSA", "OK26"));
    }

    [Fact]
    public void UnknownAgreement_ThrowsException()
    {
        Assert.Throws<InvalidOperationException>(
            () => AgreementConfigProvider.GetConfig("UNKNOWN", "OK24"));
    }
}
