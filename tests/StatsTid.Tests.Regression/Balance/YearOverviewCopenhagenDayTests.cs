using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S142 / TASK-14207 — the Docker-gated behavioural half of the census-row-12 pin: the day the
/// year-overview endpoint actually SERVES, at instants where the Danish and UTC calendars disagree.
///
/// <para><b>What is being proved, in plain language.</b> The year-overview screen tells the
/// employee which period is past, which is "now", and dates every lookup behind those figures off
/// a single server-computed "today". Denmark is one hour ahead of UTC in winter (CET) and two in
/// summer (CEST), so between Danish midnight and UTC midnight the UTC calendar still reads
/// YESTERDAY. Before S142 the endpoint computed its day from the UTC calendar, so a Danish user
/// working at 00:30 was served the previous day's screen. These tests pin the served
/// <c>today</c> at exactly those instants.</para>
///
/// <para><b>Why <c>WithFixedInstant</c> and not <c>WithFixedToday</c>.</b> The long-standing
/// harness <c>WithFixedToday(DateOnly)</c> pins UTC MIDNIGHT — the one moment of the day where
/// both calendars name the same date. Every test built on it passes identically against the
/// correct implementation and against the UTC-day bug, so none of them could ever have caught
/// this. <see cref="StatsTidWebApplicationFactory.WithFixedInstant"/> (TASK-14200, wave 1) pins a
/// full instant, which is what makes the distinction observable at all.</para>
///
/// <para><b>Expected values are literals.</b> Each expected date below is hand-computed from the
/// instant and the season's offset and written out; none is obtained by calling
/// <c>CopenhagenBusinessDate.Today</c> or by adding an offset in test code, which would only
/// assert that the production helper agrees with a copy of itself.</para>
///
/// <para><b>Its companion.</b> The settlement engine
/// (<c>VacationSettlementService.cs</c>, census row 53) reproduces this endpoint's chain
/// byte-for-byte and must derive the SAME day. That parity contract — over both sites, and against
/// literals rather than against each other — is pinned without Docker by
/// <see cref="StatsTid.Tests.Regression.ArchitectureConstraints.YearOverviewSettlementClockParityTests"/>,
/// which is also where the RED for this change was demonstrated. This class is the end-to-end
/// evidence for the endpoint half.</para>
///
/// <para>Uses the init.sql seed employee <c>emp001</c> (org STY01, agreement AC) reading their own
/// year-overview — the self-access path, so no scope fixture is needed and nothing is seeded, which
/// keeps the pin insensitive to every other suite's data.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class YearOverviewCopenhagenDayTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // init.sql seed employee: emp001 in STY01 (/MIN01/STY01/), agreement AC, OK24.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot once so Program.cs seeders (employee profiles, entitlement configs) have run before
        // any fixed-instant host is derived from this factory.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
        {
            await _harness.DisposeAsync();
        }
    }

    /// <summary>
    /// <b>Summer, the marquee case.</b> 2026-07-15 22:30Z. Copenhagen is CEST (UTC+02:00) in July,
    /// so local time is already 2026-07-16 00:30 — the Danish calendar day has rolled over while
    /// the UTC calendar day is still the 15th.
    ///
    /// <para>The endpoint must serve <c>2026-07-16</c>. This single instant fails BOTH plausible
    /// wrong implementations: the pre-S142 raw UTC day answers the 15th, and a hardcoded
    /// <c>+01:00</c> offset (22:30 + 1h = 23:30) also answers the 15th. Only conversion through the
    /// real Europe/Copenhagen zone crosses midnight here.</para>
    /// </summary>
    [Fact]
    public async Task ServedToday_SummerInstantAlreadyTomorrowInCopenhagen_IsTheDanishDay()
    {
        var served = await GetServedTodayAsync(
            BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen, year: 2026);

        Assert.Equal("2026-07-16", served);
    }

    /// <summary>
    /// <b>Winter.</b> 2026-01-15 23:30Z. Copenhagen is CET (UTC+01:00) in January, so local time is
    /// already 2026-01-16 00:30 while UTC still reads the 15th. The endpoint must serve
    /// <c>2026-01-16</c>.
    ///
    /// <para>This one fails the raw-UTC-day implementation and is DELIBERATELY silent about a
    /// hardcoded <c>+01:00</c> offset, which is accidentally correct in winter. That blind spot is
    /// what the third case exists for.</para>
    /// </summary>
    [Fact]
    public async Task ServedToday_WinterInstantAlreadyTomorrowInCopenhagen_IsTheDanishDay()
    {
        var served = await GetServedTodayAsync(
            BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen, year: 2026);

        Assert.Equal("2026-01-16", served);
    }

    /// <summary>
    /// <b>The control, and the guard against over-correction.</b> 2026-01-15 22:30Z — one hour
    /// earlier than the case above. Copenhagen is CET (UTC+01:00), so local time is 2026-01-15
    /// 23:30: still the 15th, and the two calendars AGREE. The endpoint must serve
    /// <c>2026-01-15</c>.
    ///
    /// <para>Without this, a hardcoded <c>+02:00</c> (summer offset applied year-round) would pass
    /// the two cases above: 22:30 + 2h rolls the day over a full hour before the real CET offset
    /// does. Moving business dates to Copenhagen must not move them a day too FAR — the fix is
    /// "the correct Danish day", not "always tomorrow".</para>
    /// </summary>
    [Fact]
    public async Task ServedToday_WinterInstantWhereCalendarsAgree_IsStillTheFifteenth()
    {
        var served = await GetServedTodayAsync(
            BoundaryInstants.WinterEveningCalendarsStillAgree, year: 2026);

        Assert.Equal("2026-01-15", served);
    }

    // ── helpers ──

    /// <summary>
    /// Boots a host whose <see cref="TimeProvider"/> is pinned to <paramref name="instant"/>, reads
    /// emp001's own year-overview, and returns the served <c>today</c> string verbatim.
    /// </summary>
    private async Task<string?> GetServedTodayAsync(DateTimeOffset instant, int year)
    {
        var client = _factory.WithFixedInstant(instant).CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", EmployeeBearerToken(Emp001, Emp001OrgId));

        var response = await client.GetAsync($"/api/balance/{Emp001}/year-overview?year={year}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("today").GetString();
    }

    private static string EmployeeBearerToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevJwtSettings());
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static JwtSettings DevJwtSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };
}
