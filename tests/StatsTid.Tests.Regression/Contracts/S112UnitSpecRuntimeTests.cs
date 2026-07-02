using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S112 / TASK-11204 — the per-route spec≡runtime gate extended to the UNIT family of the
/// newly-typed merged-admin slice (6 ops): units rename PUT (200) / move PUT (200) / DELETE (204) /
/// leader-designate POST (200) / leader-remove DELETE (204) + the same-Organisation person
/// unit-assign PUT (200). Each operation's response is matched against its committed
/// <c>docs/api/openapi.json</c> DECLARED success contract via <see cref="SpecRuntimeMatcher"/>
/// (status fidelity + structural schema match; 204 = status + empty body).
///
/// <para><b>Dedicated-row seeding (the ordering guarantee):</b> xUnit does NOT guarantee intra-class
/// test order, so every MUTATION acts on its OWN seeded row (distinct fixed GUIDs/user-ids, one per
/// assertion) — a DELETE assertion can never invalidate a sibling assertion's row. All rows seed at
/// version=1 (schema default), so each If-Match is deterministic. ONE Docker fixture per FAMILY
/// (FAIL-002 discipline — few Docker classes, not 20).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S112UnitSpecRuntimeTests : IAsyncLifetime
{
    private const string Mao = "S112UMAO";
    private const string Org = "S112UORG";

    // One dedicated unit per assertion (xUnit order-independence — see class doc).
    private static readonly Guid RenameUnitId = Guid.Parse("51120000-0000-0000-0000-000000000001");
    private static readonly Guid MoveParentUnitId = Guid.Parse("51120000-0000-0000-0000-000000000002");
    private static readonly Guid MoveChildUnitId = Guid.Parse("51120000-0000-0000-0000-000000000003");
    private static readonly Guid MoveTargetUnitId = Guid.Parse("51120000-0000-0000-0000-000000000004");
    private static readonly Guid DeleteUnitId = Guid.Parse("51120000-0000-0000-0000-000000000005");
    private static readonly Guid LeaderUnitId = Guid.Parse("51120000-0000-0000-0000-000000000006");
    private static readonly Guid LeaderRemoveUnitId = Guid.Parse("51120000-0000-0000-0000-000000000007");
    private static readonly Guid AssignTargetUnitId = Guid.Parse("51120000-0000-0000-0000-000000000008");

    private const string LeadDesignee = "s112u_lead1";   // member of LeaderUnit (the D3 member-invariant)
    private const string LeadRemovee = "s112u_lead2";    // member + pre-seeded leader of LeaderRemoveUnit
    private const string Assignee = "s112u_assignee";    // homed directly at the Organisation (unit_id NULL)

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
    //  The per-route gate — each op's committed DECLARED success contract ≡ its real response.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnitRename_Put200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/units/{RenameUnitId}",
                """{ "name": "S112 Omdøbt Kontor" }""", ifMatchVersion: 1),
            "/api/admin/units/{id}", "put");

    [Fact]
    public async Task UnitMove_Put200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/units/{MoveChildUnitId}/move",
                $$"""{ "newParentUnitId": "{{MoveTargetUnitId}}" }""", ifMatchVersion: 1),
            "/api/admin/units/{id}/move", "put");

    [Fact]
    public async Task UnitDelete_204_StatusAndEmptyBodyMatchRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, $"/api/admin/units/{DeleteUnitId}",
                jsonBody: null, ifMatchVersion: 1),
            "/api/admin/units/{id}", "delete");

    [Fact]
    public async Task UnitLeaderDesignate_Post200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, $"/api/admin/units/{LeaderUnitId}/leaders",
                $$"""{ "userId": "{{LeadDesignee}}" }"""),
            "/api/admin/units/{id}/leaders", "post");

    [Fact]
    public async Task UnitLeaderRemove_204_StatusAndEmptyBodyMatchRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete,
                $"/api/admin/units/{LeaderRemoveUnitId}/leaders/{LeadRemovee}"),
            "/api/admin/units/{id}/leaders/{userId}", "delete");

    [Fact]
    public async Task UserUnitAssign_Put200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/users/{Assignee}/unit",
                $$"""{ "unitId": "{{AssignTargetUnitId}}" }""", ifMatchVersion: 1),
            "/api/admin/users/{userId}/unit", "put");

    private async Task AssertOpAsync(HttpRequestMessage request, string specPath, string method)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s112u_gadmin", Mao);
        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client, request, specPath, method);
    }

    // ── Fixture seed — one MAO + one Organisation + one DEDICATED unit/user per assertion. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'S112 Unit Ministerie', 'MAO',          NULL, '/S112UMAO/',           'AC', 'OK24'),
                (@org, 'S112 Unit Styrelse',  'ORGANISATION', @mao, '/S112UMAO/S112UORG/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mao", Mao);
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        // Dedicated units (version=1 schema default). Types respect the partial-rank child ordering
        // ('kontor'(3) under 'omrade'(2); top-level = any type).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@rename,      @org, NULL,    'kontor', 'S112 Rename-kontor'),
                (@moveParent,  @org, NULL,    'omrade', 'S112 Move-fra-område'),
                (@moveChild,   @org, @moveParent, 'kontor', 'S112 Move-kontor'),
                (@moveTarget,  @org, NULL,    'omrade', 'S112 Move-til-område'),
                (@delete,      @org, NULL,    'team',   'S112 Slet-team'),
                (@leader,      @org, NULL,    'kontor', 'S112 Leder-kontor'),
                (@leaderRm,    @org, NULL,    'kontor', 'S112 Leder-fjern-kontor'),
                (@assignTarget,@org, NULL,    'kontor', 'S112 Placerings-kontor')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("rename", RenameUnitId);
            cmd.Parameters.AddWithValue("moveParent", MoveParentUnitId);
            cmd.Parameters.AddWithValue("moveChild", MoveChildUnitId);
            cmd.Parameters.AddWithValue("moveTarget", MoveTargetUnitId);
            cmd.Parameters.AddWithValue("delete", DeleteUnitId);
            cmd.Parameters.AddWithValue("leader", LeaderUnitId);
            cmd.Parameters.AddWithValue("leaderRm", LeaderRemoveUnitId);
            cmd.Parameters.AddWithValue("assignTarget", AssignTargetUnitId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Dedicated users (users.version=1 schema default; If-Match "1" deterministic).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active) VALUES
                (@lead1,    @lead1,    '$2a$11$fake', 'S112 Lederkandidat', 's112u_lead1@test.dk',    @org, @leaderUnit,   'HK', 'OK24', TRUE),
                (@lead2,    @lead2,    '$2a$11$fake', 'S112 Afgående Leder','s112u_lead2@test.dk',    @org, @leaderRmUnit, 'HK', 'OK24', TRUE),
                (@assignee, @assignee, '$2a$11$fake', 'S112 Placeringsmand','s112u_assignee@test.dk', @org, NULL,          'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("lead1", LeadDesignee);
            cmd.Parameters.AddWithValue("lead2", LeadRemovee);
            cmd.Parameters.AddWithValue("assignee", Assignee);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("leaderUnit", LeaderUnitId);
            cmd.Parameters.AddWithValue("leaderRmUnit", LeaderRemoveUnitId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Pre-seeded designation for the REMOVE assertion (its own unit+user — never the designate pair).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO unit_leaders (unit_id, user_id) VALUES (@unit, @user)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("unit", LeaderRemoveUnitId);
            cmd.Parameters.AddWithValue("user", LeadRemovee);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
