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
/// SPRINT-106 / TASK-10603 (Enhedsspor Phase 3a, ADR-038 D5) — the scoped units + people SEARCH read
/// (<c>GET /api/admin/search</c>) regression suite. The overlay returns TWO sections (units + people),
/// each row carrying the node's full PATH. The keystone (D5/P7): units carry NO scope — a unit is
/// searchable ONLY because its Organisation ∈ the actor's <c>GetAccessibleOrgsAsync</c> set (the SAME
/// admission the existing <c>users/search</c> + the forest read use); people are bounded by
/// <c>primary_org_id ∈</c> that set. A scoped HR gets NO cross-Organisation results.
///
/// <para>Isolated fixtures (disjoint from the init.sql baseline → exact assertions): a fresh MAO
/// <c>S6SMAO</c> with two SIBLING Organisations <c>S6SOA</c> / <c>S6SOB</c>. Units + people are named
/// with a SHARED token (<c>"Faelles"</c>) and DELIBERATELY DUPLICATED across the siblings — UB1's name
/// equals UA1's, and <c>sob_carl</c>'s display name shares the token — so the ONLY reason a sibling row
/// is absent for the scoped HR is SCOPE, not a name mismatch (the sharp D5 pin).</para>
///
/// <list type="number">
///   <item><b>D5 scope pin</b> — a LocalHR scoped to S6SOA sees S6SOA's units + people only; the
///     sibling S6SOB's same-named unit (UB1) AND person (sob_carl) are ABSENT. Paths are asserted.</item>
///   <item><b>GlobalAdmin spans orgs</b> — a GLOBAL actor sees BOTH siblings' units + people.</item>
///   <item><b>Empty query (mirror users/search)</b> — an empty <c>q</c> returns ALL in-scope rows
///     (still scope-bounded: the scoped HR's empty-query search excludes the sibling).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S106SearchReadTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "S6SMAO";
    private const string OrgA = "S6SOA"; // ORGANISATION A — the scoped HR's org
    private const string OrgB = "S6SOB"; // sibling ORGANISATION B — must NOT leak

    private const string OrgAName = "S6S Styrelse A";
    private const string OrgBName = "S6S Styrelse B";

    // Units: S6SOA: UA1 "Faelles Direktion" (top) → UA2 "Faelles Omrade" (child). S6SOB: UB1
    // "Faelles Direktion" (top) — SAME name as UA1 (the cross-org duplicate that must stay absent).
    private static readonly Guid UA1 = Guid.Parse("d6000000-0000-0000-0000-000000000001");
    private static readonly Guid UA2 = Guid.Parse("d6000000-0000-0000-0000-000000000002");
    private static readonly Guid UB1 = Guid.Parse("d6000000-0000-0000-0000-000000000003");

    private const string UA1Name = "Faelles Direktion";
    private const string UA2Name = "Faelles Omrade";
    private const string UB1Name = "Faelles Direktion"; // duplicate of UA1Name, in the sibling org

    // People. S6SOA: soa_anders (in UA2) + soa_home (org-homed). S6SOB: sob_carl (in UB1) — its
    // display name shares the "Faelles" token (the cross-org duplicate that must stay absent).
    private const string SoaAnders = "soa_anders";
    private const string SoaHome = "soa_home";
    private const string SobCarl = "sob_carl";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline tree)

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
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'S6S Ministerie', 'MAO',          NULL, '/S6SMAO/',          'AC', 'OK24'),
                (@oa,  @oaName,          'ORGANISATION', @mao, '/S6SMAO/S6SOA/',    'HK', 'OK24'),
                (@ob,  @obName,          'ORGANISATION', @mao, '/S6SMAO/S6SOB/',    'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mao", Mao);
            cmd.Parameters.AddWithValue("oa", OrgA);
            cmd.Parameters.AddWithValue("ob", OrgB);
            cmd.Parameters.AddWithValue("oaName", OrgAName);
            cmd.Parameters.AddWithValue("obName", OrgBName);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@ua1, @oa, NULL,  'direktion', @ua1Name),
                (@ua2, @oa, @ua1,  'omrade',    @ua2Name),
                (@ub1, @ob, NULL,  'direktion', @ub1Name)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("ua1", UA1);
            cmd.Parameters.AddWithValue("ua2", UA2);
            cmd.Parameters.AddWithValue("ub1", UB1);
            cmd.Parameters.AddWithValue("oa", OrgA);
            cmd.Parameters.AddWithValue("ob", OrgB);
            cmd.Parameters.AddWithValue("ua1Name", UA1Name);
            cmd.Parameters.AddWithValue("ua2Name", UA2Name);
            cmd.Parameters.AddWithValue("ub1Name", UB1Name);
            await cmd.ExecuteNonQueryAsync();
        }

        await InsertUserAsync(conn, SoaAnders, "Anders Faelles", OrgA, UA2, position: "Specialkonsulent");
        await InsertUserAsync(conn, SoaHome, "Bente Faelles", OrgA, null, position: null);
        await InsertUserAsync(conn, SobCarl, "Carl Faelles", OrgB, UB1, position: "Fuldmaegtig");
    }

    private static async Task InsertUserAsync(
        NpgsqlConnection conn, string userId, string displayName, string orgId, Guid? unitId, string? position)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES (@id, @id, '$2a$11$fake', @name, @id || '@test.dk', @org, @unit, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("name", displayName);
            cmd.Parameters.AddWithValue("org", orgId);
            cmd.Parameters.AddWithValue("unit", (object?)unitId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // A live employee_profiles row carrying the position (the search surfaces ep.position).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from)
            VALUES (@id, 1.000, @pos, '0001-01-01')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("pos", (object?)position ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        var users = new[] { SoaAnders, SoaHome, SobCarl };
        var orgs = new[] { OrgA, OrgB };
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM employee_profiles WHERE employee_id = ANY(@users);
            DELETE FROM users WHERE user_id = ANY(@users);
            UPDATE units SET parent_unit_id = NULL WHERE organisation_id = ANY(@orgs);
            DELETE FROM units WHERE organisation_id = ANY(@orgs);
            DELETE FROM organizations WHERE org_id = ANY(@orgs);
            DELETE FROM organizations WHERE org_id = @mao;
            """, conn);
        cmd.Parameters.AddWithValue("users", users);
        cmd.Parameters.AddWithValue("orgs", orgs);
        cmd.Parameters.AddWithValue("mao", Mao);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) D5 scope pin — a scoped HR sees own-org rows only; the sibling's duplicates are absent.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_ScopedHr_ReturnsOwnOrgUnitsAndPeople_NoSiblingResults()
    {
        var hr = LocalHrClient(OrgA); // covers S6SOA, NOT S6SOB
        var (units, people) = await SearchAsync(hr, "Faelles");

        // Units: UA1 + UA2 present; UB1 (the same-named sibling unit) ABSENT — admitted solely by org.
        Assert.NotNull(FindUnit(units, UA1));
        Assert.NotNull(FindUnit(units, UA2));
        Assert.Null(FindUnit(units, UB1));

        // Paths: UA1 (top-level) = [OrgAName]; UA2 = [OrgAName, UA1Name] (org + parent unit, excl. self).
        Assert.Equal(new[] { OrgAName }, PathOf(FindUnit(units, UA1)!.Value));
        Assert.Equal(new[] { OrgAName, UA1Name }, PathOf(FindUnit(units, UA2)!.Value));

        // People: soa_anders + soa_home present; sob_carl (the sibling, shared token) ABSENT.
        Assert.NotNull(FindPerson(people, SoaAnders));
        Assert.NotNull(FindPerson(people, SoaHome));
        Assert.Null(FindPerson(people, SobCarl));

        // soa_anders is in UA2: unitName = UA2Name; path = [OrgAName, UA1Name, UA2Name] (down to the unit).
        var anders = FindPerson(people, SoaAnders)!.Value;
        Assert.Equal(UA2Name, anders.GetProperty("unitName").GetString());
        Assert.Equal("Specialkonsulent", anders.GetProperty("position").GetString());
        Assert.Equal(new[] { OrgAName, UA1Name, UA2Name }, PathOf(anders));

        // soa_home is org-homed: unitName = null; position = null; path = [OrgAName].
        var home = FindPerson(people, SoaHome)!.Value;
        Assert.Equal(JsonValueKind.Null, home.GetProperty("unitName").ValueKind);
        Assert.Equal(JsonValueKind.Null, home.GetProperty("position").ValueKind);
        Assert.Equal(new[] { OrgAName }, PathOf(home));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) GlobalAdmin spans orgs.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_GlobalAdmin_SpansBothSiblingOrgs()
    {
        var admin = GlobalAdminClient();
        var (units, people) = await SearchAsync(admin, "Faelles");

        // BOTH siblings' units + people are visible to the unrestricted actor.
        Assert.NotNull(FindUnit(units, UA1));
        Assert.NotNull(FindUnit(units, UA2));
        Assert.NotNull(FindUnit(units, UB1)); // the sibling unit the scoped HR could not see

        Assert.NotNull(FindPerson(people, SoaAnders));
        Assert.NotNull(FindPerson(people, SobCarl)); // the sibling person the scoped HR could not see

        // UB1's path roots at its OWN Organisation (OrgB) — no cross-org name bleed.
        Assert.Equal(new[] { OrgBName }, PathOf(FindUnit(units, UB1)!.Value));
        Assert.Equal(new[] { OrgBName, UB1Name }, PathOf(FindPerson(people, SobCarl)!.Value));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) Empty query (mirror users/search) — all in-scope rows; still scope-bounded.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_EmptyQuery_ScopedHr_ReturnsAllInScope_StillExcludesSibling()
    {
        var hr = LocalHrClient(OrgA);
        var (units, people) = await SearchAsync(hr, ""); // empty q matches all in-scope (users/search parity)

        Assert.NotEmpty(units.EnumerateArray());
        Assert.NotEmpty(people.EnumerateArray());

        // In-scope rows are present...
        Assert.NotNull(FindUnit(units, UA1));
        Assert.NotNull(FindPerson(people, SoaAnders));

        // ...and the sibling org's rows are STILL absent on the empty-query path (scope is not bypassed).
        Assert.Null(FindUnit(units, UB1));
        Assert.Null(FindPerson(people, SobCarl));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<(JsonElement Units, JsonElement People)> SearchAsync(HttpClient client, string q)
    {
        var rsp = await client.GetAsync($"/api/admin/search?q={Uri.EscapeDataString(q)}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        return (body.GetProperty("units"), body.GetProperty("people"));
    }

    private static JsonElement? FindUnit(JsonElement units, Guid unitId)
    {
        foreach (var u in units.EnumerateArray())
            if (string.Equals(u.GetProperty("unitId").GetString(), unitId.ToString(), StringComparison.OrdinalIgnoreCase))
                return u;
        return null;
    }

    private static JsonElement? FindPerson(JsonElement people, string userId)
    {
        foreach (var p in people.EnumerateArray())
            if (string.Equals(p.GetProperty("userId").GetString(), userId, StringComparison.Ordinal))
                return p;
        return null;
    }

    private static string[] PathOf(JsonElement node) =>
        node.GetProperty("path").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t106s_gadmin", name: "t106s_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Mao,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient LocalHrClient(string scopeOrg)
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t106s_hr", name: "t106s_hr", role: StatsTidRoles.LocalHR,
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
