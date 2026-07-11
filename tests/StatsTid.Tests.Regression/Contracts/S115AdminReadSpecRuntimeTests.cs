using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S115 / TASK-11503 — the per-route spec≡runtime gate extended to the remaining ADMIN READS
/// drained in Pass 2 (3 ops): the units list (<c>GET /api/admin/units</c> — the envelope whose
/// <c>UnitListItem.type</c> carries the S114 <c>[AllowedValues]</c> enum, so this assertion
/// exercises the matcher's ENUM-fidelity path against REAL seeded type values), the org-users
/// BARE array (<c>GET /api/admin/organizations/{orgId}/users</c>), and the audit envelope
/// (<c>GET /api/admin/audit</c>). Each response is matched against its committed
/// <c>docs/api/openapi.json</c> DECLARED success contract via <see cref="SpecRuntimeMatcher"/>.
///
/// <para><b>The audit seed:</b> the audit read is asserted against a REAL projection row produced
/// by a REAL admin mutation performed inside the test (a dedicated <c>POST /api/admin/units</c> —
/// its <c>UnitCreated</c> audit-projection row is written sync-in-tx per ADR-026), then the read
/// is filtered to this fixture's Organisation so the asserted rows are deterministic. This both
/// seeds the row AND verifies the "any admin mutation produces projections" claim.</para>
///
/// <para><b>Seeding:</b> reads ride dedicated read-only seeds (two units — parent
/// <c>omrade</c> + child <c>kontor</c>, exercising BOTH a null and a non-null
/// <c>parentUnitId</c> plus two distinct enum members — and two users); the audit test's unit
/// CREATE targets its own dedicated name and touches no sibling's rows. ONE Docker fixture for
/// the FAMILY (FAIL-002 discipline).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S115AdminReadSpecRuntimeTests : IAsyncLifetime
{
    private const string Mao = "S115AMAO";
    private const string Org = "S115AORG";

    private static readonly Guid ParentUnitId = Guid.Parse("a1150000-0000-0000-0000-000000000001");
    private static readonly Guid ChildUnitId = Guid.Parse("a1150000-0000-0000-0000-000000000002");

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

    /// <summary>The units-list envelope — the seeded <c>omrade</c>/<c>kontor</c> rows drive the
    /// matcher's enum-fidelity walk over <c>UnitListItem.type</c>'s declared spec enum.</summary>
    [Fact]
    public async Task UnitsList_Get200_EnvelopeAndEnumSchemaMatchRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/units?organisationId={Org}"),
            "/api/admin/units", "get");
        // Non-vacuous: both seeded units are in the envelope (enum members REALLY walked).
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("units").GetArrayLength() >= 2,
            "units read returned fewer rows than seeded");
    }

    [Fact]
    public async Task OrgUsers_Get200_BareArraySchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/organizations/{Org}/users"),
            "/api/admin/organizations/{orgId}/users", "get");
        Assert.True(JsonDocument.Parse(body).RootElement.GetArrayLength() >= 1,
            "org-users read returned an empty array — seed missing");
    }

    /// <summary>The audit envelope — asserted against a REAL sync-in-tx projection row produced
    /// by a dedicated in-test admin mutation (see class doc), scoped to this fixture's org.</summary>
    [Fact]
    public async Task Audit_Get200_EnvelopeSchemaMatchesRuntime()
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115a_gadmin", Mao);

        // (1) A REAL admin mutation → its ADR-026 audit-projection row (sync, same tx).
        using (var create = await client.PostAsync("/api/admin/units",
            new StringContent(
                $$"""{ "organisationId": "{{Org}}", "type": "team", "name": "S115 Audit-kilde" }""",
                System.Text.Encoding.UTF8, "application/json")))
        {
            Assert.Equal(201, (int)create.StatusCode);
        }

        // (2) The audit read, filtered to THIS fixture's org → the row above is in scope.
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/audit?targetOrgId={Org}"),
            "/api/admin/audit", "get");

        // Non-vacuous: the mutation REALLY projected (the row schema was exercised).
        var root = JsonDocument.Parse(body).RootElement;
        Assert.True(root.GetProperty("rows").GetArrayLength() >= 1,
            "audit read returned no rows — the admin mutation did not project");
        Assert.True(root.GetProperty("totalCount").GetInt64() >= 1);
    }

    // ── Support ──

    private async Task<string> AssertOpAsync(HttpRequestMessage request, string specPath, string method)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115a_gadmin", Mao);
        return await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client, request, specPath, method);
    }

    // ── Fixture seed — one MAO + one Organisation + read-only units/users. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S115AMAO', 'S115 Læse Ministerie', 'MAO',          NULL,       '/S115AMAO/',           'AC', 'OK24'),
                ('S115AORG', 'S115 Læse Styrelse',   'ORGANISATION', 'S115AMAO', '/S115AMAO/S115AORG/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Two read-only units: a parent 'omrade' + a child 'kontor' (partial-rank respected) —
        // null AND non-null parentUnitId + two DISTINCT enum members on the wire.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@parent, 'S115AORG', NULL,    'omrade', 'S115 Læse-område'),
                (@child,  'S115AORG', @parent, 'kontor', 'S115 Læse-kontor')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("parent", ParentUnitId);
            cmd.Parameters.AddWithValue("child", ChildUnitId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s115a_u1', 's115a_u1', '$2a$11$fake', 'S115 Liste Bruger Et', 's115a_u1@test.dk', 'S115AORG', 'HK', 'OK24', TRUE),
                ('s115a_u2', 's115a_u2', '$2a$11$fake', 'S115 Liste Bruger To', 's115a_u2@test.dk', 'S115AORG', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
