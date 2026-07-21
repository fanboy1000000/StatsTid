using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S118 / TASK-11802 — the per-route spec≡runtime gate for the POSITION-OVERRIDES admin family
/// drained in retrofit Pass 5 (TASK-11800): list / by-id / by-agreement (bare array) / POST 201
/// / PUT 200 on the ONE shared 14-member <c>PositionOverrideResponse</c>, plus the
/// activate/deactivate lifecycle envelopes.
///
/// <para><b>RULING #1 PIN (position-override create):</b> the create 201 is ALWAYS the full
/// 14-member entity (INSERT…RETURNING killed the dead <c>{overrideId}</c> fallback) — asserted
/// against the EXACT key set, both directions.</para>
///
/// <para><b>init.sql seed respect (an explicit TASK constraint):</b> the 4 seeded overrides
/// (AC × OK24/OK26 × DEPARTMENT_HEAD/RESEARCHER, <c>ON CONFLICT DO NOTHING</c>) are NEVER
/// mutated or deleted — every mutation drives its OWN row created through the REAL POST, under
/// its own <c>S118PO_*</c> agreement code on the test-seeded position <c>S118_POS</c> (a
/// <c>positions</c> INPUT row — the FK target; distinct from the seed rows' position codes on
/// the seed rows' keys, so the ACTIVE-unique partial index never collides). The list test
/// positively asserts all 4 seed rows are still present and ACTIVE.</para>
///
/// <para><b>Enum fidelity:</b> <c>status</c> {ACTIVE, INACTIVE} exercised on LIVE values —
/// ACTIVE on create/list, INACTIVE through the real deactivate transition. If-Match composed
/// AS THE FE DOES (create/by-id/deactivate ETags feed the next mutation).</para>
///
/// <para><b>Seed disjointness:</b> a FRESH testcontainer per test; ids disjoint from
/// <c>PositionOverrideConcurrencyTests</c> (<c>POS_*</c>/<c>CON_PO_*</c> on DEPARTMENT_HEAD)
/// and <c>PositionOverrideAtomicTests</c> (<c>FR_PO_*</c> on DEPARTMENT_HEAD). Matcher +
/// Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S118PositionOverrideSpecRuntimeTests : IAsyncLifetime
{
    private const string ActorId = "s118p_gadmin";
    private const string JwtOrg = "S118PM"; // JWT claim only — override audit rows are GLOBAL (no org FK)
    private const string OkVersion = "OKS118";
    private const string PositionCode = "S118_POS"; // test-seeded positions row (FK target)

    /// <summary>The ruling #1 anchor: the EXACT 14 camelCase members of
    /// <c>PositionOverrideResponse</c>.</summary>
    private static readonly string[] EntityKeys =
    {
        "overrideId", "agreementCode", "okVersion", "positionCode", "status", "version",
        "maxFlexBalance", "flexCarryoverMax", "normPeriodWeeks", "weeklyNormHours",
        "createdBy", "createdAt", "updatedAt", "description",
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

        // INPUT seed only: the FK target for this suite's own overrides. The 4 init.sql
        // override seeds are left untouched (asserted below).
        await ExecAsync(
            """
            INSERT INTO positions (position_code, display_label, agreement_code)
            VALUES ('S118_POS', 'S118 Testposition', 'AC')
            ON CONFLICT DO NOTHING
            """);

        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/admin/position-overrides (bare array; seed rows ride the walk).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The matcher walks EVERY element — the 4 init.sql seed rows (ACTIVE) + the
    /// fresh S118 row. The seed rows are positively asserted PRESENT and ACTIVE (the
    /// never-mutate-the-seeds constraint made observable).</summary>
    [Fact]
    public async Task List_Get200_BareArray_InitSqlSeedRowsPresentAndUntouched()
    {
        using var admin = Admin();
        var (overrideId, _, _) = await CreateAsync(admin, "S118PO_LST");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/position-overrides"),
            "/api/admin/position-overrides", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        var mine = FindRow(rows, "S118PO_LST", PositionCode);
        Assert.Equal(overrideId, mine.GetProperty("overrideId").GetGuid());
        Assert.Equal("ACTIVE", mine.GetProperty("status").GetString());
        Assert.Equal(120.5m, mine.GetProperty("maxFlexBalance").GetDecimal()); // decimal fidelity
        Assert.Equal(1L, mine.GetProperty("version").GetInt64());

        // The 4 init.sql seeds — present, ACTIVE, untouched.
        foreach (var (seedOk, seedPosition) in new[]
                 { ("OK24", "DEPARTMENT_HEAD"), ("OK26", "DEPARTMENT_HEAD"), ("OK24", "RESEARCHER"), ("OK26", "RESEARCHER") })
        {
            var seed = FindSeedRow(rows, "AC", seedOk, seedPosition);
            Assert.Equal("ACTIVE", seed.GetProperty("status").GetString());
            Assert.Equal("SYSTEM_SEED", seed.GetProperty("createdBy").GetString());
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — POST /api/admin/position-overrides (201) — RULING #1 PIN.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>RULING #1: the create 201 is ALWAYS the full 14-member entity — never the dead
    /// <c>{overrideId}</c> fallback. Exact-201 + matcher + the EXACT key set; version 1 off
    /// RETURNING; nullable members served null-but-PRESENT.</summary>
    [Fact]
    public async Task Create_Post201Exact_RulingOnePin_Always14MemberFullEntity()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides", CreateJson("S118PO_CRT", weeklyNormHours: "null")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/position-overrides", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/admin/position-overrides (201)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, EntityKeys, "position-override create 201 (ruling #1)");
        Assert.Equal("S118PO_CRT", root.GetProperty("agreementCode").GetString());
        Assert.Equal(OkVersion, root.GetProperty("okVersion").GetString());
        Assert.Equal(PositionCode, root.GetProperty("positionCode").GetString());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());     // in the declared enum set, live
        Assert.Equal(1L, root.GetProperty("version").GetInt64());
        Assert.Equal(120.5m, root.GetProperty("maxFlexBalance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("weeklyNormHours").ValueKind); // nullable-always-present
        Assert.Equal(ActorId, root.GetProperty("createdBy").GetString());
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ops 2+5 — by-id GET (200 + ETag) then PUT (200): the FE's exact If-Match flow.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Create → by-id GET (matcher-asserted; its ETag is the version source) → PUT
    /// with <c>If-Match: "&lt;version&gt;"</c> ⇒ version 2 on the same shared record.</summary>
    [Fact]
    public async Task ById_Get200_ThenPut200_IfMatchComposedFromTheByIdEtag()
    {
        using var admin = Admin();
        var (overrideId, _, _) = await CreateAsync(admin, "S118PO_RW");

        using var getResponse = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, $"/api/admin/position-overrides/{overrideId}"));
        var getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)getResponse.StatusCode);

        var getTruth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/admin/position-overrides/{overrideId}", "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, getTruth, 200, getBody,
            "GET /api/admin/position-overrides/{overrideId} (200)");
        S118ContractAssert.AssertExactKeySet(
            JsonDocument.Parse(getBody).RootElement, EntityKeys, "position-override by-id 200");
        var etagVersion = S118ContractAssert.EtagVersion(getResponse);
        Assert.Equal(1L, etagVersion);

        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
                $"/api/admin/position-overrides/{overrideId}",
                CreateJson("S118PO_RW", weeklyNormHours: "36.75"), ifMatchVersion: etagVersion),
            "/api/admin/position-overrides/{overrideId}", "put");

        var putRoot = JsonDocument.Parse(putBody).RootElement;
        S118ContractAssert.AssertExactKeySet(putRoot, EntityKeys, "position-override PUT 200");
        Assert.Equal(overrideId, putRoot.GetProperty("overrideId").GetGuid());
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());
        Assert.Equal(36.75m, putRoot.GetProperty("weeklyNormHours").GetDecimal()); // the populated nullable state
        Assert.Equal("ACTIVE", putRoot.GetProperty("status").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /api/admin/position-overrides/agreement/{agreementCode}/{okVersion}.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ByAgreement_Get200_BareArraySchemaMatchesRuntime()
    {
        using var admin = Admin();
        var (overrideId, _, _) = await CreateAsync(admin, "S118PO_AGR");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                $"/api/admin/position-overrides/agreement/S118PO_AGR/{OkVersion}"),
            "/api/admin/position-overrides/agreement/{agreementCode}/{okVersion}", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, rows.GetArrayLength()); // only this test's own key
        Assert.Equal(overrideId, rows[0].GetProperty("overrideId").GetGuid());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 6 — POST .../deactivate (200 envelope).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The deactivate envelope — EXACT keys {overrideId, status, deactivated};
    /// <c>status</c> = the REAL post-transition "INACTIVE" (the second enum value, live);
    /// If-Match from the create ETag.</summary>
    [Fact]
    public async Task Deactivate_Post200_ThreeKeyEnvelope_StatusInactiveLive()
    {
        using var admin = Admin();
        var (overrideId, etagVersion, _) = await CreateAsync(admin, "S118PO_DEA");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/admin/position-overrides/{overrideId}/deactivate",
                jsonBody: null, ifMatchVersion: etagVersion),
            "/api/admin/position-overrides/{overrideId}/deactivate", "post");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, new[] { "overrideId", "status", "deactivated" }, "deactivate 200");
        Assert.Equal(overrideId, root.GetProperty("overrideId").GetGuid());
        Assert.Equal("INACTIVE", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("deactivated").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 7 — POST .../activate (200 envelope) — the full lifecycle round-trip.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Create (v1) → deactivate (If-Match 1 ⇒ v2, ETag read off the response) →
    /// activate (If-Match 2) — every If-Match composed from the previous response's ETag, the
    /// FE flow. The activate envelope: EXACT keys {overrideId, status, activated}, status back
    /// to "ACTIVE".</summary>
    [Fact]
    public async Task Activate_Post200_ThreeKeyEnvelope_AfterRealDeactivateTransition()
    {
        using var admin = Admin();
        var (overrideId, createVersion, _) = await CreateAsync(admin, "S118PO_ACT");

        using var deactivated = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate",
            jsonBody: null, ifMatchVersion: createVersion));
        var deactivatedBody = await deactivated.Content.ReadAsStringAsync();
        if ((int)deactivated.StatusCode != 200)
            throw new XunitException($"Deactivate for {overrideId} returned {(int)deactivated.StatusCode}: {deactivatedBody}");
        var deactivatedVersion = S118ContractAssert.EtagVersion(deactivated);
        Assert.Equal(2L, deactivatedVersion);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/admin/position-overrides/{overrideId}/activate",
                jsonBody: null, ifMatchVersion: deactivatedVersion),
            "/api/admin/position-overrides/{overrideId}/activate", "post");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, new[] { "overrideId", "status", "activated" }, "activate 200");
        Assert.Equal(overrideId, root.GetProperty("overrideId").GetGuid());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("activated").GetBoolean());
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    private async Task<(Guid OverrideId, long EtagVersion, JsonElement Body)> CreateAsync(
        HttpClient client, string agreementCode)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides", CreateJson(agreementCode, weeklyNormHours: "null")));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Position-override create for {agreementCode} returned {(int)response.StatusCode}: {body}");
        var root = JsonDocument.Parse(body).RootElement.Clone();
        return (root.GetProperty("overrideId").GetGuid(), S118ContractAssert.EtagVersion(response), root);
    }

    private static JsonElement FindRow(JsonElement array, string agreementCode, string positionCode)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("agreementCode").GetString(), agreementCode, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("positionCode").GetString(), positionCode, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a row for ({agreementCode}, {positionCode}) in: {array.GetRawText()}");
    }

    private static JsonElement FindSeedRow(JsonElement array, string agreementCode, string okVersion, string positionCode)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("agreementCode").GetString(), agreementCode, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("okVersion").GetString(), okVersion, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("positionCode").GetString(), positionCode, StringComparison.Ordinal))
                return el;
        throw new XunitException(
            $"Expected the init.sql seed row ({agreementCode}, {okVersion}, {positionCode}) to still be present. Got: {array.GetRawText()}");
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── request bodies (invariant JSON) ───────────────────────────────

    /// <summary>Create/update body — the same 8-member request shape serves both verbs.
    /// <paramref name="weeklyNormHours"/> is passed as raw JSON ("null" or a literal) so both
    /// nullable states are exercised on the wire.</summary>
    private static string CreateJson(string agreementCode, string weeklyNormHours)
        => $$"""
           { "agreementCode": "{{agreementCode}}", "okVersion": "{{OkVersion}}",
             "positionCode": "{{PositionCode}}",
             "maxFlexBalance": 120.5, "flexCarryoverMax": 40.25, "normPeriodWeeks": 4,
             "weeklyNormHours": {{weeklyNormHours}}, "description": "S118 positionsundtagelse" }
           """;
}
