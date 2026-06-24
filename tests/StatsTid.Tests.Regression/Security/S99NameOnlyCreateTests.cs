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
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S99 / TASK-9900 — the regression suite for the NAME-ONLY org create the redesigned Organisation
/// (Global administration) page needs. The Create dialog supplies only a name (+ the type/parent
/// from context per <c>design_handoff_organisation</c>); <c>POST /api/admin/organizations</c> is
/// adapted so <c>orgId</c> / <c>agreementCode</c> / <c>okVersion</c> are OPTIONAL — the backend
/// GENERATES a stable <c>orgId</c> ("ORG" + 8 hex, uppercased) and DEFAULTS the (vestigial)
/// agreement/ok to the system defaults ('AC' / 'OK24').
///
/// <list type="number">
///   <item><b>RED-on-old</b> — a name-only MAO create (GlobalAdmin) → 201 with a generated orgId +
///     defaulted agreement/ok (was 400 on the old all-<c>required</c> DTO).</item>
///   <item>A name-only ORGANISATION create under a MAO (LocalAdmin+ scoped) → 201; the generated org
///     is queryable (GET /organizations + a direct DB read).</item>
///   <item><b>Backward-compat</b> — an EXPLICIT-orgId create still → 201 with that exact id, and the
///     supplied agreement/ok is honored unchanged.</item>
///   <item>The validation still holds: ORGANISATION with no parent → 400; ORGANISATION under a non-MAO
///     parent → 400; MAO with a parent → 400.</item>
/// </list>
///
/// <para><b>Topology.</b> The fixture seeds its OWN fresh tree (disjoint from the init.sql seed orgs):
/// a MAO (<c>T99_MAO</c>) + an ORGANISATION (<c>T99_ORG</c>) under it (the non-MAO-parent subject).
/// Generated orgs created by the tests are tracked and cleaned up by prefix. Mirrors
/// <see cref="S98OrgStructureTests"/> (token minting + raw seed + cleanup).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S99NameOnlyCreateTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "T99_MAO";       // MAO root — the valid Organisation-create parent
    private const string Org = "T99_ORG";       // ORGANISATION under T99_MAO — the non-MAO-parent subject

    private const string MaoPath = "/T99_MAO/";
    private const string OrgPath = "/T99_MAO/T99_ORG/";

    private static readonly string[] SeedOrgs = { Org, Mao };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders

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
    //  (1) RED-on-old — a NAME-ONLY MAO create → 201 with a generated id + defaulted agreement/ok.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(1) RED-on-old: a name-only MAO create (no orgId / agreementCode / okVersion) by a
    /// GlobalAdmin → 201. The backend GENERATES a stable orgId ("ORG"+8 hex) + DEFAULTS agreement='AC'
    /// / okVersion='OK24'. On the old all-<c>required</c> DTO the model-bind would 400 (missing
    /// required members). The generated org is persisted as a MAO root (NULL parent, "/{id}/" path).</summary>
    [Fact]
    public async Task NameOnly_MaoCreate_GlobalAdmin_Returns201_GeneratesIdAndDefaults()
    {
        var admin = GlobalAdminClient();

        var rsp = await admin.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgType = "MAO",
            orgName = "Test MAO",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var genId = body.GetProperty("orgId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(genId));
        Assert.StartsWith("ORG", genId);                          // the backend id format
        Assert.Equal("Test MAO", body.GetProperty("orgName").GetString());
        Assert.Equal("MAO", body.GetProperty("orgType").GetString());
        Assert.True(body.GetProperty("parentOrgId").ValueKind == JsonValueKind.Null); // MAO = root
        Assert.Equal($"/{genId}/", body.GetProperty("materializedPath").GetString());
        // The vestigial fields defaulted server-side.
        Assert.Equal("AC", body.GetProperty("agreementCode").GetString());
        Assert.Equal("OK24", body.GetProperty("okVersion").GetString());

        TrackGenerated(genId!);

        // Persisted + queryable as a MAO root.
        var (type, parent, agreement, ok) = await ReadOrgAsync(genId!);
        Assert.Equal("MAO", type);
        Assert.Null(parent);
        Assert.Equal("AC", agreement);
        Assert.Equal("OK24", ok);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) A name-only ORGANISATION create under a MAO (LocalAdmin+ scoped) → 201; queryable.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(2) A name-only ORGANISATION create under T99_MAO by a LocalAdmin scoped to that MAO →
    /// 201 (the LocalAdmin floor is preserved). The generated org has the MAO parent + the computed
    /// "/{mao}/{id}/" path, and is queryable via GET /organizations.</summary>
    [Fact]
    public async Task NameOnly_OrganisationCreate_LocalAdminScoped_Returns201_Queryable()
    {
        var localAdmin = ScopedClient("t99_ladm", StatsTidRoles.LocalAdmin, Mao);

        var rsp = await localAdmin.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgType = "ORGANISATION",
            orgName = "Test Org",
            parentOrgId = Mao,
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var genId = body.GetProperty("orgId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(genId));
        Assert.StartsWith("ORG", genId);
        Assert.Equal("ORGANISATION", body.GetProperty("orgType").GetString());
        Assert.Equal(Mao, body.GetProperty("parentOrgId").GetString());
        Assert.Equal($"{MaoPath}{genId}/", body.GetProperty("materializedPath").GetString());

        TrackGenerated(genId!);

        // Queryable via the admin org list. NB: the SCOPED LocalAdmin only sees orgs its ORG_ONLY
        // scope covers (T99_MAO alone, per S93 flat role-scope — a child org is NOT auto-covered), so
        // we assert visibility via a GlobalAdmin who sees the full forest.
        var listIds = await GetOrgListIdsAsync(GlobalAdminClient());
        Assert.Contains(genId, listIds);

        // Persisted under the MAO.
        var (type, parent, _, _) = await ReadOrgAsync(genId!);
        Assert.Equal("ORGANISATION", type);
        Assert.Equal(Mao, parent);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) Backward-compat — an EXPLICIT orgId/agreement/ok still works exactly as before.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(3) Backward-compat: an EXPLICIT-orgId MAO create (with explicit agreement/ok) → 201
    /// with that exact id, and the supplied agreement/ok is honored unchanged (NOT overwritten by the
    /// defaults). The pre-S99 contract is preserved.</summary>
    [Fact]
    public async Task ExplicitOrgId_Create_Returns201_WithThatIdAndSuppliedAgreementOk()
    {
        var admin = GlobalAdminClient();
        const string explicitId = "T99_EXPLICIT";

        var rsp = await admin.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgId = explicitId,
            orgType = "MAO",
            orgName = "T99 Explicit",
            agreementCode = "HK",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(explicitId, body.GetProperty("orgId").GetString());
        Assert.Equal("HK", body.GetProperty("agreementCode").GetString()); // honored, NOT defaulted to 'AC'
        Assert.Equal("OK24", body.GetProperty("okVersion").GetString());
        Assert.Equal($"/{explicitId}/", body.GetProperty("materializedPath").GetString());

        TrackGenerated(explicitId);

        var (type, parent, agreement, ok) = await ReadOrgAsync(explicitId);
        Assert.Equal("MAO", type);
        Assert.Null(parent);
        Assert.Equal("HK", agreement);
        Assert.Equal("OK24", ok);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) The type/parent validation still holds (name-only does NOT relax it).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(4a) An ORGANISATION with NO parent → 400 (an ORGANISATION must have a MAO parent),
    /// even name-only.</summary>
    [Fact]
    public async Task NameOnly_OrganisationWithNoParent_Returns400()
    {
        var admin = GlobalAdminClient();
        var rsp = await admin.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgType = "ORGANISATION",
            orgName = "Test Org No Parent",
        });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    }

    /// <summary>(4b) An ORGANISATION under a NON-MAO parent (another ORGANISATION, T99_ORG) → 400.</summary>
    [Fact]
    public async Task NameOnly_OrganisationUnderNonMaoParent_Returns400()
    {
        var admin = GlobalAdminClient();
        var rsp = await admin.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgType = "ORGANISATION",
            orgName = "Test Org Bad Parent",
            parentOrgId = Org, // T99_ORG is an ORGANISATION, not a MAO
        });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    }

    /// <summary>(4c) A MAO WITH a parent → 400 (a MAO is a root and must not have a parent).</summary>
    [Fact]
    public async Task NameOnly_MaoWithParent_Returns400()
    {
        var admin = GlobalAdminClient();
        var rsp = await admin.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgType = "MAO",
            orgName = "Test MAO With Parent",
            parentOrgId = Mao,
        });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    // Generated/explicit orgs created during a test → tracked for cleanup.
    private readonly List<string> _created = new();
    private void TrackGenerated(string orgId)
    {
        lock (_created) _created.Add(orgId);
    }

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active)
            VALUES
                (@mao, 'T99 MAO', 'MAO',          NULL,  @maoPath, 'AC', 'OK24', TRUE),
                (@org, 'T99 Org', 'ORGANISATION', @mao,  @orgPath, 'AC', 'OK24', TRUE)
            ON CONFLICT (org_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("mao", Mao);
        cmd.Parameters.AddWithValue("org", Org);
        cmd.Parameters.AddWithValue("maoPath", MaoPath);
        cmd.Parameters.AddWithValue("orgPath", OrgPath);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        // All orgs we may have created — the fixed seed + any explicit/generated test orgs.
        string[] created;
        lock (_created) created = _created.ToArray();
        var allOrgs = SeedOrgs.Concat(created).Distinct().ToArray();
        var allStreams = allOrgs.Select(o => $"org-{o}").ToArray();

        await ExecAsync(conn,
            "DELETE FROM audit_projection WHERE target_org_id = ANY(@orgs)", allOrgs, allStreams);
        await ExecAsync(conn,
            "DELETE FROM outbox_events WHERE stream_id = ANY(@streams)", allOrgs, allStreams);
        await ExecAsync(conn,
            "DELETE FROM events WHERE stream_id = ANY(@streams)", allOrgs, allStreams);
        await ExecAsync(conn,
            "DELETE FROM event_streams WHERE stream_id = ANY(@streams)", allOrgs, allStreams);

        // Any generated orgs share the "ORG"+8 hex prefix — also sweep by prefix as a safety net
        // (a test that 201'd but failed before TrackGenerated would otherwise orphan a row).
        await using (var prefixCmd = new NpgsqlCommand(
            "DELETE FROM organizations WHERE org_id = ANY(@orgs) OR org_name LIKE 'T99 %' OR org_name = 'Test MAO' OR org_name = 'Test Org'", conn))
        {
            prefixCmd.Parameters.AddWithValue("orgs", allOrgs);
            await prefixCmd.ExecuteNonQueryAsync();
        }

        static async Task ExecAsync(NpgsqlConnection c, string sql, string[] orgs, string[] streams)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("orgs", orgs);
            cmd.Parameters.AddWithValue("streams", streams);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP / DB helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<List<string?>> GetOrgListIdsAsync(HttpClient client)
    {
        var rsp = await client.GetAsync("/api/admin/organizations");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(o => o.GetProperty("orgId").GetString()).ToList();
    }

    private async Task<(string Type, string? Parent, string Agreement, string Ok)> ReadOrgAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT org_type, parent_org_id, agreement_code, ok_version FROM organizations WHERE org_id = @o AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("o", orgId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Organization '{orgId}' not found (active).");
        var type = reader.GetString(0);
        var parent = reader.IsDBNull(1) ? null : reader.GetString(1);
        var agreement = reader.GetString(2);
        var ok = reader.GetString(3);
        return (type, parent, agreement, ok);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Tokens / clients
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t99_gadmin", name: "t99_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Mao,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A single-scope client at <paramref name="role"/> anchored at <paramref name="orgId"/>
    /// (ORG_ONLY, S93 flat role-scope) — covers exactly that Organisation.</summary>
    private HttpClient ScopedClient(string actorId, string role, string orgId)
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(role, orgId, "ORG_ONLY") });
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
