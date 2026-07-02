using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S111 / TASK-11101 (Fork B typed-client) — the PER-ROUTE spec≡runtime gate: THE closure. For EACH
/// in-scope operation it loads the committed <c>docs/api/openapi.json</c> <c>200</c> schema and asserts
/// it structurally matches that endpoint's REAL serialized response (via <see cref="SpecRuntimeMatcher"/>):
/// root kind / array-ness, property presence, camelCase keys, nullable-required fidelity, array-item +
/// dictionary-value schemas. A <c>.Produces&lt;T&gt;</c> that mis-states array-ness or a field FAILS here.
///
/// <para>The proof surface: <c>/api/admin/organizations</c> (BARE ARRAY — the array-ness sentinel),
/// <c>/organizations/tree</c>, <c>/units/forest</c>, <c>/search</c>, and the roster
/// <c>/reporting-lines/tree/{organisationId}/medarbejdere</c>. A self-contained "Spec" fixture (MAO +
/// Organisation + a nested unit chain + a unit-homed member with a position + a designated leader covered
/// by an active vikar + an active PRIMARY reporting edge) guarantees every section/nested node is exercised.</para>
///
/// <para>The final test additionally proves the gate is RED on an INJECTED array-ness lie — it matches
/// the REAL bare-array <c>/organizations</c> response against the OBJECT element schema (the
/// <c>.Produces&lt;OrgListItem&gt;</c> mistake) and asserts it throws.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OpenApiSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "SPMAO";
    private const string Org = "SPORG";
    private const string OrgName = "Spec Styrelse";
    private static readonly Guid UnitId = Guid.Parse("e9000000-0000-0000-0000-0000000000a1");
    private const string UnitName = "Spec Enhed";
    private static readonly Guid NestedUnitId = Guid.Parse("e9000000-0000-0000-0000-0000000000a2");
    private const string NestedUnitName = "Spec Underenhed";
    private const string Leader = "spec_leader";
    private const string Member = "spec_member";
    private const string Vikar = "spec_vikar";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline org tree)

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);

        _spec = LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The per-route gate — each in-scope operation's committed schema ≡ its real response.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrganizationsList_BareArray_SchemaMatchesRuntime()
        => await AssertOperationMatchesRuntime("get", "/api/admin/organizations", "/api/admin/organizations");

    [Fact]
    public async Task OrganizationsTree_Envelope_SchemaMatchesRuntime()
        => await AssertOperationMatchesRuntime("get", "/api/admin/organizations/tree", "/api/admin/organizations/tree");

    [Fact]
    public async Task UnitsForest_Envelope_SchemaMatchesRuntime()
        => await AssertOperationMatchesRuntime("get", "/api/admin/units/forest", "/api/admin/units/forest");

    [Fact]
    public async Task Search_TwoSectionEnvelope_SchemaMatchesRuntime()
        => await AssertOperationMatchesRuntime("get", "/api/admin/search", "/api/admin/search?q=Spec");

    [Fact]
    public async Task Roster_Envelope_SchemaMatchesRuntime()
        => await AssertOperationMatchesRuntime(
            "get",
            "/api/admin/reporting-lines/tree/{organisationId}/medarbejdere",
            $"/api/admin/reporting-lines/tree/{Org}/medarbejdere");

    /// <summary>Proves the gate is RED on an INJECTED array-ness lie: the REAL bare-array
    /// <c>/organizations</c> response matched against the OBJECT element schema (the
    /// <c>.Produces&lt;OrgListItem&gt;</c> mistake) MUST throw.</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedArraynessLie()
    {
        var response = await GetJson("/api/admin/organizations");
        Assert.Equal(JsonValueKind.Array, response.ValueKind);

        // The truth (the committed array schema) passes; the lie (the element OBJECT schema) is RED.
        var arraySchema = SpecRuntimeMatcher.Resolve200Schema(_spec, "/api/admin/organizations", "get");
        SpecRuntimeMatcher.AssertMatches(_spec, arraySchema, response, "truth");

        var elementObjectSchema = JsonDocument.Parse(
            """{ "$ref": "#/components/schemas/StatsTid.Backend.Api.Contracts.OrgListItem" }""").RootElement;
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertMatches(_spec, elementObjectSchema, response, "injected-lie"));
        Assert.Contains("array-ness", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Core: GET the endpoint, resolve its committed 200 schema, assert spec ≡ runtime. ──
    private async Task AssertOperationMatchesRuntime(string method, string specPath, string requestUrl)
    {
        var response = await GetJson(requestUrl);
        var schema = SpecRuntimeMatcher.Resolve200Schema(_spec, specPath, method);
        SpecRuntimeMatcher.AssertMatches(_spec, schema, response, $"{method.ToUpperInvariant()} {specPath}");
    }

    private async Task<JsonElement> GetJson(string url)
    {
        var admin = GlobalAdminClient();
        var rsp = await admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Fixture seed (mirrors the Search/Roster contract fixtures). ──
    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'Spec Ministerie', 'MAO',          NULL, '/SPMAO/',        'AC', 'OK24'),
                (@org, @orgName,          'ORGANISATION', @mao, '/SPMAO/SPORG/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mao", Mao);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("orgName", OrgName);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@id,       @org, NULL, 'direktion', @name),
                (@nestedId, @org, @id,  'kontor',    @nestedName)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", UnitId);
            cmd.Parameters.AddWithValue("nestedId", NestedUnitId);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("name", UnitName);
            cmd.Parameters.AddWithValue("nestedName", NestedUnitName);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@leader, @leader, '$2a$11$fake', 'Spec Leader', 'spec_leader@test.dk', @org, @unit, 'HK','OK24', TRUE),
                (@member, @member, '$2a$11$fake', 'Spec Member', 'spec_member@test.dk', @org, @unit, 'HK','OK24', TRUE),
                (@vikar,  @vikar,  '$2a$11$fake', 'Spec Vikar',  'spec_vikar@test.dk',  @org, @unit, 'HK','OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("leader", Leader);
            cmd.Parameters.AddWithValue("member", Member);
            cmd.Parameters.AddWithValue("vikar", Vikar);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("unit", UnitId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                (@leader, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST'),
                (@vikar,  'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST'),
                (@member, 'EMPLOYEE',     @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("leader", Leader);
            cmd.Parameters.AddWithValue("vikar", Vikar);
            cmd.Parameters.AddWithValue("member", Member);
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO unit_leaders (unit_id, user_id) VALUES (@unit, @leader)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("unit", UnitId);
            cmd.Parameters.AddWithValue("leader", Leader);
            await cmd.ExecuteNonQueryAsync();
        }

        // The leader is an away-manager covered by an ACTIVE vikar → a POPULATED outgoingVikar object.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by) VALUES
                (@leader, @vikar, @future, 'FERIE', @org, 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("leader", Leader);
            cmd.Parameters.AddWithValue("vikar", Vikar);
            cmd.Parameters.AddWithValue("future", new DateOnly(2099, 12, 31));
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from)
            VALUES (@id, 1.000, 'Kontorchef', '0001-01-01')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", Member);
            await cmd.ExecuteNonQueryAsync();
        }

        // The member's active PRIMARY reporting edge → Leader (non-null primaryReportingLineVersion etag).
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = Member,
            ManagerId = Leader,
            OrganisationId = Org,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    // ── Locate + load the committed spec (walk up from the test bin dir for StatsTid.sln). ──
    private static JsonElement LoadCommittedSpec()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "api", "openapi.json");
            if (File.Exists(candidate))
            {
                var json = File.ReadAllText(candidate);
                // Detach from the JsonDocument lifetime by cloning the root element.
                return JsonDocument.Parse(json).RootElement.Clone();
            }
            dir = dir.Parent;
        }
        throw new XunitException(
            "Could not locate docs/api/openapi.json by walking up from AppContext.BaseDirectory. " +
            "Regenerate it with `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`.");
    }

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "s111_gadmin", name: "s111_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Mao,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
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
