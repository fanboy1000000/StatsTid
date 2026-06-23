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
/// S88-8801 — the leder-oversigt expandable-detail backend reads:
/// <list type="bullet">
/// <item><description><c>GET /api/approval/{employeeId}/allocation-breakdown?year=&amp;month=</c> — the
/// per-employee Fordeling slice, designated-approver-scoped. Its <c>hasAllocationImbalance</c> is the
/// AUTHORITATIVE per-day ANY check and MUST equal the S87 team-overview row's <c>hasWarning</c> for the
/// same employee/month (B1 — the month-scalar <c>max(0,worked−allocated)</c> is provably wrong).</description></item>
/// <item><description>The B2 additive designated-approver OR-branch on
/// <c>GET /api/compliance/{employeeId}/period</c>: a vikar/escalation designated approver who sees the
/// row may now fetch its Advarsel (previously a silent 403); every existing caller is preserved.</description></item>
/// </list>
///
/// <para>
/// Topology MIRRORS <see cref="TeamOverviewAggregateTests"/> (S92/ADR-035 flatten): STY02 is an
/// ORGANISATION under MAO MIN01; STY05 is an ORGANISATION under a DIFFERENT MAO MIN02. The designated
/// approver (<c>Mgr</c>) and its report (<c>Emp</c>) are BOTH on STY02; the designated PRIMARY edge
/// grants breakdown authority (== roster). <c>Vik</c> stands in for the away <c>AwayMgr</c> over
/// <c>EmpVik</c> (STY02). The no-edge <c>Other</c> sits on a DIFFERENT Organisation (STY01, same MAO)
/// so it genuinely fails the edge AND org-scope reach → 403. <c>HrOrg</c> is an org-scope HR over the
/// whole STY02 tree (a legitimate org-scope coverer).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AllocationBreakdownEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // STY02 Organisation (MAO MIN01):
    private const string Emp = "t88_emp";          // STY02 — Mgr's PRIMARY report
    private const string Mgr = "t88_mgr";          // STY02 — designated approver
    private const string Vik = "t88_vik";          // STY02 — AwayMgr's vikar stand-in (a Leader)
    private const string EmpVik = "t88_emp_vik";   // STY02 — reports PRIMARY to AwayMgr (vikar-covered)
    private const string AwayMgr = "t88_awaymgr";  // STY02 — away manager, covered by Vik
    private const string EmpIm = "t88_emp_im";     // STY02 — reports to an INACTIVE manager → escalates to Mgr
    private const string InactiveMgr = "t88_imgr"; // STY02 — INACTIVE; escalates to Mgr
    private const string Other = "t88_other";      // STY01 — a Leader on a DIFFERENT Organisation (same
                                                   //   MAO MIN01) holding NO edge over Emp → no reach
    private const string HrOrg = "t88_hr";         // STY02 — org-scope HR over the whole STY02 tree
    // STY05 Organisation (a DIFFERENT MAO MIN02 — cross-tree):
    private const string EmpX = "t88_emp_x";       // STY05 — different tree_root
    private const string MgrX = "t88_mgr_x";       // STY05 — EmpX's own manager

    private const string TreeRootSty02 = "STY02";
    private const string TreeRootSty05 = "STY05";

    private static readonly string[] AllUsers =
    {
        Emp, Mgr, Vik, EmpVik, AwayMgr, EmpIm, InactiveMgr, Other, HrOrg, EmpX, MgrX,
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
                (@emp,    @emp,    '$2a$11$fake', 'T88 Emp',     't88_emp@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@mgr,    @mgr,    '$2a$11$fake', 'T88 Mgr',     't88_mgr@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@vik,    @vik,    '$2a$11$fake', 'T88 Vikar',   't88_vik@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@empvik, @empvik, '$2a$11$fake', 'T88 EmpVik',  't88_emp_vik@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@away,   @away,   '$2a$11$fake', 'T88 AwayMgr', 't88_awaymgr@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@empim,  @empim,  '$2a$11$fake', 'T88 EmpIM',   't88_emp_im@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@imgr,   @imgr,   '$2a$11$fake', 'T88 IMgr',    't88_imgr@test.dk',    'STY02', 'HK', 'OK24', FALSE),
                (@other,  @other,  '$2a$11$fake', 'T88 Other',   't88_other@test.dk',   'STY01', 'HK', 'OK24', TRUE),
                (@hr,     @hr,     '$2a$11$fake', 'T88 HR',      't88_hr@test.dk',      'STY02', 'HK', 'OK24', TRUE),
                (@empx,   @empx,   '$2a$11$fake', 'T88 EmpX',    't88_emp_x@test.dk',   'STY05', 'HK', 'OK24', TRUE),
                (@mgrx,   @mgrx,   '$2a$11$fake', 'T88 MgrX',    't88_mgr_x@test.dk',   'STY05', 'HK', 'OK24', TRUE)
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
                (@other,  'LOCAL_LEADER', 'STY01', 'ORG_ONLY', 'TEST'),
                (@hr,     'LOCAL_HR',     'STY02', 'ORG_ONLY', 'TEST'),
                (@mgrx,   'LOCAL_LEADER', 'STY05', 'ORG_ONLY', 'TEST'),
                (@emp,    'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empvik, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
                (@empim,  'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST'),
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
        // EmpVik (STY02) reports PRIMARY to AwayMgr (STY02) — AwayMgr is covered by a vikar (set in test).
        await rlRepo.AssignAsync(null, MakeLine(EmpVik, AwayMgr, TreeRootSty02, "PRIMARY"));
        // EmpIm (STY02) → InactiveMgr (inactive, STY02) → Mgr — inactive-escalation up to Mgr.
        await rlRepo.AssignAsync(null, MakeLine(EmpIm, InactiveMgr, TreeRootSty02, "PRIMARY"));
        await rlRepo.AssignAsync(null, MakeLine(InactiveMgr, Mgr, TreeRootSty02, "PRIMARY"));
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
        cmd.Parameters.AddWithValue("other", Other);
        cmd.Parameters.AddWithValue("hr", HrOrg);
        cmd.Parameters.AddWithValue("empx", EmpX);
        cmd.Parameters.AddWithValue("mgrx", MgrX);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId, string treeRoot, string relationship) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = treeRoot,
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

    private async Task InsertPeriodAsync(string employeeId, string orgId, string status,
        DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by)
            VALUES
                (gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
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
            TreeRootOrgId = TreeRootSty02,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
    }

    private int _outboxSeq = 1;

    // ════════════════════════════════════════════════════════════════════════════════
    //  Drift cases — hasAllocationImbalance == the team-overview row's hasWarning
    //
    //  CRUX (B1 / RED-on-old): a naive month scalar `unallocated = max(0, monthWorked −
    //  monthAllocated)` would compute hasAllocationImbalance via `(under>tol) OR (over>tol)`
    //  off MONTH sums, which DISAGREES with the per-day chip for over-allocation and for
    //  over+under-netting-to-month-0. The endpoint computes hasAllocationImbalance as the
    //  per-day ANY check; these tests pin it == the team-overview hasWarning for every shape.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(a) Under-allocation on one day → imbalance true; underAllocated &gt; 0.</summary>
    [Fact]
    public async Task Drift_UnderAllocationOneDay_ImbalanceTrue_MatchesHasWarning()
    {
        var day = new DateOnly(2026, 5, 4);
        await InsertWorkTimeAsync(Emp, day, 7.4m);                  // worked 7.4
        await InsertTimeEntryAsync(Emp, day, 3.0m, "NORMAL", "TASK-1"); // allocated 3.0
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 5);
        Assert.True(b.GetProperty("hasAllocationImbalance").GetBoolean());
        Assert.Equal(4.4m, b.GetProperty("underAllocated").GetDecimal());
        Assert.Equal(0m, b.GetProperty("overAllocated").GetDecimal());
        Assert.Equal(7.4m, b.GetProperty("worked").GetDecimal());
        Assert.Equal(3.0m, b.GetProperty("allocated").GetDecimal());

        Assert.Equal(await HasWarningAsync(Mgr, Emp, 2026, 5), b.GetProperty("hasAllocationImbalance").GetBoolean());
    }

    /// <summary>
    /// (b) OVER-allocation on one day (allocated_d &gt; worked_d) → imbalance true while
    /// <c>underAllocated == 0</c>. RED-ON-OLD: a naive month scalar
    /// <c>unallocated = max(0, monthWorked − monthAllocated) = max(0, 2 − 7.4) = 0</c> ⇒
    /// <c>hasAllocationImbalance = false</c>, which DISAGREES with the table chip (which warns).
    /// The per-day ANY check (and this endpoint) correctly returns true.
    /// </summary>
    [Fact]
    public async Task Drift_OverAllocationOneDay_ImbalanceTrue_UnderZero_MatchesHasWarning()
    {
        var day = new DateOnly(2026, 6, 2);
        await InsertWorkTimeAsync(Emp, day, 2.0m);                  // worked 2.0
        await InsertTimeEntryAsync(Emp, day, 7.4m, "NORMAL", "TASK-1"); // allocated 7.4 (OVER)
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 6);
        Assert.True(b.GetProperty("hasAllocationImbalance").GetBoolean()); // RED on naive month max(0,2−7.4)=0
        Assert.Equal(0m, b.GetProperty("underAllocated").GetDecimal());
        Assert.Equal(5.4m, b.GetProperty("overAllocated").GetDecimal());

        Assert.Equal(await HasWarningAsync(Mgr, Emp, 2026, 6), b.GetProperty("hasAllocationImbalance").GetBoolean());
    }

    /// <summary>
    /// (c) OVER one day + UNDER another netting to month-0 (Σworked == Σallocated) → imbalance true.
    /// RED-ON-OLD: a month scalar sees <c>monthWorked(16) == monthAllocated(16)</c> ⇒ <c>under=over=0</c>
    /// ⇒ false, contradicting the chip. The per-day ANY check sees both off-balance days ⇒ true; AND the
    /// directional sums here are BOTH non-zero (under 2, over 2) even though the month nets to 0.
    /// </summary>
    [Fact]
    public async Task Drift_OverPlusUnderNettingToMonthZero_ImbalanceTrue_MatchesHasWarning()
    {
        var day1 = new DateOnly(2026, 7, 1); // worked 8 / allocated 10 → over 2
        var day2 = new DateOnly(2026, 7, 2); // worked 8 / allocated 6  → under 2
        await InsertWorkTimeAsync(Emp, day1, 8.0m);
        await InsertTimeEntryAsync(Emp, day1, 10.0m, "NORMAL", "TASK-1");
        await InsertWorkTimeAsync(Emp, day2, 8.0m);
        await InsertTimeEntryAsync(Emp, day2, 6.0m, "NORMAL", "TASK-1");
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 7);
        Assert.Equal(16.0m, b.GetProperty("worked").GetDecimal());
        Assert.Equal(16.0m, b.GetProperty("allocated").GetDecimal());     // month nets to 0
        Assert.True(b.GetProperty("hasAllocationImbalance").GetBoolean()); // RED on naive month-0 scalar
        Assert.Equal(2.0m, b.GetProperty("underAllocated").GetDecimal());
        Assert.Equal(2.0m, b.GetProperty("overAllocated").GetDecimal());

        Assert.Equal(await HasWarningAsync(Mgr, Emp, 2026, 7), b.GetProperty("hasAllocationImbalance").GetBoolean());
    }

    /// <summary>(d) Allocation on a zero-worked day (worked 0, allocated 5) → imbalance true (over 5).</summary>
    [Fact]
    public async Task Drift_AllocationOnZeroWorkedDay_ImbalanceTrue_MatchesHasWarning()
    {
        var day = new DateOnly(2026, 8, 3);
        // No work_time row for the day → worked_d = 0; allocated 5 → |0 − 5| > tol.
        await InsertTimeEntryAsync(Emp, day, 5.0m, "NORMAL", "TASK-1");
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 8);
        Assert.True(b.GetProperty("hasAllocationImbalance").GetBoolean());
        Assert.Equal(0m, b.GetProperty("worked").GetDecimal());
        Assert.Equal(5.0m, b.GetProperty("allocated").GetDecimal());
        Assert.Equal(5.0m, b.GetProperty("overAllocated").GetDecimal());

        Assert.Equal(await HasWarningAsync(Mgr, Emp, 2026, 8), b.GetProperty("hasAllocationImbalance").GetBoolean());
    }

    /// <summary>(e) A clean fully-allocated month → imbalance false; both directional sums 0.</summary>
    [Fact]
    public async Task Drift_CleanFullyAllocatedMonth_ImbalanceFalse_MatchesHasWarning()
    {
        var day = new DateOnly(2026, 9, 1);
        await InsertWorkTimeAsync(Emp, day, 7.4m);
        await InsertTimeEntryAsync(Emp, day, 7.4m, "NORMAL", "TASK-1"); // exactly balanced
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 9);
        Assert.False(b.GetProperty("hasAllocationImbalance").GetBoolean());
        Assert.Equal(0m, b.GetProperty("underAllocated").GetDecimal());
        Assert.Equal(0m, b.GetProperty("overAllocated").GetDecimal());

        Assert.Equal(await HasWarningAsync(Mgr, Emp, 2026, 9), b.GetProperty("hasAllocationImbalance").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  allocations[] — per-task hours sum to `allocated` and coexist with per-day figures
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Allocations_PerTaskHoursSumToAllocated_CoexistWithPerDayFigures()
    {
        var day1 = new DateOnly(2026, 5, 4);
        var day2 = new DateOnly(2026, 5, 5);
        await InsertWorkTimeAsync(Emp, day1, 7.4m);
        await InsertWorkTimeAsync(Emp, day2, 7.4m);
        await InsertTimeEntryAsync(Emp, day1, 3.0m, "NORMAL", "TASK-A");
        await InsertTimeEntryAsync(Emp, day1, 2.0m, "NORMAL", "TASK-B");
        await InsertTimeEntryAsync(Emp, day2, 4.4m, "NORMAL", "TASK-A");
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 5);
        var allocations = b.GetProperty("allocations").EnumerateArray().ToList();
        var perTask = allocations.ToDictionary(
            a => a.GetProperty("taskId").GetString()!, a => a.GetProperty("hours").GetDecimal());
        Assert.Equal(7.4m, perTask["TASK-A"]); // 3.0 + 4.4
        Assert.Equal(2.0m, perTask["TASK-B"]);
        // Sum of the per-task bars == the `allocated` month total.
        Assert.Equal(b.GetProperty("allocated").GetDecimal(), perTask.Values.Sum());
        Assert.Equal(9.4m, b.GetProperty("allocated").GetDecimal());
        // Per-day figures coexist (worked 14.8, allocated 9.4 → under 5.4).
        Assert.Equal(14.8m, b.GetProperty("worked").GetDecimal());
        Assert.Equal(5.4m, b.GetProperty("underAllocated").GetDecimal());
    }

    /// <summary>
    /// Only NORMAL + non-null-TaskId entries count as allocated — an ABSENCE entry (e.g. VACATION) with
    /// no task is excluded from both `allocated` and `allocations[]` (mirrors the aggregate's gate arm).
    /// </summary>
    [Fact]
    public async Task Allocations_ExcludeNonNormalAndNullTask()
    {
        var day = new DateOnly(2026, 5, 4);
        await InsertWorkTimeAsync(Emp, day, 7.4m);
        await InsertTimeEntryAsync(Emp, day, 7.4m, "NORMAL", "TASK-1");          // counted
        await InsertTimeEntryAsync(Emp, day, 2.0m, "NORMAL", taskId: null);      // null task → excluded
        await InsertTimeEntryAsync(Emp, day, 7.4m, "VACATION", "TASK-1");        // non-NORMAL → excluded
        await InsertPeriodAsync(Emp, "STY02", "SUBMITTED", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 5);
        Assert.Equal(7.4m, b.GetProperty("allocated").GetDecimal());
        Assert.Single(b.GetProperty("allocations").EnumerateArray());
        Assert.False(b.GetProperty("hasAllocationImbalance").GetBoolean()); // 7.4 worked == 7.4 allocated
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Auth (breakdown) — designated-approver-scoped, == roster
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Breakdown_NonDesignatedLeader_Is403()
    {
        await InsertWorkTimeAsync(Emp, new DateOnly(2026, 5, 4), 7.4m);
        // Other is a Leader on STY01 (a DIFFERENT Organisation) holding NO designated edge over Emp,
        // and its STY01 org-scope does not reach Emp's STY02 → 403.
        var rsp = await GetBreakdownRawAsync(Other, Emp, 2026, 5);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Breakdown_DesignatedApprover_CrossAfdeling_Is200()
    {
        await InsertWorkTimeAsync(Emp, new DateOnly(2026, 5, 4), 7.4m);
        // Mgr is Emp's designated approver (same STY02 Organisation; the edge grants) → must reach 200.
        var b = await GetBreakdownAsync(Mgr, Emp, 2026, 5);
        Assert.Equal(7.4m, b.GetProperty("worked").GetDecimal());
    }

    /// <summary>
    /// A VIKAR-coverage approver (Vik standing in for AwayMgr over EmpVik, STY02) — a row that appears
    /// in Vik's team-overview — is breakdown-authorized (roster ⊇) via the designated-edge predicate.
    /// </summary>
    [Fact]
    public async Task Breakdown_CrossAfdelingVikarApprover_Is200()
    {
        await CreateVikarAsync(AwayMgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        await InsertWorkTimeAsync(EmpVik, new DateOnly(2026, 5, 4), 7.4m);

        var b = await GetBreakdownAsync(Vik, EmpVik, 2026, 5);
        Assert.Equal(7.4m, b.GetProperty("worked").GetDecimal());
    }

    [Fact]
    public async Task Breakdown_CrossStyrelseLeader_Is403()
    {
        // Mgr (STY02 tree) is NOT EmpX's (STY05 tree) effective approver — D2 tree bound.
        var rsp = await GetBreakdownRawAsync(Mgr, EmpX, 2026, 5);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Breakdown_Employee_IsForbidden_LeaderOrAbovePolicy()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp, "STY02"));
        var rsp = await client.GetAsync($"/api/approval/{Emp}/allocation-breakdown?year=2026&month=5");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Auth (B2 compliance) — the additive designated-approver OR-branch
    //
    //  The compliance /period handler calls the rule engine over HTTP; in this Postgres-only
    //  harness that call fails → 503. But the AUTH decision (403 vs not-403) happens BEFORE the
    //  rule-engine call, so a 403 vs a non-403 (503) cleanly discriminates the auth branch.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A VIKAR designated approver fetching /compliance/{id}/period via the B2 OR-branch. Vik stands in
    /// for AwayMgr over EmpVik (same STY02 Organisation); auth passes (reaching the rule-engine call →
    /// 503 here, NOT 403). We assert NOT 403.
    /// </summary>
    [Fact]
    public async Task Compliance_CrossAfdelingVikarApprover_PassesAuth_NotForbidden()
    {
        await CreateVikarAsync(AwayMgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var rsp = await GetComplianceRawAsync(Vik, EmpVik, 2026, 5);
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode); // the B2 OR-branch admits the designated approver
    }

    /// <summary>
    /// An INACTIVE-ESCALATION designated approver (Mgr over EmpIm via InactiveMgr, same STY02
    /// Organisation) also passes auth on /compliance/{id}/period.
    /// </summary>
    [Fact]
    public async Task Compliance_CrossAfdelingEscalationApprover_PassesAuth_NotForbidden()
    {
        var rsp = await GetComplianceRawAsync(Mgr, EmpIm, 2026, 5);
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Compliance_NonDesignatedLeader_StillForbidden()
    {
        // Other (STY01) holds no edge over Emp AND no org-scope reaching Emp's STY02 → still 403.
        var rsp = await GetComplianceRawAsync(Other, Emp, 2026, 5);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Compliance_EmployeeSelf_StillAllowed_NotForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp, "STY02"));
        var rsp = await client.GetAsync($"/api/compliance/{Emp}/period?year=2026&month=5");
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode); // self-access preserved
    }

    [Fact]
    public async Task Compliance_OrgScopeHr_StillAllowed_NotForbidden()
    {
        // HR org-scope over the whole STY02 tree covers Emp via ValidateEmployeeAccessAsync.
        var rsp = await GetComplianceRawAsync(HrOrg, Emp, 2026, 5, role: StatsTidRoles.LocalHR, orgId: "STY02");
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode); // existing org-scope caller preserved
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<JsonElement> GetBreakdownAsync(string actorId, string employeeId, int year, int month)
    {
        var rsp = await GetBreakdownRawAsync(actorId, employeeId, year, month);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> GetBreakdownRawAsync(string actorId, string employeeId, int year, int month)
    {
        var client = LeaderClient(actorId, ActorOrg(actorId));
        return await client.GetAsync($"/api/approval/{employeeId}/allocation-breakdown?year={year}&month={month}");
    }

    private async Task<HttpResponseMessage> GetComplianceRawAsync(
        string actorId, string employeeId, int year, int month,
        string role = StatsTidRoles.LocalLeader, string? orgId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            MintToken(actorId, orgId ?? ActorOrg(actorId), role));
        return await client.GetAsync($"/api/compliance/{employeeId}/period?year={year}&month={month}");
    }

    /// <summary>Reads the team-overview row's hasWarning for the same actor/employee/month (the value the breakdown must equal).</summary>
    private async Task<bool> HasWarningAsync(string actorId, string employeeId, int year, int month)
    {
        var client = LeaderClient(actorId, ActorOrg(actorId));
        var rsp = await client.GetAsync($"/api/approval/team-overview?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("employees").EnumerateArray()
            .Single(r => r.GetProperty("employeeId").GetString() == employeeId);
        return row.GetProperty("hasWarning").GetBoolean();
    }

    private static string ActorOrg(string actorId) => actorId switch
    {
        MgrX => "STY05",
        HrOrg => "STY02",
        Other => "STY01", // a DIFFERENT Organisation: no org-scope reach over Emp's STY02 → 403 negatives hold
        _ => "STY02",
    };

    private HttpClient LeaderClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(userId, orgId));
        return client;
    }

    private static string MintToken(string userId, string orgId, string role)
    {
        var tokenService = NewTokenService();
        var scopeType = role == StatsTidRoles.Employee ? "ORG_ONLY" : "ORG_ONLY";
        var scopes = new[] { new RoleScope(role, orgId, scopeType) };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintLeaderToken(string userId, string orgId) =>
        MintToken(userId, orgId, StatsTidRoles.LocalLeader);

    private static string MintEmployeeToken(string userId, string orgId) =>
        MintToken(userId, orgId, StatsTidRoles.Employee);

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
