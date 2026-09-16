using Microsoft.Extensions.DependencyInjection;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S142 / TASK-14200 — the FALSIFIABILITY PROBE for the fixed-INSTANT seam itself
/// (<see cref="StatsTidWebApplicationFactory.WithFixedInstant"/>, and the
/// <see cref="FixedTimeProvider"/> it wraps), run WITHOUT Docker and WITHOUT booting a host.
///
/// <para>
/// <b>Why no host is needed to prove this.</b> <c>WithFixedInstant</c> does exactly one thing:
/// <c>services.AddSingleton&lt;TimeProvider&gt;(new FixedTimeProvider(instant))</c>. Everything this
/// sprint's discriminating tests depend on — that pinning an exact instant makes
/// <see cref="FixedTimeProvider.GetUtcNow"/> answer that instant, and that
/// <see cref="CopenhagenBusinessDate.Today(TimeProvider)"/> then converts it through the REAL
/// Europe/Copenhagen zone rather than the UTC calendar day — is fully exercised by constructing
/// <see cref="FixedTimeProvider"/> directly, with no Postgres container and no ASP.NET host in the
/// loop. This is deliberately the CHEAPEST possible proof of the seam: every subsystem task that
/// pins <c>WithFixedInstant</c> against a real endpoint is layering DI wiring and HTTP on top of the
/// exact fact asserted here.
/// </para>
///
/// <para>
/// <b>Why this test can fail (and what a failure would mean).</b> If <see cref="FixedTimeProvider"/>
/// ever stopped returning the EXACT instant passed to its <c>DateTimeOffset</c> constructor (for
/// example, a future edit that normalized it to midnight, mirroring the <c>DateOnly</c> constructor
/// right above it), every S142 clock-boundary fact across the whole regression suite would silently
/// stop discriminating the bug it exists to catch — collapsing back to the exact blind spot
/// <c>WithFixedToday</c> already has. This probe is the one place that would go RED first.
/// </para>
/// </summary>
public sealed class FixedInstantSeamTests
{
    /// <summary>
    /// Pins the shared summer instant (22:30 UTC, CEST +02:00) and proves the two calendars
    /// genuinely disagree: the UTC calendar day is the LITERAL earlier day, and
    /// <see cref="CopenhagenBusinessDate.Today"/> returns the LITERAL next day — never derived from
    /// one another, so this cannot pass by both sides happening to compute the same wrong value.
    /// </summary>
    [Fact]
    public void SummerInstant_UtcDayAndCopenhagenDay_AreLiterallyDifferentCalendarDays()
    {
        var provider = new FixedTimeProvider(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);

        var utcDay = DateOnly.FromDateTime(provider.GetUtcNow().UtcDateTime);
        var copenhagenDay = CopenhagenBusinessDate.Today(provider);

        // Literals throughout — never a call back into CopenhagenBusinessDate or FixedTimeProvider.
        Assert.Equal(new DateOnly(2026, 7, 15), utcDay);
        Assert.Equal(new DateOnly(2026, 7, 16), copenhagenDay);
        Assert.NotEqual(utcDay, copenhagenDay);
    }

    /// <summary>
    /// The companion control: the winter "calendars agree" instant proves the seam does NOT force a
    /// day-rollover artefact on every pinned instant — only the ones deliberately chosen to sit at
    /// the boundary. Both reads must equal the SAME literal day.
    /// </summary>
    [Fact]
    public void WinterAgreeingInstant_UtcDayAndCopenhagenDay_AreTheSameLiteralCalendarDay()
    {
        var provider = new FixedTimeProvider(BoundaryInstants.WinterEveningCalendarsStillAgree);

        var utcDay = DateOnly.FromDateTime(provider.GetUtcNow().UtcDateTime);
        var copenhagenDay = CopenhagenBusinessDate.Today(provider);

        Assert.Equal(new DateOnly(2026, 1, 15), utcDay);
        Assert.Equal(new DateOnly(2026, 1, 15), copenhagenDay);
    }
}

/// <summary>
/// S142 / Step-5a follow-up — proves <see cref="StatsTidWebApplicationFactory.WithFixedInstant"/>
/// actually WIRES the pinned provider into a booted host's DI container.
///
/// <para>
/// <b>Why this exists, and why the cheap probe above is not sufficient.</b>
/// <see cref="FixedInstantSeamTests"/> proves that <see cref="FixedTimeProvider"/> answers the exact
/// instant it was given — and it proves that by constructing the provider directly. It therefore
/// establishes nothing about the seam itself: its own doc comment argues that
/// <c>WithFixedInstant</c> "does exactly one thing", which is an assertion ABOUT the code rather
/// than a test OF it. The external review lens caught this at Step 5a.
/// </para>
///
/// <para>
/// <b>What would go wrong without this test.</b> Seven wave-2 tasks pin their discriminating facts
/// through <c>WithFixedInstant</c> against real endpoints. If the registration were ever overridden
/// by the host's own <c>TimeProvider</c> registration, or resolved from a different container, every
/// one of those pins would silently pin NOTHING — passing against correct and broken code alike.
/// That is the exact blind spot this sprint exists to remove, and it would sit underneath the entire
/// tooling built to remove it. The failure would be invisible: a green suite proving nothing.
/// </para>
///
/// <para>Docker-gated, because proving the wiring requires really booting the host — so it is
/// verified in CI, never on the author's machine. An unverifiable-locally test that can fail beats a
/// locally-green one that cannot.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FixedInstantHostWiringTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Boots a host pinned to the shared summer boundary instant and resolves
    /// <see cref="TimeProvider"/> from the RUNNING host's container — not from the builder — so the
    /// assertion covers the whole registration path, including anything the host registers after
    /// the test override. Expected values are literals.
    /// </summary>
    [Fact]
    public void WithFixedInstant_PinnedProviderSurvivesTheHostBuild_AndDrivesTheCopenhagenDay()
    {
        var pinned = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        _ = pinned.CreateClient(); // forces the host build; DI is not resolvable before this.

        var resolved = pinned.Services.GetRequiredService<TimeProvider>();

        // 1. The host resolves OUR provider, not the system clock.
        Assert.IsType<FixedTimeProvider>(resolved);

        // 2. It answers the exact pinned instant — literals, never derived from the seam.
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero), resolved.GetUtcNow());

        // 3. And the business day the product would compute from it is the literal NEXT Danish day,
        //    while the UTC calendar day is the literal earlier one. This is the fact every wave-2
        //    pin relies on reaching the server.
        Assert.Equal(new DateOnly(2026, 7, 15), DateOnly.FromDateTime(resolved.GetUtcNow().UtcDateTime));
        Assert.Equal(new DateOnly(2026, 7, 16), CopenhagenBusinessDate.Today(resolved));
    }
}
