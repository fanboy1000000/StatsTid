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

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S106 / TASK-10603 (ADR-038 D5, PAT-010) — the endpoint RESPONSE-CONTRACT test for the scoped units +
/// people SEARCH read <c>GET /api/admin/search</c>. Runs end-to-end against the real Backend.Api via
/// <see cref="StatsTidWebApplicationFactory"/> and pins the wire shape a future FE search-overlay hook
/// will consume, closing the recurring "fetchEnheder" false-green bug class (S97 → S99 → S100) for the
/// search surface BEFORE a FE consumer exists.
///
/// <para>A self-contained "Zeta" fixture (one Organisation + one unit + one unit-homed person with a
/// position) is seeded so a GlobalAdmin search for <c>"Zeta"</c> returns EXACTLY one row in each
/// section — guaranteeing both the units AND people section shapes are exercised regardless of the
/// baseline seed.</para>
///
/// <list type="number">
///   <item><c>{ units: [...], people: [...] }</c> ENVELOPE (the design's TWO-section shape — NOT a bare
///     array, the S97/S99 distinction).</item>
///   <item>A unit element carries <c>unitId</c>/<c>organisationId</c>/<c>type</c>/<c>name</c>/
///     <c>path</c> (camelCase, literally); <c>path</c> is an Array.</item>
///   <item>A person element carries <c>userId</c>/<c>organisationId</c>/<c>displayName</c>/
///     <c>position</c>/<c>unitName</c>/<c>path</c> (camelCase, literally); <c>organisationId</c> is the
///     person's primary Organisation (the S107 Afgrænsning scope-filter key); <c>path</c> is an Array.</item>
/// </list>
///
/// <para>RED-on-old: the records are PascalCase; a dropped field, a renamed key, an envelope↔bare-array
/// drift, or a future global <c>AddJsonOptions</c> serializer regression fails the relevant
/// <see cref="ContractAssert"/> assertion.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SearchEndpointContractTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "Z6MAO";
    private const string Org = "Z6ORG";
    private const string OrgName = "Zeta Styrelse";
    private static readonly Guid UnitId = Guid.Parse("e6000000-0000-0000-0000-0000000000a1");
    private const string UnitName = "Zeta Enhed";
    // A NESTED unit under "Zeta Enhed" — exercises the multi-segment ancestor-chain PATH build (the
    // search analogue of the S100 nested-drop: a deep node whose path must carry its ancestors).
    private static readonly Guid NestedUnitId = Guid.Parse("e6000000-0000-0000-0000-0000000000a2");
    private const string NestedUnitName = "Zeta Underenhed";
    private const string PersonId = "zeta_p1";
    private const string PersonName = "Zeta Person";

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
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'Zeta Ministerie', 'MAO',          NULL, '/Z6MAO/',        'AC', 'OK24'),
                (@org, @orgName,          'ORGANISATION', @mao, '/Z6MAO/Z6ORG/',  'HK', 'OK24')
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
            VALUES (@id, @id, '$2a$11$fake', @name, @id || '@test.dk', @org, @unit, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", PersonId);
            cmd.Parameters.AddWithValue("name", PersonName);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("unit", UnitId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from)
            VALUES (@id, 1.000, 'Kontorchef', '0001-01-01')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", PersonId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>The search is the <c>{ units: [...], people: [...] }</c> two-section envelope; each unit
    /// row carries its field-set + an Array <c>path</c>; each person row carries its field-set + an Array
    /// <c>path</c> (camelCase keys, literally). RED-on-old: a dropped field fails HasFields; a
    /// bare-array drift fails IsEnvelope; a renamed key fails the literal camelCase assertion.</summary>
    [Fact]
    public async Task GetSearch_IsTwoSectionEnvelope_UnitAndPersonRowsCarryFieldsAndPath()
    {
        var admin = GlobalAdminClient();

        var rsp = await admin.GetAsync("/api/admin/search?q=Zeta");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // ── 1) the { units: [...], people: [...] } two-section ENVELOPE (NOT a bare array). ──
        var units = ContractAssert.IsEnvelope(body, "units");
        var people = ContractAssert.IsEnvelope(body, "people");

        // ── 2) the unit row — fields + the Array path (camelCase, literally). ──
        var unit = FindByProp(units, "unitId", UnitId.ToString())
            ?? throw new XunitException("The seeded Zeta unit is missing from the units section.");
        ContractAssert.HasFields(unit, "unitId", "organisationId", "type", "name", "path");
        Assert.Equal("Zeta Enhed", unit.GetProperty("name").GetString());
        Assert.Equal(Org, unit.GetProperty("organisationId").GetString());
        ContractAssert.FieldKind(unit, "path", JsonValueKind.Array);
        // A top-level unit's path is exactly [OrganisationName].
        Assert.Equal(new[] { OrgName }, unit.GetProperty("path").EnumerateArray().Select(e => e.GetString()).ToArray());

        // ── 2b) a NESTED unit row — its PATH carries the full ancestor chain [Org, parentUnit] (the
        //    multi-segment breadcrumb the overlay shows; the search analogue of the S100 nested-drop —
        //    a regression in the UnitNameChain ancestor walk would drop a segment here, RED). ──
        var nested = FindByProp(units, "unitId", NestedUnitId.ToString())
            ?? throw new XunitException("The seeded nested Zeta unit is missing from the units section.");
        ContractAssert.HasFields(nested, "unitId", "organisationId", "type", "name", "path");
        Assert.Equal(NestedUnitName, nested.GetProperty("name").GetString());
        ContractAssert.FieldKind(nested, "path", JsonValueKind.Array);
        Assert.Equal(new[] { OrgName, UnitName }, nested.GetProperty("path").EnumerateArray().Select(e => e.GetString()).ToArray());

        // ── 3) the person row — fields + the Array path (camelCase, literally). ──
        var person = FindByProp(people, "userId", PersonId)
            ?? throw new XunitException("The seeded Zeta person is missing from the people section.");
        ContractAssert.HasFields(person, "userId", "organisationId", "displayName", "position", "unitName", "path");
        Assert.Equal("Zeta Person", person.GetProperty("displayName").GetString());
        // organisationId == the person's Organisation (the S107 Afgrænsning scope-filter key — camelCase, literally).
        Assert.Equal(Org, person.GetProperty("organisationId").GetString());
        Assert.Equal("Kontorchef", person.GetProperty("position").GetString());
        Assert.Equal("Zeta Enhed", person.GetProperty("unitName").GetString());
        ContractAssert.FieldKind(person, "path", JsonValueKind.Array);
        // A unit-homed person's path is [OrganisationName, ...unit chain incl. the home unit].
        Assert.Equal(new[] { OrgName, UnitName }, person.GetProperty("path").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    private static JsonElement? FindByProp(JsonElement array, string prop, string value)
    {
        foreach (var n in array.EnumerateArray())
            if (string.Equals(n.GetProperty(prop).GetString(), value, StringComparison.OrdinalIgnoreCase))
                return n;
        return null;
    }

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "s106c_gadmin", name: "s106c_gadmin", role: StatsTidRoles.GlobalAdmin,
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
