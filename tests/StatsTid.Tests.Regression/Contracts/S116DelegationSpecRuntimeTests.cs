using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S116 / TASK-11603 — the per-route spec≡runtime gate extended to the self-service DELEGATION
/// trio drained in Pass 3 (3 ops on <c>/api/reporting-lines/delegate</c>): the GET proven on BOTH
/// return branches (the INACTIVE branch — <c>active: false</c> with the null-valued scalars and an
/// EMPTY <c>delegatedEmployees</c> — and the ACTIVE branch — all five members populated incl. the
/// nested <c>{employeeId, displayName}</c> element; ONE record, a STABLE key set, null-vs-populated
/// and NEVER polymorphic), the POST's 5-field creation receipt, and the DELETE's GENUINE
/// 200-with-body <c>{revokedCount}</c> (NOT a 204 — the S115 DELETE-vikar precedent).
///
/// <para><b>Dedicated-row seeding (the S115 ordering guarantee):</b> each scenario gets its OWN
/// Organisation + leader + rows, so the DELETE's revoke can never invalidate the GET-active
/// assertion's row and the POST's created vikar can never collide with a sibling's 409
/// active-delegation guard. Seeds are DISJOINT from the three named approval suites
/// (STY02/STY05, s78_*/s94_*/EMP_FR_AP_*). Matcher + Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S116DelegationSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "S116DMAO";
    private const string InactiveOrg = "S116D01";  // GET, inactive branch (no vikar row)
    private const string ActiveOrg = "S116D02";    // GET, active branch (pre-seeded vikar)
    private const string CreateOrg = "S116D03";    // POST (virgin leader + eligible vikar)
    private const string RevokeOrg = "S116D04";    // DELETE (pre-seeded vikar to revoke)

    // Pre-seeded manager_vikar rows (fixed ids; version=1 schema default).
    private static readonly Guid ActiveVikarId = Guid.Parse("51160000-0000-0000-0000-00000000d201");
    private static readonly Guid RevokeVikarId = Guid.Parse("51160000-0000-0000-0000-00000000d401");

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline org tree)

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);

        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  GET — BOTH branches of the ONE stable-key-set record.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The INACTIVE branch: no active self-delegation ⇒ <c>active: false</c> and — the
    /// stable-key-set contract — the actingManagerId/effectiveFrom/effectiveTo KEYS are PRESENT
    /// with null VALUES, and delegatedEmployees is an EMPTY array (never absent, never null).</summary>
    [Fact]
    public async Task DelegateGet_InactiveBranch_StableKeySetWithNulls_SchemaMatchesRuntime()
    {
        using var leader = CreateLeaderClient("s116d_l1", InactiveOrg);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, leader,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/reporting-lines/delegate"),
            "/api/reporting-lines/delegate", "get");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.False(root.GetProperty("active").GetBoolean());
        // The KEYS must be present with null values — that is the stable-key-set contract
        // (GetProperty throws on an ABSENT key; ValueKind pins the null VALUE).
        Assert.Equal(JsonValueKind.Null, root.GetProperty("actingManagerId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("effectiveFrom").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("effectiveTo").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("delegatedEmployees").ValueKind);
        Assert.Equal(0, root.GetProperty("delegatedEmployees").GetArrayLength());
    }

    /// <summary>The ACTIVE branch: the SAME record populated — actingManagerId/effectiveFrom/
    /// effectiveTo non-null and the nested <c>{employeeId, displayName}</c> element exercised
    /// against the seeded report.</summary>
    [Fact]
    public async Task DelegateGet_ActiveBranch_PopulatedWithNestedElement_SchemaMatchesRuntime()
    {
        using var leader = CreateLeaderClient("s116d_l2", ActiveOrg);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, leader,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/reporting-lines/delegate"),
            "/api/reporting-lines/delegate", "get");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.True(root.GetProperty("active").GetBoolean());
        Assert.Equal("s116d_v2", root.GetProperty("actingManagerId").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("effectiveFrom").ValueKind);
        Assert.Equal("2030-12-31", root.GetProperty("effectiveTo").GetString());

        var delegated = root.GetProperty("delegatedEmployees");
        Assert.Equal(1, delegated.GetArrayLength());
        Assert.Equal("s116d_e2", delegated[0].GetProperty("employeeId").GetString());
        Assert.Equal("S116 Delegeret Rapport", delegated[0].GetProperty("displayName").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  POST — the 5-field creation receipt.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DelegatePost_200_FiveFieldReceiptSchemaMatchesRuntime()
    {
        using var leader = CreateLeaderClient("s116d_l3", CreateOrg);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, leader,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/reporting-lines/delegate",
                """{ "actingManagerId": "s116d_v3", "effectiveTo": "2030-12-31" }"""),
            "/api/reporting-lines/delegate", "post");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, root.GetProperty("delegatedCount").GetInt32());   // the one covered report
        Assert.Equal(0, root.GetProperty("skippedCount").GetInt32());
        Assert.Equal("s116d_v3", root.GetProperty("actingManagerId").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("effectiveFrom").ValueKind);
        Assert.Equal("2030-12-31", root.GetProperty("effectiveTo").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  DELETE — a GENUINE 200-with-body {revokedCount}, NOT a 204.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DelegateDelete_Genuine200WithBody_RevokedCountSchemaMatchesRuntime()
    {
        using var leader = CreateLeaderClient("s116d_l4", RevokeOrg);
        using var response = await leader.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, "/api/reporting-lines/delegate"));
        var body = await response.Content.ReadAsStringAsync();

        // The status pin FIRST: a 200 WITH a body — the exact axis a mis-declared 204 would lie on.
        Assert.Equal(200, (int)response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body), "DELETE /delegate must carry the {revokedCount} body — it is NOT a 204.");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/reporting-lines/delegate", "delete");
        Assert.Equal(200, truth.StatusCode); // the committed contract declares 200 (not 204)
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "DELETE /api/reporting-lines/delegate");

        // revokedCount = the one covered report at revoke time.
        Assert.Equal(1, JsonDocument.Parse(body).RootElement.GetProperty("revokedCount").GetInt32());
    }

    // ── Support ──

    /// <summary>A LeaderOrAbove client for the given self-service actor (the delegate trio is a
    /// SELF-service surface — the actor IS the delegating manager; a GlobalAdmin client would
    /// exercise the wrong actor). Mirrors the Support helper's JWT minting; Support consumed AS-IS.</summary>
    private HttpClient CreateLeaderClient(string actorId, string orgId)
    {
        var client = _factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Fixture seed — one Organisation per scenario; FRESH ids, disjoint from the three named
    //    approval suites (STY02/STY05 + s78_*/s94_*/EMP_FR_AP_*). ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S116DMAO', 'S116 Delegation Ministerie', 'MAO',          NULL,       '/S116DMAO/',          'AC', 'OK24'),
                ('S116D01',  'S116 Uden-delegation',       'ORGANISATION', 'S116DMAO', '/S116DMAO/S116D01/',  'HK', 'OK24'),
                ('S116D02',  'S116 Med-delegation',        'ORGANISATION', 'S116DMAO', '/S116DMAO/S116D02/',  'HK', 'OK24'),
                ('S116D03',  'S116 Opret-delegation',      'ORGANISATION', 'S116DMAO', '/S116DMAO/S116D03/',  'HK', 'OK24'),
                ('S116D04',  'S116 Tilbagekald-delegation','ORGANISATION', 'S116DMAO', '/S116DMAO/S116D04/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s116d_l1', 's116d_l1', '$2a$11$fake', 'S116 Vikarløs Leder',      's116d_l1@test.dk', 'S116D01', 'HK', 'OK24', TRUE),
                ('s116d_l2', 's116d_l2', '$2a$11$fake', 'S116 Delegerende Leder',   's116d_l2@test.dk', 'S116D02', 'HK', 'OK24', TRUE),
                ('s116d_v2', 's116d_v2', '$2a$11$fake', 'S116 Aktiv Vikar',         's116d_v2@test.dk', 'S116D02', 'HK', 'OK24', TRUE),
                ('s116d_e2', 's116d_e2', '$2a$11$fake', 'S116 Delegeret Rapport',   's116d_e2@test.dk', 'S116D02', 'HK', 'OK24', TRUE),
                ('s116d_l3', 's116d_l3', '$2a$11$fake', 'S116 Opret Leder',         's116d_l3@test.dk', 'S116D03', 'HK', 'OK24', TRUE),
                ('s116d_v3', 's116d_v3', '$2a$11$fake', 'S116 Opret Vikar',         's116d_v3@test.dk', 'S116D03', 'HK', 'OK24', TRUE),
                ('s116d_e3', 's116d_e3', '$2a$11$fake', 'S116 Opret Rapport',       's116d_e3@test.dk', 'S116D03', 'HK', 'OK24', TRUE),
                ('s116d_l4', 's116d_l4', '$2a$11$fake', 'S116 Tilbagekald Leder',   's116d_l4@test.dk', 'S116D04', 'HK', 'OK24', TRUE),
                ('s116d_v4', 's116d_v4', '$2a$11$fake', 'S116 Tilbagekald Vikar',   's116d_v4@test.dk', 'S116D04', 'HK', 'OK24', TRUE),
                ('s116d_e4', 's116d_e4', '$2a$11$fake', 'S116 Tilbagekald Rapport', 's116d_e4@test.dk', 'S116D04', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Each delegating leader's PRIMARY report (the coverage census + the delegatedEmployees /
        // revokedCount derivations all read these).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship, effective_from, source, created_by) VALUES
                ('s116d_e2', 's116d_l2', 'S116D02', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s116d_e3', 's116d_l3', 'S116D03', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s116d_e4', 's116d_l4', 'S116D04', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Pre-seeded ACTIVE self-delegations: the GET's active branch + the DELETE's own row.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (vikar_id, absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by) VALUES
                (@activeId, 's116d_l2', 's116d_v2', '2030-12-31', 'FERIE',  'S116D02', 'seed'),
                (@revokeId, 's116d_l4', 's116d_v4', '2030-12-31', 'SYGDOM', 'S116D04', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("activeId", ActiveVikarId);
            cmd.Parameters.AddWithValue("revokeId", RevokeVikarId);
            await cmd.ExecuteNonQueryAsync();
        }

        // The POST's vikar candidate must hold a qualifying LocalLeader+ role whose ORG_ONLY scope
        // covers the delegating leader's report (exact-equals on the report's Organisation).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                ('s116d_v3', 'LOCAL_LEADER', 'S116D03', 'ORG_ONLY', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
