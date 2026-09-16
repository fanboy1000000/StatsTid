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
    /// <summary>
    /// A deterministic <see cref="TimeProvider"/> pinning one exact UTC instant AND one machine-local
    /// zone. The local zone is load-bearing, not decoration: the pre-S142 site read
    /// <c>DateTime.Today</c> — the MACHINE-LOCAL day — so modelling "a UTC CI box" means pinning
    /// <see cref="LocalTimeZone"/> to UTC. With both pinned, the test can state the old reading and
    /// the new one side by side for the same instant.
    /// </summary>
    private sealed class FixedInstantTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localZone;

        public FixedInstantTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localZone)
        {
            _utcNow = utcNow;
            _localZone = localZone;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => _localZone;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // S142 / TASK-14210 (owner ruling OQ-10, 2026-09-16) — the MONTH-BOUNDARY pin.
    //
    // Plain language: this tool's "today" must be the DANISH calendar day, because every StatsTid
    // user is Danish. Before this sprint it read the MACHINE-LOCAL day, which is the Danish day only
    // as an accident of whose laptop ran it — on a UTC CI box it is the UTC day instead.
    //
    // `rolling` consumes only today's YEAR and MONTH (it anchors at the 1st), so a one-day skew can
    // change the answer at exactly one place: a month boundary where the two calendars disagree.
    // That window is ~1-2 hours on twelve nights a year, which is why it would never be caught by
    // accident and needs a pin fired deliberately inside it.
    //
    // Both cases assert LITERAL dates. Computing the expectation from the helper under test would
    // make the pin agree with whatever the code does, which is not a test.
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Summer boundary (CEST, +02:00). 2026-07-31 22:30:00Z ⇒ Copenhagen local 2026-08-01 00:30, so
    /// the Danish day is 1 August while the UTC day is still 31 July. Rolling must anchor on AUGUST.
    /// </summary>
    [Fact]
    public void Rolling_AtASummerMonthBoundary_AnchorsOnTheCopenhagenMonth_NotTheUtcOne()
    {
        var provider = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 7, 31, 22, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

        var reference = ReferenceDateResolver.Resolve("rolling", provider);

        Assert.Equal(new DateOnly(2026, 8, 1), reference);
        // The generator derives the activity month as reference.AddMonths(-1) — the last COMPLETE
        // month. Copenhagen ⇒ July 2026. The old machine-local reading would have produced June.
        Assert.Equal(new DateOnly(2026, 7, 1), reference.AddMonths(-1));

        // Characterizes the defect directly (same style as CopenhagenBusinessDateTests): for this
        // one instant, the reading the site used before OQ-10 — machine-local, on a UTC box — lands
        // on 31 July, a DIFFERENT month. That is the whole failure window, stated as a fact.
        Assert.Equal(new DateOnly(2026, 7, 31), DateOnly.FromDateTime(provider.GetLocalNow().DateTime));
        Assert.NotEqual(new DateOnly(2026, 7, 1), reference);
    }

    /// <summary>
    /// Winter boundary (CET, +01:00) which is ALSO a year boundary — the harshest case, since the
    /// skew moves the year as well as the month. 2026-12-31 23:30:00Z ⇒ Copenhagen local
    /// 2027-01-01 00:30. Rolling must anchor on JANUARY 2027, not December 2026.
    /// </summary>
    [Fact]
    public void Rolling_AtAWinterYearBoundary_AnchorsOnTheCopenhagenYearAndMonth()
    {
        var provider = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

        var reference = ReferenceDateResolver.Resolve("rolling", provider);

        Assert.Equal(new DateOnly(2027, 1, 1), reference);
        Assert.Equal(new DateOnly(2026, 12, 1), reference.AddMonths(-1));
        Assert.Equal(new DateOnly(2026, 12, 31), DateOnly.FromDateTime(provider.GetLocalNow().DateTime));
    }

    /// <summary>
    /// The determinism guarantee, asserted through the SAME clock-carrying overload the CLI uses:
    /// with no <c>--reference-date</c>, or with an explicit ISO date, the answer is the pinned
    /// constant / that date regardless of where the clock stands. This is what keeps
    /// <c>generate --scale full</c> byte-reproducible after the SharedKernel link (TASK-14210).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void TimeProviderOverload_OnTheNonRollingPaths_IsClockIndependent(string? arg)
    {
        var atAMonthBoundary = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 7, 31, 22, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 6, 15), ReferenceDateResolver.Resolve(arg, atAMonthBoundary));
        Assert.Equal(new DateOnly(2020, 1, 2),
            ReferenceDateResolver.Resolve("2020-01-02", atAMonthBoundary));
    }

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
