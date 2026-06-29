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
/// S107 / TASK-10705 (ADR-038 D2/D4/D6, PAT-010) — the endpoint RESPONSE-CONTRACT test for the
/// unit-tagged medarbejder ROSTER read <c>GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere</c>.
/// Runs end-to-end against the real Backend.Api via <see cref="StatsTidWebApplicationFactory"/> and pins the
/// wire shape the S107 <c>useRoster</c> hook consumes, closing the recurring "fetchEnheder" false-green bug
/// class (S97 → S99 → S100) for the roster surface.
///
/// <para><b>Why a NEW Contracts/ file (not the existing Approval/S106RosterUnitTagTests):</b> the S106 roster
/// contract pin (<c>RosterEndpoint_ServesUnitTagFields_AndNameResolution</c>) is PAT-010-co-located in
/// <c>Approval/</c>, but the contract-coverage lint (<c>tools/check_endpoint_contracts.py</c>) scans ONLY
/// <c>tests/.../Contracts/</c> for the liveness check — so the roster path could not move EXEMPT→REGISTRY
/// pointing at the Approval/ method (the lint would not see it). This DEDICATED Contracts/ pin lets the lint
/// register + verify the roster; the seed-heavy behavioral S106 suite stays as the multi-leader/tile suite.</para>
///
/// <para>A self-contained "Roster Styrelse" fixture (one Organisation under a MAO + one unit + a designated
/// unit leader [who is also an away-manager covered by an active vikar] + one unit-homed member with an active
/// PRIMARY reporting edge to the leader) is seeded so a GlobalAdmin roster read returns BOTH a typical member
/// row (the unit-tag field-set + a non-null etag + a JSON-null <c>outgoingVikar</c>) AND a leader row carrying
/// a POPULATED <c>outgoingVikar</c> nested object — exercising every field of the contract.</para>
///
/// <list type="number">
///   <item><c>{ employees: [...], pendingCountByManager: {...}, nameResolution: {...} }</c> ENVELOPE
///     (NOT a bare array — the S97/S99 distinction).</item>
///   <item>A member row carries the unit-tag field-set <c>unitId</c>/<c>unitName</c>/<c>leaderIds</c>/
///     <c>primaryReportingLineVersion</c>/<c>outgoingVikar</c> (camelCase, literally); <c>leaderIds</c> is
///     an Array carrying the unit's designated leader; the etag is a non-null Number; <c>outgoingVikar</c>
///     is JSON-null (the KEY still present).</item>
///   <item>The leader row carries a POPULATED <c>outgoingVikar</c> OBJECT with the nested field-set
///     <c>vikarUserId</c>/<c>vikarDisplayName</c>/<c>untilDate</c>/<c>reason</c> (camelCase, literally).</item>
///   <item><c>nameResolution</c> is a by-id OBJECT whose entries carry <c>userId</c>/<c>displayName</c>/
///     <c>position</c>/<c>unitName</c> (camelCase, literally).</item>
/// </list>
///
/// <para>RED-on-old: the records are PascalCase; a dropped field, a renamed key, an envelope↔bare-array
/// drift, or a future global <c>AddJsonOptions</c>/serializer regression fails the relevant
/// <see cref="ContractAssert"/> assertion.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RosterEndpointContractTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "R7MAO";
    private const string Org = "R7ORG";
    private const string OrgName = "Roster Styrelse";
    private static readonly Guid UnitId = Guid.Parse("e7000000-0000-0000-0000-0000000000a1");
    private const string UnitName = "Roster Enhed";

    private const string Leader = "roster_leader"; // UnitId leader + away-manager covered by an active vikar
    private const string Member = "roster_member"; // UnitId member — active PRIMARY edge → Leader
    private const string Vikar  = "roster_vikar";  // active manager_vikar stand-in for Leader

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
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
        // The org tree (MAO + ORGANISATION) — the roster scopes over the Organisation's materialized_path.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'Roster Ministerie', 'MAO',          NULL, '/R7MAO/',        'AC', 'OK24'),
                (@org, @orgName,            'ORGANISATION', @mao, '/R7MAO/R7ORG/',  'HK', 'OK24')
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
                (@id, @org, NULL, 'kontor', @name)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", UnitId);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("name", UnitName);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@leader, @leader, '$2a$11$fake', 'Roster Leader', 'roster_leader@test.dk', @org, @unit, 'HK','OK24', TRUE),
                (@member, @member, '$2a$11$fake', 'Roster Member', 'roster_member@test.dk', @org, @unit, 'HK','OK24', TRUE),
                (@vikar,  @vikar,  '$2a$11$fake', 'Roster Vikar',  'roster_vikar@test.dk',  @org, @unit, 'HK','OK24', TRUE)
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

        // The unit's designated leader (the source of the member row's aggregated leaderIds).
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

        // The leader is an away-manager covered by an ACTIVE vikar (Vikar) → the leader row carries a
        // POPULATED outgoingVikar object (the nested field-set the contract pins).
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

        // The member's active PRIMARY reporting edge → Leader (same Organisation) — gives the member row a
        // non-null primaryReportingLineVersion etag (the S99 "Ret" supersede key).
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, MakeLine(Member, Leader));
    }

    /// <summary>The roster is the <c>{ employees, pendingCountByManager, nameResolution }</c> envelope; a
    /// member row carries the unit-tag field-set + a non-null Number etag + a JSON-null <c>outgoingVikar</c>;
    /// the leader row carries a POPULATED <c>outgoingVikar</c> object; <c>nameResolution</c> is a by-id object
    /// (camelCase keys, literally). RED-on-old: a dropped field fails HasFields; a bare-array drift fails
    /// IsEnvelope; a renamed key fails the literal camelCase assertion.</summary>
    [Fact]
    public async Task GetRoster_IsEnvelope_RowsCarryUnitTagFieldSet()
    {
        var admin = GlobalAdminClient();

        var rsp = await admin.GetAsync($"/api/admin/reporting-lines/tree/{Org}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // ── 1) the { employees, pendingCountByManager, nameResolution } ENVELOPE (NOT a bare array). ──
        var employees = ContractAssert.IsEnvelope(body, "employees");
        ContractAssert.HasFields(body, "employees", "pendingCountByManager", "nameResolution");
        ContractAssert.FieldKind(body, "pendingCountByManager", JsonValueKind.Object);
        ContractAssert.FieldKind(body, "nameResolution", JsonValueKind.Object);

        // ── 2) the MEMBER row — the unit-tag field-set (camelCase, literally) + the etag + the vikar KEY. ──
        var member = FindByProp(employees, "employeeId", Member)
            ?? throw new XunitException("The seeded roster member is missing from the employees section.");
        ContractAssert.HasFields(member,
            "employeeId", "displayName", "unitId", "unitName", "leaderIds",
            "primaryReportingLineVersion", "outgoingVikar");
        Assert.Equal(UnitId.ToString(), member.GetProperty("unitId").GetString());
        Assert.Equal(UnitName, member.GetProperty("unitName").GetString());
        // leaderIds is a clean Array carrying the unit's designated leader (never a fanned-out duplicate).
        ContractAssert.FieldKind(member, "leaderIds", JsonValueKind.Array);
        var leaderIds = member.GetProperty("leaderIds").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(Leader, leaderIds);
        // The member has an active PRIMARY edge → a non-null Number etag.
        ContractAssert.FieldKind(member, "primaryReportingLineVersion", JsonValueKind.Number);
        // The member has no vikar → outgoingVikar is JSON-null (the KEY is still present — null-emitting).
        ContractAssert.FieldKind(member, "outgoingVikar", JsonValueKind.Null);

        // ── 3) the LEADER row — a POPULATED outgoingVikar object with the nested field-set (camelCase). ──
        var leader = FindByProp(employees, "employeeId", Leader)
            ?? throw new XunitException("The seeded unit leader is missing from the employees section.");
        ContractAssert.FieldKind(leader, "outgoingVikar", JsonValueKind.Object);
        var outgoingVikar = leader.GetProperty("outgoingVikar");
        ContractAssert.HasFields(outgoingVikar, "vikarUserId", "vikarDisplayName", "untilDate", "reason");
        Assert.Equal(Vikar, outgoingVikar.GetProperty("vikarUserId").GetString());

        // ── 4) nameResolution — a by-id OBJECT; each entry carries the resolved-ref field-set (camelCase).
        //    The leader is referenced (member's structural approver + ∈ member.leaderIds) → resolvable by id.
        var nameResolution = body.GetProperty("nameResolution");
        var leaderRef = nameResolution.GetProperty(Leader);
        ContractAssert.HasFields(leaderRef, "userId", "displayName", "position", "unitName");
        Assert.Equal("Roster Leader", leaderRef.GetProperty("displayName").GetString());
    }

    // ── Helpers ──

    private static JsonElement? FindByProp(JsonElement array, string prop, string value)
    {
        foreach (var n in array.EnumerateArray())
            if (string.Equals(n.GetProperty(prop).GetString(), value, StringComparison.Ordinal))
                return n;
        return null;
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = Org,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "s107r_gadmin", name: "s107r_gadmin", role: StatsTidRoles.GlobalAdmin,
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
