using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13306 (QUAL-111) — GENUINE per-endpoint missing-If-Match (428) proofs for the three
/// admin-strict position-override mutating endpoints (PUT update / POST activate / POST deactivate).
///
/// <para><b>Why this file exists (the QUAL-111 defect):</b> the missing-precondition "428 contract"
/// slots in <c>Concurrency.PositionOverrideConcurrencyTests</c> were three identically-bodied clones
/// that called <c>EtagHeaderHelper.TryParseIfMatch</c> DIRECTLY on a hand-built header-less request.
/// They pinned the shared helper — not the endpoints they were named for — so an endpoint that
/// stopped invoking the helper (its 428 guard deleted) left every clone GREEN. That is the PAT-014
/// verification-theater class: a test named for a surface it never drives. These three
/// position-override endpoints had NO genuine 428 coverage anywhere; this file adds it, and the
/// clones were removed.</para>
///
/// <para><b>What makes each test genuine + falsifiable (PAT-014):</b> each drives the REAL route over
/// HTTP through <see cref="StatsTidWebApplicationFactory"/> against a row seeded through the REAL
/// create endpoint, sends the mutation with NO <c>If-Match</c> header, and asserts <c>428</c>. The
/// guard under test is the endpoint's first-line
/// <c>if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out _, out var headerError)) return
/// Results.Json(new { error = headerError }, statusCode: 428);</c>
/// (PositionOverrideEndpoints.cs: PUT ~:206, deactivate ~:355, activate ~:463). Delete or neuter that
/// block and the header-less request no longer short-circuits at 428 — it falls through to the
/// existence / status / optimistic-concurrency path and returns 200 / 409 / 412 — so the
/// <c>== 428</c> assertion goes RED per endpoint, independently. (For the PUT the SEC-033 range check
/// precedes the guard, so the body is range-valid; the guard still fires before existence/status.)</para>
///
/// <para><b>Auth / seed:</b> the write endpoints are policy <c>GlobalAdminOnly</c> (SEC-032;
/// <c>requireOrgScope:false</c>) → a bare <c>role=GlobalAdmin</c> token admits. The
/// <c>position_code</c> FK parent is the init.sql-seeded <c>DEPARTMENT_HEAD</c>. Each test seeds its
/// own ACTIVE override under a test-unique <c>agreement_code</c> so a header-less mutation is one that
/// would OTHERWISE succeed (or reach a state check) — the strongest form of the flip.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverridePreconditionHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OkVersion = "OK24";
    private const string PositionCode = "DEPARTMENT_HEAD"; // init.sql-seeded positions FK parent

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────────────────────────────── PUT (update) ──────────────────────────────────────

    /// <summary>
    /// <c>PUT /api/admin/position-overrides/{id}</c> with a range-valid body but NO If-Match → 428.
    /// Guard: PositionOverrideEndpoints.cs ~:206. Remove it and the header-less PUT reaches the
    /// existence/status/OCE path (200 on a clean value edit, else 409/412) — never 428 → RED.
    /// </summary>
    [Fact]
    public async Task Update_RealEndpoint_MissingIfMatch_Returns428()
    {
        var client = AdminClient();
        var uniqueAc = "PRE_PO_U_" + Rand();
        var overrideId = await CreateOverrideAsync(client, uniqueAc, maxFlex: 200m);

        // Value-only edit (same identity triple per SEC-034), range-valid — the only thing missing
        // is the If-Match header.
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}")
        {
            Content = JsonContent.Create(OverrideBody(uniqueAc, maxFlex: 250m)),
        };
        var rsp = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ─────────────────────────────────────── POST deactivate ─────────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/position-overrides/{id}/deactivate</c> with NO If-Match → 428.
    /// Guard: PositionOverrideEndpoints.cs ~:355 (first check). Remove it and the header-less POST
    /// reaches the existence/status/OCE path (200 deactivate, else 409/412) — never 428 → RED.
    /// </summary>
    [Fact]
    public async Task Deactivate_RealEndpoint_MissingIfMatch_Returns428()
    {
        var client = AdminClient();
        var uniqueAc = "PRE_PO_DA_" + Rand();
        var overrideId = await CreateOverrideAsync(client, uniqueAc, maxFlex: 200m);

        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate");
        var rsp = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ──────────────────────────────────────── POST activate ──────────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/position-overrides/{id}/activate</c> with NO If-Match → 428. The If-Match
    /// guard (PositionOverrideEndpoints.cs ~:463) is the FIRST check, ahead of the "only INACTIVE can
    /// be activated" 409 — so a header-less activate on an ACTIVE row still 428s. Remove the guard and
    /// the same request reaches that status check (409 here) — never 428 → RED.
    /// </summary>
    [Fact]
    public async Task Activate_RealEndpoint_MissingIfMatch_Returns428()
    {
        var client = AdminClient();
        var uniqueAc = "PRE_PO_AC_" + Rand();
        var overrideId = await CreateOverrideAsync(client, uniqueAc, maxFlex: 200m);

        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/activate");
        var rsp = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private static string Rand() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken("admin_qual111_po"));
        return client;
    }

    /// <summary>Creates an ACTIVE override via the REAL create endpoint; returns its server id.</summary>
    private static async Task<Guid> CreateOverrideAsync(
        HttpClient client, string agreementCode, decimal maxFlex)
    {
        var rsp = await client.PostAsJsonAsync("/api/admin/position-overrides",
            OverrideBody(agreementCode, maxFlex));
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("overrideId").GetGuid();
    }

    private static object OverrideBody(string agreementCode, decimal maxFlex) => new
    {
        agreementCode,
        okVersion = OkVersion,
        positionCode = PositionCode,
        maxFlexBalance = maxFlex,
        flexCarryoverMax = (decimal?)null,
        normPeriodWeeks = 4,
        weeklyNormHours = (decimal?)null,
        description = "QUAL-111 precondition 428",
    };

    private static string MintGlobalAdminToken(string actorId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC");
    }
}
