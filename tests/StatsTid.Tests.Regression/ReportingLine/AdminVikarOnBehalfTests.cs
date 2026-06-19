using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.ReportingLine;

/// <summary>
/// S76 / ADR-027 Phase 5 (SPRINT-76 R-7601, TASK-7601) — the admin-on-behalf vikar (stand-in)
/// endpoint <c>POST/DELETE /api/admin/reporting-lines/{managerId}/vikar</c> and the D15
/// lock-discipline hardening on the self-<c>/delegate</c>. This is an ADR-027 D13/D14 AUTHORIZATION
/// surface: the resulting <c>manager_vikar</c> row GRANTS the vikar approve/reject/reopen authority
/// over the absent manager's reports, so the full create-authority contract is asserted
/// discriminatingly.
///
/// <para>
/// <b>Topology (init.sql seed orgs):</b> STY02 tree = {STY02 root, AFD01, AFD02} under MIN01;
/// STY05 tree = {STY05 root, AFD03, AFD04} under MIN02. The admin actor (<c>t76_admin</c>) sits at
/// MIN01 with a LOCAL_ADMIN scope over MIN01 (ORG_AND_DESCENDANTS) — so it COVERS STY02 yet its
/// primary org (MIN01) is NOT the manager's tree root (STY02): the CROSS-ORG fixture that
/// discriminates the audit row's actor org from the target tree (the S71 green-but-weak lesson).
/// </para>
///
/// <para>
/// Endpoint-level via <see cref="StatsTidWebApplicationFactory"/> (the real Backend.Api over a fresh
/// testcontainer). Direct DB reads for the manager_vikar + audit_projection assertions.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminVikarOnBehalfTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private ReportingLineRepository _rlRepo = null!;
    private ManagerVikarRepository _vikarRepo = null!;

    // ── STY02 tree users ──
    private const string Admin = "t76_admin";      // MIN01 — LOCAL_ADMIN @ MIN01 (covers STY02; org ≠ STY02)
    private const string AdminX = "t76_admin_x";   // STY05 — LOCAL_ADMIN @ STY05 (does NOT cover STY02)
    private const string Mgr = "t76_mgr";          // AFD02 — the absent manager (LOCAL_LEADER)
    private const string Emp = "t76_emp";          // AFD01 — reports PRIMARY to Mgr
    private const string Sub = "t76_sub";          // AFD01 — reports PRIMARY to Emp (Mgr's descendant)
    private const string Vik = "t76_vik";          // AFD02 — valid vikar, LOCAL_LEADER @ STY02 (covers AFD01)
    private const string VikNarrow = "t76_vik_n";  // AFD02 — LOCAL_LEADER @ AFD02 ONLY (does NOT cover AFD01)
    // ── STY05 (cross-tree) ──
    private const string VikX = "t76_vik_x";       // STY05 — LOCAL_LEADER @ STY05 (different tree_root)

    private const string TreeRootSty02 = "STY02";
    private const string TreeRootSty05 = "STY05";

    private static readonly string[] AllUsers =
        { Admin, AdminX, Mgr, Emp, Sub, Vik, VikNarrow, VikX };

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _vikarRepo = new ManagerVikarRepository(_dbFactory);
        _rlRepo = new ReportingLineRepository(_dbFactory, _vikarRepo);

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
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@admin,  @admin,  '$2a$11$fake', 'T76 Admin',  't76_admin@test.dk',  'MIN01', 'AC', 'OK24', TRUE),
                (@adminx, @adminx, '$2a$11$fake', 'T76 AdminX', 't76_admin_x@test.dk','STY05', 'HK', 'OK24', TRUE),
                (@mgr,    @mgr,    '$2a$11$fake', 'T76 Mgr',    't76_mgr@test.dk',    'AFD02', 'HK', 'OK24', TRUE),
                (@emp,    @emp,    '$2a$11$fake', 'T76 Emp',    't76_emp@test.dk',    'AFD01', 'HK', 'OK24', TRUE),
                (@sub,    @sub,    '$2a$11$fake', 'T76 Sub',    't76_sub@test.dk',    'AFD01', 'HK', 'OK24', TRUE),
                (@vik,    @vik,    '$2a$11$fake', 'T76 Vik',    't76_vik@test.dk',    'AFD02', 'HK', 'OK24', TRUE),
                (@vikn,   @vikn,   '$2a$11$fake', 'T76 VikN',   't76_vik_n@test.dk',  'AFD02', 'HK', 'OK24', TRUE),
                (@vikx,   @vikx,   '$2a$11$fake', 'T76 VikX',   't76_vik_x@test.dk',  'STY05', 'HK', 'OK24', TRUE)
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
                (@admin,  'LOCAL_ADMIN',  'MIN01', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@adminx, 'LOCAL_ADMIN',  'STY05', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@mgr,    'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@vik,    'LOCAL_LEADER', 'STY02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@vikn,   'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@vikx,   'LOCAL_LEADER', 'STY05', 'ORG_AND_DESCENDANTS', 'TEST'),
                -- B4 (S76-7601 fix-forward): VikX's PRIMARY org is STY05 (a DIFFERENT tree), but this
                -- extra STY02-scoped LOCAL_LEADER grant makes its org-scope COVER the manager's AFD01
                -- report (path /MIN01/STY02/AFD01/ StartsWith /MIN01/STY02/). So the cross-tree-vikar
                -- POST now PASSES the coverage census and is rejected SPECIFICALLY by the same-tree
                -- guard (VikX's PRIMARY org STY05 resolves to tree root STY05 != STY02) — exercising
                -- the S74-7402 cross-styrelse-via-vikar defense, not the coverage gate.
                (@vikx,   'LOCAL_LEADER', 'STY02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@sub,    'LOCAL_LEADER', 'STY02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@emp,    'EMPLOYEE',     'AFD01', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (AFD01) reports PRIMARY to Mgr (AFD02) — the manager's report (cross-afdeling, same tree).
        await _rlRepo.AssignAsync(null, MakeLine(Emp, Mgr));
        // Sub (AFD01) reports PRIMARY to Emp → Sub is a DESCENDANT of Mgr (the cycle fixture).
        await _rlRepo.AssignAsync(null, MakeLine(Sub, Emp));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("admin", Admin);
        cmd.Parameters.AddWithValue("adminx", AdminX);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("sub", Sub);
        cmd.Parameters.AddWithValue("vik", Vik);
        cmd.Parameters.AddWithValue("vikn", VikNarrow);
        cmd.Parameters.AddWithValue("vikx", VikX);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = TreeRootSty02,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            """
            DELETE FROM audit_projection
            WHERE event_type IN ('ManagerVikarCreated','ManagerVikarEnded')
              AND (actor_id = ANY(@ids)
                   OR target_resource_id IN (
                       SELECT vikar_id::text FROM manager_vikar
                       WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)))
            """);
        await ExecStreamsAsync(conn);
        await ExecAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecAsync(conn,
            "DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
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

        async Task ExecStreamsAsync(NpgsqlConnection c)
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM outbox_events WHERE stream_id = ANY(@streams)", c);
            cmd.Parameters.AddWithValue("streams", AllUsers.Select(id => $"reporting-line-{id}").ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Deliverable A — admin-on-behalf POST
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminPost_HappyPath_CreatesVikar_AndAuditRow()
    {
        var effectiveTo = Today().AddDays(30);
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = effectiveTo.ToString("yyyy-MM-dd"), reason = "FERIE" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // B5: assert the FULL response contract — id, both date fields, reason (not just status).
        var body = await rsp.Content.ReadFromJsonAsync<AdminVikarResponse>();
        Assert.NotNull(body);
        Assert.Equal(Mgr, body!.managerId);
        Assert.Equal(Vik, body.vikarUserId);
        Assert.Equal("FERIE", body.reason);
        Assert.Equal(Today().ToString("yyyy-MM-dd"), body.effectiveFrom);   // effectiveFrom = today
        Assert.Equal(effectiveTo.ToString("yyyy-MM-dd"), body.effectiveTo);
        Assert.False(string.IsNullOrWhiteSpace(body.vikarId));

        // The manager_vikar row exists (active), keyed on the manager.
        var row = await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr);
        Assert.NotNull(row);
        Assert.Equal(Vik, row!.VikarUserId);
        Assert.Equal(TreeRootSty02, row.TreeRootOrgId);
        Assert.Equal("FERIE", row.Reason);
        Assert.Equal(body.vikarId, row.VikarId.ToString());

        // Both the event (outbox) AND the audit row exist (the S74-7401 dead-mapper lesson).
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'ManagerVikarCreated'",
            ("s", $"reporting-line-{Mgr}")) >= 1);
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'ManagerVikarCreated' AND target_resource_id = @id",
            ("id", row.VikarId.ToString())) == 1);
    }

    [Fact]
    public async Task AdminPost_CrossOrgAdmin_AuditRow_CarriesAdminOrg_NotManagerTree()
    {
        // The CROSS-ORG discriminator (S71): Admin's org is MIN01, the manager's tree root is STY02.
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var row = await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr);
        Assert.NotNull(row);

        // The audit row's ACTOR org = the ADMIN's MIN01 (not STY02); the TARGET org = the manager's
        // STY02 tree. A same-org fixture could not tell these apart.
        var (actorOrg, targetOrg, actorId) = await ReadAuditAttributionAsync(row!.VikarId);
        Assert.Equal(Admin, actorId);
        Assert.Equal("MIN01", actorOrg);
        Assert.Equal("STY02", targetOrg);
    }

    [Fact]
    public async Task AdminPost_CrossTreeVikar_RejectedBySameTreeGuard_NotCoverage_Returns400()
    {
        // B4 (S76-7601 fix-forward): VikX's PRIMARY org is STY05 (a DIFFERENT styrelse tree) but it
        // ALSO carries a STY02-scoped LOCAL_LEADER grant, so its org-scope DOES cover the manager's
        // AFD01 report. Coverage therefore PASSES — the ONLY thing that can reject this is the in-tx
        // same-tree guard (ValidateSameTreeAsync resolves VikX's PRIMARY org STY05 → tree root STY05
        // != STY02 → CrossTreeAssignmentException → 400). This discriminatingly exercises the
        // S74-7402 cross-styrelse-via-vikar defense (a non-discriminating fixture would be rejected by
        // coverage first, never reaching the same-tree arm).
        // Sanity: confirm VikX WOULD pass coverage (the discriminating-fixture precondition).
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM role_assignments WHERE user_id = @id AND org_id = 'STY02'",
            ("id", VikX)) >= 1);

        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = VikX, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        // The rejection is the same-tree (styrelse) message, NOT the coverage message.
        var err = await rsp.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(err);
        Assert.Contains("styrelse", err!.error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task AdminPost_SubordinateOfManager_AsVikar_RejectedAsCycle()
    {
        // Sub is a descendant of Mgr (Sub → Emp → Mgr). Sub is same-tree + leader + covers the
        // report (STY02 scope), so ONLY the cycle guard can reject it → 400.
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Sub, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task AdminPost_VikarScopeDoesNotCoverAllReports_Rejected()
    {
        // VikNarrow's scope is AFD02 ONLY; the manager's report (Emp) is in AFD01 → not covered.
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = VikNarrow, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);

        // B5: assert the EXACT error body — the coverage message + the uncovered report list.
        var err = await rsp.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(err);
        Assert.Equal("Vikar's org-scope does not cover all of the manager's reports", err!.error);
        Assert.NotNull(err.uncoveredEmployeeIds);
        Assert.Contains(Emp, err.uncoveredEmployeeIds!);
        Assert.Equal(1, err.uncoveredCount);

        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task AdminPost_ActorWithoutAdminScopeOverManager_Returns403()
    {
        // AdminX is a LOCAL_ADMIN over STY05 only — it does NOT cover STY02 (the manager's tree).
        var client = AdminClient(AdminX, "STY05");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task AdminPost_SecondActiveVikar_Returns409()
    {
        var client = AdminClient(Admin, "MIN01");
        var first = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = Today().AddDays(60).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AdminPost_InLockCensus_SeesConcurrentRoleRevocation_Rejects()
    {
        // S76-7601 fix-forward (Step-5a c1 B1) — the held-lock interleave (mirrors S74-7403). The
        // vikar-eligibility/coverage census runs IN-LOCK (after the tree advisory lock), so a
        // mutation that revokes the vikar's qualifying scope and commits WHILE the POST is parked on
        // the advisory MUST be seen by the census → 400. On a PRE-lock snapshot the POST would have
        // succeeded (Vik holds the covering STY02 LOCAL_LEADER role at POST time).
        //
        // Interleave:
        //   T_block: take pg_advisory_xact_lock('reporting-tree-STY02') + DELETE Vik's only
        //            qualifying role assignment — held UNCOMMITTED.
        //   POST   : fired async; blocks inside AcquireTreeLockAsync on the same advisory key.
        //   commit : T_block commits → Vik now has NO qualifying role.
        //   POST   : acquires the lock, the in-lock census reads Vik's (now empty) roles → 400.
        var advisoryKey = "reporting-tree-" + TreeRootSty02;

        await using var blockConn = _dbFactory.Create();
        await blockConn.OpenAsync();
        await using var blockTx = await blockConn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        // T_block holds the tree advisory lock.
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext(@k))", blockConn, blockTx))
        {
            lockCmd.Parameters.AddWithValue("k", advisoryKey);
            await lockCmd.ExecuteScalarAsync();
        }
        // ... and revokes Vik's qualifying role (uncommitted for now).
        await using (var revokeCmd = new NpgsqlCommand(
            "DELETE FROM role_assignments WHERE user_id = @vik", blockConn, blockTx))
        {
            revokeCmd.Parameters.AddWithValue("vik", Vik);
            await revokeCmd.ExecuteNonQueryAsync();
        }

        // Fire the POST — it must BLOCK on the advisory (T_block holds it).
        var client = AdminClient(Admin, "MIN01");
        var postTask = client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });

        // Give the POST time to reach + park on the advisory lock, then prove it has NOT completed
        // (it is blocked, not finished) before we release.
        await Task.Delay(750);
        Assert.False(postTask.IsCompleted, "POST must block on the tree advisory lock until T_block commits");

        // Release the revocation — the POST proceeds and its IN-LOCK census now sees no role.
        await blockTx.CommitAsync();

        var rsp = await postTask;
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var err = await rsp.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(err);
        Assert.Contains("qualifying role", err!.error, StringComparison.OrdinalIgnoreCase);
        // No vikar was committed (the in-lock census rejected before the INSERT).
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Deliverable A — admin-on-behalf DELETE (revoke-safe)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminDelete_RevokesVikar_EmitsEndedEvent_AndAudit()
    {
        var vikar = await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        var client = AdminClient(Admin, "MIN01");

        var rsp = await client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Closed (no active row remains).
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'ManagerVikarEnded' AND target_resource_id = @id",
            ("id", vikar.ToString())) == 1);

        // B5: the ManagerVikarEnded audit row carries the ADMIN actor + the ADMIN's org (MIN01),
        // while the TARGET org is the manager's STY02 tree — the cross-org discriminator (S71). A
        // same-org fixture (admin-org == manager-tree) could not tell actor-org from target-org apart.
        var (actorOrg, targetOrg, actorId) = await ReadAuditAttributionAsync(vikar, "ManagerVikarEnded");
        Assert.Equal(Admin, actorId);
        Assert.Equal("MIN01", actorOrg);
        Assert.Equal("STY02", targetOrg);
    }

    [Fact]
    public async Task AdminDelete_RevokeSafe_WhenManagerAndVikarInactive_StillSucceeds()
    {
        var vikar = await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        // Deactivate BOTH the manager and the vikar — ValidateSameTreeAsync would now fail, but the
        // revoke must still succeed via the persisted manager_vikar.tree_root_org_id.
        await DeactivateUserAsync(Mgr);
        await DeactivateUserAsync(Vik);

        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'ManagerVikarEnded' AND target_resource_id = @id",
            ("id", vikar.ToString())) == 1);
    }

    [Fact]
    public async Task AdminDelete_ActorWithoutAdminScope_Returns403()
    {
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        // AdminX covers STY05 only — not STY02 (the persisted tree root) → 403.
        var client = AdminClient(AdminX, "STY05");
        var rsp = await client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        // Still active (no mutation).
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task AdminDelete_NoActiveRow_Returns404()
    {
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S76b / TASK-7603 (BLOCKER 3) — the SINGLE-manager active-vikar GET. The unified
    //  EditPersonDrawer (opened from the UserManagement LIST, no tree context) needs this
    //  to surface + revoke an away-manager's vikar.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminGetVikar_ActiveRow_ReturnsVikarWithDisplayName()
    {
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        var client = AdminClient(Admin, "MIN01");

        var rsp = await client.GetAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<ActiveVikarEnvelope>();
        Assert.NotNull(body);
        Assert.NotNull(body!.activeVikar);
        Assert.Equal(Vik, body.activeVikar!.vikarUserId);
        // The display name is JOINed from the users row (the roster's outgoingVikar shape).
        Assert.Equal("T76 Vik", body.activeVikar.vikarDisplayName);
        Assert.Equal("ANDET", body.activeVikar.reason);
    }

    [Fact]
    public async Task AdminGetVikar_NoActiveRow_ReturnsNull()
    {
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<ActiveVikarEnvelope>();
        Assert.NotNull(body);
        Assert.Null(body!.activeVikar);
    }

    [Fact]
    public async Task AdminGetVikar_ActorWithoutAdminScope_Returns403()
    {
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        // AdminX covers STY05 only — not the manager's STY02 primary org (AFD02) → 403.
        var client = AdminClient(AdminX, "STY05");
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/{Mgr}/vikar");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task AdminGetVikar_ManagerNotFound_Returns404()
    {
        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.GetAsync("/api/admin/reporting-lines/t76_nonexistent/vikar");
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Deliverable B — /delegate D15 hardening + byte-stability
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SelfDelegate_ContractStable_ToNonDescendantLeader_StillSucceeds()
    {
        // Mgr self-delegates to Vik (a non-descendant same-tree leader covering Mgr's report).
        var effectiveTo = Today().AddDays(20);
        var client = LeaderClient(Mgr, "AFD02");
        var rsp = await client.PostAsJsonAsync(
            "/api/reporting-lines/delegate",
            new { actingManagerId = Vik, effectiveTo = effectiveTo.ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        // The byte-stable response shape: delegatedCount / skippedCount / actingManagerId / dates.
        // B5: assert the date fields too (the in-lock census must not change the contract).
        var body = await rsp.Content.ReadFromJsonAsync<DelegateResponse>();
        Assert.NotNull(body);
        Assert.Equal(Vik, body!.actingManagerId);
        Assert.Equal(1, body.delegatedCount);   // covers Mgr's one report (Emp), no admin ACTING
        Assert.Equal(0, body.skippedCount);
        Assert.Equal(Today().ToString("yyyy-MM-dd"), body.effectiveFrom);
        Assert.Equal(effectiveTo.ToString("yyyy-MM-dd"), body.effectiveTo);

        // The manager_vikar row was created keyed on Mgr (= absent approver).
        var row = await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr);
        Assert.NotNull(row);
        Assert.Equal(Vik, row!.VikarUserId);
    }

    [Fact]
    public async Task SelfDelegate_ToDescendant_NowRejectedAsCycle()
    {
        // Sub is a descendant of Mgr; the NEW D15 cycle guard rejects it (a subordinate cannot
        // stand in for their own manager). Sub is same-tree + covers Mgr's report, so only the
        // cycle guard fires → 400.
        var client = LeaderClient(Mgr, "AFD02");
        var rsp = await client.PostAsJsonAsync(
            "/api/reporting-lines/delegate",
            new { actingManagerId = Sub, effectiveTo = Today().AddDays(20).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task SelfDelegate_SecondActive_Returns409_ContractStable()
    {
        var client = LeaderClient(Mgr, "AFD02");
        var first = await client.PostAsJsonAsync(
            "/api/reporting-lines/delegate",
            new { actingManagerId = Vik, effectiveTo = Today().AddDays(20).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/reporting-lines/delegate",
            new { actingManagerId = Vik, effectiveTo = Today().AddDays(25).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SelfDelegate_NoReports_AND_ActiveVikar_Returns400_NotConflict_ContractStable()
    {
        // S76 / TASK-7601 Step-7a c1 BLOCKER — the /delegate error-contract byte-stability regression.
        // The D15 restructure had hoisted the active-vikar 409 pre-check to PRECEDE the (now in-lock)
        // no-reports 400 guard. So the COMBINED input state "the actor already holds an active vikar
        // AND has ZERO direct reports" returned 409 (active-vikar) where the ORIGINAL (pre-S76) order
        // returned 400 (no reports). That broke the live S51 self-service UI contract (R2). The fix
        // restores the ORIGINAL relative order (no-reports 400 FIRST, then active-vikar 409). This test
        // pins exactly that combined state: it MUST 400 with the no-reports body — never 409.
        //
        // Vik (AFD02, LOCAL_LEADER @ STY02) has NO direct reports. Plant an ACTIVE vikar keyed on Vik
        // (absent_approver_id = Vik) so the 409 pre-condition is ALSO satisfied. With the bug, the
        // active-vikar 409 fired first; with the fix the no-reports 400 fires first.
        await PlantVikarAsync(Vik, VikNarrow, Today().AddDays(30), TreeRootSty02);
        // Sanity: the 409 pre-condition is genuinely present (an active vikar owned by Vik).
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAnyDateAsync(Vik));

        var client = LeaderClient(Vik, "AFD02");
        var rsp = await client.PostAsJsonAsync(
            "/api/reporting-lines/delegate",
            new { actingManagerId = Mgr, effectiveTo = Today().AddDays(20).ToString("yyyy-MM-dd") });

        // 400 — the no-reports guard wins, NOT 409.
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var err = await rsp.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(err);
        Assert.Equal("You have no direct reports to delegate", err!.error);
    }

    /// <summary>
    /// S78 R9 (BLOCKER 1) — the self-service <c>/delegate</c> CREATE is now an employee-current-root
    /// mutator under the SHARED drift-guarded acquire (it keys on the self-delegating MANAGER's CURRENT
    /// tree root). This is the <c>/delegate</c>-create mirror of
    /// <c>ReportingLineWriteLifecycleTests.R9_DriftMidAcquire</c>: when a concurrent cross-styrelse
    /// transfer moves the self-delegating manager to a NEW styrelse WHILE the /delegate is parked on the
    /// OLD-root advisory, the create must detect the drift UNDER the lock, ROLL BACK, and RETRY on the NEW
    /// root (proven by the retry BLOCKING on the NEW-root advisory we also hold) — NO proceed-under-stale-
    /// key — then terminate cleanly (here: 400 cross-tree, since the chosen vikar stayed in the OLD tree),
    /// creating NO vikar row.
    ///
    /// Interleave (two held keys, mirroring R9_DriftMidAcquire):
    ///   (1) Mgr is in AFD02 (STY02 tree); HOLD both the STY02 key AND the STY05 key on side connections;
    ///   (2) Mgr self-delegates to Vik (AFD02/STY02) — the create derives Mgr's root = STY02, then BLOCKS
    ///       on the held STY02 key (advisory taken on the soon-to-be-stale key);
    ///   (3) while parked, TRANSFER Mgr to AFD03 (STY05) via a raw UPDATE (the cross-styrelse move the
    ///       unlocked pre-acquire read cannot see);
    ///   (4) release ONLY STY02 → the parked create acquires STY02, RE-DERIVES Mgr = STY05 → DRIFT →
    ///       TreeRootDriftException → rollback → RETRY. The retry derives STY05 and BLOCKS on the held
    ///       STY05 key (PROOF it re-keyed on the NEW root, not the stale STY02 one);
    ///   (5) release STY05 → the retried create proceeds; ValidateSameTreeAsync sees Mgr=STY05, Vik=STY02
    ///       → 400. No manager_vikar row created.
    /// </summary>
    [Fact]
    public async Task SelfDelegate_DriftMidAcquire_RollsBack_RetriesOnNewRoot_NoVikar()
    {
        // (1) HOLD both tree keys on independent side connections.
        var (sty02Conn, sty02Tx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        var (sty05Conn, sty05Tx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty05);
        var sty02Released = false;
        var sty05Released = false;
        try
        {
            // (2) Mgr self-delegates to Vik. Derives Mgr=STY02 (unlocked), blocks on the held STY02 key.
            var client = LeaderClient(Mgr, "AFD02");
            var delegateTask = client.PostAsJsonAsync(
                "/api/reporting-lines/delegate",
                new { actingManagerId = Vik, effectiveTo = Today().AddDays(20).ToString("yyyy-MM-dd") });

            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                "The /delegate create did not block on the STY02 advisory — it never reached the drift-guarded acquire (BLOCKER 1 not applied).");
            Assert.False(await Task.WhenAny(delegateTask, Task.Delay(500)) == delegateTask,
                "The /delegate create completed while the STY02 key was held — it did not serialize on the tree advisory.");

            // (3) TRANSFER Mgr to STY05 (raw UPDATE — does not contend on the advisory).
            await using (var sideConn = new NpgsqlConnection(_harness.ConnectionString))
            {
                await sideConn.OpenAsync();
                await using var transfer = new NpgsqlCommand(
                    "UPDATE users SET primary_org_id = 'AFD03' WHERE user_id = @id", sideConn);
                transfer.Parameters.AddWithValue("id", Mgr);
                await transfer.ExecuteNonQueryAsync();
            }

            // (4) Release ONLY STY02 → the parked create acquires STY02, RE-DERIVES Mgr=STY05 → DRIFT →
            //     rollback → RETRY. The retry must now BLOCK on the held STY05 key (re-keyed on the NEW root).
            await sty02Tx.RollbackAsync();
            await sty02Conn.DisposeAsync();
            sty02Released = true;

            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty05),
                "After the drift, the /delegate retry did not block on the STY05 advisory — it did not re-key on the NEW root (proceed-under-stale-key).");
            Assert.False(await Task.WhenAny(delegateTask, Task.Delay(500)) == delegateTask,
                "The retried /delegate create completed while the STY05 key was held — drift-retry did not re-serialize on the new root.");

            // (5) Release STY05 → the retried create proceeds and rejects cross-tree (Vik stayed in STY02).
            await sty05Tx.RollbackAsync();
            await sty05Conn.DisposeAsync();
            sty05Released = true;

            var completed = await Task.WhenAny(delegateTask, Task.Delay(5000)) == delegateTask;
            Assert.True(completed, "The retried /delegate create did not complete — the drift-retry loop hung.");
            var rsp = await delegateTask;
            Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        }
        finally
        {
            if (!sty02Released) { await sty02Tx.RollbackAsync(); await sty02Conn.DisposeAsync(); }
            if (!sty05Released) { await sty05Tx.RollbackAsync(); await sty05Conn.DisposeAsync(); }
            // Restore Mgr to AFD02 so cleanup + other fixtures are unaffected (each test gets a fresh seed,
            // but be explicit since this test mutates primary_org_id out-of-band).
            await using var reset = new NpgsqlConnection(_harness.ConnectionString);
            await reset.OpenAsync();
            await using var upd = new NpgsqlCommand(
                "UPDATE users SET primary_org_id = 'AFD02' WHERE user_id = @id", reset);
            upd.Parameters.AddWithValue("id", Mgr);
            await upd.ExecuteNonQueryAsync();
        }

        // FINAL INVARIANT: no vikar was created for Mgr (the cross-tree retry rejected before INSERT).
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Deliverable A — admin-on-behalf MIXED-ROLE denial (S76 / TASK-7600 B1 floor, on THIS
    //  endpoint). The actor is LocalAdmin@STY05 (a styrelse that does NOT contain the manager)
    //  AND holds a non-admin LocalLeader@MIN01 scope that DOES cover the manager's STY02 tree.
    //  The LocalAdminOrAbove policy passes on the JWT's primary LocalAdmin role; the FLOORED
    //  ValidateOrgAccessAsync(.., LocalAdmin) is the gate. DISCRIMINATING: on the UNFLOORED
    //  overload the covering LocalLeader@MIN01 scope would have ADMITTED (the B1 leak) — these
    //  tests prove the floor is load-bearing on the POST and the DELETE specifically.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminPost_MixedRoleActor_AdminDisjoint_LeaderCovers_Returns403()
    {
        // Mixed-role: LocalAdmin@STY05 (disjoint — STY05 path /MIN02/STY05/ does NOT cover the
        // manager's AFD02 /MIN01/STY02/AFD02/) + LocalLeader@MIN01 (/MIN01/ COVERS AFD02). Floored
        // at LocalAdmin: only the STY05 admin scope counts and it misses AFD02 → 403. On the
        // unfloored overload the LocalLeader@MIN01 scope covers AFD02 → would have ADMITTED (leak).
        var client = MixedRoleClient("t76_mix_post", StatsTidRoles.LocalAdmin, "STY05", StatsTidRoles.LocalLeader, "MIN01");
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = Vik, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        // No vikar created — the floored scope gate fired before any write.
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task AdminDelete_MixedRoleActor_AdminDisjoint_LeaderCovers_Returns403()
    {
        // Same mixed-role shape against the DELETE. The persisted vikar tree_root is STY02
        // (/MIN01/STY02/): floored at LocalAdmin the STY05 admin scope does not cover STY02 → 403,
        // even though the LocalLeader@MIN01 scope does (it is below the floor). Discriminating —
        // the unfloored overload would have admitted the revoke via the covering Leader scope.
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        var client = MixedRoleClient("t76_mix_del", StatsTidRoles.LocalAdmin, "STY05", StatsTidRoles.LocalLeader, "MIN01");
        var rsp = await client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        // Still active — no mutation.
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S83 / TASK-8301 (ADR-027 D18→D19) — the REVOKE-SAFE drift-guarded acquire on the two
    //  reporting-edge revokes. Pre-S83 the self-/delegate DELETE took NO advisory at all and the
    //  admin-vikar DELETE took ONLY the PERSISTED-root advisory; both now go through
    //  AcquireRevokeTreeLocksAsync (always lock the persisted root + the subject's current root when
    //  derivable, drift-guarded). These tests use the held-lock interleave harness
    //  (AcquireTreeLockOnSideConnAsync / WaitForAdvisoryLockWaiterAsync) and the STY02/STY05 cross-
    //  styrelse fixture (Mgr in AFD02/STY02; AFD03 is in STY05 — the transfer target).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TEST 1 — REVOKE-SAFETY / inactive subject: the admin-revoke of a manager whose
    /// <c>users.is_active = FALSE</c> still SUCCEEDS via the persisted-only lock path. The new
    /// <see cref="ReportingLineRepository.AcquireRevokeTreeLocksAsync"/> DEFENSIVELY derives the
    /// subject's current root and SWALLOWS the missing/inactive throw, so an inactive manager never
    /// 500s the revoke (it collapses to the always-locked persisted root). RED against a NAIVE variant
    /// that unconditionally derives the current root (which would throw InvalidOperationException →
    /// 500). (Distinct from <see cref="AdminDelete_RevokeSafe_WhenManagerAndVikarInactive_StillSucceeds"/>,
    /// which the pre-S83 no-derive code already passed; this one is named for the helper's defensive catch.)
    /// </summary>
    [Fact]
    public async Task AdminDelete_InactiveManager_RevokeSafe_NoCurrentRootDerive_StillSucceeds()
    {
        var vikar = await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        await DeactivateUserAsync(Mgr); // the current-root derive would now throw — must be swallowed.

        var client = AdminClient(Admin, "MIN01");
        var rsp = await client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'ManagerVikarEnded' AND target_resource_id = @id",
            ("id", vikar.ToString())) == 1);
    }

    /// <summary>
    /// TEST 2 — R1 OWNER-TRANSFER (persisted ≠ current): a self-delegation row whose owner (Mgr)
    /// transferred styrelse AFTER creating it (persisted root STY02 ≠ owner current root STY05);
    /// the self-DELETE succeeds AND SERIALIZES on the PERSISTED root (STY02). We prove serialization
    /// by holding the STY02 advisory on a side connection and showing the self-DELETE BLOCKS on it.
    ///
    /// RED on the PRE-S83 no-lock self-DELETE: it took NO advisory, so it would NOT block on the held
    /// STY02 key — it would complete while the key is held (the assertion that it stays incomplete fails).
    /// </summary>
    [Fact]
    public async Task SelfDelete_OwnerTransferred_SerializesOnPersistedRoot_AndSucceeds()
    {
        // Mgr self-delegates to Vik (persisted tree root = STY02), then TRANSFERS to AFD03 (STY05).
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        await TransferUserAsync(Mgr, "AFD03"); // STY05 tree — now persisted(STY02) ≠ current(STY05).

        // Hold the PERSISTED-root (STY02) advisory on a side connection.
        var (sideConn, sideTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        var released = false;
        try
        {
            var client = LeaderClient(Mgr, "AFD02");
            var deleteTask = client.DeleteAsync("/api/reporting-lines/delegate");

            // It must REACH + BLOCK on the held STY02 advisory (the persisted-root lock). On the
            // pre-S83 no-lock self-DELETE this waiter never appears (no advisory taken).
            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                "The self-DELETE did not block on the STY02 (persisted-root) advisory — it took no revoke-safe lock (TASK-8301 not applied to /delegate).");
            Assert.False(await Task.WhenAny(deleteTask, Task.Delay(500)) == deleteTask,
                "The self-DELETE completed while the STY02 persisted-root key was held — it did not serialize on the revoke-safe advisory.");

            // Release STY02 → the revoke proceeds and succeeds.
            await sideTx.RollbackAsync();
            await sideConn.DisposeAsync();
            released = true;

            var completed = await Task.WhenAny(deleteTask, Task.Delay(5000)) == deleteTask;
            Assert.True(completed, "The self-DELETE did not complete after the persisted-root key released.");
            var rsp = await deleteTask;
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        }
        finally
        {
            if (!released) { await sideTx.RollbackAsync(); await sideConn.DisposeAsync(); }
            await TransferUserAsync(Mgr, "AFD02"); // restore for cleanup symmetry.
        }
    }

    /// <summary>
    /// TEST 3 — ACTIVE→INACTIVE RACE: the revoke must NOT 500 when the subject is deactivated such
    /// that the current-root derive would throw — it falls back to persisted-only and succeeds. This
    /// is the self-DELETE arm (the absent approver = the actor), with Mgr deactivated before the call.
    /// On the new code the defensive try/catch in AcquireRevokeTreeLocksAsync swallows the
    /// InvalidOperationException; a NAIVE unconditional-derive variant would surface a 500.
    /// </summary>
    [Fact]
    public async Task SelfDelete_SubjectDeactivated_DoesNotError_FallsBackToPersistedOnly()
    {
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        await DeactivateUserAsync(Mgr); // derive of Mgr's current root will throw — must be swallowed.

        var client = LeaderClient(Mgr, "AFD02");
        var rsp = await client.DeleteAsync("/api/reporting-lines/delegate");

        // NOT a 500 — a clean 200, persisted-only.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    /// <summary>
    /// TEST 4 — WAITER-BASED MUTUAL EXCLUSION on the CURRENT root: with persisted(STY02) ≠
    /// current(STY05) for an ACTIVE subject, hold ONLY the CURRENT-root (STY05) advisory on a side
    /// connection and assert the admin-revoke BLOCKS until it releases. This proves the revoke-safe
    /// acquire takes the subject's CURRENT root in addition to the persisted root — a true mutual-
    /// exclusion assertion (the request must PARK on the STY05 key, not just reach a final state).
    ///
    /// RED on the PRE-S83 admin-revoke: it locked ONLY the persisted root (STY02) and never the current
    /// root, so it would NOT block on the held STY05 key — it would complete while STY05 is held.
    /// </summary>
    [Fact]
    public async Task AdminDelete_PersistedNeqCurrent_BlocksOnCurrentRootAdvisory_UntilReleased()
    {
        // Plant the vikar with persisted STY02, then transfer the (still ACTIVE) manager to STY05.
        await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), TreeRootSty02);
        await TransferUserAsync(Mgr, "AFD03"); // STY05 tree — current root now STY05, persisted STY02.

        // Hold ONLY the CURRENT-root (STY05) advisory.
        var (sideConn, sideTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty05);
        var released = false;
        try
        {
            var client = AdminClient(Admin, "MIN01");
            var deleteTask = client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");

            // The revoke must BLOCK on the held STY05 (current-root) advisory. Pre-S83 it only locked
            // STY02 → this waiter never appears and the request completes.
            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty05),
                "The admin-revoke did not block on the STY05 (current-root) advisory — it did not lock the subject's current root (TASK-8301 not applied).");
            Assert.False(await Task.WhenAny(deleteTask, Task.Delay(500)) == deleteTask,
                "The admin-revoke completed while the STY05 current-root key was held — it did not serialize on the current-root advisory.");

            // Release STY05 → the revoke proceeds.
            await sideTx.RollbackAsync();
            await sideConn.DisposeAsync();
            released = true;

            var completed = await Task.WhenAny(deleteTask, Task.Delay(5000)) == deleteTask;
            Assert.True(completed, "The admin-revoke did not complete after the current-root key released.");
            var rsp = await deleteTask;
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        }
        finally
        {
            if (!released) { await sideTx.RollbackAsync(); await sideConn.DisposeAsync(); }
            await TransferUserAsync(Mgr, "AFD02"); // restore for cleanup symmetry.
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private sealed record DelegateResponse(
        int delegatedCount, int skippedCount, string actingManagerId, string effectiveFrom, string effectiveTo);

    private sealed record AdminVikarResponse(
        string vikarId, string managerId, string vikarUserId, string effectiveFrom, string effectiveTo, string reason);

    // S76b / TASK-7603 (BLOCKER 3) — the single-manager active-vikar GET envelope.
    private sealed record ActiveVikarEnvelope(ActiveVikarDto? activeVikar);
    private sealed record ActiveVikarDto(
        string vikarUserId, string vikarDisplayName, string untilDate, string reason);

    private sealed record ErrorBody(string error, string[]? uncoveredEmployeeIds, int? uncoveredCount);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private HttpClient AdminClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(userId, orgId, StatsTidRoles.LocalAdmin, "LOCAL_ADMIN"));
        return client;
    }

    private HttpClient LeaderClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(userId, orgId, StatsTidRoles.LocalLeader, "LOCAL_LEADER"));
        return client;
    }

    /// <summary>The B1 mixed-role escalation shape (mirrors
    /// <see cref="Security.MixedRoleScopeLeakTests"/>): primary role = <paramref name="adminRole"/>
    /// anchored in the DISJOINT <paramref name="adminOrg"/>, PLUS a second non-admin scope
    /// (<paramref name="otherRole"/> @ <paramref name="otherOrg"/>) that COVERS the manager's tree.
    /// On the pre-fix UNFLOORED validator the covering non-admin scope admitted the admin gate; the
    /// LocalAdmin-floored ValidateOrgAccessAsync on this endpoint must DENY.</summary>
    private HttpClient MixedRoleClient(string userId, string adminRole, string adminOrg, string otherRole, string otherOrg)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var scopes = new[]
        {
            new RoleScope(adminRole, adminOrg, "ORG_AND_DESCENDANTS"),
            new RoleScope(otherRole, otherOrg, "ORG_AND_DESCENDANTS"),
        };
        var bearer = tokenService.GenerateToken(
            employeeId: userId, name: userId, role: adminRole,
            agreementCode: "HK", orgId: adminOrg, scopes: scopes);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static string MintToken(string userId, string orgId, string role, string scopeRoleId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        // The scope's Role must be the StatsTidRoles value — the ScopeAuthorizationHandler matches
        // RoleScope.Role against the policy's AllowedRoles, and the floored OrgScopeValidator matches
        // it against the LocalAdmin floor.
        var scopes = new[] { new RoleScope(role, orgId, "ORG_AND_DESCENDANTS") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private async Task<Guid> PlantVikarAsync(string absentApprover, string vikarUser, DateOnly untilDate, string treeRoot)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var v = await _vikarRepo.CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "ANDET",
            TreeRootOrgId = treeRoot,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
        return v.VikarId;
    }

    private async Task DeactivateUserAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE users SET is_active = FALSE WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// S83 — out-of-band cross-styrelse transfer (a raw <c>primary_org_id</c> UPDATE, the move the
    /// unlocked pre-acquire derive cannot pre-see). Used by the persisted≠current revoke tests; the
    /// owning test restores AFD02 in its <c>finally</c>.
    /// </summary>
    private async Task TransferUserAsync(string userId, string newPrimaryOrgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE users SET primary_org_id = @org WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("org", newPrimaryOrgId);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task<(string? ActorOrg, string? TargetOrg, string? ActorId)> ReadAuditAttributionAsync(Guid vikarId)
        => ReadAuditAttributionAsync(vikarId, "ManagerVikarCreated");

    private async Task<(string? ActorOrg, string? TargetOrg, string? ActorId)> ReadAuditAttributionAsync(
        Guid vikarId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT actor_primary_org_id, target_org_id, actor_id
            FROM audit_projection
            WHERE event_type = @eventType AND target_resource_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("eventType", eventType);
        cmd.Parameters.AddWithValue("id", vikarId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (null, null, null);
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<long> CountAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ── S78 R9 (BLOCKER 1) held-lock interleave harness (mirrors ReportingLineWriteLifecycleTests) ──

    /// <summary>
    /// Holds the <c>reporting-tree-{treeRoot}</c> xact advisory key on a SIDE connection so a test can
    /// deterministically BLOCK any in-tree mutator (assign / remove / acting / vikar-create / transfer)
    /// that takes the same key, until the returned tx is rolled back. The caller owns disposal.
    /// </summary>
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireTreeLockOnSideConnAsync(string treeRoot)
    {
        var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('reporting-tree-' || @root))", conn, tx))
        {
            cmd.Parameters.AddWithValue("root", treeRoot);
            await cmd.ExecuteScalarAsync();
        }
        return (conn, tx);
    }

    /// <summary>
    /// Polls <c>pg_locks</c> (joined to <c>pg_stat_activity</c> to exclude this session) until at least
    /// one OTHER backend is WAITING (<c>granted = false</c>) on the <c>reporting-tree-{treeRoot}</c>
    /// advisory key — proving a request actually REACHED and BLOCKED ON THE LOCK (a merely-slow or
    /// sequentially-run request cannot satisfy it). For a single-arg <c>pg_advisory_xact_lock(bigint)</c>
    /// the key is split classid=high32 / objid=low32, so it is rebuilt as
    /// <c>(classid::bigint &lt;&lt; 32) | objid::bigint</c>. Returns <c>true</c> once a waiter is seen;
    /// <c>false</c> on timeout.
    /// </summary>
    private async Task<bool> WaitForAdvisoryLockWaiterAsync(string treeRoot, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_locks pl
                    JOIN pg_stat_activity sa ON sa.pid = pl.pid
                    WHERE pl.locktype = 'advisory'
                      AND pl.granted = FALSE
                      AND pl.pid <> pg_backend_pid()
                      AND ((pl.classid::bigint << 32) | pl.objid::bigint)
                          = hashtext('reporting-tree-' || @root)::bigint
                )
                """, conn))
            {
                cmd.Parameters.AddWithValue("root", treeRoot);
                if (await cmd.ExecuteScalarAsync() is true)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }
}
