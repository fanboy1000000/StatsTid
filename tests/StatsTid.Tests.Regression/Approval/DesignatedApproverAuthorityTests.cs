using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S74 / ADR-027 D4 amendment (SPRINT-74 R5/R6/R7, TASK-7402) — the A3 approve-authority
/// expansion: the effective designated-approver edge now GRANTS approve / reject / reopen
/// authority and my-reports VISIBILITY for an employee anywhere in the same
/// <c>tree_root_org_id</c>, even when the approver's RBAC org-scope does not cover that
/// afdeling (OQ-3a, a P7 privilege expansion). The load-bearing invariant proven here is
/// <b>see == act</b>: every period the my-reports dashboard surfaces is approvable/reopenable
/// via the SAME canonical predicate, and ADR-027 D2 (cross-styrelse forbidden) still holds
/// because the resolving edge is intra-tree by construction.
///
/// <para>
/// <b>CROSS-ORG fixtures (the S71 green-but-weak lesson).</b> A same-org fixture cannot
/// discriminate edge-authority from org-scope. The designated approver
/// (<c>t74_mgr</c>, AFD02) is the PRIMARY manager of an employee in a SIBLING afdeling
/// (<c>t74_emp</c>, AFD01) within the same styrelse (STY02), and the approver's scope covers
/// AFD02 ONLY — so org-scope alone DENIES; only the edge grants. A cross-styrelse employee
/// (<c>t74_emp_x</c>, STY05 = a different tree_root) proves the D2 bound.
/// </para>
///
/// <para>
/// Topology (init.sql seed orgs): STY02 tree = {STY02 root, AFD01, AFD02}; STY05 tree =
/// {STY05 root, AFD03, AFD04}. The two trees sit under different ministries (MIN01 / MIN02).
/// </para>
///
/// <para>
/// Endpoint-level integration via <see cref="StatsTidWebApplicationFactory"/> (the real
/// Backend.Api over a fresh testcontainer) for the route→read→approve→reopen path; direct
/// <see cref="DesignatedApproverAuthorizer"/> + repository assertions for the discriminating
/// single-winner edges (vikar-supersedes-PRIMARY, inactive-vikar-skip, cross-styrelse-block).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DesignatedApproverAuthorityTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // ── Test users (all distinct from seed) ──────────────────────────────────────────
    // STY02 tree:
    private const string Emp = "t74_emp";        // AFD01 — the report (sibling afdeling to the approver)
    private const string Mgr = "t74_mgr";        // AFD02 — PRIMARY manager of Emp; scope = AFD02 ONLY
    private const string Vik = "t74_vik";        // AFD02 — Mgr's vikar stand-in (a Leader)
    private const string EmpInactiveMgr = "t74_emp_im"; // AFD01 — reports to an INACTIVE manager
    private const string InactiveMgr = "t74_imgr";      // AFD02 — INACTIVE; escalates up to Mgr
    private const string Other = "t74_other";    // AFD02 — a Leader who holds NO edge over Emp
    // STY05 tree (cross-styrelse):
    private const string EmpX = "t74_emp_x";     // STY05 — different tree_root
    private const string MgrX = "t74_mgr_x";     // STY05 — EmpX's own manager

    private const string TreeRootSty02 = "STY02";
    private const string TreeRootSty05 = "STY05";

    private static readonly string[] AllUsers =
        { Emp, Mgr, Vik, EmpInactiveMgr, InactiveMgr, Other, EmpX, MgrX };

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
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        // Users. InactiveMgr is is_active = FALSE (escalation source).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp,   @emp,   '$2a$11$fake', 'T74 Emp',   't74_emp@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@mgr,   @mgr,   '$2a$11$fake', 'T74 Mgr',   't74_mgr@test.dk',   'AFD02', 'HK', 'OK24', TRUE),
                (@vik,   @vik,   '$2a$11$fake', 'T74 Vikar', 't74_vik@test.dk',   'AFD02', 'HK', 'OK24', TRUE),
                (@empim, @empim, '$2a$11$fake', 'T74 EmpIM', 't74_emp_im@test.dk','AFD01', 'HK', 'OK24', TRUE),
                (@imgr,  @imgr,  '$2a$11$fake', 'T74 IMgr',  't74_imgr@test.dk',  'AFD02', 'HK', 'OK24', FALSE),
                (@other, @other, '$2a$11$fake', 'T74 Other', 't74_other@test.dk', 'AFD02', 'HK', 'OK24', TRUE),
                (@empx,  @empx,  '$2a$11$fake', 'T74 EmpX',  't74_emp_x@test.dk', 'STY05', 'HK', 'OK24', TRUE),
                (@mgrx,  @mgrx,  '$2a$11$fake', 'T74 MgrX',  't74_mgr_x@test.dk', 'STY05', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Role assignments — Mgr/Vik/Other/MgrX are Leaders; the IsActiveLeaderOrAbove gate
        // reads role_assignments + roles.hierarchy_level. Scopes are AFD02 (Mgr/Vik/Other) and
        // STY05 (MgrX) — deliberately NOT covering AFD01 (Emp) so org-scope alone denies.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@mgr,   'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@vik,   'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@other, 'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@mgrx,  'LOCAL_LEADER', 'STY05', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@emp,   'EMPLOYEE',     'AFD01', 'ORG_ONLY',            'TEST'),
                (@empim, 'EMPLOYEE',     'AFD01', 'ORG_ONLY',            'TEST'),
                (@empx,  'EMPLOYEE',     'STY05', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        var rlRepo = new ReportingLineRepository(_dbFactory);

        // Emp (AFD01) reports PRIMARY to Mgr (AFD02) — the cross-afdeling, same-tree edge.
        await rlRepo.AssignAsync(null, MakeLine(Emp, Mgr, TreeRootSty02));
        // EmpInactiveMgr (AFD01) reports PRIMARY to InactiveMgr (AFD02, inactive)…
        await rlRepo.AssignAsync(null, MakeLine(EmpInactiveMgr, InactiveMgr, TreeRootSty02));
        // …and InactiveMgr reports PRIMARY to Mgr → EmpInactiveMgr escalates up to Mgr.
        await rlRepo.AssignAsync(null, MakeLine(InactiveMgr, Mgr, TreeRootSty02));
        // EmpX (STY05) reports PRIMARY to MgrX (STY05) — the cross-styrelse tree.
        await rlRepo.AssignAsync(null, MakeLine(EmpX, MgrX, TreeRootSty05));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("vik", Vik);
        cmd.Parameters.AddWithValue("empim", EmpInactiveMgr);
        cmd.Parameters.AddWithValue("imgr", InactiveMgr);
        cmd.Parameters.AddWithValue("other", Other);
        cmd.Parameters.AddWithValue("empx", EmpX);
        cmd.Parameters.AddWithValue("mgrx", MgrX);
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

    /// <summary>
    /// A direct admin-assigned ACTING reporting line (relationship='ACTING', the resolver's
    /// highest-precedence step 1 — beats the resolved PRIMARY manager + vikar). The schema's
    /// <c>source</c> CHECK allows only MANUAL/HR_IMPORT, so an admin assignment is MANUAL
    /// (the resolver's per-report ACTING check excludes only <c>source = 'SELF_DELEGATION'</c>
    /// rows, which this schema variant does not hold). Mirrors <see cref="MakeLine"/>.
    /// </summary>
    private static ReportingLineModel MakeActingLine(string employeeId, string managerId, string treeRoot) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = treeRoot,
        Relationship = "ACTING",
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
        // The Backend.Api host startup seeders back-fill employee_profiles + user_agreement_codes
        // per user; drop ours before the users (the FK constraints to users(user_id)).
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

    private Task<Guid> InsertPeriodAsync(string employeeId, string orgId, string status)
        => InsertPeriodWithRangeAsync(employeeId, orgId, status, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

    /// <summary>
    /// Inserts an approval period with an explicit date range — lets a single employee carry TWO
    /// distinct periods (no unique key on employee_id/period range, only an index), which the R2
    /// two-period vikar verb-set test needs (period-1 for approve+reopen, period-2 for reject).
    /// </summary>
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

    private Task CreateVikarAsync(string absentApprover, string vikarUser, DateOnly untilDate)
        => CreateVikarRawAsync(absentApprover, vikarUser, untilDate, TreeRootSty02);

    /// <summary>
    /// Directly plants a <c>manager_vikar</c> row with an explicit <paramref name="treeRoot"/>.
    /// Used by the S74-7402 B1 cross-tree-vikar regression to construct a CROSS-styrelse vikar
    /// row that the Layer-1 POST guard now refuses — proving the Layer-2 authority predicate
    /// denies even a directly-planted cross-tree vikar row, independent of edge-creation.
    /// </summary>
    private async Task CreateVikarRawAsync(string absentApprover, string vikarUser, DateOnly untilDate, string treeRoot)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await new ManagerVikarRepository(_dbFactory).CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "FERIE",
            TreeRootOrgId = treeRoot,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
    }

    private async Task GrantGlobalLeaderScopeAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@uid, 'LOCAL_LEADER', NULL, 'GLOBAL', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("uid", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 — Phase-1 INTEGRATION: route → read → approve → reopen via the edge (incl. vikar)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The end-to-end Phase-1 test: a cross-afdeling designated approver (Mgr, AFD02, scope
    /// does NOT cover Emp's AFD01) SEES Emp's period on the my-reports dashboard AND can
    /// approve it AND reopen it — all via the edge, with org-scope denying throughout.
    /// </summary>
    [Fact]
    public async Task Edge_CrossAfdeling_Manager_Sees_Approves_AndReopens_OnMyReports()
    {
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        // READ: the period appears on Mgr's my-reports (edge visibility, NOT org-scope).
        var pending = await GetMyReportsAsync(client);
        Assert.Contains(pending, p => p == periodId);

        // ACT (approve): org-scope denies (AFD01 ⊄ AFD02), the edge grants → 200.
        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));

        // ACT (reopen, Leader arm): the edge grants the reopen too → back to DRAFT.
        var reopenRsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "edge-reopen" });
        Assert.Equal(HttpStatusCode.OK, reopenRsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// The vikar-holds-authority case end-to-end: while Mgr has an active vikar (Vik) covering
    /// today, Vik (NOT Mgr) is the single effective approver — Vik SEES Emp's period on
    /// my-reports AND can approve it; Vik's org-scope (AFD02) does not cover Emp's AFD01.
    /// </summary>
    [Fact]
    public async Task Vikar_HoldsAuthority_Sees_AndApproves_WhileMgrIsSuperseded()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var vikClient = _factory.CreateClient();
        vikClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "AFD02"));

        // The vikar SEES the absent manager's report.
        var vikPending = await GetMyReportsAsync(vikClient);
        Assert.Contains(vikPending, p => p == periodId);

        // …and can approve it.
        var approveRsp = await vikClient.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// S77-7701 (R2) — the vikar holds the FULL manager verb-set end-to-end, not just approve.
    /// While Mgr has an active vikar (Vik) covering today, Vik (a Leader-level stand-in,
    /// deliberately NOT LocalHR — the token is minted <c>LocalLeader</c>, so the ONLY authority
    /// is the vikar EDGE, never a role) can REJECT and REOPEN Emp's periods through the real HTTP
    /// endpoints, with Vik's org-scope (AFD02) NOT covering Emp's AFD01 throughout.
    ///
    /// <para>
    /// The approval state machine forbids <c>approve→reopen→reject</c> on one period (reopen takes
    /// APPROVED→DRAFT, which reject won't accept — reject needs SUBMITTED/EMPLOYEE_APPROVED;
    /// <see cref="ApprovalEndpoints"/> reject :346, reopen :971/:982). So this uses the LEGAL
    /// two-period path:
    /// <list type="bullet">
    /// <item><description>period-1: SUBMITTED → vikar APPROVE (→ APPROVED) → vikar REOPEN
    /// (→ DRAFT) — proves the vikar holds approve + the reopen-Leader-arm edge branch.</description></item>
    /// <item><description>period-2 (a distinct date range): SUBMITTED → vikar REJECT
    /// (→ REJECTED) — proves the vikar holds reject.</description></item>
    /// </list>
    /// Each verb is asserted to succeed (HTTP 200 + the resulting status) driven by the vikar's
    /// client, closing the absent-vikar reject + reopen full-stack coverage gap.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Vikar_HoldsAuthority_Rejects_AndReopens_ViaEndpoints_NotViaRole()
    {
        // Vik is Mgr's active vikar covering today → Vik (NOT Mgr) is Emp's single effective
        // approver. Vik's token is LocalLeader (NOT LocalHR) — only the edge grants authority.
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        var vikClient = _factory.CreateClient();
        vikClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "AFD02"));

        // ── period-1: SUBMITTED → APPROVE → REOPEN (proves approve + reopen-Leader-arm) ──
        var p1 = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var approveRsp = await vikClient.PostAsync($"/api/approval/{p1}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(p1));

        // reopen (Leader arm): the vikar edge grants it (org-scope denies AFD01) → APPROVED → DRAFT.
        var reopenRsp = await vikClient.PostAsJsonAsync(
            $"/api/approval/{p1}/reopen", new { reason = "vikar-reopen" });
        Assert.Equal(HttpStatusCode.OK, reopenRsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(p1));

        // ── period-2 (distinct range): SUBMITTED → REJECT (proves reject) ──
        var p2 = await InsertPeriodWithRangeAsync(
            Emp, "AFD01", "SUBMITTED", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var rejectRsp = await vikClient.PostAsJsonAsync(
            $"/api/approval/{p2}/reject", new { reason = "vikar-reject" });
        Assert.Equal(HttpStatusCode.OK, rejectRsp.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(p2));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (1) — sibling-afdeling edge → approvable AND reopenable AND visible
    //  (covered by the integration test above; this asserts the reject path of the same edge)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edge_CrossAfdeling_Manager_CanReject()
    {
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        var rejectRsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason = "edge-reject" });
        Assert.Equal(HttpStatusCode.OK, rejectRsp.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (2) — cross-styrelse edge → BLOCKED (different tree_root, D2)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CrossStyrelse_Manager_CannotSee_NorApprove_NorIsEffectiveApprover()
    {
        var periodId = await InsertPeriodAsync(EmpX, "STY05", "SUBMITTED");

        // Mgr (STY02 tree) is NOT EmpX's (STY05 tree) effective approver — the resolver returns
        // EmpX's own manager, never a cross-tree actor (ValidateSameTreeAsync invariant).
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var isEdge = await authorizer.IsEffectiveDesignatedApproverAsync(
            Mgr, EmpX, asOf: DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.False(isEdge);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        // Not on Mgr's my-reports…
        var pending = await GetMyReportsAsync(client);
        Assert.DoesNotContain(pending, p => p == periodId);

        // …and the approve is denied (org-scope no + edge no → 403). ADR-027 D2 holds.
        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S74-7402 (completeness, unproven-by-test) — the by-month dashboard read
    //
    //  Both my-reports reads (GetPendingForDesignatedReportsAsync AND
    //  GetByMonthForDesignatedReportsAsync) were rewritten to the single-immediate-effective-
    //  approver semantics (shared candidate CTE + the R5 predicate), but only the pending read
    //  was covered. This proves see == act on the by-month surface too, with the SAME cross-org
    //  discrimination: the cross-afdeling designated approver sees the report on by-month, and a
    //  cross-styrelse manager does not.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// By-month read completeness: the cross-AFDELING designated approver (Mgr, AFD02, the
    /// PRIMARY effective approver of Emp in the sibling AFD01, org-scope NOT covering AFD01)
    /// SEES Emp's period on the <c>/api/approval/by-month?my-reports=true</c> read for the
    /// period's month — edge visibility, not org-scope. A cross-STYRELSE manager (MgrX, STY05 =
    /// a different tree_root) does NOT see it (ADR-027 D2). Emp's seeded period is May 2026.
    /// </summary>
    [Fact]
    public async Task ByMonth_CrossAfdelingDesignated_Sees_AndCrossStyrelse_DoesNot()
    {
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED"); // May 2026

        // Mgr (cross-afdeling designated approver) SEES Emp's period on the by-month read.
        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));
        var mgrByMonth = await GetByMonthMyReportsAsync(mgrClient, 2026, 5);
        Assert.Contains(mgrByMonth, p => p == periodId);

        // MgrX (cross-styrelse, STY05 tree) does NOT see Emp's STY02 period — the candidate set
        // is tree-root bounded and the R5 predicate denies the cross-tree actor. D2 holds.
        var mgrxClient = _factory.CreateClient();
        mgrxClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(MgrX, "STY05"));
        var mgrxByMonth = await GetByMonthMyReportsAsync(mgrxClient, 2026, 5);
        Assert.DoesNotContain(mgrxByMonth, p => p == periodId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S74-7402 (completeness, unproven-by-test) — admin-ACTING precedence on the dashboard
    //
    //  The resolver's highest-precedence winner (step 1) is a per-report admin-assigned ACTING
    //  edge (relationship='ACTING'), which beats the resolved PRIMARY manager. Code-traced
    //  complete but untested end-to-end: prove a DIRECT admin-ACTING holder (not the PRIMARY
    //  manager, not a vikar) gains BOTH visibility and authority cross-afdeling within the tree.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Direct admin-ACTING precedence: <c>Other</c> (AFD02 Leader, NOT Emp's PRIMARY manager
    /// [that is Mgr], NOT a vikar) holds a DIRECT admin-assigned ACTING edge over Emp (AFD01).
    /// ACTING is the resolver's precedence step 1, so Other — not Mgr — is the single effective
    /// approver. Asserts the predicate grants Other authority, the period is visible on Other's
    /// my-reports, and approve succeeds (200) — precedence step 1 grants BOTH visibility and
    /// authority cross-afdeling within the styrelse (Other's AFD02 scope does NOT cover AFD01).
    /// </summary>
    [Fact]
    public async Task AdminActing_DirectEdge_TakesPrecedence_Sees_AndApproves_CrossAfdeling()
    {
        // Other holds a DIRECT admin-ACTING edge over Emp (cross-afdeling, same STY02 tree).
        // ACTING wins resolver precedence step 1 over Emp's PRIMARY manager (Mgr).
        await new ReportingLineRepository(_dbFactory)
            .AssignAsync(null, MakeActingLine(Emp, Other, TreeRootSty02));
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // The admin-ACTING holder is the single effective approver (precedence over PRIMARY).
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Other, Emp, asOf: today));

        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Other, "AFD02"));

        // SEES it on my-reports (visibility), org-scope (AFD02) NOT covering Emp's AFD01.
        var pending = await GetMyReportsAsync(otherClient);
        Assert.Contains(pending, p => p == periodId);

        // …and APPROVES it (authority) — precedence step 1 grants both see and act.
        var approveRsp = await otherClient.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S74-7402 B1 (SECURITY, ADR-027 D2) — the cross-styrelse VIKAR hole, two-layer fix
    //
    //  Defect: the /delegate POST validated the vikar's ROLE + org-scope coverage but NEVER
    //  same-tree between the absent approver and the vikar. A global-scoped (or cross-tree-
    //  scoped) leader could be planted as a vikar in another styrelse, win the resolver's
    //  vikar consult, and — under 7402's authority expansion — gain approve/reject/reopen over
    //  a report in a DIFFERENT styrelse. The earlier cross-styrelse test only covered an actor
    //  holding NO edge; it masked this vikar route.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LAYER 2 (predicate, defense-in-depth): even a DIRECTLY-PLANTED cross-tree vikar row
    /// (MgrX, STY05, set as Mgr's vikar) is denied authority over Emp (STY02). The resolver's
    /// vikar consult WOULD return MgrX as the single winner for Emp (it does not itself check
    /// tree), so without the predicate same-tree re-check MgrX would gain cross-styrelse
    /// authority. Assert: not the effective approver, not on my-reports, approve → 403.
    /// </summary>
    [Fact]
    public async Task CrossTreeVikar_DirectlyPlanted_IsDenied_ByPredicate_NotVisible_NorApprovable()
    {
        // MgrX lives in STY05; plant him as Mgr's (STY02) vikar — a cross-styrelse vikar row
        // (the row the Layer-1 POST guard now refuses; here we plant it directly to prove the
        // predicate Layer-2 denies it independently of edge creation).
        await CreateVikarRawAsync(Mgr, MgrX, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), TreeRootSty05);
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Sanity: the resolver DOES return MgrX as the single winner for Emp (cross-tree vikar
        // wins the consult) — so the ONLY thing standing between MgrX and cross-styrelse
        // authority is the predicate's Layer-2 same-tree re-check.
        var (resolved, _, _) = await new ReportingLineRepository(_dbFactory)
            .ResolveDesignatedApproverAsync(Emp, asOf: today);
        Assert.Equal(MgrX, resolved);

        // Layer 2 denies: MgrX (STY05) and Emp (STY02) are different tree roots.
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(MgrX, Emp, asOf: today));

        var mgrxClient = _factory.CreateClient();
        mgrxClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(MgrX, "STY05"));

        // Not on MgrX's my-reports…
        var pending = await GetMyReportsAsync(mgrxClient);
        Assert.DoesNotContain(pending, p => p == periodId);

        // …and the approve is denied (org-scope no + edge denied by Layer 2 → 403). D2 holds.
        var approveRsp = await mgrxClient.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// LAYER 1 (source): the /delegate POST refuses a cross-styrelse <c>actingManagerId</c>.
    /// Mgr (STY02, the absent approver, PRIMARY manager of Emp) attempts to delegate to MgrX
    /// (STY05). MgrX is granted a GLOBAL scope so the role + org-scope coverage checks PASS —
    /// isolating the new same-tree guard as the gate that fires → 400 "same styrelse". No vikar
    /// row is created.
    /// </summary>
    [Fact]
    public async Task DelegatePost_CrossStyrelseActingManager_IsRejected_SameTree_400()
    {
        // Make MgrX a global-scoped leader so steps 5 (role) + 6 (org-scope coverage of Emp,
        // AFD01) PASS — the realistic attack vector, and it isolates the Layer-1 same-tree guard.
        await GrantGlobalLeaderScopeAsync(MgrX);

        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        var rsp = await mgrClient.PostAsJsonAsync("/api/reporting-lines/delegate", new
        {
            actingManagerId = MgrX,
            effectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("styrelse", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        // No vikar row was created (the guard fired BEFORE the create tx).
        var planted = await new ManagerVikarRepository(_dbFactory).GetActiveByApproverAnyDateAsync(Mgr);
        Assert.Null(planted);
    }

    /// <summary>
    /// The fix did NOT over-block: a LEGITIMATE same-styrelse cross-AFDELING vikar still holds
    /// authority. Vik (AFD02) is Mgr's (AFD02) vikar and the single effective approver of Emp
    /// (AFD01) — afdelinger AFD01/AFD02 share the STY02 tree_root, so BOTH layers pass: the
    /// predicate grants Vik authority over Emp AND the period is visible on Vik's my-reports.
    /// </summary>
    [Fact]
    public async Task SameStyrelse_CrossAfdelingVikar_StillHoldsAuthority_AndIsVisible()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)); // STY02 root
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Layer 2 PASSES: Vik (AFD02) and Emp (AFD01) share the STY02 tree root.
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Vik, Emp, asOf: today));

        var vikClient = _factory.CreateClient();
        vikClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "AFD02"));

        // Visible on my-reports (the legitimate cross-afdeling vikar is NOT over-blocked).
        var pending = await GetMyReportsAsync(vikClient);
        Assert.Contains(pending, p => p == periodId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (3) — edge NOT held → not approvable; and PRIMARY-superseded-by-vikar
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActorWithoutEdge_IsNotEffectiveApprover_AndCannotApprove()
    {
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        // Other is a Leader in AFD02 but holds NO reporting edge over Emp.
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(
            Other, Emp, asOf: DateOnly.FromDateTime(DateTime.UtcNow)));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Other, "AFD02"));

        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    [Fact]
    public async Task PrimaryManager_SupersededByActiveVikar_IsNotTheSingleWinner_AndCannotApprove()
    {
        // Vik is the active vikar for Mgr → the single effective approver of Emp is Vik, NOT Mgr.
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Vik, Emp, asOf: today));
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(Mgr, Emp, asOf: today));

        // The HTTP path agrees: Mgr (the superseded PRIMARY, scope AFD02) cannot approve Emp's
        // AFD01 period — org-scope denies and the edge now resolves to Vik, not Mgr → 403.
        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));
        var approveRsp = await mgrClient.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);

        // And Mgr does not SEE it on my-reports (see == act).
        var mgrPending = await GetMyReportsAsync(mgrClient);
        Assert.DoesNotContain(mgrPending, p => p == periodId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression — inactive-manager escalation (recursion advances UP only, see == act)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InactiveManagerEscalation_GrandManager_IsEffectiveApprover_Sees_AndApproves()
    {
        // EmpInactiveMgr → InactiveMgr (inactive) → Mgr. The single effective approver of
        // EmpInactiveMgr escalates UP past the inactive manager to Mgr.
        var periodId = await InsertPeriodAsync(EmpInactiveMgr, "AFD01", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(
            Mgr, EmpInactiveMgr, asOf: DateOnly.FromDateTime(DateTime.UtcNow)));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        var pending = await GetMyReportsAsync(client);
        Assert.Contains(pending, p => p == periodId); // sees

        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode); // acts
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (5) — existing pure-org-scope path UNCHANGED (default /pending)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgScopeManager_StillApproves_ViaOrgScope_NoEdgeNeeded()
    {
        // Mgr's scope covers AFD02. A period FOR an AFD02 employee (Vik, in AFD02, with no
        // reporting edge to Mgr) is approvable by Mgr purely on org-scope. STY02's tree is
        // seeded REQUIRED, so an org-scope-FALLBACK approval first 428s for confirmation (the
        // unchanged S50 flow) — confirmFallback=true then completes it. This proves the pure
        // org-scope path is UNAFFECTED by the A3 edge expansion.
        var periodId = await InsertPeriodAsync(Vik, "AFD02", "SUBMITTED");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        // Without confirmation: the S50 REQUIRED-mode 428 (org-scope fallback), preserved.
        var unconfirmed = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal((HttpStatusCode)428, unconfirmed.StatusCode);

        // With confirmation: the org-scope manager completes the approval → 200.
        var confirmed = await client.PostAsync($"/api/approval/{periodId}/approve?confirmFallback=true", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    [Fact]
    public async Task DefaultPending_OrgScopeView_Unchanged_ShowsOrgScopedPeriods_NotCrossAfdelingEdge()
    {
        // Default /pending (no my-reports) is the pure org-scope view: Mgr (scope AFD02) sees an
        // AFD02 period but NOT the cross-afdeling edge report in AFD01.
        var afd02PeriodId = await InsertPeriodAsync(Vik, "AFD02", "SUBMITTED");
        var afd01EdgePeriodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        var defaultPending = await GetPendingAsync(client, myReports: false);
        Assert.Contains(defaultPending, p => p == afd02PeriodId);          // org-scope shows AFD02
        Assert.DoesNotContain(defaultPending, p => p == afd01EdgePeriodId); // NOT the cross-afd edge
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (6) — employee-approve UNAFFECTED by edges
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmployeeApprove_UnaffectedByEdge_OnlyOwnPeriod()
    {
        // Mgr holds the designated edge over Emp, but employee-approve is the EMPLOYEE's own
        // gate — Mgr (a Leader, not Emp) cannot employee-approve Emp's period through the edge.
        var periodId = await InsertPeriodAsync(Emp, "AFD01", "SUBMITTED");

        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));

        // employee-approve requires own-data (scope) — Mgr's AFD02 scope does not cover Emp's
        // AFD01, and the edge does NOT apply to this employee-gate endpoint → 403.
        var rsp = await mgrClient.PostAsync($"/api/approval/{periodId}/employee-approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (7) — reopen: edge works on Leader arm; EMPLOYEE reopen unaffected
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reopen_Employee_Unaffected_CannotReopenOthersPeriodViaAnyEdge()
    {
        // An EMPLOYEE actor (Emp) cannot reopen via an edge — the edge OR-branch lives only in
        // the Leader arm. Emp reopening EmpInactiveMgr's (someone else's) period is denied by
        // the employee-own-data gate, never relaxed by an edge.
        var periodId = await InsertPeriodAsync(EmpInactiveMgr, "AFD01", "EMPLOYEE_APPROVED");

        var empClient = _factory.CreateClient();
        empClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp, "AFD01"));

        var rsp = await empClient.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadStatusAsync(periodId));
    }

    [Fact]
    public async Task InactiveVikar_IsSkipped_PrimaryManagerRemainsTheSingleWinner()
    {
        // R3b: if the vikar's stand-in user is INACTIVE, the vikar is skipped — resolution falls
        // back to M-if-active. We make Vik inactive, then assert Mgr (M) is again the winner.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var deact = new NpgsqlCommand(
                "UPDATE users SET is_active = FALSE WHERE user_id = @vik", conn);
            deact.Parameters.AddWithValue("vik", Vik);
            await deact.ExecuteNonQueryAsync();
        }
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Inactive vikar skipped → Mgr is again the single effective approver.
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Mgr, Emp, asOf: today));
        // The inactive vikar holds no usable authority.
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(Vik, Emp, asOf: today));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S83 / TASK-8303 — DEACTIVATED designated approver fails the in-lock authority re-check.
    //  The in-lock approve/reject path re-evaluates IsEffectiveDesignatedApproverAsync UNDER the
    //  held tree advisory (ApprovalEndpoints ~:290/:299); its FIRST gate is
    //  DesignatedApproverAuthorizer.IsActiveLeaderOrAboveAsync (the authorizer :131-145), which
    //  requires the actor to be an ACTIVE user holding a LeaderOrAbove role. When the designated
    //  approver is deactivated (users.is_active = FALSE) that gate fails CLOSED → the edge no longer
    //  grants authority, org-scope still denies (AFD02 ⊄ AFD01) → 403 on BOTH approve and reject.
    //  NO production change pins this — it asserts the existing fail-closed behavior is intact, the
    //  authorization complement to the TASK-8301 revoke-serialization change.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeactivatedDesignatedApprover_CannotApprove_NorReject_FailsClosed()
    {
        // Mgr is Emp's cross-afdeling PRIMARY designated approver (org-scope AFD02 does NOT cover
        // Emp's AFD01) — only the edge grants authority. Sanity: the edge grants it WHILE Mgr is active.
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Mgr, Emp, asOf: today),
            "Precondition: an ACTIVE Mgr must hold the designated edge over Emp.");

        // DEACTIVATE the designated approver.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var deact = new NpgsqlCommand(
                "UPDATE users SET is_active = FALSE WHERE user_id = @mgr", conn);
            deact.Parameters.AddWithValue("mgr", Mgr);
            await deact.ExecuteNonQueryAsync();
        }

        // The authority predicate now denies (the IsActiveLeaderOrAbove gate fails closed).
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(Mgr, Emp, asOf: today),
            "A deactivated approver must not hold the designated edge (fail-closed).");

        // APPROVE is denied (org-scope no + the deactivated edge denied by the in-lock re-check → 403).
        var approvePeriod = await InsertPeriodWithRangeAsync(
            Emp, "AFD01", "SUBMITTED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "AFD02"));
        var approveRsp = await client.PostAsync($"/api/approval/{approvePeriod}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(approvePeriod));

        // REJECT is denied too — a SEPARATE period (reject needs SUBMITTED/EMPLOYEE_APPROVED).
        var rejectPeriod = await InsertPeriodWithRangeAsync(
            Emp, "AFD01", "SUBMITTED", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var rejectRsp = await client.PostAsJsonAsync(
            $"/api/approval/{rejectPeriod}/reject", new { reason = "should-be-denied" });
        Assert.Equal(HttpStatusCode.Forbidden, rejectRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(rejectPeriod));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private static async Task<List<Guid>> GetMyReportsAsync(HttpClient client)
        => await GetPendingAsync(client, myReports: true);

    private static async Task<List<Guid>> GetPendingAsync(HttpClient client, bool myReports)
    {
        var url = myReports ? "/api/approval/pending?my-reports=true" : "/api/approval/pending";
        return await GetPeriodIdsAsync(client, url);
    }

    /// <summary>
    /// The by-month dashboard read. Mirrors <see cref="GetPendingAsync"/> but hits the
    /// <c>/api/approval/by-month</c> route; with <paramref name="myReports"/>=true this is the
    /// SECOND edge-authority read (<c>GetByMonthForDesignatedReportsAsync</c>) that shares the
    /// candidate CTE + the R5 single-effective-approver predicate with the pending read.
    /// </summary>
    private static Task<List<Guid>> GetByMonthMyReportsAsync(HttpClient client, int year, int month)
        => GetPeriodIdsAsync(client, $"/api/approval/by-month?year={year}&month={month}&my-reports=true");

    private static async Task<List<Guid>> GetPeriodIdsAsync(HttpClient client, string url)
    {
        var rsp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var arr = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = new List<Guid>();
        foreach (var item in arr.EnumerateArray())
            ids.Add(item.GetProperty("periodId").GetGuid());
        return ids;
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

    private static string MintLeaderToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
        // The scope's Role must be the StatsTidRoles value ("LocalLeader") — the
        // ScopeAuthorizationHandler matches RoleScope.Role against the policy's AllowedRoles.
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_AND_DESCENDANTS") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintEmployeeToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
