using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// SPRINT-106 / TASK-10601 (Enhedsspor Phase 3a, ADR-038 D1/D5) — the unified scoped FOREST read
/// (<c>GET /api/admin/units/forest</c>) regression suite. The forest MERGES <c>organizations</c>
/// (MAO + Organisation) with <c>units</c> (direktion…enhed) for DISPLAY only; the keystone (D5/P7) is
/// that units carry NO scope — a unit node is admitted SOLELY because its parent Organisation ∈ the
/// actor's <c>GetAccessibleOrgsAsync</c> set, with NO per-unit predicate and NO descendant/sibling/
/// count widening.
///
/// <para>Isolated fixtures: a fresh MAO <c>S6MAO</c> with two SIBLING Organisations <c>S6OA</c> /
/// <c>S6OB</c> carrying DISTINCT unit/member counts (S6OA = 3 active: a 2-level unit chain UA1→UA2 +
/// one org-homed; S6OB = 5 active in UB1) — disjoint from the init.sql baseline so the count
/// assertions are exact. Three [Fact]s:</para>
/// <list type="number">
///   <item><b>D5 scope non-leakage (RED test)</b> — a LocalHR scoped to S6OA ONLY sees the S6MAO
///     read-only context + S6OA's units AND counts only; S6OB's node AND its 5 members are ABSENT,
///     and the S6MAO total is S6OA's 3 (NOT 3+5). RED on any per-unit/descendant/count widening.</item>
///   <item><b>Count reconciliation</b> — S6OA's node count == Σ(its top-level units' rolled-up
///     counts) + the org-homed NULL users == the independent <c>COUNT(primary_org_id)</c> (the S98
///     identity).</item>
///   <item><b>GlobalAdmin sees all</b> — a GLOBAL actor sees BOTH siblings (S6OA + S6OB) under S6MAO
///     with the full counts, plus a baseline MAO (MIN01) the scoped HR could not see.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S106ForestReadTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // ── Isolated org fixtures (disjoint from the init.sql baseline → exact counts). ──
    private const string Mao = "S6MAO";   // a fresh MAO (root)
    private const string OrgA = "S6OA";   // ORGANISATION under S6MAO — 3 active members
    private const string OrgB = "S6OB";   // sibling ORGANISATION under S6MAO — 5 active members
    private const string BaselineMao = "MIN01"; // an init.sql MAO the scoped HR cannot see

    // Units: a 2-level chain under S6OA + a single top-level unit under S6OB.
    private static readonly Guid UA1 = Guid.Parse("c6000000-0000-0000-0000-000000000001"); // direktion, top (S6OA)
    private static readonly Guid UA2 = Guid.Parse("c6000000-0000-0000-0000-000000000002"); // omrade, child of UA1
    private static readonly Guid UB1 = Guid.Parse("c6000000-0000-0000-0000-000000000003"); // direktion, top (S6OB)

    // Users (all is_active TRUE). S6OA: 2 unit members + 1 org-homed = 3. S6OB: 5 in UB1.
    private static readonly string[] OrgAUsers = { "s6_a_ua1", "s6_a_ua2", "s6_a_home" };
    private static readonly string[] OrgBUsers = { "s6_b1", "s6_b2", "s6_b3", "s6_b4", "s6_b5" };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline MAO→Organisation tree + the demo unit tree)

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await CleanupAsync(conn);
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await CleanupAsync(conn);
        }
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        // Orgs: MAO S6MAO → ORGANISATIONs S6OA / S6OB.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'S6 Ministerie',   'MAO',          NULL, '/S6MAO/',       'AC', 'OK24'),
                (@oa,  'S6 Styrelse A',    'ORGANISATION', @mao, '/S6MAO/S6OA/',  'HK', 'OK24'),
                (@ob,  'S6 Styrelse B',    'ORGANISATION', @mao, '/S6MAO/S6OB/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mao", Mao);
            cmd.Parameters.AddWithValue("oa", OrgA);
            cmd.Parameters.AddWithValue("ob", OrgB);
            await cmd.ExecuteNonQueryAsync();
        }

        // Units: UA1 (direktion, top) → UA2 (omrade) under S6OA; UB1 (direktion, top) under S6OB.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@ua1, @oa, NULL,  'direktion', 'S6 A Direktion'),
                (@ua2, @oa, @ua1,  'omrade',    'S6 A Omrade'),
                (@ub1, @ob, NULL,  'direktion', 'S6 B Direktion')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("ua1", UA1);
            cmd.Parameters.AddWithValue("ua2", UA2);
            cmd.Parameters.AddWithValue("ub1", UB1);
            cmd.Parameters.AddWithValue("oa", OrgA);
            cmd.Parameters.AddWithValue("ob", OrgB);
            await cmd.ExecuteNonQueryAsync();
        }

        // S6OA members: one in UA1, one in UA2, one org-homed (unit_id NULL). All primary_org_id = S6OA.
        await InsertUserAsync(conn, "s6_a_ua1", OrgA, UA1);
        await InsertUserAsync(conn, "s6_a_ua2", OrgA, UA2);
        await InsertUserAsync(conn, "s6_a_home", OrgA, null);

        // S6OB members: 5 in UB1. All primary_org_id = S6OB.
        foreach (var u in OrgBUsers)
            await InsertUserAsync(conn, u, OrgB, UB1);
    }

    private static async Task InsertUserAsync(NpgsqlConnection conn, string userId, string orgId, Guid? unitId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES (@id, @id, '$2a$11$fake', @id, @id || '@test.dk', @org, @unit, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("unit", (object?)unitId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        var allUsers = OrgAUsers.Concat(OrgBUsers).ToArray();
        var orgs = new[] { OrgA, OrgB };

        await using (var cmd = new NpgsqlCommand(
            """
            DELETE FROM unit_leaders WHERE user_id = ANY(@users)
              OR unit_id IN (SELECT unit_id FROM units WHERE organisation_id = ANY(@orgs));
            DELETE FROM users WHERE user_id = ANY(@users);
            UPDATE units SET parent_unit_id = NULL WHERE organisation_id = ANY(@orgs);
            DELETE FROM units WHERE organisation_id = ANY(@orgs);
            DELETE FROM organizations WHERE org_id = ANY(@orgs);
            DELETE FROM organizations WHERE org_id = @mao;
            """, conn))
        {
            cmd.Parameters.AddWithValue("users", allUsers);
            cmd.Parameters.AddWithValue("orgs", orgs);
            cmd.Parameters.AddWithValue("mao", Mao);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) D5 scope non-leakage — the RED test (count + node).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A LocalHR scoped to S6OA ONLY sees the S6MAO read-only context + S6OA's units AND
    /// counts only. The sibling S6OB's node AND its 5 members are ABSENT, and the S6MAO total is
    /// S6OA's 3 — NOT 3+5. RED on any per-unit/descendant/count widening (a unit-level visibility
    /// join or a global count rollup would leak S6OB through the MAO total / a stray node).</summary>
    [Fact]
    public async Task Forest_ScopedHr_SeesOwnOrgAndMaoContextOnly_NoSiblingNodesOrCounts()
    {
        var hr = LocalHrClient(OrgA); // covers S6OA, NOT S6OB
        var forest = await GetForestAsync(hr);

        // Exactly ONE MAO visible — S6MAO (the only MAO with a visible child Organisation). No baseline
        // MAO leaks in (a scoped HR sees the MAO header only for the Organisations it can reach).
        var maos = forest.EnumerateArray().ToList();
        Assert.Single(maos);
        var s6mao = FindMao(forest, Mao) ?? throw new XunitException("S6MAO context node absent for the S6OA-scoped HR.");

        // S6OA is present; S6OB (the sibling) is ABSENT — admission is solely via the accessible-org set.
        var orgs = s6mao.GetProperty("organisations");
        Assert.Single(orgs.EnumerateArray());
        var orgANode = FindOrg(s6mao, OrgA) ?? throw new XunitException("S6OA node absent for the S6OA-scoped HR.");
        Assert.Null(FindOrg(s6mao, OrgB));

        // S6OB's unit (UB1) appears NOWHERE in the visible forest (no descendant/sibling widening).
        Assert.Null(FindUnit(forest, UB1));

        // Counts: S6OA's node total is 3 (2 unit members + 1 org-homed); the S6MAO total is 3 — NOT
        // 3+5. A global/sibling count rollup would push the MAO total to 8 (RED).
        Assert.Equal(3, orgANode.GetProperty("memberCount").GetInt64());
        Assert.Equal(3, s6mao.GetProperty("memberCount").GetInt64());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) Count reconciliation — Org count == Σ unit rollups + homed-NULL == COUNT(primary_org).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>S6OA's node count reconciles: the rolled-up unit counts + the org-homed NULL users ==
    /// the node's <c>memberCount</c> == the independent <c>COUNT(primary_org_id)</c> (the S98 identity).
    /// Also pins the derived <c>level</c> (UA1 = 1, the child UA2 = 2) and the unit roll-up
    /// (UA1.memberCount = its 1 direct + UA2's 1 = 2; UA2.memberCount = 1).</summary>
    [Fact]
    public async Task Forest_OrgCount_ReconcilesToUnitRollupsPlusHomedNull_AndToPrimaryOrgCount()
    {
        var admin = GlobalAdminClient();
        var forest = await GetForestAsync(admin);

        var s6mao = FindMao(forest, Mao) ?? throw new XunitException("S6MAO absent for GlobalAdmin.");
        var orgA = FindOrg(s6mao, OrgA) ?? throw new XunitException("S6OA absent for GlobalAdmin.");

        var orgCount = orgA.GetProperty("memberCount").GetInt64();
        var directOrgHomed = orgA.GetProperty("directMemberCount").GetInt64();
        var topUnits = orgA.GetProperty("units").EnumerateArray().ToList();
        var unitRollup = topUnits.Sum(u => u.GetProperty("memberCount").GetInt64());

        // Identity 1: org count == Σ top-level unit rollups + org-homed-NULL.
        Assert.Equal(orgCount, unitRollup + directOrgHomed);
        Assert.Equal(1, directOrgHomed);  // s6_a_home

        // Identity 2: org count == the independent COUNT(primary_org_id) (the S98 employeeCount).
        Assert.Equal(await CountActiveByPrimaryOrgAsync(OrgA), orgCount);
        Assert.Equal(3, orgCount);

        // Unit roll-up + derived level: UA1 (level 1) rolls up UA2 (level 2).
        var ua1 = FindUnit(forest, UA1) ?? throw new XunitException("UA1 absent.");
        var ua2 = FindUnit(forest, UA2) ?? throw new XunitException("UA2 absent.");
        Assert.Equal(1, ua1.GetProperty("level").GetInt32());
        Assert.Equal(2, ua2.GetProperty("level").GetInt32());
        Assert.Equal(1, ua2.GetProperty("directMemberCount").GetInt64());
        Assert.Equal(1, ua2.GetProperty("memberCount").GetInt64());
        Assert.Equal(1, ua1.GetProperty("directMemberCount").GetInt64());
        Assert.Equal(2, ua1.GetProperty("memberCount").GetInt64()); // 1 direct + UA2's 1
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) GlobalAdmin sees all.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A GLOBAL actor sees BOTH siblings (S6OA + S6OB) under S6MAO with full counts, plus a
    /// baseline MAO (MIN01) the scoped HR could never see — confirming the unrestricted reach.</summary>
    [Fact]
    public async Task Forest_GlobalAdmin_SeesBothSiblingsAndBaselineMao()
    {
        var admin = GlobalAdminClient();
        var forest = await GetForestAsync(admin);

        var s6mao = FindMao(forest, Mao) ?? throw new XunitException("S6MAO absent for GlobalAdmin.");
        var orgA = FindOrg(s6mao, OrgA) ?? throw new XunitException("S6OA absent for GlobalAdmin.");
        var orgB = FindOrg(s6mao, OrgB) ?? throw new XunitException("S6OB absent for GlobalAdmin.");

        Assert.Equal(3, orgA.GetProperty("memberCount").GetInt64());
        Assert.Equal(5, orgB.GetProperty("memberCount").GetInt64());
        // S6OB's 5 are all in UB1 (no org-homed); the unit rollup carries them.
        Assert.Equal(0, orgB.GetProperty("directMemberCount").GetInt64());
        var ub1 = FindUnit(forest, UB1) ?? throw new XunitException("UB1 absent for GlobalAdmin.");
        Assert.Equal(5, ub1.GetProperty("memberCount").GetInt64());

        // The MAO total sums both siblings (3 + 5 = 8) for the unrestricted actor.
        Assert.Equal(8, s6mao.GetProperty("memberCount").GetInt64());

        // A baseline MAO (MIN01) the S6OA-scoped HR cannot see IS visible to GlobalAdmin.
        Assert.NotNull(FindMao(forest, BaselineMao));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<JsonElement> GetForestAsync(HttpClient client)
    {
        var rsp = await client.GetAsync("/api/admin/units/forest");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        return body.GetProperty("forest");
    }

    private static JsonElement? FindMao(JsonElement forest, string orgId)
    {
        foreach (var mao in forest.EnumerateArray())
            if (string.Equals(mao.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                return mao;
        return null;
    }

    private static JsonElement? FindOrg(JsonElement maoNode, string orgId)
    {
        foreach (var org in maoNode.GetProperty("organisations").EnumerateArray())
            if (string.Equals(org.GetProperty("orgId").GetString(), orgId, StringComparison.Ordinal))
                return org;
        return null;
    }

    /// <summary>Depth-first search for a unit node by id across every MAO→Organisation→unit subtree.</summary>
    private static JsonElement? FindUnit(JsonElement forest, Guid unitId)
    {
        foreach (var mao in forest.EnumerateArray())
            foreach (var org in mao.GetProperty("organisations").EnumerateArray())
                foreach (var unit in org.GetProperty("units").EnumerateArray())
                {
                    var hit = FindUnitRec(unit, unitId);
                    if (hit is not null) return hit;
                }
        return null;
    }

    private static JsonElement? FindUnitRec(JsonElement unit, Guid unitId)
    {
        if (string.Equals(unit.GetProperty("unitId").GetString(), unitId.ToString(), StringComparison.OrdinalIgnoreCase))
            return unit;
        foreach (var child in unit.GetProperty("children").EnumerateArray())
        {
            var hit = FindUnitRec(child, unitId);
            if (hit is not null) return hit;
        }
        return null;
    }

    private async Task<long> CountActiveByPrimaryOrgAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE primary_org_id = @org AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("org", orgId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ── clients / tokens ──

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t106_gadmin", name: "t106_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: BaselineMao,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient LocalHrClient(string scopeOrg)
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t106_hr", name: "t106_hr", role: StatsTidRoles.LocalHR,
            agreementCode: "HK", orgId: scopeOrg,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, scopeOrg, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
