using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S118 / TASK-11802 — the per-route spec≡runtime gate for the ENTITLEMENT-CONFIGS admin family
/// drained in retrofit Pass 5 (TASK-11800): list (bare array) / by-id GET / POST 201 / PUT 200
/// / DELETE 204, all serializing the ONE shared 16-member <c>EntitlementConfigResponse</c>
/// (owner ruling #2's collapse point — this primary admin surface was one of the two
/// byte-identical pre-S118 mapper copies, so all five ops here are byte-faithful,
/// non-wire-changed shapes).
///
/// <para><b>If-Match as the FE composes it:</b> the PUT reads the by-id GET's ETag header
/// first and sends that version — never a hard-coded token. The DELETE composes off the
/// create 201's ETag. Every row is CARE_DAY (<c>fullDayOnly: true</c> guard-forced — the S73
/// D-A ruling — so the shared record's repaired member is live TRUE on every walk).</para>
///
/// <para><b>Seed discipline:</b> a FRESH testcontainer per test; every natural key is
/// (CARE_DAY, <c>S118EC</c>, <c>OKS118_*</c>) — agreement code + okVersions DISJOINT from the
/// boot seeders (AC/HK/PROSA × OK24/OK26) and from the three existing EntitlementConfig suites
/// (<c>EntitlementConfigEndpointTests</c> <c>OK_S30POST_*</c>/<c>OK_S68RESET_*</c>;
/// <c>EntitlementConfigSupersessionTests</c> <c>OK_CASEA_*</c>/<c>OK_CASEC_*</c> + its
/// CARE_DAY/SENIOR_DAY/CHILD_SICK/SPECIAL_HOLIDAY rows under PROSA×OK24/OK26;
/// <c>EntitlementConfigFullDayOnlyAdminTests</c> <c>OK_S73FDO_*</c>). Each mutation owns its
/// key. Matcher + Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S118EntitlementConfigSpecRuntimeTests : IAsyncLifetime
{
    private const string ActorId = "s118e_gadmin";
    private const string JwtOrg = "S118EM"; // JWT claim only — entitlement-config audit rows are GLOBAL (no org FK)
    private const string AgreementCode = "S118EC";

    /// <summary>The 16-member shared child record (incl. <c>fullDayOnly</c>).</summary>
    private static readonly string[] EntityKeys =
    {
        "configId", "entitlementType", "agreementCode", "okVersion",
        "annualQuota", "accrualModel", "resetMonth", "carryoverMax",
        "proRateByPartTime", "isPerEpisode", "minAge", "description",
        "fullDayOnly", "effectiveFrom", "effectiveTo", "version",
    };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline entitlement configs)
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/admin/entitlement-configs (bare array of OPEN rows).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The matcher walks EVERY element (the boot-seeded AC/HK/PROSA rows ride along);
    /// the fresh S118 row is pinned field-wise incl. the live <c>fullDayOnly: true</c>.</summary>
    [Fact]
    public async Task List_Get200_BareArraySchemaMatchesRuntime()
    {
        using var admin = Admin();
        var (configId, _, _) = await CreateAsync(admin, "OKS118_LST");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/entitlement-configs"),
            "/api/admin/entitlement-configs", "get");

        var row = FindByKey(JsonDocument.Parse(body).RootElement, "OKS118_LST");
        Assert.Equal(configId, row.GetProperty("configId").GetGuid());
        Assert.True(row.GetProperty("fullDayOnly").GetBoolean());
        Assert.Equal(1L, row.GetProperty("version").GetInt64()); // the in-body If-Match source
        Assert.Equal(JsonValueKind.Null, row.GetProperty("effectiveTo").ValueKind); // open row
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — POST /api/admin/entitlement-configs (TRUE 201 + the 16-member body).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The exact-201 assertion (a 200 here is RED) + the matcher + the EXACT
    /// 16-member key set; decimal fidelity on <c>annualQuota</c>; ETag "1".</summary>
    [Fact]
    public async Task Create_Post201Exact_SixteenMemberSharedRecord()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/entitlement-configs", CreateJson("OKS118_CRT")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/entitlement-configs", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/admin/entitlement-configs (201)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, EntityKeys, "entitlement-config POST 201");
        Assert.Equal("CARE_DAY", root.GetProperty("entitlementType").GetString());
        Assert.Equal(AgreementCode, root.GetProperty("agreementCode").GetString());
        Assert.Equal("OKS118_CRT", root.GetProperty("okVersion").GetString());
        Assert.Equal(2.0m, root.GetProperty("annualQuota").GetDecimal()); // decimal fidelity
        Assert.True(root.GetProperty("fullDayOnly").GetBoolean());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ops 2+4 — by-id GET (200 + ETag) then PUT (200): the FE's exact If-Match flow.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The caller's real composition: create → by-id GET (matcher-asserted 200 whose
    /// ETag is the version source) → PUT with <c>If-Match: "&lt;version&gt;"</c> (same-day
    /// Case C ⇒ IN-PLACE edit: same configId, version 2). Both ops matcher-asserted.</summary>
    [Fact]
    public async Task ById_Get200_ThenPut200_IfMatchComposedFromTheByIdEtag()
    {
        using var admin = Admin();
        var (configId, _, _) = await CreateAsync(admin, "OKS118_PUT");

        // By-id GET — matcher + the ETag the FE composes the next If-Match from.
        using var getResponse = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, $"/api/admin/entitlement-configs/{configId}"));
        var getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)getResponse.StatusCode);

        var getTruth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/admin/entitlement-configs/{configId}", "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, getTruth, 200, getBody,
            "GET /api/admin/entitlement-configs/{configId} (200)");
        S118ContractAssert.AssertExactKeySet(
            JsonDocument.Parse(getBody).RootElement, EntityKeys, "entitlement-config by-id 200");
        var etagVersion = S118ContractAssert.EtagVersion(getResponse);
        Assert.Equal(1L, etagVersion);

        // PUT — the version from the ETag, same-day Case C in-place.
        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
                $"/api/admin/entitlement-configs/{configId}", PutJson("OKS118_PUT"), ifMatchVersion: etagVersion),
            "/api/admin/entitlement-configs/{configId}", "put");

        var putRoot = JsonDocument.Parse(putBody).RootElement;
        S118ContractAssert.AssertExactKeySet(putRoot, EntityKeys, "entitlement-config PUT 200");
        Assert.Equal(configId, putRoot.GetProperty("configId").GetGuid()); // Case C: the SAME row
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());
        Assert.Equal(3.0m, putRoot.GetProperty("annualQuota").GetDecimal()); // the edit, decimal-faithful
        Assert.True(putRoot.GetProperty("fullDayOnly").GetBoolean());        // re-asserted by the guard
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 5 — DELETE /api/admin/entitlement-configs/{configId} (declared 204).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Soft-delete under admin-strict If-Match from the create ETag: declared 204 =
    /// status + EMPTY body, both matcher-asserted.</summary>
    [Fact]
    public async Task Delete_204_StatusAndEmptyBodyMatchRuntime()
    {
        using var admin = Admin();
        var (configId, etagVersion, _) = await CreateAsync(admin, "OKS118_DEL");

        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete,
                $"/api/admin/entitlement-configs/{configId}", jsonBody: null, ifMatchVersion: etagVersion),
            "/api/admin/entitlement-configs/{configId}", "delete");
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    private async Task<(Guid ConfigId, long EtagVersion, JsonElement Body)> CreateAsync(
        HttpClient client, string okVersion)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/entitlement-configs", CreateJson(okVersion)));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Entitlement-config create for {okVersion} returned {(int)response.StatusCode}: {body}");
        var root = JsonDocument.Parse(body).RootElement.Clone();
        return (root.GetProperty("configId").GetGuid(), S118ContractAssert.EtagVersion(response), root);
    }

    private static JsonElement FindByKey(JsonElement array, string okVersion)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("agreementCode").GetString(), AgreementCode, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("okVersion").GetString(), okVersion, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a row for (CARE_DAY, {AgreementCode}, {okVersion}) in: {array.GetRawText()}");
    }

    // ─────────────────────────────── request bodies (invariant JSON) ───────────────────────────────

    /// <summary>POST body — CARE_DAY with the guard-forced <c>fullDayOnly: true</c>;
    /// effectiveFrom omitted ⇒ defaulted to today by the endpoint.</summary>
    private static string CreateJson(string okVersion)
        => $$"""
           { "entitlementType": "CARE_DAY", "agreementCode": "{{AgreementCode}}", "okVersion": "{{okVersion}}",
             "annualQuota": 2.0, "accrualModel": "IMMEDIATE", "resetMonth": 1, "carryoverMax": 0,
             "proRateByPartTime": true, "isPerEpisode": false,
             "description": "S118 omsorgsdage", "fullDayOnly": true }
           """;

    /// <summary>PUT body — effectiveFrom = today (required by the same-day validator);
    /// resetMonth/accrualModel unchanged (the immutability guard); quota edited 2.0 → 3.0.</summary>
    private static string PutJson(string okVersion)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $$"""
               { "entitlementType": "CARE_DAY", "agreementCode": "{{AgreementCode}}", "okVersion": "{{okVersion}}",
                 "annualQuota": 3.0, "accrualModel": "IMMEDIATE", "resetMonth": 1, "carryoverMax": 0,
                 "proRateByPartTime": true, "isPerEpisode": false,
                 "description": "S118 omsorgsdage (redigeret)", "fullDayOnly": true,
                 "effectiveFrom": "{{today}}" }
               """;
    }
}
