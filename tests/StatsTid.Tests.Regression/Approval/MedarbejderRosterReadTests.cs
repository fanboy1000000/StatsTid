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
/// S75-7500 (R1-R3) — the consolidated medarbejder-roster read the redesigned Medarbejder-
/// administration STRUCTURAL tree (FE Phase 2) consumes:
/// <c>GET /api/admin/reporting-lines/tree/{treeRootOrgId}/medarbejdere</c>
/// (<see cref="ApprovalPeriodRepository.GetMedarbejderRosterForTreeAsync"/>).
///
/// <para>
/// The tree is STRUCTURAL: each row's <c>structuralApproverId</c> is the person's RAW active
/// PRIMARY <c>reporting_lines.manager_id</c> (a left-join edge, NOT a resolver result); the vikar
/// is a per-away-manager annotation (the person's OWN active <c>manager_vikar</c> row). The read
/// enriches the styrelse roster with <c>enhedLabel ?? primaryOrgName</c> + <c>position</c> +
/// last-closed-month <c>periodStatus</c> + <c>outgoingVikar</c> + the deterministic
/// <c>isRoot</c>/<c>isOrphan</c> flags, and reuses the existing S74 <c>pendingCountByManager</c>
/// tally unchanged. Reads only — no events, no writes.
/// </para>
///
/// <para>
/// Topology reuses the seed STY02 tree (<c>/MIN01/STY02/</c> root, AFD01/AFD02 afdelinger) with
/// isolated <c>t7500_*</c> users; a cross-styrelse STY05 user proves the scope bound and a
/// cross-org fixture proves the roster scoping excludes out-of-tree people.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class MedarbejderRosterReadTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // ── STY02 tree (/MIN01/STY02/) ─────────────────────────────────────────────────────
    // The people hierarchy:
    //   RootMgr (AFD02) — NO active PRIMARY approver, approves >=1 → isRoot
    //   ├─ EmpA / EmpB (AFD01)   — report PRIMARY to RootMgr → have an approver (neither root nor orphan)
    //   AwayMgr (AFD02)          — also a structural parent; has an active OUTGOING vikar → outgoingVikar set
    //   └─ EmpAway (AFD01)       — reports PRIMARY to AwayMgr (stays under the away manager)
    //   Orphan (AFD01)           — NO approver, approves NO one → isOrphan
    private const string RootMgr = "t7500_root_mgr";   // AFD02 — root (no approver, approves EmpA/EmpB)
    private const string EmpA = "t7500_emp_a";         // AFD01 — reports to RootMgr; last-closed APPROVED
    private const string EmpB = "t7500_emp_b";         // AFD01 — reports to RootMgr; last-closed SUBMITTED (pending)
    private const string AwayMgr = "t7500_away_mgr";   // AFD02 — has an active outgoing vikar; approves EmpAway
    private const string EmpAway = "t7500_emp_away";   // AFD01 — reports to AwayMgr; last-closed DRAFT → OPEN
    private const string Vikar = "t7500_vikar";        // AFD02 — the stand-in named on AwayMgr's vikar row
    private const string Orphan = "t7500_orphan";      // AFD01 — no approver, approves no one → isOrphan

    // Cross-styrelse (STY05, /MIN02/STY05/) — out-of-scope for a STY02-scoped admin AND out-of-roster.
    private const string EmpX = "t7500_x";             // STY05

    private const string TreeRootSty02 = "STY02";

    private static readonly string[] AllUsers =
    {
        RootMgr, EmpA, EmpB, AwayMgr, EmpAway, Vikar, Orphan, EmpX,
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
                (@rootmgr, @rootmgr, '$2a$11$fake', 'T7500 RootMgr',  't7500_root_mgr@test.dk', 'AFD02', 'HK', 'OK24', TRUE),
                (@empa,    @empa,    '$2a$11$fake', 'T7500 EmpA',     't7500_emp_a@test.dk',    'AFD01', 'HK', 'OK24', TRUE),
                (@empb,    @empb,    '$2a$11$fake', 'T7500 EmpB',     't7500_emp_b@test.dk',    'AFD01', 'HK', 'OK24', TRUE),
                (@awaymgr, @awaymgr, '$2a$11$fake', 'T7500 AwayMgr',  't7500_away_mgr@test.dk', 'AFD02', 'HK', 'OK24', TRUE),
                (@empaway, @empaway, '$2a$11$fake', 'T7500 EmpAway',  't7500_emp_away@test.dk', 'AFD01', 'HK', 'OK24', TRUE),
                (@vikar,   @vikar,   '$2a$11$fake', 'T7500 Vikar',    't7500_vikar@test.dk',    'AFD02', 'HK', 'OK24', TRUE),
                (@orphan,  @orphan,  '$2a$11$fake', 'T7500 Orphan',   't7500_orphan@test.dk',   'AFD01', 'HK', 'OK24', TRUE),
                (@empx,    @empx,    '$2a$11$fake', 'T7500 EmpX',     't7500_x@test.dk',        'STY05', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // RootMgr is a LocalLeader covering AFD02 (so the resolver returns it for the pending tally).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@rootmgr, 'LOCAL_LEADER', 'AFD02', 'ORG_AND_DESCENDANTS', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        var rlRepo = new ReportingLineRepository(_dbFactory);
        // Structural edges: EmpA/EmpB → RootMgr; EmpAway → AwayMgr. RootMgr/AwayMgr/Orphan/Vikar
        // have NO active PRIMARY approver. RootMgr + AwayMgr approve >=1 (roots); Orphan approves
        // no one (orphan).
        await rlRepo.AssignAsync(null, MakeLine(EmpA, RootMgr));
        await rlRepo.AssignAsync(null, MakeLine(EmpB, RootMgr));
        await rlRepo.AssignAsync(null, MakeLine(EmpAway, AwayMgr));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("rootmgr", RootMgr);
        cmd.Parameters.AddWithValue("empa", EmpA);
        cmd.Parameters.AddWithValue("empb", EmpB);
        cmd.Parameters.AddWithValue("awaymgr", AwayMgr);
        cmd.Parameters.AddWithValue("empaway", EmpAway);
        cmd.Parameters.AddWithValue("vikar", Vikar);
        cmd.Parameters.AddWithValue("orphan", Orphan);
        cmd.Parameters.AddWithValue("empx", EmpX);
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

    /// <summary>Inserts a CLOSED-month period (period_end strictly before today) so it is the
    /// employee's "last closed month".</summary>
    private async Task InsertClosedPeriodAsync(string employeeId, string orgId, string status)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodEnd = today.AddDays(-1);
        var periodStart = periodEnd.AddDays(-30);
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
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SetProfileAsync(string employeeId, string? enhedLabel, string? position)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var upd = new NpgsqlCommand(
            "UPDATE employee_profiles SET enhed_label = @label, position = @pos WHERE employee_id = @emp AND effective_to IS NULL", conn);
        upd.Parameters.AddWithValue("label", (object?)enhedLabel ?? DBNull.Value);
        upd.Parameters.AddWithValue("pos", (object?)position ?? DBNull.Value);
        upd.Parameters.AddWithValue("emp", employeeId);
        var rows = await upd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            await using var ins = new NpgsqlCommand(
                """
                INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from, effective_to, enhed_label)
                VALUES (@emp, 1.000, @pos, '0001-01-01', NULL, @label)
                """, conn);
            ins.Parameters.AddWithValue("emp", employeeId);
            ins.Parameters.AddWithValue("pos", (object?)position ?? DBNull.Value);
            ins.Parameters.AddWithValue("label", (object?)enhedLabel ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertActiveVikarAsync(string absentApproverId, string vikarUserId, DateOnly until, string reason)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar
                (absent_approver_id, vikar_user_id, until_date, reason, tree_root_org_id, version, created_by, effective_to)
            VALUES (@absent, @vikar, @until, @reason, @root, 1, 'TEST', NULL)
            """, conn);
        cmd.Parameters.AddWithValue("absent", absentApproverId);
        cmd.Parameters.AddWithValue("vikar", vikarUserId);
        cmd.Parameters.AddWithValue("until", until);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("root", TreeRootSty02);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Repository-direct: composition (structuralApproverId, status, isRoot/isOrphan,
    //  enhedLabel fallback, position, outgoingVikar, pendingCountByManager passthrough)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Roster_StructuralApproverId_IsRawPrimaryEdge_AndStatusMapsPerState()
    {
        await InsertClosedPeriodAsync(EmpA, "AFD01", "APPROVED");     // → APPROVED
        await InsertClosedPeriodAsync(EmpB, "AFD01", "SUBMITTED");    // → SUBMITTED
        await InsertClosedPeriodAsync(EmpAway, "AFD01", "DRAFT");     // → OPEN (DRAFT)
        // Orphan/RootMgr: no closed period → OPEN.

        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");

        MedarbejderRosterRow Row(string id) => roster.Employees.Single(e => e.EmployeeId == id);

        // structuralApproverId = the RAW active PRIMARY manager_id (not a resolver result).
        Assert.Equal(RootMgr, Row(EmpA).StructuralApproverId);
        Assert.Equal(RootMgr, Row(EmpB).StructuralApproverId);
        Assert.Equal(AwayMgr, Row(EmpAway).StructuralApproverId);
        Assert.Null(Row(RootMgr).StructuralApproverId);   // no active PRIMARY approver
        Assert.Null(Row(Orphan).StructuralApproverId);

        // periodStatus mapping per state.
        Assert.Equal("APPROVED", Row(EmpA).PeriodStatus);
        Assert.Equal("SUBMITTED", Row(EmpB).PeriodStatus);
        Assert.Equal("OPEN", Row(EmpAway).PeriodStatus);      // DRAFT → OPEN
        Assert.Equal("OPEN", Row(RootMgr).PeriodStatus);      // no closed period → OPEN

        // Cross-styrelse EmpX is NOT in the STY02 roster (path-prefix scoping).
        Assert.DoesNotContain(roster.Employees, e => e.EmployeeId == EmpX);
    }

    [Fact]
    public async Task Roster_PathPrefix_EscapesLikeMetacharacters_NoWildcardOverMatch()
    {
        // B2 (Step-5a): the path-prefix scope must treat a literal '_' / '%' as a literal, not as
        // a LIKE wildcard. A prefix "/MIN01/STY0_/" — where an UNescaped '_' would match the '2' in
        // "/MIN01/STY02/" — MUST resolve to NO org (no path carries a literal underscore there) and
        // therefore return an EMPTY roster. Without the ESCAPE '\' fix this prefix leaks the whole
        // STY02 roster, so this test fails on the pre-fix code.
        var repo = NewApprovalRepo();
        var leaked = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY0_/");
        Assert.Empty(leaked.Employees);

        // The SIBLING tree-scoped read — the period-status projection (the other site that carries
        // the same idiom + the same ESCAPE fix, and which feeds the S75 tiles) — must ALSO escape
        // the '_': it returns NO employees for the metacharacter prefix. Without the fix this read
        // would leak the STY02 roster's status rows too (Step-7a WARNING — assert the second site).
        var leakedStatus = await repo.GetPeriodStatusProjectionForTreeAsync("/MIN01/STY0_/");
        Assert.Empty(leakedStatus.Employees);

        // Sanity: the correctly-spelled literal prefix still returns the STY02 roster — the escape
        // does not break the normal (metacharacter-free) path match.
        var ok = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");
        Assert.Contains(ok.Employees, e => e.EmployeeId == EmpA);
    }

    [Fact]
    public async Task Roster_IsRootVsIsOrphan_ClassifiedDeterministically()
    {
        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");

        MedarbejderRosterRow Row(string id) => roster.Employees.Single(e => e.EmployeeId == id);

        // RootMgr: no approver, approves EmpA/EmpB → isRoot (NOT orphan).
        Assert.True(Row(RootMgr).IsRoot);
        Assert.False(Row(RootMgr).IsOrphan);

        // AwayMgr: no approver, approves EmpAway → also a root.
        Assert.True(Row(AwayMgr).IsRoot);
        Assert.False(Row(AwayMgr).IsOrphan);

        // Orphan: no approver, approves no one → isOrphan (NOT root).
        Assert.False(Row(Orphan).IsRoot);
        Assert.True(Row(Orphan).IsOrphan);

        // Vikar: no approver, approves no one (the vikar row makes them a stand-in, NOT a
        // structural parent) → isOrphan.
        Assert.False(Row(Vikar).IsRoot);
        Assert.True(Row(Vikar).IsOrphan);

        // EmpA: HAS an approver → neither root nor orphan.
        Assert.False(Row(EmpA).IsRoot);
        Assert.False(Row(EmpA).IsOrphan);
    }

    [Fact]
    public async Task Roster_OutgoingVikar_SetOnlyForAwayManagerWithActiveVikarRow()
    {
        var until = new DateOnly(2099, 12, 31);
        await InsertActiveVikarAsync(AwayMgr, Vikar, until, "FERIE");

        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");

        MedarbejderRosterRow Row(string id) => roster.Employees.Single(e => e.EmployeeId == id);

        // The away-manager carries the outgoing-vikar marker (vikar id + resolved display name +
        // inclusive until + reason).
        var v = Row(AwayMgr).OutgoingVikar;
        Assert.NotNull(v);
        Assert.Equal(Vikar, v!.VikarUserId);
        Assert.Equal("T7500 Vikar", v.VikarDisplayName);
        Assert.Equal(until, v.UntilDate);
        Assert.Equal("FERIE", v.Reason);

        // Everyone else has a null outgoingVikar (incl. the report under the away manager and the
        // stand-in himself).
        Assert.Null(Row(RootMgr).OutgoingVikar);
        Assert.Null(Row(EmpAway).OutgoingVikar);
        Assert.Null(Row(Vikar).OutgoingVikar);
    }

    [Fact]
    public async Task Roster_EnhedLabel_FallsBackToOrgName_WhenNull_AndPositionServed()
    {
        // EmpA: explicit enhed_label + position. EmpB: position but NO label → enhedLabel falls
        // back to the primary-org name (AFD01).
        await SetProfileAsync(EmpA, enhedLabel: "Team Alpha", position: "Sagsbehandler");
        await SetProfileAsync(EmpB, enhedLabel: null, position: "Fuldmægtig");

        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");

        MedarbejderRosterRow Row(string id) => roster.Employees.Single(e => e.EmployeeId == id);

        // Explicit label wins; position served.
        Assert.Equal("Team Alpha", Row(EmpA).EnhedLabel);
        Assert.Equal("Sagsbehandler", Row(EmpA).Position);

        // Null label → org-NAME fallback (AFD01's org_name = "IT-Drift", the human-readable name,
        // NOT the org id); position still served.
        Assert.Equal("IT-Drift", Row(EmpB).EnhedLabel);
        Assert.Equal("Fuldmægtig", Row(EmpB).Position);

        // No profile at all (Orphan) → org-name fallback + null position.
        Assert.Equal("IT-Drift", Row(Orphan).EnhedLabel);
        Assert.Null(Row(Orphan).Position);
    }

    [Fact]
    public async Task Roster_PendingCountByManager_PassesThroughTheExistingTally()
    {
        // EmpB holds a pending (SUBMITTED) period and resolves to RootMgr (an active LocalLeader)
        // → RootMgr tallies 1. EmpA's APPROVED period is not pending.
        await InsertClosedPeriodAsync(EmpA, "AFD01", "APPROVED");
        await InsertClosedPeriodAsync(EmpB, "AFD01", "SUBMITTED");

        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync("/MIN01/STY02/");

        // The roster tally MATCHES the existing period-status projection's tally exactly.
        var statusProjection = await repo.GetPeriodStatusProjectionForTreeAsync("/MIN01/STY02/");
        Assert.Equal(statusProjection.PendingCountByManager, roster.PendingCountByManager);

        Assert.True(roster.PendingCountByManager.TryGetValue(RootMgr, out var n));
        Assert.Equal(1, n);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP endpoint: scope gate + the exact served shape
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RosterEndpoint_ScopedAdmin_GetsRoster_AndCrossStyrelseDenied()
    {
        await InsertClosedPeriodAsync(EmpA, "AFD01", "APPROVED");
        await SetProfileAsync(EmpA, enhedLabel: "Team Alpha", position: "Sagsbehandler");
        await InsertActiveVikarAsync(AwayMgr, Vikar, new DateOnly(2099, 12, 31), "SYGDOM");

        // STY02-scoped LocalAdmin → 200 with the full served shape.
        var sty02Client = _factory.CreateClient();
        sty02Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken("admin_sty02", "STY02"));
        var rsp = await sty02Client.GetAsync("/api/admin/reporting-lines/tree/STY02/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var employees = body.GetProperty("employees");

        // EmpA: structuralApproverId + status + enhedLabel + position served.
        var empA = employees.EnumerateArray().First(e => e.GetProperty("employeeId").GetString() == EmpA);
        Assert.Equal(RootMgr, empA.GetProperty("structuralApproverId").GetString());
        Assert.Equal("APPROVED", empA.GetProperty("periodStatus").GetString());
        Assert.Equal("Team Alpha", empA.GetProperty("enhedLabel").GetString());
        Assert.Equal("Sagsbehandler", empA.GetProperty("position").GetString());
        Assert.Equal(JsonValueKind.Null, empA.GetProperty("outgoingVikar").ValueKind);
        Assert.False(empA.GetProperty("isRoot").GetBoolean());
        Assert.False(empA.GetProperty("isOrphan").GetBoolean());

        // RootMgr: isRoot true, no approver, null vikar.
        var rootMgr = employees.EnumerateArray().First(e => e.GetProperty("employeeId").GetString() == RootMgr);
        Assert.Equal(JsonValueKind.Null, rootMgr.GetProperty("structuralApproverId").ValueKind);
        Assert.True(rootMgr.GetProperty("isRoot").GetBoolean());

        // AwayMgr: outgoingVikar object served with the nested shape.
        var awayMgr = employees.EnumerateArray().First(e => e.GetProperty("employeeId").GetString() == AwayMgr);
        var vikar = awayMgr.GetProperty("outgoingVikar");
        Assert.Equal(JsonValueKind.Object, vikar.ValueKind);
        Assert.Equal(Vikar, vikar.GetProperty("vikarUserId").GetString());
        Assert.Equal("T7500 Vikar", vikar.GetProperty("vikarDisplayName").GetString());
        Assert.Equal("SYGDOM", vikar.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrEmpty(vikar.GetProperty("untilDate").GetString()));

        // Orphan: isOrphan true.
        var orphan = employees.EnumerateArray().First(e => e.GetProperty("employeeId").GetString() == Orphan);
        Assert.True(orphan.GetProperty("isOrphan").GetBoolean());

        // No-leak: the cross-styrelse EmpX (STY05) MUST be absent from the STY02 roster the
        // endpoint serves — the path-prefix scope bound holds through the HTTP surface, not just
        // the repository-direct call (Codex Step-5a no-leak WARNING).
        Assert.DoesNotContain(
            employees.EnumerateArray(),
            e => e.GetProperty("employeeId").GetString() == EmpX);

        // pendingCountByManager present (object).
        Assert.Equal(JsonValueKind.Object, body.GetProperty("pendingCountByManager").ValueKind);

        // A STY05-scoped LocalAdmin cannot read the STY02 tree → 403.
        var sty05Client = _factory.CreateClient();
        sty05Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken("admin_sty05", "STY05"));
        var denied = await sty05Client.GetAsync("/api/admin/reporting-lines/tree/STY02/medarbejdere");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
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

    private static string MintAdminToken(string userId, string orgId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_AND_DESCENDANTS") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalAdmin,
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
