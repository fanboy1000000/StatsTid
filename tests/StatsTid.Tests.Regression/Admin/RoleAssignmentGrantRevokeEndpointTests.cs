using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Admin;

/// <summary>
/// S85 / TASK-8501 — WAF endpoint tests for <c>POST /api/admin/roles/grant</c> and
/// <c>POST /api/admin/roles/revoke</c> against the REAL <c>docker/postgres/init.sql</c> schema
/// (the canonical <c>role_assignment_audit</c> table), via
/// <see cref="StatsTidWebApplicationFactory"/>. This is the harness the masking test
/// (<c>AdminAtomicTests</c>) lacked — its private fixture DDL mirrored the buggy audit columns,
/// so it could not surface the production INSERT defect.
///
/// <para>
/// Two defects are pinned here:
/// <list type="bullet">
///   <item><description>The broken audit INSERT (grant + revoke 500'd on every call —
///   wrong column list, <c>action='GRANT'/'REVOKE'</c> vs the CHECK vocabulary, plain-string
///   <c>details</c> into a JSONB column). Success-path tests assert 201/200 + the persisted
///   <c>role_assignments</c> row + the <c>role_assignment_audit</c> row
///   (action='GRANTED'/'REVOKED', actor_id/actor_role set, details valid JSON).</description></item>
///   <item><description>The unmasked privilege escalation — the grant guard keyed "global" off
///   <c>OrgId is null</c> not <c>ScopeType=='GLOBAL'</c>. Escalation A
///   (<c>{scopeType:'GLOBAL', orgId:'STY01'}</c>) and escalation B
///   (<c>{roleId:'GLOBAL_ADMIN', scopeType:'ORG_ONLY', orgId:'STY01'}</c>) must NOT mint a row,
///   and a lower actor granting GLOBAL_ADMIN is denied by the privilege hierarchy.</description></item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RoleAssignmentGrantRevokeEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    // Distinct-from-seed target users + admin actors (seed orgs from init.sql: STY01 under MIN01).
    private const string TargetEmp = "t85_target_emp";   // STY01 — the role-grant target
    private const string LocalAdminActor = "t85_ladmin"; // STY01 LocalAdmin (the attacker in escalation A/B)
    private const string GlobalAdminActor = "t85_gadmin"; // GLOBAL GlobalAdmin (legitimate global grants)

    private const string OrgStorelse = "STY01";

    private static readonly string[] AllUsers = { TargetEmp, LocalAdminActor, GlobalAdminActor };

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);

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
    //  Success paths
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Grant (LocalAdmin, in-scope, non-GLOBAL) → 201 + a live role_assignments row + a
    /// role_assignment_audit row (action='GRANTED', actor_id/actor_role set, details valid JSON).
    /// RED on pre-S85 code: the audit INSERT 500'd (wrong columns / action / JSONB), so the call
    /// never returned 201 and never committed any row.
    /// </summary>
    [Fact]
    public async Task Grant_LocalAdmin_InScope_OrgOnly_Returns201_PersistsAssignment_AndAudit()
    {
        var client = LocalAdminClient();

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_LEADER",
            orgId = OrgStorelse,
            scopeType = "ORG_ONLY",
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = body.GetProperty("assignmentId").GetGuid();

        // role_assignments row is live + correct.
        var (roleId, orgId, scopeType, isActive) = await ReadAssignmentAsync(assignmentId);
        Assert.Equal("LOCAL_LEADER", roleId);
        Assert.Equal(OrgStorelse, orgId);
        Assert.Equal("ORG_ONLY", scopeType);
        Assert.True(isActive);

        // role_assignment_audit row landed with the corrected shape.
        var (action, actorId, actorRole, detailsJson) = await ReadLatestAuditAsync(assignmentId);
        Assert.Equal("GRANTED", action);
        Assert.Equal(LocalAdminActor, actorId);
        Assert.Equal(StatsTidRoles.LocalAdmin, actorRole);
        // details must be valid JSON (it is stored JSONB) and carry the structured fields.
        using var detailsDoc = JsonDocument.Parse(detailsJson);
        Assert.Equal("LOCAL_LEADER", detailsDoc.RootElement.GetProperty("roleId").GetString());
        Assert.Equal(TargetEmp, detailsDoc.RootElement.GetProperty("userId").GetString());
    }

    /// <summary>
    /// Grant a GLOBAL scope as GlobalAdmin (legitimate) → 201. Proves the corrected shape gate
    /// still admits the legitimate GLOBAL grant (orgId null + HasGlobalScope), not just denies.
    /// </summary>
    [Fact]
    public async Task Grant_GlobalAdmin_GlobalScope_NoOrg_Returns201()
    {
        var client = GlobalAdminClient();

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_HR",
            orgId = (string?)null,
            scopeType = "GLOBAL",
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = body.GetProperty("assignmentId").GetGuid();

        var (roleId, orgId, scopeType, isActive) = await ReadAssignmentAsync(assignmentId);
        Assert.Equal("LOCAL_HR", roleId);
        Assert.Null(orgId);
        Assert.Equal("GLOBAL", scopeType);
        Assert.True(isActive);
    }

    /// <summary>
    /// Revoke (LocalAdmin, in-scope) → 200 + is_active=FALSE + an audit row (action='REVOKED').
    /// RED on pre-S85 code: the revoke audit INSERT 500'd the same way.
    /// </summary>
    [Fact]
    public async Task Revoke_LocalAdmin_InScope_Returns200_Deactivates_AndAudits()
    {
        var client = LocalAdminClient();

        // First grant a live assignment to revoke.
        var grantRsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_LEADER",
            orgId = OrgStorelse,
            scopeType = "ORG_ONLY",
        });
        Assert.Equal(HttpStatusCode.Created, grantRsp.StatusCode);
        var grantBody = await grantRsp.Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = grantBody.GetProperty("assignmentId").GetGuid();

        var revokeRsp = await client.PostAsJsonAsync("/api/admin/roles/revoke", new
        {
            assignmentId,
            reason = "test-revoke",
        });
        Assert.Equal(HttpStatusCode.OK, revokeRsp.StatusCode);

        var (_, _, _, isActive) = await ReadAssignmentAsync(assignmentId);
        Assert.False(isActive);

        var (action, actorId, actorRole, _) = await ReadLatestAuditAsync(assignmentId);
        Assert.Equal("REVOKED", action);
        Assert.Equal(LocalAdminActor, actorId);
        Assert.Equal(StatsTidRoles.LocalAdmin, actorRole);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Privilege-escalation guards (P7) — must NOT mint a row
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Escalation A: a LocalAdmin granting <c>{scopeType:'GLOBAL', orgId:'STY01'}</c> — a non-null
    /// org with a GLOBAL scope. Pre-S85 the guard keyed off <c>OrgId is null</c>, so the non-null
    /// org routed through ValidateOrgAccessAsync (which the in-scope LocalAdmin passed) → the row
    /// minted, and RoleScope.CoversOrg treats any GLOBAL as all-org → effective global escalation.
    /// Must be rejected (not 201) and NO row minted.
    /// </summary>
    [Fact]
    public async Task Escalation_A_LocalAdmin_GlobalScope_WithOrg_IsRejected_NoRowMinted()
    {
        var client = LocalAdminClient();

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_LEADER",
            orgId = OrgStorelse,
            scopeType = "GLOBAL",
        });

        Assert.NotEqual(HttpStatusCode.Created, rsp.StatusCode);
        Assert.True(
            rsp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Expected 403 or 400, got {(int)rsp.StatusCode}.");
        Assert.Equal(0, await CountAssignmentsAsync(TargetEmp));
    }

    /// <summary>
    /// Escalation B: granting <c>{roleId:'GLOBAL_ADMIN', scopeType:'ORG_ONLY', orgId:'STY01'}</c>.
    /// AuthEndpoints.MapRoleIdToName maps GLOBAL_ADMIN → the JWT primary role, and GlobalAdminOnly
    /// checks the role, not the scope — so a GLOBAL_ADMIN row with a non-GLOBAL scope still mints
    /// an effective GlobalAdmin on the holder's next login. Must be rejected (not 201), no row.
    /// </summary>
    [Fact]
    public async Task Escalation_B_GlobalAdminRole_WithNonGlobalScope_IsRejected_NoRowMinted()
    {
        var client = GlobalAdminClient(); // even a legitimate GlobalAdmin cannot mint this shape

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "GLOBAL_ADMIN",
            orgId = OrgStorelse,
            scopeType = "ORG_ONLY",
        });

        Assert.NotEqual(HttpStatusCode.Created, rsp.StatusCode);
        Assert.True(
            rsp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Expected 403 or 400, got {(int)rsp.StatusCode}.");
        Assert.Equal(0, await CountAssignmentsAsync(TargetEmp));
    }

    /// <summary>
    /// Privilege-hierarchy: a LocalAdmin (level 2) granting GLOBAL_ADMIN (level 1) — even with the
    /// correct GLOBAL/no-org shape — is denied (the existing hierarchy check + the GLOBAL_ADMIN
    /// role↔scope guard both refuse a non-GlobalAdmin actor). Must be rejected (not 201), no row.
    /// </summary>
    [Fact]
    public async Task Hierarchy_LocalAdmin_GrantsGlobalAdmin_GlobalScope_IsForbidden_NoRowMinted()
    {
        var client = LocalAdminClient();

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "GLOBAL_ADMIN",
            orgId = (string?)null,
            scopeType = "GLOBAL",
        });

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal(0, await CountAssignmentsAsync(TargetEmp));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S93 / ADR-035 slice 2 — flat role-scope grant guards (RED on pre-S93 code)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S93 (a): the grant endpoint REJECTS an ORG_AND_DESCENDANTS scopeType with 400 (the enum
    /// collapsed to {GLOBAL, ORG_ONLY}). RED on pre-S93 code: ORG_AND_DESCENDANTS was an accepted
    /// scopeType and the row minted (201).
    /// </summary>
    [Fact]
    public async Task Grant_OrgAndDescendantsScopeType_IsRejected_400_NoRowMinted()
    {
        var client = GlobalAdminClient(); // a fully-privileged actor — the rejection is scope-shape, not authz

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_LEADER",
            orgId = OrgStorelse,
            scopeType = "ORG_AND_DESCENDANTS",
        });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Equal(0, await CountAssignmentsAsync(TargetEmp));
    }

    /// <summary>
    /// S93 OQ1 (b): an ORG_ONLY grant whose org_id resolves to a MAO (org_type='MAO', e.g. MIN01)
    /// is REJECTED with 400 — a MAO is not an authority unit (a MAO-typed scope confers
    /// org-structure admin, not inert roster reach). RED on pre-S93 code: the MAO org_id passed
    /// (ORG_AND_DESCENDANTS over a MAO covered the whole subtree) and the row minted.
    /// The actor is GlobalAdmin so it clears ValidateOrgAccessAsync — the rejection is the OQ1
    /// org-type gate, not authorization.
    /// </summary>
    [Fact]
    public async Task Grant_OrgOnly_OnMaoOrgId_IsRejected_400_NoRowMinted()
    {
        var client = GlobalAdminClient();

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_HR",
            orgId = "MIN01",            // a MAO (org_type='MAO'), not an ORGANISATION
            scopeType = "ORG_ONLY",
        });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Equal(0, await CountAssignmentsAsync(TargetEmp));
    }

    /// <summary>
    /// S93 OQ1 (b, positive control): an ORG_ONLY grant whose org_id is an ORGANISATION (STY01) is
    /// ACCEPTED (201) — proves the OQ1 gate rejects only MAOs, not every non-GLOBAL grant.
    /// </summary>
    [Fact]
    public async Task Grant_OrgOnly_OnOrganisationOrgId_Returns201()
    {
        var client = GlobalAdminClient();

        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId = TargetEmp,
            roleId = "LOCAL_HR",
            orgId = OrgStorelse,        // STY01 — an ORGANISATION under MIN01
            scopeType = "ORG_ONLY",
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = body.GetProperty("assignmentId").GetGuid();
        var (_, orgId, scopeType, isActive) = await ReadAssignmentAsync(assignmentId);
        Assert.Equal(OrgStorelse, orgId);
        Assert.Equal("ORG_ONLY", scopeType);
        Assert.True(isActive);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup / helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp,    @emp,    '$2a$11$fake', 'T85 Target',  't85_target@test.dk', @org, 'AC', 'OK24', TRUE),
                (@ladmin, @ladmin, '$2a$11$fake', 'T85 LAdmin',  't85_ladmin@test.dk', @org, 'AC', 'OK24', TRUE),
                (@gadmin, @gadmin, '$2a$11$fake', 'T85 GAdmin',  't85_gadmin@test.dk', 'MIN01', 'AC', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", TargetEmp);
            cmd.Parameters.AddWithValue("ladmin", LocalAdminActor);
            cmd.Parameters.AddWithValue("gadmin", GlobalAdminActor);
            cmd.Parameters.AddWithValue("org", OrgStorelse);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        // Drop any audit rows pointing at our users' assignments, then the assignments, then users.
        await ExecAsync(conn,
            "DELETE FROM role_assignment_audit WHERE assignment_id IN (SELECT assignment_id FROM role_assignments WHERE user_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        // Backend.Api host seeders back-fill employee_profiles + user_agreement_codes per user.
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<(string RoleId, string? OrgId, string ScopeType, bool IsActive)> ReadAssignmentAsync(Guid assignmentId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT role_id, org_id, scope_type, is_active FROM role_assignments WHERE assignment_id = @id", conn);
        cmd.Parameters.AddWithValue("id", assignmentId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "role_assignments row not found");
        var roleId = reader.GetString(0);
        var orgId = reader.IsDBNull(1) ? null : reader.GetString(1);
        var scopeType = reader.GetString(2);
        var isActive = reader.GetBoolean(3);
        return (roleId, orgId, scopeType, isActive);
    }

    private async Task<(string Action, string ActorId, string ActorRole, string DetailsJson)> ReadLatestAuditAsync(Guid assignmentId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT action, actor_id, actor_role, details::text FROM role_assignment_audit WHERE assignment_id = @id ORDER BY audit_id DESC LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", assignmentId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "role_assignment_audit row not found");
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private async Task<int> CountAssignmentsAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM role_assignments WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private HttpClient LocalAdminClient()
    {
        var client = _factory.CreateClient();
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgStorelse, "ORG_ONLY") };
        var token = NewTokenService().GenerateToken(
            employeeId: LocalAdminActor, name: LocalAdminActor, role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC", orgId: OrgStorelse, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var scopes = new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") };
        var token = NewTokenService().GenerateToken(
            employeeId: GlobalAdminActor, name: GlobalAdminActor, role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: "MIN01", scopes: scopes);
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
