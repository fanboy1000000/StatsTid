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
