using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S29 / TASK-2909 D-tests #7 (ETag/If-Match contract) + #12 (same-day-only-edit validator).
/// HTTP-level tests against the live <see cref="WageTypeMappingEndpoints"/> via
/// <see cref="StatsTidWebApplicationFactory"/> (the same harness used in S27 / TASK-2710).
///
/// <list type="bullet">
///   <item><b>#7</b>: PUT with stale <c>If-Match: 5</c> against current version=6 → 412;
///   PUT without <c>If-Match</c> → 428; DELETE without <c>If-Match</c> → 428; DELETE with
///   matching <c>If-Match</c> → 204; after cross-day supersession the response carries a
///   new ETag for the new row (<c>version=1</c>).</item>
///   <item><b>#12</b>: POST with <c>effective_from</c> in the past → 422 with
///   <c>suppliedEffectiveFrom</c> + <c>today</c> in the body; PUT with future
///   <c>effective_from</c> → 422 with the same shape; POST with
///   <c>effective_from = today</c> → 201 (success).</item>
/// </list>
///
/// JWT minting follows the dev-fallback signing key pattern verbatim from
/// <see cref="PublisherStallReadYourWriteTests"/>. The <c>GlobalAdminOnly</c> policy
/// requires the GlobalAdmin role on the JWT — no org-scope required (admin-strict
/// surface per ADR-019).
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingEndpointTests : IAsyncLifetime
{
    // Verbatim from JwtValidationSetup.DevFallbackSigningKey — same approach as
    // PublisherStallReadYourWriteTests. Hosting env defaults to Development in
    // WebApplicationFactory<Program>, so the dev-fallback signing key fires.
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

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

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #7 — ETag/If-Match contract: 412, 428, 204 + ETag carry on supersession.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_StaleIfMatch_Returns412()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Seed: pick a known seed row from init.sql ('OVERTIME_50' / 'OK24' / 'HK' / position='').
        // Read the current version through the list endpoint to anchor the test independent of
        // any prior mutation a sibling test may have done.
        var (currentVersion, _) = await ReadVersionAsync(client, "OVERTIME_50", "OK24", "HK", position: "");

        // Issue PUT with a stale If-Match (currentVersion - 1) → 412.
        var staleIfMatch = currentVersion - 1;
        var staleRsp = await PutAsync(client,
            timeType: "OVERTIME_50", okVersion: "OK24", agreementCode: "HK", position: "",
            wageType: "SLS_STALE", description: "stale-test",
            effectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            ifMatchValue: staleIfMatch.ToString());

        Assert.Equal(HttpStatusCode.PreconditionFailed, staleRsp.StatusCode);
        var body = await staleRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(staleIfMatch, body.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(currentVersion, body.GetProperty("actualVersion").GetInt64());
    }

    [Fact]
    public async Task Put_MissingIfMatch_Returns428()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // PUT without If-Match. The endpoint helper rejects with 428 + the hint message.
        var rsp = await PutAsync(client,
            timeType: "OVERTIME_50", okVersion: "OK24", agreementCode: "HK", position: "",
            wageType: "SLS_NOPRE", description: "no-precondition",
            effectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            ifMatchValue: null);
        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingIfMatch_Returns428()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // DELETE without If-Match. The endpoint helper rejects with 428.
        var req = new HttpRequestMessage(HttpMethod.Delete,
            "/api/admin/wage-type-mappings?timeType=OVERTIME_50&okVersion=OK24&agreementCode=HK&position=");
        var rsp = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    [Fact]
    public async Task Delete_WithMatchingIfMatch_Returns204_AndSoftClosesRow()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Pick a seed row not used by sibling tests — CARE_DAY/AC/OK24/'' from init.sql seeds.
        var (currentVersion, _) = await ReadVersionAsync(client, "CARE_DAY", "OK24", "AC", position: "");

        var req = new HttpRequestMessage(HttpMethod.Delete,
            "/api/admin/wage-type-mappings?timeType=CARE_DAY&okVersion=OK24&agreementCode=AC&position=");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{currentVersion}\"");
        var rsp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        // Verify the row was soft-closed (effective_to = today, not hard-deleted — replay
        // determinism preserved per ADR-020 D2).
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_to FROM wage_type_mappings
            WHERE time_type = 'CARE_DAY' AND ok_version = 'OK24' AND agreement_code = 'AC'
              AND position = ''
            ORDER BY effective_from DESC
            LIMIT 1
            """, conn);
        var raw = await cmd.ExecuteScalarAsync();
        Assert.NotNull(raw);
        Assert.NotEqual(DBNull.Value, raw);
        var actualClose = DateOnly.FromDateTime((DateTime)raw!);
        Assert.Equal(today, actualClose);
    }

    [Fact]
    public async Task Put_CrossDaySupersession_Returns200_AndEmitsNewETagForVersion1()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Pick a seed row whose predecessor.effective_from = '2020-01-01' (so today > 2020-01-01
        // forces the cross-day path inside SupersedeAndCreateAsync).
        var (currentVersion, _) = await ReadVersionAsync(client, "VACATION", "OK24", "PROSA", position: "");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var rsp = await PutAsync(client,
            timeType: "VACATION", okVersion: "OK24", agreementCode: "PROSA", position: "",
            wageType: "SLS_VACA_UPDATED",
            description: "cross-day-supersession",
            effectiveFrom: today,
            ifMatchValue: currentVersion.ToString());

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // ETag on the response = "1" (the new row's version after the cross-day INSERT).
        Assert.NotNull(rsp.Headers.ETag);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #12 — same-day-only-edit validator (refinement L127 cycle 3 symmetric forbid).
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_WithPastEffectiveFrom_Returns422_WithSuppliedAndTodayInBody()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var pastDate = new DateOnly(2025, 6, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var body = new
        {
            timeType = "WTM_S29_VALIDATOR_PAST",
            wageType = "SLS_PAST",
            okVersion = "OK24",
            agreementCode = "HK",
            position = "",
            description = "past-date",
            effectiveFrom = pastDate.ToString("yyyy-MM-dd"),
        };
        var rsp = await client.PostAsJsonAsync("/api/admin/wage-type-mappings", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(pastDate.ToString("yyyy-MM-dd"),
            json.GetProperty("suppliedEffectiveFrom").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"),
            json.GetProperty("today").GetString());
    }

    [Fact]
    public async Task Put_WithFutureEffectiveFrom_Returns422_WithSuppliedAndTodayInBody()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var futureDate = today.AddDays(30);

        // Read current version for an existing seed so the PUT shape is otherwise valid;
        // the validator should fire BEFORE the If-Match parse + DB lookup.
        var (currentVersion, _) = await ReadVersionAsync(client, "HOLIDAY_SUPPLEMENT", "OK24", "HK", position: "");

        var rsp = await PutAsync(client,
            timeType: "HOLIDAY_SUPPLEMENT", okVersion: "OK24", agreementCode: "HK", position: "",
            wageType: "SLS_FUT",
            description: "future-date",
            effectiveFrom: futureDate,
            ifMatchValue: currentVersion.ToString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(futureDate.ToString("yyyy-MM-dd"),
            json.GetProperty("suppliedEffectiveFrom").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"),
            json.GetProperty("today").GetString());
    }

    [Fact]
    public async Task Post_WithTodayEffectiveFrom_Returns201()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var uniqueTimeType = "WTM_S29_OK_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var body = new
        {
            timeType = uniqueTimeType,
            wageType = "SLS_NEW",
            okVersion = "OK24",
            agreementCode = "HK",
            position = "",
            description = "today-create",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        };
        var rsp = await client.PostAsJsonAsync("/api/admin/wage-type-mappings", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current version for a given natural key by listing the admin endpoint and
    /// filtering by the key fields. Returns <c>(version, exists)</c>.
    /// </summary>
    private static async Task<(long Version, bool Exists)> ReadVersionAsync(
        HttpClient client, string timeType, string okVersion, string agreementCode, string position)
    {
        var listRsp = await client.GetAsync(
            $"/api/admin/wage-type-mappings/agreement/{agreementCode}/{okVersion}");
        Assert.Equal(HttpStatusCode.OK, listRsp.StatusCode);
        var arr = await listRsp.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.GetProperty("timeType").GetString() == timeType
                && item.GetProperty("position").GetString() == position)
            {
                return (item.GetProperty("version").GetInt64(), true);
            }
        }
        return (0L, false);
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        string timeType, string okVersion, string agreementCode, string position,
        string wageType, string description, DateOnly effectiveFrom,
        string? ifMatchValue)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/wage-type-mappings")
        {
            Content = JsonContent.Create(new
            {
                timeType,
                wageType,
                okVersion,
                agreementCode,
                position,
                description,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            }),
        };
        if (ifMatchValue is not null)
        {
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchValue}\"");
        }
        return await client.SendAsync(req);
    }

    private static string MintAdminToken()
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: "ADMIN_S29_QA",
            name: "S29 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "HK");
    }
}
