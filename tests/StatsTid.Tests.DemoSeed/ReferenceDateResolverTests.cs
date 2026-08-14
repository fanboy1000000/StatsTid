using StatsTid.Tools.DemoSeed;
using StatsTid.Tools.DemoSeed.Generation;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// The <c>--reference-date rolling</c> resolution: keeps the committed/default path deterministic,
/// and puts "rolling" activity in the previous (last complete) calendar month. `today` is injected
/// so the assertions never touch wall-clock.
/// </summary>
public sealed class ReferenceDateResolverTests
{
    [Fact]
    public void Rolling_ResolvesToFirstOfTodaysMonth_SoActivityIsThePreviousMonth()
    {
        var today = new DateOnly(2026, 8, 14);
        var reference = ReferenceDateResolver.Resolve("rolling", today);

        Assert.Equal(new DateOnly(2026, 8, 1), reference);
        // The generator derives the activity month as reference.AddMonths(-1) — the last COMPLETE month.
        Assert.Equal(new DateOnly(2026, 7, 1), reference.AddMonths(-1));
    }

    [Fact]
    public void Rolling_IsCaseAndWhitespaceInsensitive_AndHandlesTheYearBoundary()
    {
        var jan = new DateOnly(2026, 1, 9);
        var reference = ReferenceDateResolver.Resolve("  ROLLING ", jan);
        Assert.Equal(new DateOnly(2026, 1, 1), reference);
        Assert.Equal(new DateOnly(2025, 12, 1), reference.AddMonths(-1)); // → December of the prior year
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void AbsentOrUnparseable_FallsBackToThePinnedDefault(string? arg)
    {
        Assert.Equal(ReferenceDateResolver.PinnedDefault,
            ReferenceDateResolver.Resolve(arg, new DateOnly(2026, 8, 14)));
        Assert.Equal(new DateOnly(2026, 6, 15), ReferenceDateResolver.PinnedDefault);
    }

    [Fact]
    public void ExplicitIsoDate_IsHonoured_Verbatim()
    {
        Assert.Equal(new DateOnly(2020, 1, 2),
            ReferenceDateResolver.Resolve("2020-01-02", new DateOnly(2026, 8, 14)));
    }

    [Fact]
    public void Rolling_FeedsThroughTheGenerator_LandingActivityInThePreviousMonth()
    {
        // End-to-end through the real generator (smoke scale for speed): a rolling reference date
        // of 2026-08-01 must produce activity stamped July 2026.
        var reference = ReferenceDateResolver.Resolve("rolling", new DateOnly(2026, 8, 14));
        var ds = new DemoGenerator("smoke", 42, reference).Generate();

        Assert.NotEmpty(ds.Manifest.Activity);
        Assert.All(ds.Manifest.Activity, a =>
        {
            Assert.Equal(2026, a.Year);
            Assert.Equal(7, a.Month);
        });
    }
}
