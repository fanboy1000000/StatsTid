using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S143 / TASK-14300 — the end-to-end evidence for <c>GET /api/calendar/today</c>: the per-route
/// spec≡runtime gate plus the values the endpoint actually SERVES at the instants that discriminate
/// a correct implementation from the plausible wrong ones.
///
/// <para><b>What is being proved, in plain language.</b> Four screens used to decide "which month am
/// I looking at" from the BROWSER's clock, and that month is the period envelope the skema save and
/// the approval send are filed under — so a device in the wrong time zone filed real hours into the
/// wrong month. This endpoint is the replacement: the server tells the client which Danish calendar
/// day it is, and how many seconds remain until that day rolls over so the client can schedule its
/// own refresh without ever reading the device clock again. Owner ruling OQ-1d gates the whole app
/// shell on this read, which is why it is worth this much evidence.</para>
///
/// <para><b>Why <c>WithFixedInstant</c> and not <c>WithFixedToday</c>.</b> The long-standing harness
/// <c>WithFixedToday(DateOnly)</c> pins UTC MIDNIGHT — the one moment of the day where the Danish
/// and UTC calendars name the same date. A test built on it passes identically against the correct
/// implementation and against a raw-UTC-day bug, so it could never catch this class of defect.
/// <see cref="StatsTidWebApplicationFactory.WithFixedInstant"/> (S142 / TASK-14200) pins a full
/// instant, which is what makes the distinction observable at all.</para>
///
/// <para><b>Expected values are LITERALS.</b> Every date and every second-count below is
/// hand-computed from the pinned instant and the published EU transition rule (last Sunday of March,
/// 02:00 CET → 03:00 CEST; last Sunday of October, 03:00 CEST → 02:00 CET — in 2026, 29 March and
/// 25 October) and written out. None is obtained by calling the helper under test or by adding an
/// offset in test code; that self-referential shape is exactly what S142 spent a sprint deleting.</para>
///
/// <para><b>Its companion.</b> The arithmetic itself is pinned WITHOUT Docker by
/// <c>StatsTid.Tests.Unit.Endpoints.CalendarServerDayTests</c>, which is also where the RED for this
/// change was demonstrated (against both an "add 24 hours" and a "reuse today's UTC offset"
/// implementation). This class is the evidence that the WIRE carries those same values, that the
/// committed spec describes them, and that the authentication decision is the one that was
/// ruled.</para>
///
/// <para><b>Seed discipline.</b> Nothing is seeded. The endpoint has no employee, no organisation
/// and no repository — it is the wall clock — so the only fixture is a JWT, and the class is
/// therefore insensitive to every other suite's data. The actor is the init.sql seed employee
/// <c>emp001</c> (org STY01) precisely because it is the LOWEST role: proving an ordinary employee's
/// app shell can boot is the point of the access decision.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S143CalendarSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string SpecPath = "/api/calendar/today";

    // init.sql seed employee: emp001 in STY01, agreement AC. Chosen for being the lowest role.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    /// <summary>The EXACT camelCase member set of <c>CalendarTodayResponse</c>. An added member is a
    /// wire change and must be a deliberate one, so the key set is asserted exactly rather than by
    /// presence.</summary>
    private static readonly string[] ResponseKeys = { "today", "secondsUntilNextMidnight" };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot once so Program.cs's seeders have run before any fixed-instant host is derived.
        _ = _factory.CreateClient();
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The per-route spec≡runtime gate.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The committed <c>docs/api/openapi.json</c> <c>200</c> schema for this operation structurally
    /// matches the REAL serialized response: root kind, property presence, camelCase keys,
    /// nullable/required fidelity. A <c>.Produces&lt;T&gt;</c> that named the wrong type, or a stale
    /// committed spec, fails here — which is the whole point of the endpoint being "born typed"
    /// (PAT-012) rather than retrofitted later.
    /// </summary>
    [Fact]
    public async Task CalendarToday_SchemaMatchesRuntime()
    {
        using var client = EmployeeClient(_factory);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec,
            client,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, SpecPath),
            SpecPath,
            "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ResponseKeys, "GET /api/calendar/today 200");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The served DAY — the UTC-vs-Danish boundary.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The marquee case.</b> <c>BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen</c> is
    /// 2026-07-15 22:30Z. Copenhagen is CEST (UTC+02:00) in July, so local time is already
    /// 2026-07-16 00:30 — the Danish calendar day has rolled over while the UTC day still reads the
    /// 15th. The endpoint must serve the literal <c>2026-07-16</c>.
    ///
    /// <para>This single instant fails BOTH plausible day bugs at once: a raw-UTC-day implementation
    /// answers the 15th, and a hardcoded <c>+01:00</c> offset (22:30 + 1h = 23:30) also answers the
    /// 15th. Only conversion through the real Europe/Copenhagen zone crosses midnight here.</para>
    ///
    /// <para>The remaining seconds at 00:30 local on an ordinary 24-hour day are 23 h 30 m =
    /// <b>84600</b>, asserted alongside so the two members are proved consistent on the wire and not
    /// merely individually plausible.</para>
    /// </summary>
    [Fact]
    public async Task ServedDay_SummerInstantAlreadyTomorrowInCopenhagen_IsTheDanishDay()
    {
        var (today, seconds) = await GetServerDayAsync(
            BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);

        Assert.Equal("2026-07-16", today);
        Assert.Equal(84600, seconds);
    }

    /// <summary>
    /// <b>The guard against over-correction.</b>
    /// <c>BoundaryInstants.WinterEveningCalendarsStillAgree</c> is 2026-01-15 22:30Z. Copenhagen is
    /// CET (UTC+01:00) in January, so local time is 2026-01-15 23:30 — still the 15th, and the two
    /// calendars AGREE. The endpoint must serve <c>2026-01-15</c>, with 30 minutes (<b>1800</b>
    /// seconds) to the rollover.
    ///
    /// <para>Without this, a hardcoded <c>+02:00</c> (the summer offset applied year-round) would
    /// pass the case above: 22:30 + 2h rolls the day over a full hour before the real CET offset
    /// does. Moving business dates to Copenhagen must not move them a day too FAR — the rule is "the
    /// correct Danish day", not "always tomorrow".</para>
    /// </summary>
    [Fact]
    public async Task ServedDay_WinterInstantWhereCalendarsAgree_IsStillTheFifteenth()
    {
        var (today, seconds) = await GetServerDayAsync(
            BoundaryInstants.WinterEveningCalendarsStillAgree);

        Assert.Equal("2026-01-15", today);
        Assert.Equal(1800, seconds);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The served DURATION — the two days that are not 24 hours long.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The 23-hour day, over the wire.</b> 2026-03-29 is the last Sunday of March, when Danish
    /// clocks jump from 02:00 CET straight to 03:00 CEST. The pinned instant 2026-03-28 23:00Z is
    /// exactly 2026-03-29 00:00 local (still CET), the first moment of that short day; the next
    /// Danish midnight is 2026-03-30 00:00 local, which under CEST is 2026-03-29 22:00Z. The endpoint
    /// must serve <c>2026-03-29</c> and <b>82800</b> seconds.
    ///
    /// <para>An implementation that adds 24 hours, or that reuses today's UTC offset to place
    /// tomorrow's midnight, serves 86400 — an hour too long, so the client keeps rendering and filing
    /// against 29 March for the first hour of 30 March.</para>
    /// </summary>
    [Fact]
    public async Task ServedDuration_AtStartOfThe23HourDay_Is82800Seconds()
    {
        var (today, seconds) = await GetServerDayAsync(
            new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-03-29", today);
        Assert.Equal(82800, seconds);
    }

    /// <summary>
    /// <b>The 25-hour day, over the wire.</b> 2026-10-25 is the last Sunday of October, when Danish
    /// clocks fall back from 03:00 CEST to 02:00 CET. The pinned instant 2026-10-24 22:00Z is exactly
    /// 2026-10-25 00:00 local (still CEST), the first moment of that long day; the next Danish
    /// midnight is 2026-10-26 00:00 local, which under CET is 2026-10-25 23:00Z. The endpoint must
    /// serve <c>2026-10-25</c> and <b>90000</b> seconds.
    ///
    /// <para>The same two wrong implementations serve 86400 here — an hour too SHORT. This case and
    /// the March one therefore fail them in opposite directions, which is why both are pinned rather
    /// than one.</para>
    /// </summary>
    [Fact]
    public async Task ServedDuration_AtStartOfThe25HourDay_Is90000Seconds()
    {
        var (today, seconds) = await GetServerDayAsync(
            new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-10-25", today);
        Assert.Equal(90000, seconds);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The access decision.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The endpoint is authenticated, not anonymous</b> — the ruled decision, pinned so it cannot
    /// be relaxed by accident. A request with no bearer token is rejected with 401.
    ///
    /// <para>This matters more than it looks: the Backend's only two unauthenticated routes are
    /// <c>GET /health</c> (an infrastructure liveness probe, not a product contract) and
    /// <c>POST /api/auth/login</c> (which necessarily precedes authentication) — a census that is
    /// counted rather than assumed, with the count and its method recorded at
    /// <c>CalendarEndpoints</c>'s class doc. An anonymous PRODUCT endpoint would be a new
    /// security-surface precedent, and the frontend does not need one — it restores its session
    /// locally and instantly, so the shell is already authenticated when it issues this read.</para>
    /// </summary>
    [Fact]
    public async Task CalendarToday_WithoutAToken_IsRejected()
    {
        using var anonymous = _factory.CreateClient();

        using var response = await anonymous.GetAsync(SpecPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// <b>The lowest role is admitted</b> — the other half of the access decision, and the one that
    /// would actually break the product if it were wrong. Every screen the bootstrap read gates is an
    /// employee screen, so an Employee token must pass. The <c>"Authenticated"</c> policy is
    /// role-insensitive by construction; this pins that the endpoint really carries that policy and
    /// not a stricter one copied from a neighbouring admin file.
    /// </summary>
    [Fact]
    public async Task CalendarToday_WithAPlainEmployeeToken_IsAdmitted()
    {
        using var client = EmployeeClient(_factory);

        using var response = await client.GetAsync(SpecPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── helpers ──

    /// <summary>
    /// Boots a host whose <see cref="TimeProvider"/> is pinned to <paramref name="instant"/>, reads
    /// the endpoint as a plain employee, and returns the two served members verbatim (the date as the
    /// raw wire string, so a serialization change is visible rather than parsed away).
    /// </summary>
    private async Task<(string? Today, int SecondsUntilNextMidnight)> GetServerDayAsync(DateTimeOffset instant)
    {
        using var host = _factory.WithFixedInstant(instant);
        using var client = EmployeeClient(host);

        using var response = await client.GetAsync(SpecPath);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ResponseKeys, $"GET {SpecPath} @ {instant:O}");

        return (root.GetProperty("today").GetString(),
                root.GetProperty("secondsUntilNextMidnight").GetInt32());
    }

    private static HttpClient EmployeeClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", EmployeeBearerToken(Emp001, Emp001OrgId));
        return client;
    }

    private static string EmployeeBearerToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }
}
