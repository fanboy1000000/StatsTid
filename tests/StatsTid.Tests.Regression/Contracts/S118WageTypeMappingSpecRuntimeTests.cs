using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S118 / TASK-11802 — the per-route spec≡runtime gate for the WAGE-TYPE-MAPPINGS admin family
/// drained in retrofit Pass 5 (TASK-11800): list + by-agreement (bare arrays), POST 201, the
/// NATURAL-KEY PUT (no by-id route exists — the body's (timeType, okVersion, agreementCode,
/// position) IS the resource address), and the QUERY-PARAM-KEYED DELETE (declared 204) — all
/// on the ONE shared 7-member <c>WageTypeMappingResponse</c>. Every op here is a byte-faithful,
/// non-wire-changed shape (the POST 201 already built this exact shape pre-S112 — the
/// fork-free-create precedent).
///
/// <para><b>If-Match as the FE composes it:</b> this resource has NO by-id GET, so the LIST
/// response's in-body <c>version</c> is the documented single source of truth for If-Match
/// composition — the PUT test reads its version off the by-agreement list before mutating
/// (never a hard-coded token); the DELETE composes off the create 201's ETag.</para>
///
/// <para><b>Seed disjointness:</b> a FRESH testcontainer per test; every natural key is
/// (<c>S118_TT_*</c>, <c>OKS118</c>, <c>S118WTM</c>, position "") with wage types
/// <c>SLS_S118*</c> — DISJOINT from the init.sql seed families (NORMAL_HOURS/OVERTIME_*/… under
/// AC/HK/PROSA × OK24/OK26) and from all 7 existing WTM suites
/// (<c>WK_D_*</c>/<c>WK_E_*</c>/<c>WK_U_*</c>, <c>FR_WTM_C_/U_/D_*</c>, <c>WTM_S29_OK_*</c>,
/// the CASEB/CASEC/CROSSDAY/SAMEDAY/DELPOST/POSTPOST/UNIQ/DATED keys and their
/// <c>SLS_*</c> wage types). Matcher + Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S118WageTypeMappingSpecRuntimeTests : IAsyncLifetime
{
    private const string ActorId = "s118w_gadmin";
    private const string JwtOrg = "S118WM"; // JWT claim only — WTM audit rows are GLOBAL (no org FK)
    private const string AgreementCode = "S118WTM";
    private const string OkVersion = "OKS118";

    /// <summary>The EXACT 7 camelCase members of <c>WageTypeMappingResponse</c>.</summary>
    private static readonly string[] EntityKeys =
    {
        "timeType", "wageType", "okVersion", "agreementCode", "position", "description", "version",
    };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/admin/wage-type-mappings (bare array; init.sql seed families ride
    //  the walk).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_Get200_BareArraySchemaMatchesRuntime()
    {
        using var admin = Admin();
        await CreateAsync(admin, "S118_TT_LST");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/wage-type-mappings"),
            "/api/admin/wage-type-mappings", "get");

        var row = FindByTimeType(JsonDocument.Parse(body).RootElement, "S118_TT_LST");
        Assert.Equal("SLS_S118", row.GetProperty("wageType").GetString());
        Assert.Equal("", row.GetProperty("position").GetString()); // the omitted-position "" echo
        Assert.Equal(1L, row.GetProperty("version").GetInt64());   // the in-body If-Match source
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — POST /api/admin/wage-type-mappings (TRUE 201 + the 7-member body).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The exact-201 assertion + the matcher + the EXACT 7-member key set (the
    /// fork-free create — this 201 was always the full shape); ETag "1".</summary>
    [Fact]
    public async Task Create_Post201Exact_SevenMemberSharedRecord()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/wage-type-mappings", CreateJson("S118_TT_CRT")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/wage-type-mappings", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/admin/wage-type-mappings (201)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, EntityKeys, "wage-type-mapping POST 201");
        Assert.Equal("S118_TT_CRT", root.GetProperty("timeType").GetString());
        Assert.Equal("SLS_S118", root.GetProperty("wageType").GetString());
        Assert.Equal(OkVersion, root.GetProperty("okVersion").GetString());
        Assert.Equal(AgreementCode, root.GetProperty("agreementCode").GetString());
        Assert.Equal("", root.GetProperty("position").GetString());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ops 2+4 — by-agreement GET (200) then the NATURAL-KEY PUT (200): the FE's exact
    //  version-composition flow (no by-id GET exists on this resource).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Create → by-agreement list (matcher-asserted; ITS in-body <c>version</c> is the
    /// If-Match source — the documented composition path for this composite-keyed resource) →
    /// the natural-key PUT (the BODY addresses the row; same-day ⇒ in-place UPDATE, version 2,
    /// wageType edited).</summary>
    [Fact]
    public async Task ByAgreement_Get200_ThenNaturalKeyPut200_IfMatchFromTheListVersion()
    {
        using var admin = Admin();
        await CreateAsync(admin, "S118_TT_PUT");

        // The by-agreement list — matcher + the version the FE composes If-Match from.
        var listBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                $"/api/admin/wage-type-mappings/agreement/{AgreementCode}/{OkVersion}"),
            "/api/admin/wage-type-mappings/agreement/{agreementCode}/{okVersion}", "get");
        var listRow = FindByTimeType(JsonDocument.Parse(listBody).RootElement, "S118_TT_PUT");
        var version = listRow.GetProperty("version").GetInt64();
        Assert.Equal(1L, version);

        // The natural-key PUT — no id in the URL; the body's natural key addresses the row.
        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, "/api/admin/wage-type-mappings",
                PutJson("S118_TT_PUT"), ifMatchVersion: version),
            "/api/admin/wage-type-mappings", "put");

        var putRoot = JsonDocument.Parse(putBody).RootElement;
        S118ContractAssert.AssertExactKeySet(putRoot, EntityKeys, "wage-type-mapping PUT 200");
        Assert.Equal("S118_TT_PUT", putRoot.GetProperty("timeType").GetString());
        Assert.Equal("SLS_S118B", putRoot.GetProperty("wageType").GetString()); // the edit
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());            // same-day in-place bump
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 5 — DELETE /api/admin/wage-type-mappings (QUERY-PARAM-KEYED; declared 204).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The query-param-keyed soft-DELETE (no route id — the query string carries the
    /// natural key; position omitted ⇒ ""): declared 204 = status + EMPTY body, both
    /// matcher-asserted; admin-strict If-Match from the create ETag.</summary>
    [Fact]
    public async Task Delete_204_QueryParamKeyed_StatusAndEmptyBodyMatchRuntime()
    {
        using var admin = Admin();
        var etagVersion = await CreateAsync(admin, "S118_TT_DEL");

        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete,
                $"/api/admin/wage-type-mappings?timeType=S118_TT_DEL&okVersion={OkVersion}&agreementCode={AgreementCode}",
                jsonBody: null, ifMatchVersion: etagVersion),
            "/api/admin/wage-type-mappings", "delete");
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    /// <summary>Create a mapping through the REAL POST; returns the 201 ETag version.</summary>
    private async Task<long> CreateAsync(HttpClient client, string timeType)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/wage-type-mappings", CreateJson(timeType)));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Wage-type-mapping create for {timeType} returned {(int)response.StatusCode}: {body}");
        return S118ContractAssert.EtagVersion(response);
    }

    private static JsonElement FindByTimeType(JsonElement array, string timeType)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("timeType").GetString(), timeType, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("agreementCode").GetString(), AgreementCode, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("okVersion").GetString(), OkVersion, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a row for ({timeType}, {OkVersion}, {AgreementCode}) in: {array.GetRawText()}");
    }

    // ─────────────────────────────── request bodies (invariant JSON) ───────────────────────────────

    /// <summary>POST body — position omitted (⇒ "" on the row), effectiveFrom omitted (⇒ today).</summary>
    private static string CreateJson(string timeType)
        => $$"""
           { "timeType": "{{timeType}}", "wageType": "SLS_S118", "okVersion": "{{OkVersion}}",
             "agreementCode": "{{AgreementCode}}", "description": "S118 lønartsmapping" }
           """;

    /// <summary>PUT body — the natural key addresses the row; effectiveFrom = today (required
    /// by the same-day validator; same-day ⇒ in-place UPDATE); wageType edited.</summary>
    private static string PutJson(string timeType)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $$"""
               { "timeType": "{{timeType}}", "wageType": "SLS_S118B", "okVersion": "{{OkVersion}}",
                 "agreementCode": "{{AgreementCode}}", "description": "S118 lønartsmapping (redigeret)",
                 "effectiveFrom": "{{today}}" }
               """;
    }
}
