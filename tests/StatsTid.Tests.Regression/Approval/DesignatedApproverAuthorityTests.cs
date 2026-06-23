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
/// <c>organisation_id</c> (OQ-3a, a P7 privilege expansion). The load-bearing invariant proven
/// here is <b>see == act</b>: every period the my-reports dashboard surfaces is
/// approvable/reopenable via the SAME canonical predicate, and ADR-027 D2 (cross-tree forbidden)
/// still holds because the resolving edge is intra-tree by construction.
///
/// <para>
/// <b>CROSS-ORG fixtures (the S71 green-but-weak lesson; S92/ADR-035 flatten).</b> Post-flatten
/// the smallest authority unit is the Organisation. The designated approver (<c>t74_mgr</c>) and
/// its report (<c>t74_emp</c>) are BOTH on the STY02 Organisation, and the PRIMARY edge grants —
/// authority is proven by the EDGE (no sub-Organisation scope exists any more). A no-edge Leader on a
/// DIFFERENT Organisation under the SAME MAO (<c>t74_other</c>, STY01) proves org-scope alone does
/// NOT reach Emp (the approve still 403s), and a cross-MAO employee (<c>t74_emp_x</c>, STY05 = a
/// different tree_root) proves the D2 bound.
/// </para>
///
/// <para>
/// Topology (init.sql seed orgs, post-flatten): MIN01 (MAO) has Organisations STY01 + STY02;
/// MIN02 (MAO) has Organisation STY05 (a different tree_root). Each Organisation is its own tree
/// root.
/// </para>
///
/// <para>
/// Endpoint-level integration via <see cref="StatsTidWebApplicationFactory"/> (the real
/// Backend.Api over a fresh testcontainer) for the route→read→approve→reopen path; direct
/// <see cref="DesignatedApproverAuthorizer"/> + repository assertions for the discriminating
/// single-winner edges (vikar-supersedes-PRIMARY, inactive-vikar-skip, cross-tree-block).
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
    // STY02 Organisation (MAO MIN01):
    private const string Emp = "t74_emp";        // STY02 — the report
    private const string Mgr = "t74_mgr";        // STY02 — PRIMARY manager of Emp
    private const string Vik = "t74_vik";        // STY02 — Mgr's vikar stand-in (a Leader)
    private const string EmpInactiveMgr = "t74_emp_im"; // STY02 — reports to an INACTIVE manager
    private const string InactiveMgr = "t74_imgr";      // STY02 — INACTIVE; escalates up to Mgr
    private const string Other = "t74_other";    // STY01 — a Leader on a DIFFERENT Organisation (same
                                                 //   MAO MIN01) who holds NO edge over Emp: org-scope
                                                 //   does NOT reach Emp (STY01 ≠ STY02), so it stays a
                                                 //   genuinely-disjoint non-covering scope post-flatten.
    // STY05 Organisation (a DIFFERENT MAO MIN02 — cross-tree):
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
                (@emp,   @emp,   '$2a$11$fake', 'T74 Emp',   't74_emp@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@mgr,   @mgr,   '$2a$11$fake', 'T74 Mgr',   't74_mgr@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@vik,   @vik,   '$2a$11$fake', 'T74 Vikar', 't74_vik@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@empim, @empim, '$2a$11$fake', 'T74 EmpIM', 't74_emp_im@test.dk','STY02', 'HK', 'OK24', TRUE),
                (@imgr,  @imgr,  '$2a$11$fake', 'T74 IMgr',  't74_imgr@test.dk',  'STY02', 'HK', 'OK24', FALSE),
                (@other, @other, '$2a$11$fake', 'T74 Other', 't74_other@test.dk', 'STY01', 'HK', 'OK24', TRUE),
                (@empx,  @empx,  '$2a$11$fake', 'T74 EmpX',  't74_emp_x@test.dk', 'STY05', 'HK', 'OK24', TRUE),
                (@mgrx,  @mgrx,  '$2a$11$fake', 'T74 MgrX',  't74_mgr_x@test.dk', 'STY05', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Role assignments — Mgr/Vik/Other/MgrX are Leaders; the IsActiveLeaderOrAbove gate
        // reads role_assignments + roles.hierarchy_level. Mgr/Vik are scoped at STY02 (they DO cover
        // Emp post-flatten, but authority over Emp is proven by the designated EDGE). Other is scoped
        // at STY01 (a DIFFERENT Organisation under the same MAO) — deliberately NOT covering STY02's
        // Emp, so the no-edge Other still org-scope-DENIES Emp. MgrX is STY05 (a different MAO/tree).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@mgr,   'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@vik,   'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@other, 'LOCAL_LEADER', 'STY01', 'ORG_ONLY', 'TEST'),
                (@mgrx,  'LOCAL_LEADER', 'STY05', 'ORG_ONLY', 'TEST'),
                (@emp,   'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empim, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empx,  'EMPLOYEE',     'STY05', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        var rlRepo = new ReportingLineRepository(_dbFactory);

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the same-Organisation, same-tree edge.
        await rlRepo.AssignAsync(null, MakeLine(Emp, Mgr, TreeRootSty02));
        // EmpInactiveMgr (STY02) reports PRIMARY to InactiveMgr (STY02, inactive)…
        await rlRepo.AssignAsync(null, MakeLine(EmpInactiveMgr, InactiveMgr, TreeRootSty02));
        // …and InactiveMgr reports PRIMARY to Mgr → EmpInactiveMgr escalates up to Mgr.
        await rlRepo.AssignAsync(null, MakeLine(InactiveMgr, Mgr, TreeRootSty02));
        // EmpX (STY05) reports PRIMARY to MgrX (STY05) — the cross-MAO (cross-tree) Organisation.
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
        OrganisationId = treeRoot,
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
        OrganisationId = treeRoot,
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
    /// Used by the S74-7402 B1 cross-tree-vikar regression to construct a CROSS-MAO (cross-tree)
    /// vikar row that the Layer-1 POST guard now refuses — proving the Layer-2 authority predicate
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
            OrganisationId = treeRoot,
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
    /// The end-to-end Phase-1 test: a designated approver (Mgr, STY02) SEES Emp's period on the
    /// my-reports dashboard AND can approve it AND reopen it — all via the designated PRIMARY edge.
    /// Post-flatten Mgr ALSO org-scope-covers Emp (both on STY02), but the see/act path is the edge.
    /// </summary>
    [Fact]
    public async Task Edge_CrossAfdeling_Manager_Sees_Approves_AndReopens_OnMyReports()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

        // READ: the period appears on Mgr's my-reports (edge visibility, NOT org-scope).
        var pending = await GetMyReportsAsync(client);
        Assert.Contains(pending, p => p == periodId);

        // ACT (approve): Mgr is the designated PRIMARY approver of Emp → DESIGNATED_MANAGER (no
        // fallback 428) → 200. Post-flatten Mgr also org-scope-covers Emp, but the edge classifies it.
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
    /// my-reports AND can approve it; the vikar EDGE is the grant (Vik + Emp both on STY02).
    /// </summary>
    [Fact]
    public async Task Vikar_HoldsAuthority_Sees_AndApproves_WhileMgrIsSuperseded()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var vikClient = _factory.CreateClient();
        vikClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "STY02"));

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
    /// endpoints (Vik + Emp both on STY02; the vikar edge classifies the action as DESIGNATED).
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
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "STY02"));

        // ── period-1: SUBMITTED → APPROVE → REOPEN (proves approve + reopen-Leader-arm) ──
        var p1 = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var approveRsp = await vikClient.PostAsync($"/api/approval/{p1}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(p1));

        // reopen (Leader arm): the vikar edge grants it (Vik + Emp both on STY02) → APPROVED → DRAFT.
        var reopenRsp = await vikClient.PostAsJsonAsync(
            $"/api/approval/{p1}/reopen", new { reason = "vikar-reopen" });
        Assert.Equal(HttpStatusCode.OK, reopenRsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(p1));

        // ── period-2 (distinct range): SUBMITTED → REJECT (proves reject) ──
        var p2 = await InsertPeriodWithRangeAsync(
            Emp, "STY02", "SUBMITTED", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var rejectRsp = await vikClient.PostAsJsonAsync(
            $"/api/approval/{p2}/reject", new { reason = "vikar-reject" });
        Assert.Equal(HttpStatusCode.OK, rejectRsp.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(p2));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (1) — same-Organisation designated edge → approvable AND reopenable AND
    //  visible (covered by the integration test above; this asserts the reject path of the same edge)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edge_CrossAfdeling_Manager_CanReject()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

        var rejectRsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reject", new { reason = "edge-reject" });
        Assert.Equal(HttpStatusCode.OK, rejectRsp.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (2) — cross-MAO edge → BLOCKED (different tree_root, D2)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CrossStyrelse_Manager_CannotSee_NorApprove_NorIsEffectiveApprover()
    {
        var periodId = await InsertPeriodAsync(EmpX, "STY05", "SUBMITTED");

        // Mgr (STY02) is NOT EmpX's (STY05) effective approver — the resolver returns
        // EmpX's own manager, never a cross-Organisation actor (ValidateSameOrganisationAsync invariant).
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var isEdge = await authorizer.IsEffectiveDesignatedApproverAsync(
            Mgr, EmpX, asOf: DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.False(isEdge);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

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
    //  discrimination: the same-Organisation designated approver sees the report on by-month, and a
    //  cross-MAO (cross-tree) manager does not.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// By-month read completeness: the designated approver (Mgr, STY02, the PRIMARY effective approver
    /// of Emp on the same STY02 Organisation) SEES Emp's period on the
    /// <c>/api/approval/by-month?my-reports=true</c> read for the period's month — edge visibility. A
    /// cross-MAO manager (MgrX, STY05 = a different tree_root) does NOT see it (ADR-027 D2). Emp's
    /// seeded period is May 2026.
    /// </summary>
    [Fact]
    public async Task ByMonth_CrossAfdelingDesignated_Sees_AndCrossStyrelse_DoesNot()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED"); // May 2026

        // Mgr (the same-Organisation designated approver) SEES Emp's period on the by-month read.
        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));
        var mgrByMonth = await GetByMonthMyReportsAsync(mgrClient, 2026, 5);
        Assert.Contains(mgrByMonth, p => p == periodId);

        // MgrX (cross-MAO, STY05 tree) does NOT see Emp's STY02 period — the candidate set
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
    //  manager, not a vikar) gains BOTH visibility and authority within the same Organisation/tree.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Direct admin-ACTING precedence: <c>Vik</c> (a STY02 Leader, NOT Emp's PRIMARY manager
    /// [that is Mgr]) holds a DIRECT admin-assigned ACTING edge over Emp. ACTING is the resolver's
    /// precedence step 1, so Vik — not Mgr — is the single effective approver. Asserts the predicate
    /// grants Vik authority, the period is visible on Vik's my-reports, and approve succeeds (200) —
    /// precedence step 1 grants BOTH visibility and authority via the edge. (Pre-flatten this used the
    /// no-edge <c>Other</c> as the ACTING holder; post-flatten Other lives on a DIFFERENT Organisation
    /// STY01 — a cross-tree ACTING edge would be denied by the same-tree predicate — so the same-tree
    /// Leader <c>Vik</c> carries the ACTING edge here.)
    /// </summary>
    [Fact]
    public async Task AdminActing_DirectEdge_TakesPrecedence_Sees_AndApproves_SameOrganisation()
    {
        // Vik holds a DIRECT admin-ACTING edge over Emp (same STY02 Organisation/tree).
        // ACTING wins resolver precedence step 1 over Emp's PRIMARY manager (Mgr).
        await new ReportingLineRepository(_dbFactory)
            .AssignAsync(null, MakeActingLine(Emp, Vik, TreeRootSty02));
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // The admin-ACTING holder is the single effective approver (precedence over PRIMARY).
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Vik, Emp, asOf: today));

        var vikClient = _factory.CreateClient();
        vikClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "STY02"));

        // SEES it on my-reports via the designated (ACTING) edge.
        var pending = await GetMyReportsAsync(vikClient);
        Assert.Contains(pending, p => p == periodId);

        // …and APPROVES it (authority) — precedence step 1 grants both see and act.
        var approveRsp = await vikClient.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S74-7402 B1 (SECURITY, ADR-027 D2) — the cross-MAO VIKAR hole, two-layer fix
    //
    //  Defect: the /delegate POST validated the vikar's ROLE + org-scope coverage but NEVER
    //  same-tree between the absent approver and the vikar. A global-scoped (or cross-tree-
    //  scoped) leader could be planted as a vikar in another Organisation/MAO, win the resolver's
    //  vikar consult, and — under 7402's authority expansion — gain approve/reject/reopen over
    //  a report in a DIFFERENT tree. The earlier cross-tree test only covered an actor
    //  holding NO edge; it masked this vikar route.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LAYER 2 (predicate, defense-in-depth): even a DIRECTLY-PLANTED cross-tree vikar row
    /// (MgrX, STY05, set as Mgr's vikar) is denied authority over Emp (STY02). The resolver's
    /// vikar consult WOULD return MgrX as the single winner for Emp (it does not itself check
    /// tree), so without the predicate same-tree re-check MgrX would gain cross-tree
    /// authority. Assert: not the effective approver, not on my-reports, approve → 403.
    /// </summary>
    [Fact]
    public async Task CrossTreeVikar_DirectlyPlanted_IsDenied_ByPredicate_NotVisible_NorApprovable()
    {
        // MgrX lives in STY05; plant him as Mgr's (STY02) vikar — a cross-MAO (cross-tree) vikar row
        // (the row the Layer-1 POST guard now refuses; here we plant it directly to prove the
        // predicate Layer-2 denies it independently of edge creation).
        await CreateVikarRawAsync(Mgr, MgrX, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), TreeRootSty05);
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Sanity: the resolver DOES return MgrX as the single winner for Emp (cross-tree vikar
        // wins the consult) — so the ONLY thing standing between MgrX and cross-tree
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
    /// LAYER 1 (source): the /delegate POST refuses a cross-tree (cross-MAO) <c>actingManagerId</c>.
    /// Mgr (STY02, the absent approver, PRIMARY manager of Emp) attempts to delegate to MgrX
    /// (STY05). MgrX is granted a GLOBAL scope so the role + org-scope coverage checks PASS —
    /// isolating the new same-tree guard as the gate that fires → 400 (the server message still says
    /// "same styrelse (tree)"). No vikar row is created.
    /// </summary>
    [Fact]
    public async Task DelegatePost_CrossStyrelseActingManager_IsRejected_SameTree_400()
    {
        // Make MgrX a global-scoped leader so steps 5 (role) + 6 (org-scope coverage of Emp on
        // STY02) PASS — the realistic attack vector, and it isolates the Layer-1 same-tree guard.
        await GrantGlobalLeaderScopeAsync(MgrX);

        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

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
    /// The fix did NOT over-block: a LEGITIMATE same-Organisation vikar still holds authority.
    /// Vik (STY02) is Mgr's (STY02) vikar and the single effective approver of Emp (STY02) — Vik
    /// and Emp share the STY02 tree_root, so BOTH layers pass: the predicate grants Vik authority
    /// over Emp AND the period is visible on Vik's my-reports.
    /// </summary>
    [Fact]
    public async Task SameStyrelse_CrossAfdelingVikar_StillHoldsAuthority_AndIsVisible()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)); // STY02 root
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Layer 2 PASSES: Vik (STY02) and Emp (STY02) share the STY02 tree root.
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Vik, Emp, asOf: today));

        var vikClient = _factory.CreateClient();
        vikClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Vik, "STY02"));

        // Visible on my-reports (the legitimate same-Organisation vikar is NOT over-blocked).
        var pending = await GetMyReportsAsync(vikClient);
        Assert.Contains(pending, p => p == periodId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (3) — edge NOT held → not approvable; and PRIMARY-superseded-by-vikar
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActorWithoutEdge_IsNotEffectiveApprover_AndCannotApprove()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        // Other is a Leader on STY01 (a DIFFERENT Organisation) holding NO reporting edge over Emp —
        // neither an edge nor org-scope (STY01 ⊉ STY02) reaches Emp.
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(
            Other, Emp, asOf: DateOnly.FromDateTime(DateTime.UtcNow)));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Other, "STY01"));

        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    [Fact]
    public async Task PrimaryManager_SupersededByActiveVikar_IsNotTheSingleWinner_AndCannotApprove()
    {
        // Vik is the active vikar for Mgr → the single effective approver of Emp is Vik, NOT Mgr.
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(Vik, Emp, asOf: today));
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(Mgr, Emp, asOf: today));

        // The HTTP path agrees that Mgr is NOT the designated approver: the edge now resolves to Vik,
        // not Mgr, so Mgr is no longer a DESIGNATED_MANAGER for Emp. S94/ADR-035 flat authority:
        // CanApprove = edge OR HR/Admin-over-emp-Org. Mgr is a LEADER (not HR/Admin), so even though
        // Mgr (STY02) org-scope-covers Emp (STY02), a non-designated LEADER is NO LONGER admitted via
        // org-scope → the approve is DENIED (403). (Pre-S94 the org-scope arm was unfloored, so this
        // case was an ORG_SCOPE_FALLBACK 428; the leader-org-scope branch is now retired.)
        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));
        var approveRsp = await mgrClient.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);

        // And Mgr does not SEE it on my-reports (see == act): the designated edge resolves to Vik.
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
        var periodId = await InsertPeriodAsync(EmpInactiveMgr, "STY02", "SUBMITTED");

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        Assert.True(await authorizer.IsEffectiveDesignatedApproverAsync(
            Mgr, EmpInactiveMgr, asOf: DateOnly.FromDateTime(DateTime.UtcNow)));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

        var pending = await GetMyReportsAsync(client);
        Assert.Contains(pending, p => p == periodId); // sees

        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode); // acts
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S94 / ADR-035 OQ4 (INVERTED) — a non-designated LEADER on org-scope is now DENIED
    //
    //  Pre-S94 this test (`OrgScopeManager_StillApproves_ViaOrgScope_NoEdgeNeeded`) asserted that a
    //  non-designated LEADER scoped over the employee's org could approve PURELY on org-scope (a
    //  428→confirmFallback→200 round-trip). S94 retires the unfloored leader-org-scope branch:
    //  CanApprove = edge OR HR/Admin-over-emp-Org. A LEADER is neither designated (no edge) nor
    //  HR/Admin → DENIED (403). RED-on-old: pre-S94 this was a 200.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgScopeLeader_NoEdge_IsNowDenied_NotApprovableViaOrgScope()
    {
        // Mgr's scope covers STY02. A period FOR a STY02 employee (Vik, with no reporting edge to
        // Mgr) was approvable by Mgr purely on org-scope pre-S94 (the 428→confirmFallback flow). S94
        // floors the org-scope fallback at LocalHR: Mgr is a LEADER (not HR/Admin) and holds no edge
        // over Vik → DENIED. There is no 428 gate any more.
        var periodId = await InsertPeriodAsync(Vik, "STY02", "SUBMITTED");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

        // The non-designated leader is DENIED (403) — no edge, and the leader-org-scope branch is retired.
        var rsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    [Fact]
    public async Task DefaultPending_OrgScopeView_Unchanged_ShowsOrgScopedPeriods_NotOutOfScope()
    {
        // Default /pending (no my-reports) is the pure org-scope view: Mgr (scope STY02) sees an
        // in-scope STY02 period (Vik's, no edge to Mgr) but NOT a period in a DIFFERENT Organisation
        // (Other, STY01 — same MAO, no org-scope reach, no edge). S92 flatten: with the
        // sub-Organisation scope gone, the out-of-org-scope discriminator is now a sibling
        // Organisation — Other on STY01 is the genuinely-disjoint subject.
        var inScopePeriodId = await InsertPeriodAsync(Vik, "STY02", "SUBMITTED");
        var outOfScopePeriodId = await InsertPeriodAsync(Other, "STY01", "SUBMITTED");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY02"));

        var defaultPending = await GetPendingAsync(client, myReports: false);
        Assert.Contains(defaultPending, p => p == inScopePeriodId);       // org-scope shows STY02
        Assert.DoesNotContain(defaultPending, p => p == outOfScopePeriodId); // NOT the out-of-scope STY01 period
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  D4 regression case (6) — employee-approve UNAFFECTED by edges
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmployeeApprove_UnaffectedByEdge_OnlyOwnPeriod()
    {
        // Mgr holds the designated edge over Emp, but employee-approve is the EMPLOYEE's own
        // gate — a Leader (not Emp) cannot employee-approve Emp's period THROUGH THE EDGE.
        // S92 flatten: Mgr's DB org (STY02) now org-scope-covers Emp, so to isolate the EDGE (and
        // prove it does NOT unlock employee-approve) we mint Mgr's token with a NON-covering scope
        // (STY01 — a different Organisation). Mgr still HOLDS the PRIMARY edge in the DB, but neither
        // the edge nor the (deliberately disjoint) token scope grants the employee gate → 403.
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY01"));

        // employee-approve requires own-data (scope) — Mgr's token scope (STY01) does not cover Emp's
        // STY02 period, and the edge does NOT apply to this employee-gate endpoint → 403.
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
        var periodId = await InsertPeriodAsync(EmpInactiveMgr, "STY02", "EMPLOYEE_APPROVED");

        var empClient = _factory.CreateClient();
        empClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp, "STY02"));

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
    //  grants authority; with a deliberately disjoint token scope (STY01, NOT covering Emp's STY02)
    //  org-scope also denies → 403 on BOTH approve and reject. (S92 flatten: Mgr's DB org STY02 would
    //  org-cover Emp, so to isolate the deactivated-EDGE fail-closed we mint a non-covering STY01 token.)
    //  NO production change pins this — it asserts the existing fail-closed behavior is intact, the
    //  authorization complement to the TASK-8301 revoke-serialization change.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeactivatedDesignatedApprover_CannotApprove_NorReject_FailsClosed()
    {
        // Mgr is Emp's PRIMARY designated approver on the same STY02 Organisation — the edge grants
        // authority. Sanity: the edge grants it WHILE Mgr is active.
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

        // APPROVE is denied: the deactivated edge is denied by the in-lock re-check AND Mgr's token
        // scope (STY01) does not cover Emp's STY02 period → org-scope also denies → 403.
        var approvePeriod = await InsertPeriodWithRangeAsync(
            Emp, "STY02", "SUBMITTED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(Mgr, "STY01"));
        var approveRsp = await client.PostAsync($"/api/approval/{approvePeriod}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(approvePeriod));

        // REJECT is denied too — a SEPARATE period (reject needs SUBMITTED/EMPLOYEE_APPROVED).
        var rejectPeriod = await InsertPeriodWithRangeAsync(
            Emp, "STY02", "SUBMITTED", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
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
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") };
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
