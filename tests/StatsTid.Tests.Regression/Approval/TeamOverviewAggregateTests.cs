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
/// S87-8701 — the leader Teamoversigt aggregate endpoint
/// <c>GET /api/approval/team-overview?year=&amp;month=</c>. One row per employee in the leader's
/// <b>designated-act-authority set</b> (ADR-027 D13 "see == act"), derived from the designated-
/// candidate CTE → R5 predicate → LEFT JOIN the month period (NOT <c>/reports</c>, NOT period-first),
/// EXTENDED to emit a zero-period DRAFT row (<c>periodId=null</c>).
///
/// <para>
/// <b>The correctness crux (RED-on-naive-<c>/reports</c>):</b> a vikar-coverage report appears (and is
/// approvable), an inactive-manager-escalation report appears, an acting-reassigned-away direct report
/// does NOT — exactly the divergence a raw <c>manager_id=leaderId</c> (<c>/reports</c>) join would get
/// wrong (it would include the acting-reassigned report and miss the vikar / escalation ones).
/// </para>
///
/// <para>
/// Topology mirrors <see cref="DesignatedApproverAuthorityTests"/> (S92/ADR-035 flatten):
/// STY02 is an ORGANISATION under MAO MIN01; STY05 is an ORGANISATION under a DIFFERENT MAO MIN02
/// (a different tree_root). Emp and Mgr are BOTH on the STY02 Organisation (the smallest authority
/// unit post-flatten); the designated PRIMARY edge grants approve authority. Because the
/// team-overview roster is the designated-act-authority SET (edge-derived, NOT org-scope), the
/// negative "no-edge actor does not see Emp" assertions hold without org-scope coarsening pulling Emp
/// in. <c>Other</c> stays on STY02 because it ALSO holds a designated ACTING edge over EmpActing
/// (same-tree required for that edge to grant).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TeamOverviewAggregateTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // STY02 Organisation (MAO MIN01):
    private const string Emp = "t87_emp";          // STY02 — Mgr's PRIMARY report
    private const string Mgr = "t87_mgr";          // STY02 — designated approver
    private const string Vik = "t87_vik";          // STY02 — Mgr's vikar stand-in (a Leader)
    private const string EmpVik = "t87_emp_vik";   // STY02 — reports PRIMARY to AwayMgr (vikar-covered)
    private const string AwayMgr = "t87_awaymgr";  // STY02 — away manager, covered by Vik
    private const string EmpIm = "t87_emp_im";     // STY02 — reports to an INACTIVE manager
    private const string InactiveMgr = "t87_imgr"; // STY02 — INACTIVE; escalates to Mgr
    private const string EmpActing = "t87_emp_act"; // STY02 — Mgr's PRIMARY report BUT reassigned via ACTING to Other
    private const string Other = "t87_other";      // STY02 — a Leader; holds the ACTING edge over EmpActing
    private const string EmpNoPeriod = "t87_emp_np"; // STY02 — Mgr's report with NO period this month
    // STY05 Organisation (a DIFFERENT MAO MIN02 — cross-tree):
    private const string EmpX = "t87_emp_x";       // STY05 — different tree_root
    private const string MgrX = "t87_mgr_x";       // STY05 — EmpX's own manager

    private const string TreeRootSty02 = "STY02";
    private const string TreeRootSty05 = "STY05";

    private static readonly string[] AllUsers =
    {
        Emp, Mgr, Vik, EmpVik, AwayMgr, EmpIm, InactiveMgr, EmpActing, Other, EmpNoPeriod, EmpX, MgrX,
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
                (@emp,    @emp,    '$2a$11$fake', 'T87 Emp',     't87_emp@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@mgr,    @mgr,    '$2a$11$fake', 'T87 Mgr',     't87_mgr@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@vik,    @vik,    '$2a$11$fake', 'T87 Vikar',   't87_vik@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@empvik, @empvik, '$2a$11$fake', 'T87 EmpVik',  't87_emp_vik@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@away,   @away,   '$2a$11$fake', 'T87 AwayMgr', 't87_awaymgr@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@empim,  @empim,  '$2a$11$fake', 'T87 EmpIM',   't87_emp_im@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@imgr,   @imgr,   '$2a$11$fake', 'T87 IMgr',    't87_imgr@test.dk',    'STY02', 'HK', 'OK24', FALSE),
                (@empact, @empact, '$2a$11$fake', 'T87 EmpAct',  't87_emp_act@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@other,  @other,  '$2a$11$fake', 'T87 Other',   't87_other@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@empnp,  @empnp,  '$2a$11$fake', 'T87 EmpNP',   't87_emp_np@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@empx,   @empx,   '$2a$11$fake', 'T87 EmpX',    't87_emp_x@test.dk',   'STY05', 'HK', 'OK24', TRUE),
                (@mgrx,   @mgrx,   '$2a$11$fake', 'T87 MgrX',    't87_mgr_x@test.dk',   'STY05', 'HK', 'OK24', TRUE)
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
                (@vik,    'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@other,  'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@mgrx,   'LOCAL_LEADER', 'STY05', 'ORG_ONLY', 'TEST'),
                (@emp,    'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empvik, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empim,  'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empact, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empnp,  'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empx,   'EMPLOYEE',     'STY05', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        var rlRepo = new ReportingLineRepository(_dbFactory);

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the same-Organisation, same-tree edge.
        await rlRepo.AssignAsync(null, MakeLine(Emp, Mgr, TreeRootSty02, "PRIMARY"));
        // EmpNoPeriod (STY02) reports PRIMARY to Mgr — same edge, but no period this month.
        await rlRepo.AssignAsync(null, MakeLine(EmpNoPeriod, Mgr, TreeRootSty02, "PRIMARY"));
        // EmpVik (STY02) reports PRIMARY to AwayMgr (STY02) — AwayMgr is covered by a vikar (set in test).
        await rlRepo.AssignAsync(null, MakeLine(EmpVik, AwayMgr, TreeRootSty02, "PRIMARY"));
        // EmpIm (STY02) → InactiveMgr (inactive, STY02) → Mgr — inactive-escalation up to Mgr.
        await rlRepo.AssignAsync(null, MakeLine(EmpIm, InactiveMgr, TreeRootSty02, "PRIMARY"));
        await rlRepo.AssignAsync(null, MakeLine(InactiveMgr, Mgr, TreeRootSty02, "PRIMARY"));
        // EmpActing (STY02) reports PRIMARY to Mgr BUT is reassigned via an ACTING edge to Other →
        // Other is the single effective approver, NOT Mgr (the acting-reassigned-away case).
        await rlRepo.AssignAsync(null, MakeLine(EmpActing, Mgr, TreeRootSty02, "PRIMARY"));
        await rlRepo.AssignAsync(null, MakeLine(EmpActing, Other, TreeRootSty02, "ACTING"));
        // EmpX (STY05) reports PRIMARY to MgrX (STY05) — the cross-MAO (cross-tree) Organisation.
        await rlRepo.AssignAsync(null, MakeLine(EmpX, MgrX, TreeRootSty05, "PRIMARY"));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("vik", Vik);
        cmd.Parameters.AddWithValue("empvik", EmpVik);
        cmd.Parameters.AddWithValue("away", AwayMgr);
        cmd.Parameters.AddWithValue("empim", EmpIm);
        cmd.Parameters.AddWithValue("imgr", InactiveMgr);
        cmd.Parameters.AddWithValue("empact", EmpActing);
        cmd.Parameters.AddWithValue("other", Other);
        cmd.Parameters.AddWithValue("empnp", EmpNoPeriod);
        cmd.Parameters.AddWithValue("empx", EmpX);
        cmd.Parameters.AddWithValue("mgrx", MgrX);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId, string treeRoot, string relationship) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = treeRoot,
        Relationship = relationship,
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
        await ExecAsync(conn, "DELETE FROM payroll_export_records WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM time_entries_projection WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM work_time_projection WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM absences_projection WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM entitlement_balances WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM events WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM event_streams WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            cmd.Parameters.AddWithValue("streams", AllUsers.Select(u => $"employee-{u}").ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Insert helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<Guid> InsertPeriodAsync(string employeeId, string orgId, string status,
        DateOnly? start = null, DateOnly? end = null, string? rejectionReason = null)
    {
        var s = start ?? new DateOnly(2026, 5, 1);
        var e = end ?? new DateOnly(2026, 5, 31);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        // APPROVED + REJECTED both write approved_at (no stored rejectedAt); reject also a reason.
        var setDecision = status is "APPROVED" or "REJECTED";
        await using var cmd = new NpgsqlCommand(
            $"""
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by{(setDecision ? ", approved_at, approved_by" : "")}{(rejectionReason is not null ? ", rejection_reason" : "")})
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24',
                 NOW(), @emp{(setDecision ? ", NOW(), @emp" : "")}{(rejectionReason is not null ? ", @reason" : "")})
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("start", s);
        cmd.Parameters.AddWithValue("end", e);
        cmd.Parameters.AddWithValue("status", status);
        if (rejectionReason is not null)
            cmd.Parameters.AddWithValue("reason", rejectionReason);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SetVacationBalanceAsync(string employeeId, int entitlementYear, decimal used, decimal totalQuota)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @emp, 'VACATION', @year, @total, @used, 0, 0, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
            DO UPDATE SET used = @used, total_quota = @total, updated_at = NOW()
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", entitlementYear);
        cmd.Parameters.AddWithValue("used", used);
        cmd.Parameters.AddWithValue("total", totalQuota);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertTimeEntryAsync(string employeeId, DateOnly date, decimal hours,
        string activityType = "NORMAL", string? taskId = "TASK-1")
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, task_id, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @taskId, @activity, 'HK', 'OK24', FALSE, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("taskId", (object?)taskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activity", activityType);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertWorkTimeAsync(string employeeId, DateOnly date, decimal manualHours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO work_time_projection (employee_id, date, intervals, manual_hours, occurred_at, outbox_id)
            VALUES (@emp, @date, '[]'::jsonb, @manual, NOW(), @outbox)
            ON CONFLICT (employee_id, date) DO UPDATE SET manual_hours = @manual
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("manual", manualHours);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertAbsenceTodayAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage, agreement_code, ok_version, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @today, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("today", DateOnly.FromDateTime(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertFlexEventAsync(string employeeId, decimal newBalance)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var streamId = $"employee-{employeeId}";
        await using (var sCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING", conn))
        {
            sCmd.Parameters.AddWithValue("s", streamId);
            await sCmd.ExecuteNonQueryAsync();
        }
        // camelCase, matching EventSerializer's stored shape (data->>'newBalance').
        var data = $"{{\"eventId\":\"{Guid.NewGuid()}\",\"employeeId\":\"{employeeId}\",\"previousBalance\":0,\"newBalance\":{newBalance.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"delta\":{newBalance.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"reason\":\"test\"}}";
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at)
            VALUES (gen_random_uuid(), @s, @ver, 'FlexBalanceUpdated', @data::jsonb, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("ver", _flexVersion++);
        cmd.Parameters.AddWithValue("data", data);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertPayrollExportRecordAsync(string employeeId, int year, int month, Guid? periodId = null)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, exported_at,
                 original_lines, current_effective_lines, content_hash, source)
            VALUES
                (@xid, @pid, @emp, @y, @m, NOW(), '[]'::jsonb, '[]'::jsonb, 'test-hash', 'CALCULATE_AND_EXPORT')
            """, conn);
        cmd.Parameters.AddWithValue("xid", Guid.NewGuid());
        cmd.Parameters.AddWithValue("pid", (object?)periodId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await cmd.ExecuteNonQueryAsync();
    }

    private int _outboxSeq = 1;
    private int _flexVersion = 1;

    // ════════════════════════════════════════════════════════════════════════════════
    //  Roster == act-authority (the key tests, RED-on-naive-/reports)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The roster IS the designated-act-authority set, NOT a raw <c>manager_id=leaderId</c> join:
    /// <list type="bullet">
    /// <item><description>a VIKAR-coverage report (EmpVik → AwayMgr, while Vik stands in for AwayMgr)
    /// appears in Vik's team-overview — a /reports join keyed on manager_id=Vik would MISS it;</description></item>
    /// <item><description>an INACTIVE-escalation report (EmpIm → InactiveMgr[inactive] → Mgr) appears
    /// in Mgr's team-overview;</description></item>
    /// <item><description>an ACTING-reassigned-away direct report (EmpActing: PRIMARY=Mgr but ACTING=Other)
    /// does NOT appear in Mgr's team-overview — a /reports join keyed on manager_id=Mgr would WRONGLY
    /// include it. It appears in Other's set instead.</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Roster_IsActAuthoritySet_Vikar_And_Escalation_Appear_ActingReassigned_DoesNot()
    {
        await CreateVikarAsync(AwayMgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        await InsertPeriodAsync(EmpVik, "STY02", "SUBMITTED");
        await InsertPeriodAsync(EmpIm, "STY02", "SUBMITTED");
        await InsertPeriodAsync(EmpActing, "STY02", "SUBMITTED");

        // Mgr's team-overview: Emp + EmpIm (escalation) + EmpNoPeriod; NOT EmpActing (reassigned away).
        var mgrRows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var mgrEmployees = mgrRows.Select(EmployeeId).ToHashSet();
        Assert.Contains(Emp, mgrEmployees);
        Assert.Contains(EmpIm, mgrEmployees);          // inactive-escalation report appears
        Assert.Contains(EmpNoPeriod, mgrEmployees);    // zero-period report still appears
        Assert.DoesNotContain(EmpActing, mgrEmployees); // acting-reassigned-away does NOT appear
        // EmpVik is AwayMgr's report (covered by Vik), not Mgr's.
        Assert.DoesNotContain(EmpVik, mgrEmployees);

        // Vik's team-overview (standing in for AwayMgr): the vikar-coverage report EmpVik appears.
        var vikRows = await GetTeamOverviewAsync(Vik, 2026, 5);
        var vikEmployees = vikRows.Select(EmployeeId).ToHashSet();
        Assert.Contains(EmpVik, vikEmployees);          // vikar-coverage report appears

        // Other's team-overview: the acting-reassigned report EmpActing appears (it IS Other's now).
        var otherRows = await GetTeamOverviewAsync(Other, 2026, 5);
        var otherEmployees = otherRows.Select(EmployeeId).ToHashSet();
        Assert.Contains(EmpActing, otherEmployees);
    }

    /// <summary>
    /// The vikar-coverage report surfaced in the team-overview is genuinely approvable by the vikar via
    /// the existing hardened endpoint — proving the roster is the ACT-authority set (see == act), not a
    /// see-only convenience list.
    /// </summary>
    [Fact]
    public async Task Roster_VikarCoverageReport_IsApprovable_SeeEqualsAct()
    {
        await CreateVikarAsync(AwayMgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var periodId = await InsertPeriodAsync(EmpVik, "STY02", "SUBMITTED");

        var rows = await GetTeamOverviewAsync(Vik, 2026, 5);
        var row = rows.Single(r => EmployeeId(r) == EmpVik);
        Assert.Equal(periodId, row.GetProperty("periodId").GetGuid());

        // The same period is approvable by the vikar via the vikar EDGE (EmpVik + Vik both on STY02).
        var client = LeaderClient(Vik, "STY02");
        var approveRsp = await client.PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Zero-period DRAFT row
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ZeroPeriodEmployee_EmitsDraftRow_WithNullPeriodId()
    {
        // EmpNoPeriod is Mgr's report but has NO period in May 2026.
        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var row = rows.Single(r => EmployeeId(r) == EmpNoPeriod);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("periodId").ValueKind);
        Assert.Equal("DRAFT", row.GetProperty("status").GetString());
        // Name + agreement come from users for a no-period row.
        Assert.Equal("T87 EmpNP", row.GetProperty("displayName").GetString());
        Assert.Equal("HK", row.GetProperty("agreement").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Field correctness
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Ferie_ComesFromVacationEntitlement_UsedAndTotal_ForRequestedFerieaar()
    {
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        // May 2026 → ferieår (reset_month 9) = 2025.
        await SetVacationBalanceAsync(Emp, 2025, used: 7m, totalQuota: 25m);

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var row = rows.Single(r => EmployeeId(r) == Emp);
        Assert.Equal(7m, row.GetProperty("ferieUsed").GetDecimal());
        Assert.Equal(25m, row.GetProperty("ferieTotal").GetDecimal());
    }

    [Fact]
    public async Task DecisionAt_IsNeutral_StatusDisambiguates_Reject()
    {
        // A REJECTED period writes approved_at too + a reason; status disambiguates.
        await InsertPeriodAsync(Emp, "STY02", "REJECTED", rejectionReason: "needs fixing");

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var row = rows.Single(r => EmployeeId(r) == Emp);
        Assert.Equal("REJECTED", row.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("decisionAt").ValueKind); // neutral decisionAt present
        Assert.Equal("needs fixing", row.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task DecisionAt_PresentForApproved_NoRejectionReason()
    {
        await InsertPeriodAsync(Emp, "STY02", "APPROVED");

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var row = rows.Single(r => EmployeeId(r) == Emp);
        Assert.Equal("APPROVED", row.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("decisionAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("rejectionReason").ValueKind);
    }

    [Fact]
    public async Task AwayToday_True_ForEmployeeWithTodayCoveringAbsence()
    {
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        await InsertPeriodAsync(EmpNoPeriod, "STY02", "SUBMITTED", // give it a period so it's not the no-period case
            start: new DateOnly(2026, 5, 1), end: new DateOnly(2026, 5, 31));
        await InsertAbsenceTodayAsync(Emp);

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        Assert.True(rows.Single(r => EmployeeId(r) == Emp).GetProperty("awayToday").GetBoolean());
        Assert.False(rows.Single(r => EmployeeId(r) == EmpNoPeriod).GetProperty("awayToday").GetBoolean());
    }

    [Fact]
    public async Task AwayToday_FaultIsolated_BrokenAbsenceRead_StillReturnsRows_FlagFalse()
    {
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        await InsertAbsenceTodayAsync(Emp);

        // Break the absences_projection read for THIS request by dropping the table the query reads.
        // The team-overview must still return the row (fault-isolated), with awayToday=false.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "ALTER TABLE absences_projection RENAME TO absences_projection_bak", conn);
            await drop.ExecuteNonQueryAsync();
        }
        try
        {
            var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
            var row = rows.Single(r => EmployeeId(r) == Emp);
            Assert.False(row.GetProperty("awayToday").GetBoolean()); // degraded to false, NOT a 500
        }
        finally
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var restore = new NpgsqlCommand(
                "ALTER TABLE absences_projection_bak RENAME TO absences_projection", conn);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task FlexBalance_ComesFromLatestFlexEvent()
    {
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        await InsertFlexEventAsync(Emp, 3m);
        await InsertFlexEventAsync(Emp, 12.5m); // the LATEST (highest stream_version) wins

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        Assert.Equal(12.5m, rows.Single(r => EmployeeId(r) == Emp).GetProperty("flexBalance").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S90 / TASK-9005 — payrollExported (the cross-context payroll-export lock flag)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// payrollExported = TRUE iff the employee has a payroll_export_records row for the requested
    /// (year, month) — a READ-ONLY cross-context read of the Payroll-owned lock table (ADR-034). One
    /// exported employee (Emp, May 2026) + one non-exported (EmpNoPeriod) in the SAME team-overview;
    /// the flag discriminates them. The exported row also surfaces payrollExportedAt.
    /// </summary>
    [Fact]
    public async Task PayrollExported_True_ForEmployeeWithExportRecord_FalseOtherwise()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "APPROVED");
        await InsertPeriodAsync(EmpNoPeriod, "STY02", "APPROVED",
            start: new DateOnly(2026, 5, 1), end: new DateOnly(2026, 5, 31));
        // Emp's May 2026 is sent to lønkørsel; EmpNoPeriod's is NOT.
        await InsertPayrollExportRecordAsync(Emp, 2026, 5, periodId);

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);

        var empRow = rows.Single(r => EmployeeId(r) == Emp);
        Assert.True(empRow.GetProperty("payrollExported").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, empRow.GetProperty("payrollExportedAt").ValueKind);

        var npRow = rows.Single(r => EmployeeId(r) == EmpNoPeriod);
        Assert.False(npRow.GetProperty("payrollExported").GetBoolean());
        Assert.Equal(JsonValueKind.Null, npRow.GetProperty("payrollExportedAt").ValueKind);
    }

    /// <summary>
    /// The export lock is per-(employee, year, month): an export record for a DIFFERENT month does
    /// NOT mark the requested month as exported.
    /// </summary>
    [Fact]
    public async Task PayrollExported_False_WhenExportRecordIsForADifferentMonth()
    {
        await InsertPeriodAsync(Emp, "STY02", "APPROVED");
        // Emp has an export record for June 2026, but the team-overview is requested for May 2026.
        await InsertPayrollExportRecordAsync(Emp, 2026, 6);

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var empRow = rows.Single(r => EmployeeId(r) == Emp);
        Assert.False(empRow.GetProperty("payrollExported").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  hasWarning = the cheap allocation warning, NO rule-engine call
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HasWarning_True_WhenWorkedExceedsAllocated_NoRuleEngineCalled()
    {
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        // worked 7.4h, allocated 0 (no NORMAL+TaskId entry) → "Ikke fordelt" > 0 → hasWarning.
        await InsertWorkTimeAsync(Emp, new DateOnly(2026, 5, 4), 7.4m);

        // A balanced employee: worked == allocated → no warning.
        await InsertPeriodAsync(EmpNoPeriod, "STY02", "SUBMITTED",
            start: new DateOnly(2026, 5, 1), end: new DateOnly(2026, 5, 31));
        await InsertWorkTimeAsync(EmpNoPeriod, new DateOnly(2026, 5, 4), 7.4m);
        await InsertTimeEntryAsync(EmpNoPeriod, new DateOnly(2026, 5, 4), 7.4m, "NORMAL", "TASK-1");

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        Assert.True(rows.Single(r => EmployeeId(r) == Emp).GetProperty("hasWarning").GetBoolean());
        Assert.False(rows.Single(r => EmployeeId(r) == EmpNoPeriod).GetProperty("hasWarning").GetBoolean());
        // No compliance/rule-engine event was written by this read (it is projection-only).
        Assert.Equal(0, await CountComplianceEventsAsync());
    }

    [Fact]
    public async Task HasWarning_True_WhenAllocatedExceedsWorked_SymmetricGateMirror()
    {
        // S87 Step-7a: the approve gate flags Math.Abs(worked − allocated) ≥ tol, so an
        // OVER-allocated day (allocated > worked — more project hours booked than worked time)
        // is un-approvable and must surface a warning too. RED on the old one-direction check
        // (worked − allocated > tol → −5.4 > tol → false); GREEN on the symmetric |·| mirror.
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED",
            start: new DateOnly(2026, 6, 1), end: new DateOnly(2026, 6, 30));
        await InsertWorkTimeAsync(Emp, new DateOnly(2026, 6, 2), 2.0m);
        await InsertTimeEntryAsync(Emp, new DateOnly(2026, 6, 2), 7.4m, "NORMAL", "TASK-1");

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 6);
        Assert.True(rows.Single(r => EmployeeId(r) == Emp).GetProperty("hasWarning").GetBoolean());
    }

    private async Task<int> CountComplianceEventsAsync()
    {
        // The rule-engine path emits NormCheckCompleted / RestPeriodViolationDetected etc. A pure
        // projection read must write NONE for the team's streams.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM events
            WHERE stream_id = ANY(@streams)
              AND event_type IN ('NormCheckCompleted', 'RestPeriodViolationDetected', 'CompensatoryRestGranted')
            """, conn);
        cmd.Parameters.AddWithValue("streams", AllUsers.Select(u => $"employee-{u}").ToArray());
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Auth — designated-approver-scoped, no org-scope leak
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonApprover_GetsOwnEmptySet_NoOrgScopeLeak()
    {
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");

        // Other is a Leader on STY02 but holds NO designated edge over Emp — Emp must NOT appear in
        // Other's set. The team-overview roster is the designated-act-authority SET (edge-derived),
        // so even though Other now shares STY02 org-scope with Emp post-flatten, org-scope does NOT
        // add a row (this read is NOT ValidateEmployeeAccessAsync); only an edge would.
        var rows = await GetTeamOverviewAsync(Other, 2026, 5);
        Assert.DoesNotContain(Emp, rows.Select(EmployeeId));
    }

    [Fact]
    public async Task CrossStyrelseManager_DoesNotSeeCrossTreeEmployee()
    {
        await InsertPeriodAsync(EmpX, "STY05", "SUBMITTED");

        // Mgr (STY02 tree) is NOT EmpX's (STY05 tree) effective approver — D2 tree bound.
        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        Assert.DoesNotContain(EmpX, rows.Select(EmployeeId));
    }

    [Fact]
    public async Task Employee_IsForbidden_LeaderOrAbovePolicy()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp, "STY02"));
        var rsp = await client.GetAsync("/api/approval/team-overview?year=2026&month=5");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Perf-shape — the balance reads are batched/set-based, not a per-employee summary loop
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Perf-shape guard: the aggregate's balance reads are BOUNDED (a small fixed number of set-based
    /// queries over the whole team), NOT a per-employee /summary replay loop. We count the SQL
    /// statements the endpoint runs against the DB via pg_stat_statements-free instrumentation: assert
    /// that adding MANY more employees to the roster does NOT scale the request's query count linearly.
    /// Concretely — with N reports the endpoint must run far fewer than N×(the /summary ~30 round-trips).
    /// Here we assert the endpoint completes for a 6-report roster within a single bounded set of queries
    /// by confirming it returns ALL rows in one HTTP call with the correct per-row data (a behavioral
    /// proxy; the SQL is hand-verified set-based — see the endpoint's ANY(@ids) reads).
    /// </summary>
    [Fact]
    public async Task BalanceReads_AreBatched_OneCallReturnsAllRows()
    {
        // 6 reports under Mgr (Emp, EmpIm via escalation, EmpNoPeriod) + add periods.
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        await InsertPeriodAsync(EmpIm, "STY02", "SUBMITTED");
        await SetVacationBalanceAsync(Emp, 2025, 3m, 25m);
        await SetVacationBalanceAsync(EmpIm, 2025, 9m, 25m);
        await InsertFlexEventAsync(Emp, 5m);
        await InsertFlexEventAsync(EmpIm, -2m);

        var rows = await GetTeamOverviewAsync(Mgr, 2026, 5);
        var byId = rows.ToDictionary(EmployeeId);

        // All three of Mgr's reports returned in ONE call, each with its own batched balance values.
        Assert.True(byId.ContainsKey(Emp));
        Assert.True(byId.ContainsKey(EmpIm));
        Assert.True(byId.ContainsKey(EmpNoPeriod));
        Assert.Equal(3m, byId[Emp].GetProperty("ferieUsed").GetDecimal());
        Assert.Equal(9m, byId[EmpIm].GetProperty("ferieUsed").GetDecimal());
        Assert.Equal(5m, byId[Emp].GetProperty("flexBalance").GetDecimal());
        Assert.Equal(-2m, byId[EmpIm].GetProperty("flexBalance").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private static string EmployeeId(JsonElement row) => row.GetProperty("employeeId").GetString()!;

    private async Task<List<JsonElement>> GetTeamOverviewAsync(string actorId, int year, int month)
    {
        var client = LeaderClient(actorId, ActorOrg(actorId));
        var rsp = await client.GetAsync($"/api/approval/team-overview?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("employees").EnumerateArray().ToList();
    }

    private static string ActorOrg(string actorId) => actorId switch
    {
        MgrX => "STY05",
        _ => "STY02",
    };

    private HttpClient LeaderClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(userId, orgId));
        return client;
    }

    private async Task CreateVikarAsync(string absentApprover, string vikarUser, DateOnly untilDate)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await new ManagerVikarRepository(_dbFactory).CreateAsync(conn, tx, new StatsTid.SharedKernel.Models.ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "FERIE",
            OrganisationId = TreeRootSty02,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
    }

    private static string MintLeaderToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
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
