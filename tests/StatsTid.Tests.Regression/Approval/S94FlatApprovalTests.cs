using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S94 / ADR-035 slice 3 (OQ4/OQ5/OQ6) — the FLAT approval-authority model end-to-end:
/// <b><c>CanApprove(actor, emp) = IsEffectiveDesignatedApprover (edge) OR HasHrAdminScopeOverEmpOrg</c></b>
/// (HR/Admin scoped over the employee's CURRENT Organisation). The unfloored leader-by-org-scope
/// branch is RETIRED, and REQUIRED-mode enforcement (the 428 <c>ORG_SCOPE_FALLBACK</c> gate /
/// <c>confirmFallback</c>) is GONE — an HR/Admin fallback approval is a plain 200, never a 428.
///
/// <para><b>RED-on-old.</b> Pre-S94 the approve/reject org-scope gate was
/// <c>ValidateOrgAccessAsync(actor, period.OrgId)</c> with roleFloor <c>null</c> — so ANY in-scope
/// LEADER (no edge) could approve via org-scope (a 428→confirmFallback→200 round-trip in REQUIRED
/// mode). S94 floors that gate at <c>LocalHR</c>
/// (<c>ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR)</c>):
/// <list type="bullet">
/// <item><description>(a) a non-designated in-scope LEADER is now DENIED approve AND reject (was a
/// 428/200) — the inversion;</description></item>
/// <item><description>(b) a LocalHR / LocalAdmin scoped over the employee's CURRENT Organisation
/// approves with NO edge and NO 428;</description></item>
/// <item><description>(c) an out-of-scope HR (scoped over a DIFFERENT Organisation) is DENIED;</description></item>
/// <item><description>(d) a designated leader (holds the PRIMARY edge) still approves (no regression);</description></item>
/// <item><description>(e) the reopen LEADER arm follows the same floor (a non-designated leader can't
/// reopen via org-scope; HR can);</description></item>
/// <item><description>(f) an ORPHAN employee (no PRIMARY edge) is approvable by in-scope HR and DENIED
/// to a non-designated in-scope leader (NOTE 4 — orphans route to HR/Admin).</description></item>
/// </list>
/// </para>
///
/// <para>Topology (init.sql seed orgs, post-S92 flatten): STY02 is an ORGANISATION under MAO MIN01;
/// STY05 is an ORGANISATION under a DIFFERENT MAO MIN02 (disjoint). Every actor + employee here is a
/// fresh test user (distinct from the seed + the S74/S78 fixtures); the HR/Admin actors carry a single
/// ORG_ONLY scope (S93 flat role-scope) over their home Organisation.</para>
///
/// <para>Endpoint-level integration via <see cref="StatsTidWebApplicationFactory"/> (the real Backend.Api
/// over a fresh testcontainer); mirrors <see cref="DesignatedApproverAuthorityTests"/>
/// idioms (WAF + token minting + direct period inserts).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S94FlatApprovalTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // STY02 Organisation (MAO MIN01):
    private const string Emp = "s94_emp";        // STY02 — the report (PRIMARY edge → Mgr)
    private const string Orphan = "s94_orphan";  // STY02 — NO PRIMARY edge (the orphan)
    private const string Mgr = "s94_mgr";        // STY02 — Emp's designated PRIMARY manager (a Leader)
    private const string Leader = "s94_leader";  // STY02 — a non-designated in-scope Leader (no edge)
    private const string Hr = "s94_hr";          // STY02 — LocalHR over STY02 (the fallback)
    private const string Admin = "s94_admin";    // STY02 — LocalAdmin over STY02 (the fallback)
    // STY05 Organisation (a DIFFERENT MAO MIN02 — disjoint):
    private const string HrOos = "s94_hr_oos";   // STY05 — LocalHR over a DIFFERENT Organisation

    private const string TreeRootSty02 = "STY02";

    private static readonly string[] AllUsers = { Emp, Orphan, Mgr, Leader, Hr, Admin, HrOos };

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);

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
    //  (a) RED-ON-OLD — a non-designated in-scope LEADER is now DENIED approve AND reject
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A LEADER scoped over STY02 (Leader, holding NO reporting edge over Emp) is DENIED the approve.
    /// Pre-S94 the unfloored leader-org-scope branch admitted this as an ORG_SCOPE_FALLBACK
    /// (428→confirmFallback→200); S94 floors the gate at LocalHR → a leader is below the floor and
    /// holds no edge → 403. The period stays SUBMITTED.
    /// </summary>
    [Fact]
    public async Task NonDesignatedInScopeLeader_IsDenied_Approve_RedOnOld()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = LeaderClient(Leader, "STY02");

        var rsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// The reject mirror of (a): the same non-designated in-scope LEADER is DENIED the reject (was an
    /// org-scope-fallback approval pre-S94). The period stays SUBMITTED.
    /// </summary>
    [Fact]
    public async Task NonDesignatedInScopeLeader_IsDenied_Reject_RedOnOld()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = LeaderClient(Leader, "STY02");

        var rsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (b) HR / Admin scoped over the employee's CURRENT Organisation approve — NO edge, NO 428
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A LocalHR scoped over STY02 (the employee's CURRENT Organisation) approves Emp's period with NO
    /// reporting edge — the HR/Admin fallback. The response is a plain 200 (NOT a 428; REQUIRED-mode
    /// enforcement is retired). The persisted classification is ORG_SCOPE_FALLBACK (audit retained).
    /// </summary>
    [Fact]
    public async Task InScopeHr_Approves_NoEdge_No428()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = AdminRoleClient(StatsTidRoles.LocalHR, Hr, "STY02");

        var rsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadColumnAsync(periodId, "approval_method"));
    }

    /// <summary>
    /// A LocalAdmin scoped over STY02 approves Emp's period with NO reporting edge — the HR/Admin
    /// fallback admits an admin too. Plain 200, NO 428.
    /// </summary>
    [Fact]
    public async Task InScopeAdmin_Approves_NoEdge_No428()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = AdminRoleClient(StatsTidRoles.LocalAdmin, Admin, "STY02");

        var rsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (c) An out-of-scope HR (scoped over a DIFFERENT Organisation) is DENIED
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A LocalHR scoped over STY05 (a DIFFERENT, disjoint Organisation) is DENIED Emp's STY02 period —
    /// the HR/Admin fallback is bound to the employee's CURRENT Organisation (the floored
    /// ValidateEmployeeAccessAsync requires the admitting scope to COVER the target org). Containment
    /// preserved → 403, period stays SUBMITTED.
    /// </summary>
    [Fact]
    public async Task OutOfScopeHr_IsDenied()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = AdminRoleClient(StatsTidRoles.LocalHR, HrOos, "STY05");

        var rsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (d) A designated leader (holds the edge) STILL approves — no regression
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mgr is Emp's designated PRIMARY manager (the edge). Mgr approves via the edge arm (the edge
    /// classifies it DESIGNATED_MANAGER) → 200. The edge arm is unchanged by S94 — the floor only
    /// narrows the OTHER (org-scope) arm.
    /// </summary>
    [Fact]
    public async Task DesignatedLeader_WithEdge_StillApproves_NoRegression()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = LeaderClient(Mgr, "STY02");

        var rsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
        Assert.Equal("DESIGNATED_MANAGER", await ReadColumnAsync(periodId, "approval_method"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (e) The reopen LEADER arm follows the SAME floor
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The reopen LEADER arm follows the same flat floor: a non-designated in-scope LEADER cannot reopen
    /// an APPROVED period via org-scope (no edge, below the LocalHR floor) → 403; an in-scope HR (the
    /// fallback) CAN reopen it → 200 → DRAFT. Proves the OQ4 floor reaches the reopen leader arm too.
    /// </summary>
    [Fact]
    public async Task ReopenLeaderArm_FollowsFloor_LeaderDenied_HrAllowed()
    {
        // Leg 1: a non-designated leader is denied the reopen via org-scope.
        var p1 = await InsertPeriodWithRangeAsync(
            Emp, "STY02", "APPROVED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var leaderClient = LeaderClient(Leader, "STY02");
        var leaderRsp = await leaderClient.PostAsJsonAsync($"/api/approval/{p1}/reopen", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(p1));

        // Leg 2: an in-scope HR (the fallback) reopens the same period → DRAFT.
        var hrClient = AdminRoleClient(StatsTidRoles.LocalHR, Hr, "STY02");
        var hrRsp = await hrClient.PostAsJsonAsync($"/api/approval/{p1}/reopen", new { reason = "hr-reopen" });
        Assert.Equal(HttpStatusCode.OK, hrRsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(p1));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (f) An ORPHAN employee (no PRIMARY edge) — approvable by in-scope HR, DENIED to a leader
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Orphan has NO PRIMARY reporting edge (no designated approver). Under the flat model the orphan's
    /// period routes to the HR/Admin fallback: an in-scope LocalHR approves it (NO edge needed, NO 428),
    /// while a non-designated in-scope LEADER is DENIED (neither an edge nor — below the floor —
    /// org-scope admits). This is NOTE 4: orphans route to HR/Admin.
    /// </summary>
    [Fact]
    public async Task Orphan_NoEdge_ApprovableByInScopeHr_DeniedToInScopeLeader()
    {
        // The non-designated in-scope LEADER is DENIED the orphan's period (no edge, below the floor).
        var pLeader = await InsertPeriodWithRangeAsync(
            Orphan, "STY02", "SUBMITTED", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var leaderClient = LeaderClient(Leader, "STY02");
        var leaderRsp = await leaderClient.PostAsync($"/api/approval/{pLeader}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pLeader));

        // The in-scope HR fallback APPROVES the orphan's period → 200.
        var pHr = await InsertPeriodWithRangeAsync(
            Orphan, "STY02", "SUBMITTED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var hrClient = AdminRoleClient(StatsTidRoles.LocalHR, Hr, "STY02");
        var hrRsp = await hrClient.PostAsync($"/api/approval/{pHr}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hrRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pHr));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp,    @emp,    '$2a$11$fake', 'S94 Emp',    's94_emp@test.dk',    'STY02', 'HK', 'OK24', TRUE),
                (@orphan, @orphan, '$2a$11$fake', 'S94 Orphan', 's94_orphan@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@mgr,    @mgr,    '$2a$11$fake', 'S94 Mgr',    's94_mgr@test.dk',    'STY02', 'HK', 'OK24', TRUE),
                (@leader, @leader, '$2a$11$fake', 'S94 Leader', 's94_leader@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@hr,     @hr,     '$2a$11$fake', 'S94 Hr',     's94_hr@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@admin,  @admin,  '$2a$11$fake', 'S94 Admin',  's94_admin@test.dk',  'STY02', 'AC', 'OK24', TRUE),
                (@hroos,  @hroos,  '$2a$11$fake', 'S94 HrOos',  's94_hr_oos@test.dk', 'STY05', 'AC', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@mgr,    'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@leader, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@hr,     'LOCAL_HR',     'STY02', 'ORG_ONLY', 'TEST'),
                (@admin,  'LOCAL_ADMIN',  'STY02', 'ORG_ONLY', 'TEST'),
                (@hroos,  'LOCAL_HR',     'STY05', 'ORG_ONLY', 'TEST'),
                (@emp,    'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST'),
                (@orphan, 'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the designated edge. Orphan has NO edge.
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, MakeLine(Emp, Mgr, TreeRootSty02));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("orphan", Orphan);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("leader", Leader);
        cmd.Parameters.AddWithValue("hr", Hr);
        cmd.Parameters.AddWithValue("admin", Admin);
        cmd.Parameters.AddWithValue("hroos", HrOos);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId, string treeRoot) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = treeRoot,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            "DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Period / read helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private Task<Guid> InsertPeriodAsync(string employeeId, string orgId, string status)
        => InsertPeriodWithRangeAsync(employeeId, orgId, status, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

    private async Task<Guid> InsertPeriodWithRangeAsync(
        string employeeId, string orgId, string status, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<string> ReadStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Reads a single (string) column of the period row. The column name is a fixed
    /// test-local literal (never user input), so direct interpolation is safe here.</summary>
    private async Task<string?> ReadColumnAsync(Guid periodId, string column)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT {column} FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient LeaderClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(userId, orgId));
        return client;
    }

    /// <summary>A single-scope admin client (LocalHR / LocalAdmin / GlobalAdmin) anchored at
    /// <paramref name="orgId"/> (ORG_ONLY, S93 flat role-scope). The scope's Role IS the floored
    /// admin role so the floored <c>ValidateEmployeeAccessAsync(LocalHR)</c> admits it.</summary>
    private HttpClient AdminRoleClient(string role, string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminRoleToken(role, userId, orgId));
        return client;
    }

    private static string MintLeaderToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintAdminRoleToken(string role, string userId, string orgId)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(role, orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "AC", orgId: orgId, scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
