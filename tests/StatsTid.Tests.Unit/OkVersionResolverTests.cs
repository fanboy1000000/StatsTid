using StatsTid.RuleEngine.Api.Config;

namespace StatsTid.Tests.Unit;

public class OkVersionResolverTests
{
    [Fact]
    public void DateInOK24Period_ResolvesToOK24()
    {
        var result = OkVersionResolver.ResolveVersion(new DateOnly(2025, 1, 15));
        Assert.Equal("OK24", result);
    }

    [Fact]
    public void DateInOK26Period_ResolvesToOK26()
    {
        var result = OkVersionResolver.ResolveVersion(new DateOnly(2026, 6, 1));
        Assert.Equal("OK26", result);
    }

    [Fact]
    public void BoundaryDate_OK24End_ResolvesToOK24()
    {
        var result = OkVersionResolver.ResolveVersion(new DateOnly(2026, 3, 31));
        Assert.Equal("OK24", result);
    }

    [Fact]
    public void BoundaryDate_OK26Start_ResolvesToOK26()
    {
        var result = OkVersionResolver.ResolveVersion(new DateOnly(2026, 4, 1));
        Assert.Equal("OK26", result);
    }

    [Fact]
    public void PeriodSpanningTransition_ReturnsBothVersions()
    {
        var versions = OkVersionResolver.ResolveVersionsForPeriod(
            new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));

        Assert.Equal(2, versions.Count);
        Assert.Equal("OK24", versions[0].Version);
        Assert.Equal(new DateOnly(2026, 3, 25), versions[0].Start);
        Assert.Equal(new DateOnly(2026, 3, 31), versions[0].End);
        Assert.Equal("OK26", versions[1].Version);
        Assert.Equal(new DateOnly(2026, 4, 1), versions[1].Start);
        Assert.Equal(new DateOnly(2026, 4, 7), versions[1].End);
    }

    [Fact]
    public void PeriodFullyWithinOK24_ReturnsSingleVersion()
    {
        var versions = OkVersionResolver.ResolveVersionsForPeriod(
            new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30));

        Assert.Single(versions);
        Assert.Equal("OK24", versions[0].Version);
    }
}
