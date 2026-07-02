using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S112 / TASK-11204 — the per-route spec≡runtime gate extended to the ORG + USER + ROLE family of
/// the newly-typed merged-admin slice (11 ops): organizations POST (201) / PUT (200) / PUT move
/// (200) / DELETE (204); users POST (201) / PUT (200) / GET search (200 envelope) / GET {userId}
/// (200) / GET {userId}/roles (200 BARE ARRAY — the array-ness sentinel); roles grant POST (201) /
/// revoke POST (200). Each response is matched against its committed <c>docs/api/openapi.json</c>
/// DECLARED success contract via <see cref="SpecRuntimeMatcher"/> (status fidelity + structural
/// schema match; 204 = status + empty body; 201s match the 201 schema).
///
/// <para><b>Dedicated-row seeding (the ordering guarantee):</b> xUnit does NOT guarantee intra-class
/// test order, so every MUTATION acts on its OWN seeded row (distinct org-ids / user-ids /
/// assignment GUIDs) — the org DELETE can never invalidate the rename/move assertions' rows, the
/// revoke flips only ITS OWN pre-seeded assignment, the grant targets a user nothing else touches.
/// GETs ride read-only seeds no mutation touches. All versioned rows seed at version=1.</para>
///
/// <para><b>RED-on-lie proofs (mutation routes):</b> (1) the REAL bare-array
/// <c>/users/{userId}/roles</c> response matched against the OBJECT element schema (the
/// <c>.Produces&lt;UserRoleAssignmentItem&gt;</c> mistake) MUST throw; (2) the REAL 201 grant
/// response asserted against a spec-lying-200 contract MUST throw (the status lie).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S112AdminOrgUserRoleSpecRuntimeTests : IAsyncLifetime
{
    private const string Mao1 = "S112BM1";
    private const string Mao2 = "S112BM2";           // the org-move TARGET
    private const string HomeOrg = "S112BORG";       // home Organisation for every user seed
    private const string RenameOrg = "S112BREN";     // PUT rename target
    private const string MoveOrg = "S112BMOV";       // PUT move subject (M1 → M2)
    private const string DeleteOrg = "S112BDEL";     // DELETE target — stays EMPTY (no users)

    private const string PutUser = "s112b_put";
    private const string GetUser = "s112b_get";
    private const string RolesUser = "s112b_roles";   // read-only: carries the bare-array roles seed
    private const string GrantUser = "s112b_grant";
    private const string GrantUser2 = "s112b_grant2"; // dedicated row for the status-lie proof
    private const string RevokeUser = "s112b_revoke";
    private const string CreatedUser = "s112b_created";
    private const string SearchNeedle = "S112Søg";    // matches ONLY the two search seeds

    private static readonly Guid RevokeAssignmentId = Guid.Parse("b1120000-0000-0000-0000-00000000000a");

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
    //  Organizations (4 ops)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgCreate_Post201_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/organizations",
                $$"""{ "orgName": "S112 Ny Styrelse", "orgType": "ORGANISATION", "parentOrgId": "{{Mao1}}" }"""),
            "/api/admin/organizations", "post");

    [Fact]
    public async Task OrgUpdate_Put200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/organizations/{RenameOrg}",
                """{ "orgName": "S112 Omdøbt Styrelse" }"""),
            "/api/admin/organizations/{orgId}", "put");

    [Fact]
    public async Task OrgMove_Put200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/organizations/{MoveOrg}/move",
                $$"""{ "newParentOrgId": "{{Mao2}}" }"""),
            "/api/admin/organizations/{orgId}/move", "put");

    [Fact]
    public async Task OrgDelete_204_StatusAndEmptyBodyMatchRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, $"/api/admin/organizations/{DeleteOrg}"),
            "/api/admin/organizations/{orgId}", "delete");

    // ════════════════════════════════════════════════════════════════════════════════
    //  Users (5 ops)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UserCreate_Post201_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/users",
                $$"""
                {
                  "userId": "{{CreatedUser}}", "username": "{{CreatedUser}}", "password": "S112!TestPassw0rd",
                  "displayName": "S112 Oprettet Bruger", "email": "s112b_created@test.dk",
                  "primaryOrgId": "{{HomeOrg}}", "agreementCode": "HK", "okVersion": "OK24"
                }
                """),
            "/api/admin/users", "post");

    [Fact]
    public async Task UserUpdate_Put200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/users/{PutUser}",
                """{ "displayName": "S112 Opdateret Bruger" }""", ifMatchVersion: 1),
            "/api/admin/users/{userId}", "put");

    [Fact]
    public async Task UserSearch_Get200Envelope_SchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                $"/api/admin/users/search?q={Uri.EscapeDataString(SearchNeedle)}"),
            "/api/admin/users/search", "get");

        // The item schema must actually be EXERCISED — an empty page would green-wash it.
        var items = JsonDocument.Parse(body).RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1, "search seed must yield >= 1 item so the item schema is exercised");
    }

    [Fact]
    public async Task UserDetail_Get200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/users/{GetUser}"),
            "/api/admin/users/{userId}", "get");

    [Fact]
    public async Task UserRoles_Get200BareArray_SchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/users/{RolesUser}/roles"),
            "/api/admin/users/{userId}/roles", "get");

        // BARE ARRAY (the S97/S99 envelope-vs-array distinction) with >= 1 element so the
        // element schema is exercised.
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.True(root.GetArrayLength() >= 1, "roles seed must yield >= 1 assignment so the element schema is exercised");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Roles (2 ops)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RoleGrant_Post201_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/roles/grant",
                $$"""{ "userId": "{{GrantUser}}", "roleId": "LOCAL_LEADER", "orgId": "{{HomeOrg}}", "scopeType": "ORG_ONLY" }"""),
            "/api/admin/roles/grant", "post");

    [Fact]
    public async Task RoleRevoke_Post200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/roles/revoke",
                $$"""{ "assignmentId": "{{RevokeAssignmentId}}", "reason": "S112 spec-runtime gate" }"""),
            "/api/admin/roles/revoke", "post");

    // ════════════════════════════════════════════════════════════════════════════════
    //  RED-on-lie proofs — the gate must be RED on the exact lies it exists to catch,
    //  proven against REAL mutation-family responses.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The REAL bare-array roles response matched against the OBJECT element schema
    /// (the <c>.Produces&lt;UserRoleAssignmentItem&gt;</c> mistake) MUST throw.</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedArraynessLie_RolesBareArrayVsObjectItemSchema()
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s112b_gadmin", Mao1);
        using var response = await client.GetAsync($"/api/admin/users/{RolesUser}/roles");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)response.StatusCode);

        // The truth (the committed bare-array contract) passes…
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/users/{userId}/roles", "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "truth");

        // …the lie (the element OBJECT schema as the root) is RED.
        var elementObjectSchema = JsonDocument.Parse(
            """{ "$ref": "#/components/schemas/StatsTid.Backend.Api.Contracts.UserRoleAssignmentItem" }""").RootElement;
        var lie = new SpecRuntimeMatcher.SuccessContract(200, elementObjectSchema);
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(_spec, lie, 200, body, "injected-lie"));
        Assert.Contains("array-ness", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The REAL 201 grant response asserted against a spec-lying-200 contract MUST throw
    /// (the status lie a mis-declared <c>.Produces</c> status can tell) — on a DEDICATED grant row
    /// so it cannot collide with the grant assertion above.</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedStatusLie_Real201GrantVsSpecLying200()
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s112b_gadmin", Mao1);
        using var response = await client.PostAsync("/api/admin/roles/grant",
            new StringContent(
                $$"""{ "userId": "{{GrantUser2}}", "roleId": "EMPLOYEE", "orgId": "{{HomeOrg}}", "scopeType": "ORG_ONLY" }""",
                System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(201, (int)response.StatusCode);

        // The truth (the committed 201 contract) passes…
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/roles/grant", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "truth");

        // …the lie (the SAME schema but a declared-200 status) is RED on the real 201.
        var lie = new SpecRuntimeMatcher.SuccessContract(200, truth.Schema);
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(_spec, lie, 201, body, "injected-status-lie"));
        Assert.Contains("status", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> AssertOpAsync(HttpRequestMessage request, string specPath, string method)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s112b_gadmin", Mao1);
        return await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client, request, specPath, method);
    }

    // ── Fixture seed — dedicated orgs / users / assignments, one per assertion. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S112BM1',  'S112 Ministerie Et', 'MAO',          NULL,      '/S112BM1/',          'AC', 'OK24'),
                ('S112BM2',  'S112 Ministerie To', 'MAO',          NULL,      '/S112BM2/',          'AC', 'OK24'),
                ('S112BORG', 'S112 Hjemstyrelse',  'ORGANISATION', 'S112BM1', '/S112BM1/S112BORG/', 'HK', 'OK24'),
                ('S112BREN', 'S112 Omdøb-mig',     'ORGANISATION', 'S112BM1', '/S112BM1/S112BREN/', 'HK', 'OK24'),
                ('S112BMOV', 'S112 Flyt-mig',      'ORGANISATION', 'S112BM1', '/S112BM1/S112BMOV/', 'HK', 'OK24'),
                ('S112BDEL', 'S112 Slet-mig',      'ORGANISATION', 'S112BM1', '/S112BM1/S112BDEL/', 'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Dedicated users, ALL homed on S112BORG (S112BDEL must stay EMPTY for the delete's
        // blocked-if-employees guard). employment_category explicit — UserDetailResponse serves it
        // NON-nullable. users.version=1 schema default (deterministic If-Match "1").
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, is_active) VALUES
                (@put,     @put,     '$2a$11$fake', 'S112 Ret-mig Bruger',   's112b_put@test.dk',     @home, 'HK', 'OK24', 'Standard', TRUE),
                (@get,     @get,     '$2a$11$fake', 'S112 Læs-mig Bruger',   's112b_get@test.dk',     @home, 'HK', 'OK24', 'Standard', TRUE),
                (@roles,   @roles,   '$2a$11$fake', 'S112 Roller Bruger',    's112b_roles@test.dk',   @home, 'HK', 'OK24', 'Standard', TRUE),
                (@grant,   @grant,   '$2a$11$fake', 'S112 Tildel Bruger',    's112b_grant@test.dk',   @home, 'HK', 'OK24', 'Standard', TRUE),
                (@grant2,  @grant2,  '$2a$11$fake', 'S112 Tildel Bruger To', 's112b_grant2@test.dk',  @home, 'HK', 'OK24', 'Standard', TRUE),
                (@revoke,  @revoke,  '$2a$11$fake', 'S112 Fratag Bruger',    's112b_revoke@test.dk',  @home, 'HK', 'OK24', 'Standard', TRUE),
                (@search1, @search1, '$2a$11$fake', 'S112Søg Alfa',          's112b_search1@test.dk', @home, 'HK', 'OK24', 'Standard', TRUE),
                (@search2, @search2, '$2a$11$fake', 'S112Søg Beta',          's112b_search2@test.dk', @home, 'HK', 'OK24', 'Standard', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("put", PutUser);
            cmd.Parameters.AddWithValue("get", GetUser);
            cmd.Parameters.AddWithValue("roles", RolesUser);
            cmd.Parameters.AddWithValue("grant", GrantUser);
            cmd.Parameters.AddWithValue("grant2", GrantUser2);
            cmd.Parameters.AddWithValue("revoke", RevokeUser);
            cmd.Parameters.AddWithValue("search1", "s112b_search1");
            cmd.Parameters.AddWithValue("search2", "s112b_search2");
            cmd.Parameters.AddWithValue("home", HomeOrg);
            await cmd.ExecuteNonQueryAsync();
        }

        // (a) The roles-GET read seed: expires_at NON-null so the nullable DateTime scalar is
        //     exercised with a value. (b) The revoke's own assignment (explicit id — the revoke
        //     flips ONLY this row).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (assignment_id, user_id, role_id, org_id, scope_type, assigned_by, expires_at) VALUES
                (gen_random_uuid(), @roles,  'EMPLOYEE', @home, 'ORG_ONLY', 'TEST', '2099-12-31T00:00:00Z'),
                (@revokeId,         @revoke, 'EMPLOYEE', @home, 'ORG_ONLY', 'TEST', NULL)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("roles", RolesUser);
            cmd.Parameters.AddWithValue("revoke", RevokeUser);
            cmd.Parameters.AddWithValue("revokeId", RevokeAssignmentId);
            cmd.Parameters.AddWithValue("home", HomeOrg);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
