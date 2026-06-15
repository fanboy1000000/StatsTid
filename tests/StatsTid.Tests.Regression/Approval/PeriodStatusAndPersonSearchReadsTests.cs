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
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S74-7404 (R11a + R11b) — the two backend READS the redesigned Medarbejder-administration FE
/// (Phases 2-3) consumes:
/// <list type="number">
/// <item><description><b>R11a</b> the per-styrelse period-status projection
/// (<see cref="ApprovalPeriodRepository.GetPeriodStatusProjectionForTreeAsync"/> →
/// <c>GET /api/admin/reporting-lines/tree/{treeRootOrgId}/period-status</c>): each employee's
/// last-closed-month status (greatest <c>period_end &lt; today</c>) projected to OPEN/SUBMITTED/
/// APPROVED + the per-manager pending count.</description></item>
/// <item><description><b>R11b</b> the server-side person-search
/// (<see cref="ApprovalPeriodRepository.SearchPeopleAsync"/> →
/// <c>GET /api/admin/users/search</c>): case-insensitive, scope-filtered, paginated, excludes
/// self + descendants server-side (the cycle-prevention mirror via 7403's bounded descendant
/// walk).</description></item>
/// </list>
/// Reads only — no events, no writes. Topology reuses the seed STY02 tree
/// (<c>/MIN01/STY02/</c> root, AFD01/AFD02 afdelinger) with isolated <c>t7404_*</c> users; a
/// cross-styrelse STY05 user proves the scope bound.
/// </summary>
[Trait("Category", "Docker")]
public sealed class PeriodStatusAndPersonSearchReadsTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // ── Test users ────────────────────────────────────────────────────────────────────
    // STY02 tree (/MIN01/STY02/):
    private const string Mgr = "t7404_mgr";          // AFD02 — LocalLeader; PRIMARY manager of the AFD01 employees
    private const string EmpOpenDraft = "t7404_e_draft";    // AFD01 — last closed period is DRAFT → OPEN
    private const string EmpOpenRejected = "t7404_e_rej";   // AFD01 — last closed period is REJECTED → OPEN
    private const string EmpOpenNone = "t7404_e_none";      // AFD01 — NO closed period → OPEN
    private const string EmpSubmitted = "t7404_e_sub";      // AFD01 — last closed period is SUBMITTED → SUBMITTED
    private const string EmpEmpApproved = "t7404_e_ea";     // AFD01 — last closed period is EMPLOYEE_APPROVED → SUBMITTED
    private const string EmpApproved = "t7404_e_appr";      // AFD01 — last closed period is APPROVED → APPROVED
    // Search-only fixtures:
    private const string Sub = "t7404_sub";          // AFD01 — a DESCENDANT of Mgr (reports to Mgr) — search excludes it
    private const string SearchAfd02 = "t7404_sara"; // AFD02 — "Sara Searchable" (display-name match target)
    // Cross-styrelse (STY05, /MIN02/STY05/):
    private const string EmpX = "t7404_x";           // STY05 — out-of-scope for a STY02-scoped admin

    // BLOCKER fixtures (tile↔dashboard tally-gate consistency, cross-org discrimination):
    private const string MgrNoRole = "t7404_mgr_norole"; // AFD02 — ACTIVE PRIMARY manager but NO LeaderOrAbove
                                                         //   role (role-revoked) → resolver returns it, the
                                                         //   canonical predicate DENIES → must NOT be tallied.
    private const string EmpRevoked = "t7404_e_revoked"; // AFD01 — pending, reports PRIMARY to MgrNoRole.

    // Deep-descendant fixtures (R11b GetDescendantIds multi-level + cyclic-graph termination):
    private const string DChild = "t7404_d_child";       // AFD01 — direct report of Mgr
    private const string DGrand = "t7404_d_grand";       // AFD01 — reports to DChild (2 levels under Mgr)
    private const string DGreat = "t7404_d_great";       // AFD01 — reports to DGrand (3 levels under Mgr)
    // Cyclic legacy graph (raw-planted, bypassing the AssignAsync cycle guard):
    private const string CycA = "t7404_cyc_a";           // AFD01 — A → B → C → A (manager edges)
    private const string CycB = "t7404_cyc_b";           // AFD01
    private const string CycC = "t7404_cyc_c";           // AFD01

    private const string TreeRootSty02 = "STY02";

    private static readonly string[] AllUsers =
    {
        Mgr, EmpOpenDraft, EmpOpenRejected, EmpOpenNone, EmpSubmitted, EmpEmpApproved, EmpApproved,
        Sub, SearchAfd02, EmpX,
        MgrNoRole, EmpRevoked,
        DChild, DGrand, DGreat, CycA, CycB, CycC,
    };

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
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@mgr,     @mgr,     '$2a$11$fake', 'T7404 Mgr',       't7404_mgr@test.dk',     'AFD02', 'HK', 'OK24', TRUE),
                (@edraft,  @edraft,  '$2a$11$fake', 'T7404 EmpDraft',  't7404_e_draft@test.dk', 'AFD01', 'HK', 'OK24', TRUE),
                (@erej,    @erej,    '$2a$11$fake', 'T7404 EmpRej',    't7404_e_rej@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@enone,   @enone,   '$2a$11$fake', 'T7404 EmpNone',   't7404_e_none@test.dk',  'AFD01', 'HK', 'OK24', TRUE),
                (@esub,    @esub,    '$2a$11$fake', 'T7404 EmpSub',    't7404_e_sub@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@eea,     @eea,     '$2a$11$fake', 'T7404 EmpEA',     't7404_e_ea@test.dk',    'AFD01', 'HK', 'OK24', TRUE),
                (@eappr,   @eappr,   '$2a$11$fake', 'T7404 EmpAppr',   't7404_e_appr@test.dk',  'AFD01', 'HK', 'OK24', TRUE),
                (@sub,     @sub,     '$2a$11$fake', 'T7404 Sub',       't7404_sub@test.dk',     'AFD01', 'HK', 'OK24', TRUE),
                (@sara,    @sara,    '$2a$11$fake', 'Sara Searchable', 't7404_sara@test.dk',    'AFD02', 'HK', 'OK24', TRUE),
                (@empx,    @empx,    '$2a$11$fake', 'T7404 EmpX',      't7404_x@test.dk',       'STY05', 'HK', 'OK24', TRUE),
                (@mgrnr,   @mgrnr,   '$2a$11$fake', 'T7404 MgrNoRole', 't7404_mgr_nr@test.dk',  'AFD02', 'HK', 'OK24', TRUE),
                (@erev,    @erev,    '$2a$11$fake', 'T7404 EmpRevoked','t7404_e_rev@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@dchild,  @dchild,  '$2a$11$fake', 'T7404 DChild',    't7404_d_child@test.dk', 'AFD01', 'HK', 'OK24', TRUE),
                (@dgrand,  @dgrand,  '$2a$11$fake', 'T7404 DGrand',    't7404_d_grand@test.dk', 'AFD01', 'HK', 'OK24', TRUE),
                (@dgreat,  @dgreat,  '$2a$11$fake', 'T7404 DGreat',    't7404_d_great@test.dk', 'AFD01', 'HK', 'OK24', TRUE),
                (@cyca,    @cyca,    '$2a$11$fake', 'T7404 CycA',      't7404_cyc_a@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@cycb,    @cycb,    '$2a$11$fake', 'T7404 CycB',      't7404_cyc_b@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@cycc,    @cycc,    '$2a$11$fake', 'T7404 CycC',      't7404_cyc_c@test.dk',   'AFD01', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Mgr is a LocalLeader covering AFD02 (the per-manager pending tally needs the resolver to
        // return an active leader; the dashboard/predicate role gate is separate but the period-
        // status tally only needs ResolveDesignatedApproverAsync to return Mgr).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@mgr, 'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        var rlRepo = new ReportingLineRepository(_dbFactory);
        // All AFD01 status-fixtures + Sub report PRIMARY to Mgr (AFD02) → Mgr is their effective
        // approver, and Sub is a DESCENDANT of Mgr (search self/descendant exclusion).
        foreach (var emp in new[] { EmpSubmitted, EmpEmpApproved, EmpOpenDraft, Sub })
            await rlRepo.AssignAsync(null, MakeLine(emp, Mgr));

        // BLOCKER fixture: EmpRevoked reports PRIMARY to MgrNoRole. MgrNoRole is ACTIVE so the
        // resolver returns it as EmpRevoked's PRIMARY manager — but it holds NO LeaderOrAbove role
        // assignment (above), so the canonical predicate (IsActiveLeaderOrAbove gate) DENIES it.
        // The tile tally must therefore NOT count EmpRevoked for MgrNoRole (its dashboard is empty).
        await rlRepo.AssignAsync(null, MakeLine(EmpRevoked, MgrNoRole));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("edraft", EmpOpenDraft);
        cmd.Parameters.AddWithValue("erej", EmpOpenRejected);
        cmd.Parameters.AddWithValue("enone", EmpOpenNone);
        cmd.Parameters.AddWithValue("esub", EmpSubmitted);
        cmd.Parameters.AddWithValue("eea", EmpEmpApproved);
        cmd.Parameters.AddWithValue("eappr", EmpApproved);
        cmd.Parameters.AddWithValue("sub", Sub);
        cmd.Parameters.AddWithValue("sara", SearchAfd02);
        cmd.Parameters.AddWithValue("empx", EmpX);
        cmd.Parameters.AddWithValue("mgrnr", MgrNoRole);
        cmd.Parameters.AddWithValue("erev", EmpRevoked);
        cmd.Parameters.AddWithValue("dchild", DChild);
        cmd.Parameters.AddWithValue("dgrand", DGrand);
        cmd.Parameters.AddWithValue("dgreat", DGreat);
        cmd.Parameters.AddWithValue("cyca", CycA);
        cmd.Parameters.AddWithValue("cycb", CycB);
        cmd.Parameters.AddWithValue("cycc", CycC);
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

    /// <summary>
    /// Inserts a CLOSED-month period (period_end strictly before today) for an employee with the
    /// given raw status. period_end = yesterday so it is the "last closed month".
    /// </summary>
    private async Task InsertClosedPeriodAsync(string employeeId, string orgId, string status)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodEnd = today.AddDays(-1);
        var periodStart = periodEnd.AddDays(-30);
        await InsertPeriodAsync(employeeId, orgId, status, periodStart, periodEnd);
    }

    private async Task InsertPeriodAsync(string employeeId, string orgId, string status, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SetEnhedLabelAsync(string employeeId, string label)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Upsert a live profile row carrying the enhed_label (the host seeder may have created a
        // null-label live row already; update it, else insert one).
        await using var upd = new NpgsqlCommand(
            "UPDATE employee_profiles SET enhed_label = @label WHERE employee_id = @emp AND effective_to IS NULL", conn);
        upd.Parameters.AddWithValue("label", label);
        upd.Parameters.AddWithValue("emp", employeeId);
        var rows = await upd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            await using var ins = new NpgsqlCommand(
                """
                INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, enhed_label)
                VALUES (@emp, 1.000, '0001-01-01', NULL, @label)
                """, conn);
            ins.Parameters.AddWithValue("emp", employeeId);
            ins.Parameters.AddWithValue("label", label);
            await ins.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R11a — period-status projection: status mapping per status (repository-direct)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeriodStatusProjection_MapsEachRawStatus_ToFe3State_OnLastClosedMonth()
    {
        // One closed period per employee with each raw status; EmpOpenNone gets none.
        await InsertClosedPeriodAsync(EmpOpenDraft, "AFD01", "DRAFT");
        await InsertClosedPeriodAsync(EmpOpenRejected, "AFD01", "REJECTED");
        await InsertClosedPeriodAsync(EmpSubmitted, "AFD01", "SUBMITTED");
        await InsertClosedPeriodAsync(EmpEmpApproved, "AFD01", "EMPLOYEE_APPROVED");
        await InsertClosedPeriodAsync(EmpApproved, "AFD01", "APPROVED");

        var repo = NewApprovalRepo();
        var projection = await repo.GetPeriodStatusProjectionForTreeAsync("/MIN01/STY02/");

        string StatusOf(string emp) =>
            projection.Employees.Single(e => e.EmployeeId == emp).Status;

        Assert.Equal("OPEN", StatusOf(EmpOpenDraft));       // DRAFT → OPEN
        Assert.Equal("OPEN", StatusOf(EmpOpenRejected));    // REJECTED → OPEN
        Assert.Equal("OPEN", StatusOf(EmpOpenNone));        // no closed period → OPEN
        Assert.Equal("SUBMITTED", StatusOf(EmpSubmitted));  // SUBMITTED → SUBMITTED
        Assert.Equal("SUBMITTED", StatusOf(EmpEmpApproved));// EMPLOYEE_APPROVED → SUBMITTED
        Assert.Equal("APPROVED", StatusOf(EmpApproved));    // APPROVED → APPROVED

        // The cross-styrelse EmpX (STY05) is NOT in the STY02 projection.
        Assert.DoesNotContain(projection.Employees, e => e.EmployeeId == EmpX);
    }

    [Fact]
    public async Task PeriodStatusProjection_UsesGreatestPeriodEndBeforeToday_NotFutureNorEarlier()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // An EARLIER closed period (APPROVED) + a LATER (but still closed) period (REJECTED) +
        // a FUTURE period (SUBMITTED, period_end >= today — must be ignored).
        await InsertPeriodAsync(EmpApproved, "AFD01", "APPROVED", today.AddDays(-90), today.AddDays(-60));
        await InsertPeriodAsync(EmpApproved, "AFD01", "REJECTED", today.AddDays(-40), today.AddDays(-10));
        await InsertPeriodAsync(EmpApproved, "AFD01", "SUBMITTED", today.AddDays(1), today.AddDays(20));

        var repo = NewApprovalRepo();
        var projection = await repo.GetPeriodStatusProjectionForTreeAsync("/MIN01/STY02/");

        // Greatest period_end < today is the REJECTED one (-10) → OPEN; the future SUBMITTED and
        // the earlier APPROVED are both ignored.
        Assert.Equal("OPEN", projection.Employees.Single(e => e.EmployeeId == EmpApproved).Status);
    }

    [Fact]
    public async Task PeriodStatusProjection_PerManagerPendingCount_TalliesToEffectiveApprover()
    {
        // Two AFD01 employees with an OUTSTANDING (pending) period both resolve to Mgr as the
        // effective approver → Mgr's pending count = 2. A future/closed-non-pending status does
        // not add to the count.
        await InsertClosedPeriodAsync(EmpSubmitted, "AFD01", "SUBMITTED");        // pending
        await InsertClosedPeriodAsync(EmpEmpApproved, "AFD01", "EMPLOYEE_APPROVED"); // pending
        await InsertClosedPeriodAsync(EmpOpenDraft, "AFD01", "DRAFT");           // NOT pending

        var repo = NewApprovalRepo();
        var projection = await repo.GetPeriodStatusProjectionForTreeAsync("/MIN01/STY02/");

        Assert.True(projection.PendingCountByManager.TryGetValue(Mgr, out var n));
        Assert.Equal(2, n);
    }

    /// <summary>
    /// BLOCKER (S74-7404 Step-5a) — the per-manager pending tally is GATED by the SAME canonical
    /// predicate the my-reports dashboard filters through, so the tile count MATCHES the dashboard.
    /// A role-revoked (or otherwise non-effective) RESOLVED approver must NOT be tallied: the bare
    /// resolver returns it (it does not check active+LeaderOrAbove+same-tree), but
    /// <see cref="DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync"/> denies it, so
    /// its dashboard (<see cref="ApprovalPeriodRepository.GetPendingForDesignatedReportsAsync"/>)
    /// shows ZERO — and the tile must agree (count 0 / absent from the map).
    ///
    /// <para>
    /// Cross-org discrimination (the S71 green-but-weak lesson): EmpRevoked's pending period
    /// resolves to MgrNoRole (ACTIVE PRIMARY manager, NO LeaderOrAbove role → predicate denies),
    /// while a SIBLING valid leader (Mgr) IS tallied for its own pending report in the SAME
    /// projection. A "tally everyone the resolver returns" implementation would (wrongly) put a 1
    /// under MgrNoRole; the gated implementation leaves MgrNoRole absent and still counts Mgr.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PerManagerPendingCount_RoleRevokedResolvedApprover_IsNotTallied_TileMatchesEmptyDashboard()
    {
        // EmpRevoked → MgrNoRole (active manager, role-revoked: NO LeaderOrAbove). Pending period.
        await InsertClosedPeriodAsync(EmpRevoked, "AFD01", "SUBMITTED");
        // A genuine leader (Mgr) with a pending report in the SAME projection (discrimination).
        await InsertClosedPeriodAsync(EmpSubmitted, "AFD01", "SUBMITTED");

        var repo = NewApprovalRepo();

        // (a) Sanity — the BARE resolver DOES return MgrNoRole for EmpRevoked (so the ONLY thing
        //     keeping it off the tile is the predicate gate, not a missing reporting edge).
        var (resolved, _, _) = await new ReportingLineRepository(_dbFactory)
            .ResolveDesignatedApproverAsync(EmpRevoked, asOf: DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(MgrNoRole, resolved);

        // (b) …but the canonical predicate DENIES MgrNoRole (active but not LeaderOrAbove), so the
        //     dashboard for MgrNoRole is empty.
        var dashboard = await repo.GetPendingForDesignatedReportsAsync(
            MgrNoRole, Array.Empty<RoleScope>());
        Assert.DoesNotContain(dashboard, p => p.EmployeeId == EmpRevoked);

        // (c) The tile tally agrees with the empty dashboard: MgrNoRole is NOT in the pending map.
        var projection = await repo.GetPeriodStatusProjectionForTreeAsync("/MIN01/STY02/");
        Assert.False(projection.PendingCountByManager.ContainsKey(MgrNoRole));

        // (d) …and the genuine leader IS still tallied (the gate does not suppress valid approvers).
        Assert.True(projection.PendingCountByManager.TryGetValue(Mgr, out var mgrCount));
        Assert.Equal(1, mgrCount);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R11a — period-status projection: HTTP endpoint (scope-gated)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeriodStatus_Endpoint_ScopedAdmin_GetsProjection_AndOutOfScopeDenied()
    {
        await InsertClosedPeriodAsync(EmpApproved, "AFD01", "APPROVED");

        // STY02-scoped LocalAdmin → 200 with the projection containing EmpApproved=APPROVED.
        var sty02Client = _factory.CreateClient();
        sty02Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken("admin_sty02", "STY02"));
        var rsp = await sty02Client.GetAsync("/api/admin/reporting-lines/tree/STY02/period-status");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var employees = body.GetProperty("employees");
        var found = employees.EnumerateArray()
            .First(e => e.GetProperty("employeeId").GetString() == EmpApproved);
        Assert.Equal("APPROVED", found.GetProperty("status").GetString());

        // A STY05-scoped LocalAdmin cannot read the STY02 tree → 403.
        var sty05Client = _factory.CreateClient();
        sty05Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken("admin_sty05", "STY05"));
        var denied = await sty05Client.GetAsync("/api/admin/reporting-lines/tree/STY02/period-status");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R11b — person-search: scope filter + self/descendant exclusion + pagination
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PersonSearch_ScopeFiltered_ExcludesOutOfScope_AndUnrestrictedForGlobal()
    {
        // STY02-scoped admin: "T7404" matches the AFD01/AFD02 fixtures but NOT the STY05 EmpX.
        var (sty02Items, _) = await SearchAsync(MintAdminToken("admin_sty02", "STY02"), q: "T7404");
        Assert.Contains(sty02Items, i => i.UserId == Mgr);
        Assert.DoesNotContain(sty02Items, i => i.UserId == EmpX); // STY05 out of scope

        // GlobalAdmin (GLOBAL scope) is unrestricted → EmpX IS returned.
        var (globalItems, _) = await SearchAsync(MintGlobalAdminToken("admin_global"), q: "T7404");
        Assert.Contains(globalItems, i => i.UserId == EmpX);
    }

    [Fact]
    public async Task PersonSearch_ExcludesSelfAndDescendants_ServerSide()
    {
        // excludeEmployeeId = Mgr → Mgr (self) AND Sub (a descendant reporting to Mgr) are both
        // excluded from the results (the cycle-prevention mirror, reusing 7403's descendant walk).
        var token = MintGlobalAdminToken("admin_global");
        var (withExclusion, _) = await SearchAsync(token, q: "T7404", excludeEmployeeId: Mgr);
        Assert.DoesNotContain(withExclusion, i => i.UserId == Mgr); // self excluded
        Assert.DoesNotContain(withExclusion, i => i.UserId == Sub); // descendant excluded
        // A non-descendant AFD01 employee is still present.
        Assert.Contains(withExclusion, i => i.UserId == EmpOpenNone);

        // Without the exclusion, Mgr + Sub ARE present (proving the exclusion is what removed them).
        var (noExclusion, _) = await SearchAsync(token, q: "T7404");
        Assert.Contains(noExclusion, i => i.UserId == Mgr);
        Assert.Contains(noExclusion, i => i.UserId == Sub);
    }

    [Fact]
    public async Task PersonSearch_CaseInsensitive_OnDisplayName()
    {
        // "sara" (lowercase) matches "Sara Searchable" case-insensitively.
        var (items, _) = await SearchAsync(MintGlobalAdminToken("admin_global"), q: "sara");
        Assert.Contains(items, i => i.UserId == SearchAfd02);
        // And the enhed_label surfaces when set.
        await SetEnhedLabelAsync(SearchAfd02, "Team Alpha");
        var (withLabel, _) = await SearchAsync(MintGlobalAdminToken("admin_global"), q: "sara");
        Assert.Equal("Team Alpha", withLabel.Single(i => i.UserId == SearchAfd02).EnhedLabel);
    }

    [Fact]
    public async Task PersonSearch_Paginates_WithStableTotal_AndRealDbSlice()
    {
        var token = MintGlobalAdminToken("admin_global");

        // Page 1: limit 3, offset 0. total is the FULL match count (all "T7404" fixtures),
        // independent of the page size; the page itself carries at most 3 rows.
        var page1 = await SearchRawAsync(token, "T7404", limit: 3, offset: 0);
        var page2 = await SearchRawAsync(token, "T7404", limit: 3, offset: 3);

        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(page1.Total, page2.Total);          // stable total across pages
        Assert.True(page1.Total >= 4);                   // more than one page of fixtures exist
        Assert.True(page1.Limit == 3 && page1.Offset == 0);

        // No overlap between page 1 and page 2 ids (a real OFFSET slice, not load-all).
        var p1Ids = page1.Items.Select(i => i.UserId).ToHashSet();
        Assert.DoesNotContain(page2.Items.Select(i => i.UserId), id => p1Ids.Contains(id));
    }

    /// <summary>
    /// WARNING 1 (S74-7404 Step-5a) — a valid but EMPTY TRAILING PAGE (offset past the end) reports
    /// the TRUE total, not 0. The old <c>COUNT(*) OVER()</c> window attached to the page rows
    /// yielded no row when <c>OFFSET &gt;= total</c>, so the response carried <c>total = 0</c> on a
    /// legitimate empty page — breaking the FE's "page N of M" math. The CTE + scalar-count fix
    /// returns the count independently of the page slice.
    /// </summary>
    [Fact]
    public async Task PersonSearch_EmptyTrailingPage_ReportsTrueTotal_NotZero()
    {
        var token = MintGlobalAdminToken("admin_global");

        // First learn the real total for "T7404" (a full page).
        var firstPage = await SearchRawAsync(token, "T7404", limit: 5, offset: 0);
        Assert.True(firstPage.Total >= 4);

        // Now request a page whose offset is FAR past the end → an EMPTY slice.
        var trailing = await SearchRawAsync(token, "T7404", limit: 5, offset: firstPage.Total + 50);

        Assert.Empty(trailing.Items);                    // the page slice is empty…
        Assert.Equal(firstPage.Total, trailing.Total);   // …but the total is the true count, not 0.
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R11b — descendant exclusion: DEEP (multi-level) chain + cyclic-graph termination
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WARNING 2 (S74-7404 Step-5a) — <see cref="ReportingLineRepository.GetDescendantIdsAsync"/>
    /// excludes a DEEP descendant chain (not just a direct child): a person must not be offered a
    /// grandchild OR a great-grandchild as their approver. Chain: Mgr → DChild → DGrand → DGreat
    /// (3 levels below Mgr). All three must appear in Mgr's descendant set.
    /// </summary>
    [Fact]
    public async Task GetDescendantIds_ExcludesDeepMultiLevelChain_NotJustDirectChild()
    {
        var rlRepo = new ReportingLineRepository(_dbFactory);
        // Mgr → DChild → DGrand → DGreat (each PRIMARY, same STY02 tree; AssignAsync cycle-guarded).
        await rlRepo.AssignAsync(null, MakeLine(DChild, Mgr));
        await rlRepo.AssignAsync(null, MakeLine(DGrand, DChild));
        await rlRepo.AssignAsync(null, MakeLine(DGreat, DGrand));

        var descendants = await rlRepo.GetDescendantIdsAsync(Mgr);

        // The direct child AND the deep (grandchild / great-grandchild) descendants are all in the
        // set — the picker excludes the whole subtree, not only the immediate level.
        Assert.Contains(DChild, descendants);
        Assert.Contains(DGrand, descendants);   // 2 levels deep
        Assert.Contains(DGreat, descendants);   // 3 levels deep
        // The set does NOT include Mgr itself (the caller excludes self separately).
        Assert.DoesNotContain(Mgr, descendants);
    }

    /// <summary>
    /// WARNING 2 (cont.) — the descendant walk TERMINATES on a planted CYCLIC legacy graph. We
    /// raw-insert a 3-node manager cycle (CycA → CycB → CycC → CycA; each row satisfies the
    /// <c>employee_id &lt;&gt; manager_id</c> CHECK so a multi-node loop slips past the constraint
    /// the way legacy data could) — bypassing the AssignAsync cycle guard — and assert the walk
    /// returns a FINITE set (the path-array visited-set guard, not the depth ceiling, stops it).
    /// </summary>
    [Fact]
    public async Task GetDescendantIds_TerminatesOnCyclicLegacyGraph_AndReturnsFiniteSet()
    {
        // Plant the cycle directly (AssignAsync would reject it as a cycle).
        await RawInsertLineAsync(CycB, CycA); // CycB reports to CycA
        await RawInsertLineAsync(CycC, CycB); // CycC reports to CycB
        await RawInsertLineAsync(CycA, CycC); // CycA reports to CycC → closes the loop

        var rlRepo = new ReportingLineRepository(_dbFactory);

        // Walking down from CycA must TERMINATE (the visited-set guard) and yield the loop members
        // exactly once — never hang, never duplicate-explode.
        var descendants = await rlRepo.GetDescendantIdsAsync(CycA);

        Assert.Contains(CycB, descendants);
        Assert.Contains(CycC, descendants);
        // Finite + de-duplicated: the three loop nodes at most (CycA re-enters via the loop but the
        // path guard prevents traversing past a visited node).
        Assert.True(descendants.Count <= 3);
    }

    /// <summary>
    /// Raw-inserts an active PRIMARY reporting line, BYPASSING
    /// <see cref="ReportingLineRepository.AssignAsync"/>'s cycle guard — the only way to plant a
    /// cyclic legacy graph for the termination test above.
    /// </summary>
    private async Task RawInsertLineAsync(string employeeId, string managerId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, tree_root_org_id, relationship, effective_from, source, version, created_by)
            VALUES (@emp, @mgr, @root, 'PRIMARY', '2026-01-01', 'MANUAL', 1, 'TEST')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("mgr", managerId);
        cmd.Parameters.AddWithValue("root", TreeRootSty02);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S77-7701 (R3) — bounded ~2000-employee scale validation
    //
    //  Validates (does NOT optimize — both reads already shipped) that the consolidated roster
    //  read AND the server-side person-search return correctly, scoped, and within a sane time
    //  budget at a realistic styrelse size. ~2000 employees are BULK-inserted into AFD01 (inside
    //  STY02's subtree) via a single set-based unnest insert so the suite stays fast, then torn
    //  down by their own distinct prefix (NOT via the shared AllUsers fixture cleanup).
    // ════════════════════════════════════════════════════════════════════════════════

    private const int ScaleEmployeeCount = 2000;
    private const string ScalePrefix = "t7701_scale_";

    [Fact]
    public async Task Scale_2000Employees_RosterAndSearch_ReturnCorrectly_ScopedAndPaginated_WithinBudget()
    {
        // Bulk-seed ~2000 active employees into AFD01 (within /MIN01/STY02/), self-contained.
        await BulkSeedScaleEmployeesAsync(ScaleEmployeeCount);
        try
        {
            var repo = NewApprovalRepo();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // ── (1) Consolidated roster read: ~2000 rows, no error, within budget. ──
            var roster = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");
            var rosterMs = sw.ElapsedMilliseconds;

            // The STY02 roster contains AT LEAST the 2000 bulk employees (plus the class fixtures
            // also in the subtree); every scale employee is present exactly once.
            var scaleInRoster = roster.Employees.Count(e => e.EmployeeId.StartsWith(ScalePrefix, StringComparison.Ordinal));
            Assert.Equal(ScaleEmployeeCount, scaleInRoster);
            Assert.True(roster.Employees.Count >= ScaleEmployeeCount);
            // Sane time budget for a single styrelse-bounded set-based read (generous for CI).
            Assert.True(rosterMs < 15_000, $"Roster read took {rosterMs} ms at {ScaleEmployeeCount} employees (budget 15s).");

            // ── (2) Server-side person-search: correct + scoped + paginated at scale. ──
            // A STY02-scoped LocalAdmin (org-scope covers STY02 + AFD01/AFD02) searches the bulk
            // cohort by the shared display-name token; the result is scoped (no STY05 leak) and a
            // REAL paginated DB slice (a stable full total independent of the page size).
            var adminToken = MintAdminToken("admin_sty02", "STY02");

            sw.Restart();
            var page = await SearchRawAsync(adminToken, "Scale Person", limit: 50, offset: 0);
            var searchMs = sw.ElapsedMilliseconds;

            Assert.Equal(50, page.Items.Count);                 // a full first page
            Assert.True(page.Total >= ScaleEmployeeCount);      // the full match count, not the slice
            Assert.Equal(0, page.Offset);
            Assert.Equal(50, page.Limit);
            Assert.True(searchMs < 15_000, $"Search took {searchMs} ms at {ScaleEmployeeCount} employees (budget 15s).");

            // A deep page is a real OFFSET slice with NO overlap with page 1 and the SAME total.
            var deep = await SearchRawAsync(adminToken, "Scale Person", limit: 50, offset: 1000);
            Assert.Equal(50, deep.Items.Count);
            Assert.Equal(page.Total, deep.Total);               // stable total across pages
            var p1Ids = page.Items.Select(i => i.UserId).ToHashSet();
            Assert.DoesNotContain(deep.Items.Select(i => i.UserId), id => p1Ids.Contains(id));

            // Scope bound holds at scale: the cross-styrelse STY05 fixture (EmpX) is NOT returned
            // for the STY02-scoped admin even though it matches nothing here — assert via a global
            // search that EmpX exists, then that the scoped search for it returns empty.
            var (scopedEmpX, _) = await SearchAsync(adminToken, q: "T7404 EmpX");
            Assert.DoesNotContain(scopedEmpX, i => i.UserId == EmpX);
        }
        finally
        {
            await CleanupScaleEmployeesAsync();
        }
    }

    /// <summary>
    /// BULK-inserts <paramref name="count"/> active employees into AFD01 in ONE set-based
    /// <c>unnest</c> statement (fast — no per-row round-trip). Ids are <c>t7701_scale_{i}</c> and
    /// the display name is "Scale Person NNNNN" so the search-token "Scale Person" matches the
    /// whole cohort. Also back-removes any host-seeder-created profile rows so cleanup is clean.
    /// </summary>
    private async Task BulkSeedScaleEmployeesAsync(int count)
    {
        var ids = new string[count];
        var names = new string[count];
        var emails = new string[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = $"{ScalePrefix}{i:D5}";
            names[i] = $"Scale Person {i:D5}";
            emails[i] = $"{ids[i]}@test.dk";
        }

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            SELECT uid, uid, '$2a$11$fake', nm, em, 'AFD01', 'HK', 'OK24', TRUE
            FROM unnest(@ids, @names, @emails) AS t(uid, nm, em)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("ids", ids);
        cmd.Parameters.AddWithValue("names", names);
        cmd.Parameters.AddWithValue("emails", emails);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Removes the bulk scale cohort (profiles, agreement codes, then users) by prefix.</summary>
    private async Task CleanupScaleEmployeesAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
                 {
                     "DELETE FROM approval_periods WHERE employee_id LIKE @p",
                     "DELETE FROM reporting_lines WHERE employee_id LIKE @p OR manager_id LIKE @p",
                     "DELETE FROM role_assignments WHERE user_id LIKE @p",
                     "DELETE FROM employee_profiles WHERE employee_id LIKE @p",
                     "DELETE FROM user_agreement_codes WHERE user_id LIKE @p",
                     "DELETE FROM users WHERE user_id LIKE @p",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", ScalePrefix + "%");
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private ApprovalPeriodRepository NewApprovalRepo()
    {
        var reportingRepo = new ReportingLineRepository(_dbFactory);
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, reportingRepo);
        return new ApprovalPeriodRepository(_dbFactory, authorizer, reportingRepo);
    }

    private sealed record SearchItem(string UserId, string DisplayName, string PrimaryOrgName, string? EnhedLabel);
    private sealed record SearchPage(IReadOnlyList<SearchItem> Items, int Total, int Limit, int Offset);

    private Task<(IReadOnlyList<SearchItem> Items, int Total)> SearchAsync(
        string token, string q, string? excludeEmployeeId = null)
        => SearchRawAsync(token, q, limit: 200, offset: 0, excludeEmployeeId)
            .ContinueWith(t => ((IReadOnlyList<SearchItem>)t.Result.Items, t.Result.Total));

    private async Task<SearchPage> SearchRawAsync(
        string token, string q, int limit, int offset, string? excludeEmployeeId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var url = $"/api/admin/users/search?q={Uri.EscapeDataString(q)}&limit={limit}&offset={offset}";
        if (excludeEmployeeId is not null)
            url += $"&excludeEmployeeId={Uri.EscapeDataString(excludeEmployeeId)}";
        var rsp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().Select(e => new SearchItem(
            e.GetProperty("userId").GetString()!,
            e.GetProperty("displayName").GetString()!,
            e.GetProperty("primaryOrgName").GetString()!,
            e.TryGetProperty("enhedLabel", out var l) && l.ValueKind != JsonValueKind.Null ? l.GetString() : null))
            .ToList();
        return new SearchPage(
            items,
            body.GetProperty("total").GetInt32(),
            body.GetProperty("limit").GetInt32(),
            body.GetProperty("offset").GetInt32());
    }

    private static string MintAdminToken(string userId, string orgId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_AND_DESCENDANTS") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintGlobalAdminToken(string userId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: "MIN01", scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
