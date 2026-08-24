using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13306 (QUAL-111) — GENUINE per-endpoint missing-If-Match (428) proofs for the three
/// admin-strict agreement-config mutating endpoints (PUT update DRAFT / POST publish / POST archive).
///
/// <para><b>Why this file exists (the QUAL-111 defect):</b> the missing-precondition "428 contract"
/// slots in <c>Concurrency.AgreementConfigConcurrencyTests</c> were three identically-bodied clones
/// that called <c>EtagHeaderHelper.TryParseIfMatch</c> DIRECTLY on a hand-built header-less request.
/// They pinned the shared helper — not the endpoints they were named for — so an endpoint that
/// stopped invoking the helper (its 428 guard deleted) left every clone GREEN. That is the PAT-014
/// verification-theater class: a test named for a surface it never drives. These three
/// agreement-config endpoints had NO genuine 428 coverage anywhere; this file adds it, and the clones
/// were removed.</para>
///
/// <para><b>What makes each test genuine + falsifiable (PAT-014):</b> each drives the REAL route over
/// HTTP through <see cref="StatsTidWebApplicationFactory"/> against a DRAFT seeded through the REAL
/// create endpoint, sends the mutation with NO <c>If-Match</c> header, and asserts <c>428</c>. The
/// guard under test is the endpoint's first-line
/// <c>if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out _, out var headerError)) return
/// Results.Json(new { error = headerError }, statusCode: 428);</c>
/// (AgreementConfigEndpoints.cs: PUT ~:331, publish ~:449, archive ~:616 — check #1, ahead of the
/// existence / status pre-checks). Delete or neuter that block and the header-less request no longer
/// short-circuits at 428 — it falls through to the existence / status / optimistic-concurrency path
/// and returns 200 / 404 / 409 / 412 — so the <c>== 428</c> assertion goes RED per endpoint,
/// independently.</para>
///
/// <para><b>Auth / seed:</b> the write endpoints are policy <c>GlobalAdminOnly</c> → a bare
/// <c>role=GlobalAdmin</c> token admits. Each test seeds its own DRAFT under a test-unique
/// <c>agreement_code</c> (<c>PRE_AGC_*</c>, disjoint from the boot seeders and the S118/atomic/
/// concurrency suites) via the REAL create endpoint, so a header-less mutation is one that would
/// OTHERWISE proceed — the strongest form of the flip. The seed body is the committed S118
/// full-request shape.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementConfigPreconditionHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OkVersion = "OK24";

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

    // ───────────────────────────────────────── PUT (update DRAFT) ─────────────────────────────────

    /// <summary>
    /// <c>PUT /api/agreement-configs/{id}</c> with a bindable, valid body but NO If-Match → 428.
    /// Guard: AgreementConfigEndpoints.cs ~:331 (check #1). Remove it and the header-less PUT reaches
    /// the existence/status/OCE path (200 on a clean DRAFT edit, else 404/409/412) — never 428 → RED.
    /// </summary>
    [Fact]
    public async Task Update_RealEndpoint_MissingIfMatch_Returns428()
    {
        var client = AdminClient();
        var uniqueAc = "PRE_AGC_U_" + Rand();
        var configId = await CreateDraftAsync(client, uniqueAc);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/agreement-configs/{configId}")
        {
            Content = JsonBody(ConfigRequestJson(uniqueAc)),
        };
        var rsp = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ─────────────────────────────────────────── POST publish ────────────────────────────────────

    /// <summary>
    /// <c>POST /api/agreement-configs/{id}/publish</c> with NO If-Match → 428. Guard:
    /// AgreementConfigEndpoints.cs ~:449 (check #1). Remove it and the header-less publish reaches the
    /// existence/publish path (200 DRAFT→ACTIVE, else 404/412) — never 428 → RED.
    /// </summary>
    [Fact]
    public async Task Publish_RealEndpoint_MissingIfMatch_Returns428()
    {
        var client = AdminClient();
        var uniqueAc = "PRE_AGC_P_" + Rand();
        var configId = await CreateDraftAsync(client, uniqueAc);

        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/agreement-configs/{configId}/publish");
        var rsp = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ─────────────────────────────────────────── POST archive ────────────────────────────────────

    /// <summary>
    /// <c>POST /api/agreement-configs/{id}/archive</c> with NO If-Match → 428. Guard:
    /// AgreementConfigEndpoints.cs ~:616 (check #1). Remove it and the header-less archive reaches the
    /// existence/archive path (200 →ARCHIVED, else 404/412) — never 428 → RED.
    /// </summary>
    [Fact]
    public async Task Archive_RealEndpoint_MissingIfMatch_Returns428()
    {
        var client = AdminClient();
        var uniqueAc = "PRE_AGC_A_" + Rand();
        var configId = await CreateDraftAsync(client, uniqueAc);

        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/agreement-configs/{configId}/archive");
        var rsp = await client.SendAsync(req);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private static string Rand() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken("admin_qual111_agc"));
        return client;
    }

    /// <summary>Creates a DRAFT config via the REAL create endpoint; returns its server id.</summary>
    private static async Task<Guid> CreateDraftAsync(HttpClient client, string agreementCode)
    {
        var rsp = await client.PostAsync("/api/agreement-configs", JsonBody(ConfigRequestJson(agreementCode)));
        var body = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Created,
            $"Config create for {agreementCode} returned {(int)rsp.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("configId").GetGuid();
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>The committed S118 full AgreementConfigRequest shape (all C#-required members);
    /// only the natural key varies per test.</summary>
    private static string ConfigRequestJson(string agreementCode)
        => $$"""
           {
             "agreementCode": "{{agreementCode}}", "okVersion": "{{OkVersion}}",
             "description": "QUAL-111 precondition 428", "normModel": "WEEKLY_HOURS",
             "weeklyNormHours": 37.0, "normPeriodWeeks": 4, "annualNormHours": 1924.0,
             "maxFlexBalance": 74.0, "flexCarryoverMax": 37.0,
             "hasOvertime": true, "hasMerarbejde": false,
             "overtimeThreshold50": 37.0, "overtimeThreshold100": 44.0,
             "eveningSupplementEnabled": true, "nightSupplementEnabled": true,
             "weekendSupplementEnabled": true, "holidaySupplementEnabled": true,
             "eveningStart": 17, "eveningEnd": 23, "nightStart": 23, "nightEnd": 6,
             "eveningRate": 0.25, "nightRate": 0.50, "weekendSaturdayRate": 0.50,
             "weekendSundayRate": 1.00, "holidayRate": 1.00,
             "onCallDutyEnabled": true, "onCallDutyRate": 0.25,
             "callInWorkEnabled": true, "callInMinimumHours": 3.0, "callInRate": 1.50,
             "travelTimeEnabled": true, "workingTravelRate": 1.00, "nonWorkingTravelRate": 0.50
           }
           """;

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
