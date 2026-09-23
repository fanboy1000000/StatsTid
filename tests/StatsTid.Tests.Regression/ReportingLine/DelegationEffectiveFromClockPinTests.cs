using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.ReportingLine;

/// <summary>
/// S143 / QUAL-176 — the pin that COULD NOT EXIST before this task: the date a manager is SHOWN for
/// their stand-in (vikar) arrangement, asserted against a LITERAL expected date under a pinned clock.
///
/// <para>
/// <b>What a "vikar" is, and what date this is about.</b> A vikar is a stand-in approver: while a
/// manager is away, someone else may approve their people's timesheets. The arrangement has a start
/// date ("from when") and an inclusive last day ("til og med"). This suite is about the START date —
/// the <c>effectiveFrom</c> that <c>POST /api/reporting-lines/delegate</c> echoes back and that
/// <c>GET /api/reporting-lines/delegate</c> reports afterwards.
/// </para>
///
/// <para>
/// <b>The defect this pins, in plain language.</b> The POST computed and echoed that date from the
/// INJECTED clock (the <see cref="TimeProvider"/> seam), while the row's <c>created_at</c> was
/// stamped from the REAL clock (<c>DateTime.UtcNow</c>) — and the GET then derived the DISPLAYED
/// start date from that <c>created_at</c>. One date, two clocks: the value the user is shown was
/// computed from a different clock than the value the write recorded.
/// </para>
///
/// <para>
/// <b>Why "they are the same clock in production" was not a reason to leave it alone.</b> It is true
/// that <c>TimeProvider.System</c> and <c>DateTime.UtcNow</c> read the same underlying clock, so in
/// production the two values diverge only if a midnight tick falls between two adjacent statements —
/// a microsecond window, never observed. The cost was never the divergence; it was TESTABILITY. A
/// test host pinned with <see cref="StatsTidWebApplicationFactory.WithFixedInstant"/> can control the
/// injected provider but cannot control <c>DateTime.UtcNow</c>, so the displayed date could not be
/// pinned AT ALL — which is precisely why this user-visible behaviour had no test until S143, and why
/// the nearest existing coverage (<c>Contracts/S116DelegationSpecRuntimeTests</c>) can only assert
/// that <c>effectiveFrom</c> is "some string". An unpinnable displayed date is the exact shape S142
/// spent a sprint removing. S143 moved the two <c>manager_vikar</c> creates onto the injected seam —
/// still a UTC INSTANT, ADR-041 untouched, only the SOURCE moved — and this file is what that buys.
/// </para>
///
/// <para>
/// <b>★ THE TWO PROPERTIES THIS SUITE PINS, AND WHY THEY NEED DIFFERENT CLOCKS.</b>
/// <list type="number">
///   <item><b>Clock-source unity</b> — the date the write records and the date the read displays both
///     trace to the injected clock rather than the wall clock. Pinned by the three FROZEN-clock facts
///     (<c>WithFixedInstant</c>) against literal expected dates.</item>
///   <item><b>Single-read</b> — the handler reads that clock ONCE, so the two values cannot straddle a
///     midnight tick. Pinned by the ADVANCING-clock fact at the end of this file. A frozen clock is
///     structurally incapable of seeing this: it answers the same instant on every call, so one read
///     and two reads produce identical output.</item>
/// </list>
/// Property 1 without property 2 is what QUAL-176's first fix delivered, and it is not enough: one
/// clock read twice still disagrees with itself across midnight. Both are needed, and neither
/// instrument can substitute for the other.
/// </para>
///
/// <para>
/// <b>Why the expected dates are LITERALS.</b> Every assertion below compares against a hand-written
/// <c>DateOnly</c>/string derived from the spec (the pinned instant + Denmark's known UTC offset),
/// never against a fresh call to <c>CopenhagenBusinessDate.Today</c> or to the endpoint itself. S142
/// found self-referential assertions of that kind providing no protection at all: a helper compared
/// against itself proves only that it agrees with itself.
/// </para>
///
/// <para>
/// <b>Docker-gated, and CI-VERIFIED rather than locally green.</b> <c>WithFixedInstant</c> rides the
/// Postgres-backed test factory and Docker is unavailable on the authoring machine (standing project
/// constraint), so nothing in this file has been executed locally — RED or GREEN. Each fact states
/// its own RED condition as REASONED from the product source; the first actual execution is the
/// sprint close's watched CI job.
/// </para>
///
/// <para>
/// <b>Boot order (PAT-008).</b> Each fact builds its own instant-pinned host and seeds its fixture
/// only AFTER that host's first <c>CreateClient()</c>, which re-runs <c>Program.cs</c>'s startup
/// seeders. xunit gives every <c>[Fact]</c> its own class instance under <see cref="IAsyncLifetime"/>,
/// so that is one container and one pinned host per fact.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DelegationEffectiveFromClockPinTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // ── The fixture's own org tree: one MAO root with two Organisations, disjoint from the
    //    init.sql seed orgs (STY01/STY02/STY05) and from the named approval suites. ──
    private const string Mao = "S143QMAO";
    private const string SelfOrg = "S143Q01";   // the self-service /delegate scenario
    private const string AdminOrg = "S143Q02";  // the admin-on-behalf vikar scenario

    // Self-service actors.
    private const string SelfLeader = "s143q_l1";   // the delegating manager (and the GET's actor)
    private const string SelfStandIn = "s143q_v1";  // the stand-in
    private const string SelfReport = "s143q_e1";   // the leader's PRIMARY report

    // Admin-on-behalf actors.
    private const string AdminManager = "s143q_m2"; // the absent manager (and the GET's actor)
    private const string AdminStandIn = "s143q_v2";
    private const string AdminReport = "s143q_e2";

    /// <summary>
    /// The Copenhagen calendar day at <see cref="BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen"/>
    /// (2026-07-15 22:30Z). Denmark is CEST (UTC+02:00) in July, so local time is already
    /// 2026-07-16 00:30 — the Danish day has turned over while the UTC day is still the 15th.
    /// A LITERAL, and deliberately NOT the UTC day at that instant.
    /// </summary>
    private static readonly DateOnly DanishSummerDay = new(2026, 7, 16);

    /// <summary>The UTC calendar day at the same instant — the WRONG answer for a displayed business
    /// date, and the RIGHT answer for the stored INSTANT's date (see the created_at assertion).</summary>
    private static readonly DateOnly UtcSummerDay = new(2026, 7, 15);

    /// <summary>
    /// The Copenhagen calendar day at <see cref="BoundaryInstants.WinterEveningCalendarsStillAgree"/>
    /// (2026-01-15 22:30Z). Denmark is CET (UTC+01:00) in January, so local time is 23:30 on the
    /// 15th — both calendars say the 15th. A LITERAL.
    /// </summary>
    private static readonly DateOnly WinterAgreedDay = new(2026, 1, 15);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here — each fact boots its own instant-pinned host and seeds after.
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ★ THE MARQUEE — the self-service delegation created after Danish midnight.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A manager who delegates at 00:30 Danish time on 16 July must be SHOWN 16 July as the start
    /// date — by the POST that creates the delegation AND by the GET that reports it afterwards.
    ///
    /// <para><b>RED condition (reasoned, not locally executed).</b> Revert
    /// <c>ReportingLineEndpoints.cs</c>'s self-service vikar create to
    /// <c>CreatedAt = DateTime.UtcNow</c> and the GET's displayed <c>effectiveFrom</c> is derived
    /// from the REAL wall clock at suite-run time — the actual calendar day CI happens to run on,
    /// which is not 2026-07-16 on any day but one, and which drifts daily. The POST's echoed date
    /// stays 2026-07-16 (it already reads the injected provider), so the two halves DISAGREE: that
    /// disagreement is QUAL-176 itself, and the third assertion below names it.</para>
    ///
    /// <para><b>What the <c>created_at</c> assertion adds.</b> It reads the stored instant straight
    /// from Postgres and asserts its UTC date is 2026-07-<b>15</b> — a DIFFERENT literal from the
    /// displayed 16th. That single line proves three things at once: the write took the pinned clock
    /// (not the wall clock); the instant STAYED a UTC instant and was not shifted by a calendar
    /// conversion at the write (the ADR-041 rule this task must not break); and the displayed Danish
    /// day is genuinely produced by a conversion ON READ rather than by a pre-converted stored value.
    /// Without it, a (wrong) implementation that stored the Copenhagen day as the instant would pass
    /// the two date assertions.</para>
    /// </summary>
    [Fact]
    public async Task SelfServiceDelegate_CreatedAfterDanishMidnight_IsShownTheDanishDay_ByBothWriteAndRead()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        using var client = LeaderClient(host, SelfLeader, SelfOrg);
        await SeedSelfServiceFixtureAsync();

        // effectiveTo is a LITERAL strictly after the expected Danish start day (the handler refuses
        // `effectiveTo <= today`), chosen so the stand-in is still covering at the GET.
        var post = await client.PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = SelfStandIn, effectiveTo = "2026-08-31" });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var postBody = await post.Content.ReadFromJsonAsync<JsonElement>();
        var echoed = postBody.GetProperty("effectiveFrom").GetString();
        Assert.Equal("2026-07-16", echoed);

        var get = await client.GetAsync("/api/reporting-lines/delegate");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(getBody.GetProperty("active").GetBoolean());
        var displayed = getBody.GetProperty("effectiveFrom").GetString();

        // (1) The displayed date against the LITERAL Danish day — the assertion that was impossible
        //     before S143, because created_at could not be pinned.
        Assert.Equal("2026-07-16", displayed);
        Assert.Equal(DanishSummerDay, DateOnly.Parse(displayed!));

        // (2) Writer and reader agree — QUAL-176 stated directly as an assertion.
        Assert.Equal(echoed, displayed);

        // (3) The stored value is still a UTC INSTANT (ADR-041), on the UTC day 2026-07-15 — one day
        //     BEFORE the Danish day it is displayed as. See the doc comment for why this matters.
        var storedUtcDay = await ReadVikarCreatedAtUtcDayAsync(SelfLeader);
        Assert.Equal(UtcSummerDay, storedUtcDay);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The companion instant — the calendars AGREE, so an over-correction shows up.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// At 23:30 Danish time on 15 January both calendars say the 15th, and the displayed start date
    /// must be the 15th.
    ///
    /// <para><b>Why this fact earns its container.</b> The marquee above kills a raw-UTC-day
    /// implementation and a real-wall-clock implementation, but it is passed by a hardcoded
    /// <c>+02:00</c> conversion (summer's offset applied year-round), which also answers "the 16th"
    /// there. This instant is the one where that bug becomes visible: 22:30Z + 2h = 00:30 on the
    /// 16th, an hour before the real CET offset rolls the day over, so a <c>+02:00</c>
    /// implementation answers the 16th where the correct answer is still the 15th. See
    /// <see cref="BoundaryInstants.WinterEveningCalendarsStillAgree"/>, which exists for exactly this
    /// discrimination. It also guards the direction of travel: the fix must not turn into "always
    /// add a day".</para>
    ///
    /// <para><b>RED condition (reasoned).</b> Same revert as the marquee — the displayed date falls
    /// back to the wall-clock day, which is not 2026-01-15. Additionally RED against a hardcoded
    /// +02:00 conversion in the GET (it would report 2026-01-16). The <c>created_at</c> UTC-day
    /// assertion is DELIBERATELY not discriminating here (both calendars say the 15th) and is
    /// omitted rather than written as a line that cannot fail.</para>
    /// </summary>
    [Fact]
    public async Task SelfServiceDelegate_WhenBothCalendarsAgree_IsShownThatSameDay()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.WinterEveningCalendarsStillAgree);
        using var client = LeaderClient(host, SelfLeader, SelfOrg);
        await SeedSelfServiceFixtureAsync();

        var post = await client.PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = SelfStandIn, effectiveTo = "2026-02-28" });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var echoed = (await post.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("effectiveFrom").GetString();
        Assert.Equal("2026-01-15", echoed);

        var get = await client.GetAsync("/api/reporting-lines/delegate");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(getBody.GetProperty("active").GetBoolean());
        var displayed = getBody.GetProperty("effectiveFrom").GetString();

        Assert.Equal("2026-01-15", displayed);
        Assert.Equal(WinterAgreedDay, DateOnly.Parse(displayed!));
        Assert.Equal(echoed, displayed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The SECOND changed stamp — the admin-on-behalf vikar create, read back through
    //  the SAME GET (the manager sees what the admin arranged for them).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The same displayed-date guarantee for a stand-in an ADMIN arranges on a manager's behalf:
    /// <c>POST /api/admin/reporting-lines/{managerId}/vikar</c> writes the row, and the manager's own
    /// <c>GET /api/reporting-lines/delegate</c> is where they see it. Both must say 16 July.
    ///
    /// <para><b>Why a separate fact rather than trusting the marquee.</b> These are two DIFFERENT
    /// <c>created_at</c> statements in <c>ReportingLineEndpoints.cs</c> (the self-service create and
    /// the admin create), and QUAL-176 named both. A fix applied to one and missed on the other is a
    /// realistic outcome, and only this fact would catch it — the two share a reader but not a
    /// writer.</para>
    ///
    /// <para><b>RED condition (reasoned).</b> Revert the ADMIN create's <c>CreatedAt</c> to
    /// <c>DateTime.UtcNow</c>: the POST still echoes 2026-07-16 (its own <c>effectiveFrom</c> reads
    /// the injected provider) while the GET reports the wall-clock day, so the last assertion —
    /// writer and reader agreeing — fails, as does the literal.</para>
    /// </summary>
    [Fact]
    public async Task AdminCreatedVikar_IsShownTheDanishDay_ThroughTheManagersOwnRead()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        using var adminClient = AdminClient(host);
        await SeedAdminFixtureAsync();

        var post = await adminClient.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{AdminManager}/vikar",
            new { vikarUserId = AdminStandIn, effectiveTo = "2026-08-31", reason = "FERIE" });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var echoed = (await post.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("effectiveFrom").GetString();
        Assert.Equal("2026-07-16", echoed);

        // The MANAGER reads their own delegation status — the surface where this date is displayed.
        using var managerClient = LeaderClient(host, AdminManager, AdminOrg);
        var get = await managerClient.GetAsync("/api/reporting-lines/delegate");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(getBody.GetProperty("active").GetBoolean());
        var displayed = getBody.GetProperty("effectiveFrom").GetString();

        Assert.Equal("2026-07-16", displayed);
        Assert.Equal(DanishSummerDay, DateOnly.Parse(displayed!));
        Assert.Equal(echoed, displayed);

        var storedUtcDay = await ReadVikarCreatedAtUtcDayAsync(AdminManager);
        Assert.Equal(UtcSummerDay, storedUtcDay);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ★ THE SINGLE-READ PIN — the one property a frozen clock can never see.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The date the POST echoes and the date the GET displays must agree even when the clock MOVES
    /// between two reads — which is only guaranteed if the handler reads the clock ONCE.
    ///
    /// <para><b>Why the other three facts in this file cannot cover this.</b> They pin CLOCK-SOURCE
    /// UNITY: both values come from the injected provider rather than the wall clock. They cannot
    /// pin SINGLE-READ, because a <c>FixedTimeProvider</c> answers the same instant on every call —
    /// so one read and two reads produce identical output and no assertion can separate them. That
    /// blind spot is why QUAL-176 survived its own first fix: S143 moved both values onto one clock
    /// and left the handler reading that clock twice, which still disagrees across a midnight tick.
    /// This fact is the instrument for exactly that gap.</para>
    ///
    /// <para><b>How the instrument works, and why a DAY per call.</b>
    /// <see cref="DayPerCallTimeProvider"/> answers a new instant 24 hours later on every call, so
    /// ANY two distinct reads land on distinct Copenhagen calendar days. That is deliberate and it
    /// is what makes the pin robust: a smaller step would only discriminate if the handler's two
    /// reads happened to straddle a midnight, which depends on how many times the host read the
    /// clock first (startup runs one sweep pass in <c>DelegationExpiryService</c>, and more pollers
    /// may be added later). With a full day per call the result does not depend on the call index at
    /// all — one read means one day, two reads mean two different days, whatever happened before.</para>
    ///
    /// <para><b>RED condition — and this one was genuinely RED-FIRST.</b> This fact was written
    /// against the two-read code and BEFORE the fix existed. At that point both create handlers read
    /// the clock twice (once for <c>effectiveFrom</c> via <c>CopenhagenBusinessDate.Today</c>, once
    /// for <c>created_at</c>), so under this provider those two reads returned instants a DAY apart:
    /// the POST echoed one day and the GET — which derives the displayed date from the stored
    /// <c>created_at</c> — displayed the other, failing the assertion by exactly one day. It went
    /// GREEN when TASK-14311's <c>CopenhagenBusinessDate.FromInstant(DateTimeOffset)</c> let each
    /// handler take <c>var now = timeProvider.GetUtcNow()</c> ONCE and derive both values from that
    /// captured moment. Re-introduce a second read at either site and this fact fails again.</para>
    ///
    /// <para><b>Honest limit on that claim.</b> Docker is unavailable on the authoring machine
    /// (standing project constraint), so neither the RED nor the GREEN was OBSERVED locally — both
    /// are reasoned from the handler source, and CI is where the flip is actually witnessed.</para>
    ///
    /// <para><b>Guards against failing for the WRONG reason.</b> A clock that marches a day per call
    /// has side effects beyond the two reads under test, so each precondition is asserted separately
    /// with a message naming the cause: a far-future <c>effectiveTo</c> (2030-12-31, ~1600 days of
    /// headroom) keeps the <c>effectiveTo &lt;= today</c> validator satisfied however far the clock
    /// has run; the <c>active</c> check fails loudly if the expiry sweep closed the row; and the
    /// final call-count assertion fails if the clock did NOT advance, so the equality above can
    /// never pass vacuously against a stalled instrument.</para>
    /// </summary>
    [Fact]
    public async Task SelfServiceDelegate_EchoedAndDisplayedDatesAgree_EvenWhenTheClockMovesBetweenReads()
    {
        var clock = new DayPerCallTimeProvider(
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        using var host = HostWithClock(clock);
        using var client = LeaderClient(host, SelfLeader, SelfOrg);
        await SeedSelfServiceFixtureAsync();

        var callsBefore = clock.CallCount;

        var post = await client.PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = SelfStandIn, effectiveTo = "2030-12-31" });
        Assert.True(post.StatusCode == HttpStatusCode.OK,
            $"POST /delegate returned {(int)post.StatusCode}, expected 200. A 400 here means the " +
            "advancing clock outran the literal effectiveTo (2030-12-31) — an instrument problem, " +
            "not the single-read property under test.");

        var echoed = (await post.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("effectiveFrom").GetString();

        var get = await client.GetAsync("/api/reporting-lines/delegate");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(getBody.GetProperty("active").GetBoolean(),
            "GET /delegate reported no active delegation. The row was created moments earlier, so " +
            "this means the expiry sweep closed it under the advancing clock — an instrument " +
            "problem, not the single-read property under test.");

        var displayed = getBody.GetProperty("effectiveFrom").GetString();

        // THE PIN: one handler, one clock read ⇒ the echoed and the displayed date are the same day.
        // Two reads ⇒ they differ by exactly the provider's step (one day).
        Assert.Equal(echoed, displayed);

        // The instrument was live. Without this, a provider that stopped advancing would make the
        // assertion above pass while proving nothing — the "test that cannot fail" shape.
        Assert.True(clock.CallCount > callsBefore + 1,
            $"The clock was read {clock.CallCount - callsBefore} time(s) across the POST and GET; " +
            "expected at least two. A non-advancing clock makes the equality above vacuous.");
    }

    // ─────────────────────────────── clients ───────────────────────────────

    /// <summary>
    /// A host whose <see cref="TimeProvider"/> is an arbitrary instance — the same registration
    /// mechanism as <see cref="StatsTidWebApplicationFactory.WithFixedInstant"/>, inlined here rather
    /// than added to that shared factory on purpose: sibling agents hold that file in other
    /// worktrees this sprint, and a new overload there would be a merge conflict for a helper only
    /// this one fact uses.
    /// </summary>
    private WebApplicationFactory<Program> HostWithClock(TimeProvider clock)
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(clock)));

    /// <summary>
    /// A LeaderOrAbove client for the given self-service actor. The delegate GET/POST are a SELF-
    /// service surface — the actor IS the manager whose delegation it is — so a GlobalAdmin client
    /// would exercise the wrong actor entirely.
    ///
    /// <para>The client is taken from the INSTANT-PINNED <paramref name="host"/>, never from the
    /// base factory: a client from the un-pinned factory would run against the real clock and make
    /// every literal below meaningless. The token is minted directly rather than through a shared
    /// helper because <c>WithFixedInstant</c> returns the base
    /// <see cref="WebApplicationFactory{TEntryPoint}"/>, which carries no auth helper of its own.</para>
    /// </summary>
    private static HttpClient LeaderClient(WebApplicationFactory<Program> host, string actorId, string orgId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            NewTokenService().GenerateToken(
                employeeId: actorId, name: actorId, role: StatsTidRoles.LocalLeader,
                agreementCode: "HK", orgId: orgId,
                scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") }));
        return client;
    }

    /// <summary>A GlobalAdmin client for the admin-on-behalf POST (policy <c>HROrAbove</c>); the
    /// GLOBAL scope covers the manager's tree without a per-org scope row.</summary>
    private static HttpClient AdminClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            NewTokenService().GenerateToken(
                employeeId: "s143q_admin", name: "S143 QUAL-176 Admin",
                role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
                scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") }));
        return client;
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> that answers an instant ONE DAY later on every call. It exists
    /// because a frozen clock cannot distinguish "the handler reads the clock once" from "twice" —
    /// see the single-read fact's doc for the full reasoning and for why the step is a whole day.
    ///
    /// <para><b>Why this is not the same instrument as <c>ProfileMigrationTests</c>'s
    /// stepping provider, and why neither is promoted to shared test infrastructure.</b> That one
    /// advances by ONE SECOND and is handed to a single pure component; this one advances by a DAY
    /// and is registered host-wide, where it also moves background sweeps and audit timestamps. The
    /// step size IS the discriminating design in both cases, and a shared "stepping provider" would
    /// invite callers to pick a step without thinking about what their own blind spot is — which is
    /// the mistake that made QUAL-176's first fix look complete.</para>
    /// </summary>
    private sealed class DayPerCallTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _start;
        private int _calls;

        public DayPerCallTimeProvider(DateTimeOffset start) => _start = start;

        /// <summary>How many times the host has read this clock, across every consumer.</summary>
        public int CallCount => Volatile.Read(ref _calls);

        // Interlocked, not _calls++: hosted services (DelegationExpiryService, SettlementCloseService)
        // read this provider on their own threads while a request is in flight, so an unsynchronised
        // counter could lose a step and hand two callers the SAME instant — which would silently
        // weaken the very property this provider exists to discriminate.
        public override DateTimeOffset GetUtcNow()
            => _start + TimeSpan.FromDays(Interlocked.Increment(ref _calls) - 1);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    // ─────────────────────────────── fixture seeds ───────────────────────────────
    //
    // Shape copied from Contracts/S116DelegationSpecRuntimeTests.SeedAsync, which is the proven
    // recipe for a self-service /delegate POST that reaches 200: the actor and the stand-in share
    // one Organisation (same tree root), the stand-in holds a LocalLeader-or-above role whose
    // ORG_ONLY scope covers the actor's report, and the actor has at least one PRIMARY report (the
    // handler returns 400 "no reports to delegate" otherwise). Seeded AFTER the pinned host's first
    // CreateClient() — PAT-008 boot order.

    private async Task SeedSelfServiceFixtureAsync() => await SeedAsync(
        SelfOrg, "S143 Selvbetjening", SelfLeader, SelfStandIn, SelfReport);

    private async Task SeedAdminFixtureAsync() => await SeedAsync(
        AdminOrg, "S143 Admin-paa-vegne-af", AdminManager, AdminStandIn, AdminReport);

    private async Task SeedAsync(string orgId, string orgName, string manager, string standIn, string report)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version)
            VALUES
                (@mao, 'S143 QUAL-176 Ministerie', 'MAO', NULL, '/' || @mao || '/', 'AC', 'OK24'),
                (@org, @orgName, 'ORGANISATION', @mao, '/' || @mao || '/' || @org || '/', 'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mao", Mao);
            cmd.Parameters.AddWithValue("org", orgId);
            cmd.Parameters.AddWithValue("orgName", orgName);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@mgr,   @mgr,   'dev-only', 'S143 Leder',     NULL, @org, 'HK', 'OK24', TRUE),
                (@stand, @stand, 'dev-only', 'S143 Vikar',     NULL, @org, 'HK', 'OK24', TRUE),
                (@rep,   @rep,   'dev-only', 'S143 Rapport',   NULL, @org, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mgr", manager);
            cmd.Parameters.AddWithValue("stand", standIn);
            cmd.Parameters.AddWithValue("rep", report);
            cmd.Parameters.AddWithValue("org", orgId);
            await cmd.ExecuteNonQueryAsync();
        }

        // The stand-in's qualifying role: LocalLeader-or-above, ORG_ONLY on the Organisation that
        // holds the report — the coverage census the create handlers run in-tx.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@stand, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("stand", standIn);
            cmd.Parameters.AddWithValue("org", orgId);
            await cmd.ExecuteNonQueryAsync();
        }

        // The manager's PRIMARY report. effective_from is a LITERAL well before both pinned
        // instants, so the edge is already in force under either clock — never derived from "now",
        // which would put a second clock into the fixture itself.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship,
                                         effective_from, source, created_by)
            VALUES (@rep, @mgr, @org, 'PRIMARY', DATE '2024-01-01', 'MANUAL', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("rep", report);
            cmd.Parameters.AddWithValue("mgr", manager);
            cmd.Parameters.AddWithValue("org", orgId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────── read-back helper ───────────────────────────────

    /// <summary>
    /// The UTC calendar day of the stored <c>created_at</c> INSTANT for the given approver's active
    /// vikar row.
    ///
    /// <para>The cast goes through <see cref="DateTime"/> and not straight to <see cref="DateOnly"/>:
    /// Npgsql boxes a timestamp column as <c>DateTime</c>, so a direct <c>(DateOnly)</c> cast
    /// compiles and then throws <c>InvalidCastException</c> at run time — a trap four facts in
    /// <c>EmployeeProfileCopenhagenBoundaryTests</c> shipped with and only discovered in CI.
    /// <c>ToUniversalTime()</c> is a no-op when Npgsql hands back a UTC-kind value (which it does for
    /// <c>TIMESTAMPTZ</c>) and corrects it if the provider ever returns a local-kind one.</para>
    /// </summary>
    private async Task<DateOnly> ReadVikarCreatedAtUtcDayAsync(string absentApproverId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT created_at FROM manager_vikar
            WHERE absent_approver_id = @approver AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("approver", absentApproverId);
        var raw = await cmd.ExecuteScalarAsync();
        Assert.NotNull(raw);
        return DateOnly.FromDateTime(((DateTime)raw!).ToUniversalTime());
    }
}
