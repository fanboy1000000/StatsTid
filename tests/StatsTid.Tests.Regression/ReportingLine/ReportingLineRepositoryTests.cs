using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.ReportingLine;

/// <summary>
/// Docker-gated integration tests for <see cref="ReportingLineRepository"/>.
/// Connects to the running PostgreSQL container (init.sql already applied) and
/// validates CRUD operations, seed data, partial-unique-index enforcement,
/// optimistic concurrency, tree-root resolution, and cross-tree rejection.
///
/// Tests that WRITE use dedicated test users (test_emp_rl / test_mgr_rl_a/b /
/// test_emp_rl_cross) to avoid polluting shared seed data. Cleanup in DisposeAsync.
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReportingLineRepositoryTests : IAsyncLifetime
{
    private const string ConnStr =
        "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

    private readonly DbConnectionFactory _factory = new(ConnStr);
    private readonly ReportingLineRepository _repo;

    // Test-only user IDs — distinct from seed users.
    private const string TestEmp = "test_emp_rl";
    private const string TestMgrA = "test_mgr_rl_a";
    private const string TestMgrB = "test_mgr_rl_b";
    private const string TestEmpCross = "test_emp_rl_cross";

    public ReportingLineRepositoryTests()
    {
        _repo = new ReportingLineRepository(_factory);
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();

        // Clean up any leftover test data from a previous interrupted run.
        await CleanupTestDataAsync(conn);

        // Insert test users in orgs that let us exercise same-tree and cross-tree logic.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version)
            VALUES
                (@emp,      @emp,      '$2a$11$fake', 'Test Employee RL',   'test_emp@test.dk',   'AFD01', 'HK', 'OK24'),
                (@mgrA,     @mgrA,     '$2a$11$fake', 'Test Manager A',     'test_mgra@test.dk',  'STY02', 'HK', 'OK24'),
                (@mgrB,     @mgrB,     '$2a$11$fake', 'Test Manager B',     'test_mgrb@test.dk',  'AFD02', 'HK', 'OK24'),
                (@empCross, @empCross, '$2a$11$fake', 'Test Cross-Tree',    'test_cross@test.dk', 'STY05', 'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("emp", TestEmp);
        cmd.Parameters.AddWithValue("mgrA", TestMgrA);
        cmd.Parameters.AddWithValue("mgrB", TestMgrB);
        cmd.Parameters.AddWithValue("empCross", TestEmpCross);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await CleanupTestDataAsync(conn);
    }

    private static async Task CleanupTestDataAsync(NpgsqlConnection conn)
    {
        // Delete reporting lines first (FK on employee_id/manager_id → users).
        await using (var del = new NpgsqlCommand(
            """
            DELETE FROM reporting_lines
            WHERE employee_id IN (@emp, @mgrA, @mgrB, @empCross)
               OR manager_id  IN (@emp, @mgrA, @mgrB, @empCross)
            """, conn))
        {
            del.Parameters.AddWithValue("emp", TestEmp);
            del.Parameters.AddWithValue("mgrA", TestMgrA);
            del.Parameters.AddWithValue("mgrB", TestMgrB);
            del.Parameters.AddWithValue("empCross", TestEmpCross);
            await del.ExecuteNonQueryAsync();
        }

        await using (var del = new NpgsqlCommand(
            """
            DELETE FROM users
            WHERE user_id IN (@emp, @mgrA, @mgrB, @empCross)
            """, conn))
        {
            del.Parameters.AddWithValue("emp", TestEmp);
            del.Parameters.AddWithValue("mgrA", TestMgrA);
            del.Parameters.AddWithValue("mgrB", TestMgrB);
            del.Parameters.AddWithValue("empCross", TestEmpCross);
            await del.ExecuteNonQueryAsync();
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  Helper — builds a ReportingLine ready for AssignAsync
    // ────────────────────────────────────────────────────────────────

    private static SharedKernel.Models.ReportingLine MakeLine(
        string employeeId,
        string managerId,
        string treeRootOrgId = "STY02",
        string relationship = "PRIMARY",
        string source = "MANUAL") => new()
    {
        ReportingLineId = Guid.Empty,           // AssignAsync generates a new UUID
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = treeRootOrgId,
        Relationship = relationship,
        EffectiveFrom = new DateOnly(2026, 5, 1),
        Source = source,
        Version = 0,                            // ignored by InsertLineAsync (always 1)
        CreatedBy = "TEST",
    };

    // ════════════════════════════════════════════════════════════════
    //  1. Assign PRIMARY — first assignment returns version 1
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_Primary_FirstAssignment_ReturnsVersion1()
    {
        var line = MakeLine(TestEmp, TestMgrA);
        var result = await _repo.AssignAsync(expectedCurrentVersion: null, line);

        Assert.NotEqual(Guid.Empty, result.ReportingLineId);
        Assert.Equal(1, result.Version);
        Assert.Equal("PRIMARY", result.Relationship);
        Assert.Equal(TestEmp, result.EmployeeId);
        Assert.Equal(TestMgrA, result.ManagerId);
        Assert.Null(result.EffectiveTo);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Assign PRIMARY — reassign supersedes previous
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_Primary_Reassign_SupersedesPrevious()
    {
        // First assignment
        var first = await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA));

        // Reassign to manager B (supersede with version check)
        var second = await _repo.AssignAsync(
            expectedCurrentVersion: first.Version,
            MakeLine(TestEmp, TestMgrB));

        Assert.Equal(2, second.Version);            // monotonic: predecessor v1 + 1 = v2
        Assert.NotEqual(first.ReportingLineId, second.ReportingLineId);

        // Verify the original line was closed
        var history = await _repo.GetHistoryAsync(TestEmp);
        var closed = history.FirstOrDefault(l => l.ReportingLineId == first.ReportingLineId);
        Assert.NotNull(closed);
        Assert.NotNull(closed!.EffectiveTo);        // superseded → closed
    }

    // ════════════════════════════════════════════════════════════════
    //  3. Remove PRIMARY — closes active line
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Remove_Primary_ClosesActiveLine()
    {
        var assigned = await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA));

        var removed = await _repo.RemoveAsync(
            expectedCurrentVersion: assigned.Version,
            TestEmp, "PRIMARY");

        Assert.NotNull(removed.EffectiveTo);
        // CURRENT_DATE is server-local (typically UTC in Docker).
        // Accept today in either UTC or local to avoid TZ edge-case flakiness.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        Assert.True(
            removed.EffectiveTo == today || removed.EffectiveTo == todayLocal,
            $"Expected effective_to={removed.EffectiveTo} to be {today} or {todayLocal}");

        // GetActiveByEmployeeAndRelationshipAsync should return null now.
        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "PRIMARY");
        Assert.Null(active);
    }

    // ════════════════════════════════════════════════════════════════
    //  4. GetTreeAsync — returns all active lines in STY02 tree
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTree_STY02_ReturnsSeededLines()
    {
        var lines = await _repo.GetTreeAsync("STY02");

        // Seed data: 6 PRIMARY + 1 ACTING in STY02 = 7 lines minimum
        Assert.True(lines.Count >= 7,
            $"Expected at least 7 active lines in STY02 tree, got {lines.Count}");

        // All returned lines must belong to the STY02 tree and be active.
        Assert.All(lines, l =>
        {
            Assert.Equal("STY02", l.TreeRootOrgId);
            Assert.Null(l.EffectiveTo);
        });
    }

    // ════════════════════════════════════════════════════════════════
    //  5. GetDirectReportsAsync — ladm01's reports in STY02
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDirectReports_Ladm01_ReturnsExpectedEmployees()
    {
        var reports = await _repo.GetDirectReportsAsync("ladm01");

        // Seed: hr01, mgr01, emp003, emp010 (PRIMARY) + emp002 (ACTING) = 5
        Assert.True(reports.Count >= 5,
            $"Expected at least 5 direct reports for ladm01, got {reports.Count}");

        var employeeIds = reports.Select(r => r.EmployeeId).ToHashSet();
        Assert.Contains("hr01", employeeIds);
        Assert.Contains("mgr01", employeeIds);
        Assert.Contains("emp003", employeeIds);
        Assert.Contains("emp010", employeeIds);
        Assert.Contains("emp002", employeeIds);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. GetHistoryAsync — returns all lines including closed
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetHistory_AfterReassign_ReturnsBothLines()
    {
        // Assign then reassign → 1 closed + 1 active
        await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA));
        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "PRIMARY");
        Assert.NotNull(active);

        await _repo.AssignAsync(
            expectedCurrentVersion: active!.Version,
            MakeLine(TestEmp, TestMgrB));

        var history = await _repo.GetHistoryAsync(TestEmp);
        Assert.Equal(2, history.Count);

        Assert.Single(history.Where(l => l.EffectiveTo is not null));   // 1 closed
        Assert.Single(history.Where(l => l.EffectiveTo is null));       // 1 active
    }

    // ════════════════════════════════════════════════════════════════
    //  7. Assign ACTING — creates acting line
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_Acting_CreatesActingLine()
    {
        var result = await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA, relationship: "ACTING"));

        Assert.Equal("ACTING", result.Relationship);
        Assert.Equal(1, result.Version);
        Assert.Null(result.EffectiveTo);
    }

    // ════════════════════════════════════════════════════════════════
    //  8. Remove ACTING — closes acting line
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Remove_Acting_ClosesActingLine()
    {
        var assigned = await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA, relationship: "ACTING"));

        var removed = await _repo.RemoveAsync(
            expectedCurrentVersion: assigned.Version,
            TestEmp, "ACTING");

        Assert.NotNull(removed.EffectiveTo);

        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "ACTING");
        Assert.Null(active);
    }

    // ════════════════════════════════════════════════════════════════
    //  9. Cross-tree rejection — ValidateSameTreeAsync throws
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateSameTree_CrossTree_ThrowsCrossTreeAssignmentException()
    {
        // TestEmp is in AFD01 → tree root STY02
        // TestEmpCross is in STY05 → tree root STY05
        // Assigning TestEmp under TestEmpCross (different trees) should throw.
        await Assert.ThrowsAsync<CrossTreeAssignmentException>(
            () => _repo.ValidateSameTreeAsync(TestEmp, TestEmpCross));
    }

    // ════════════════════════════════════════════════════════════════
    // 10. Self-management rejection — SQL CHECK constraint
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_SelfManager_ThrowsPostgresException()
    {
        // CHECK (employee_id <> manager_id) in the schema.
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => _repo.AssignAsync(
                expectedCurrentVersion: null,
                MakeLine(TestEmp, TestEmp)));

        Assert.Equal("23514", ex.SqlState); // check_violation
    }

    // ════════════════════════════════════════════════════════════════
    // 11. Optimistic concurrency — stale version throws
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_StaleVersion_ThrowsOptimisticConcurrencyException()
    {
        var first = await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA));

        // Reassign with correct version to bump version on the closed row
        // and create a new row at version 1.
        var second = await _repo.AssignAsync(
            expectedCurrentVersion: first.Version,
            MakeLine(TestEmp, TestMgrB));

        // Now try to reassign using the OLD version (stale).
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            () => _repo.AssignAsync(
                expectedCurrentVersion: 999,   // definitely stale
                MakeLine(TestEmp, TestMgrA)));
    }

    // ════════════════════════════════════════════════════════════════
    // 12. ResolveTreeRootOrgIdAsync — AFD01 → STY02
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveTreeRoot_AFD01_ReturnsSTY02()
    {
        var root = await _repo.ResolveTreeRootOrgIdAsync("AFD01");
        Assert.Equal("STY02", root);
    }

    // ════════════════════════════════════════════════════════════════
    // 13. ResolveTreeRootOrgIdAsync — MIN01 → MIN01
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveTreeRoot_MIN01_ReturnsMIN01()
    {
        var root = await _repo.ResolveTreeRootOrgIdAsync("MIN01");
        Assert.Equal("MIN01", root);
    }

    // ════════════════════════════════════════════════════════════════
    // 14. Seed data — 14 rows total (TASK-4815)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeedData_Has13Rows()
    {
        // Seed: 12 PRIMARY + 1 ACTING = 13 rows (comment in init.sql says "14"
        // but actual VALUES list has 13 rows — the comment over-counts PRIMARY by 1).
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM reporting_lines WHERE created_by = 'SYSTEM'", conn);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(13, count);
    }

    // ════════════════════════════════════════════════════════════════
    // 15. Seed data — partial unique index enforced (TASK-4815)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeedData_PartialUniqueIndex_RejectsDuplicateActivePrimary()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();

        // emp001 already has an active PRIMARY in the seed. Attempting
        // a second active PRIMARY must violate uq_reporting_line_active_primary.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, tree_root_org_id, relationship,
                 effective_from, source, created_by)
            VALUES
                ('emp001', 'mgr03', 'STY01', 'PRIMARY',
                 '2026-01-01', 'MANUAL', 'TEST')
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23505", ex.SqlState); // unique_violation
    }

    // ════════════════════════════════════════════════════════════════
    // S49 TASK-4912: Designated approver resolution tests
    // ════════════════════════════════════════════════════════════════

    // 16. ResolveDesignatedApprover — active PRIMARY returns DESIGNATED_MANAGER
    [Fact]
    public async Task ResolveDesignatedApprover_ActivePrimary_ReturnsDesignatedManager()
    {
        // Assign test_emp_rl → test_mgr_rl_a (PRIMARY)
        await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA));

        var (managerId, approvalMethod, depth) = await _repo.ResolveDesignatedApproverAsync(TestEmp);

        Assert.Equal(TestMgrA, managerId);
        Assert.Equal("DESIGNATED_MANAGER", approvalMethod);
        Assert.Equal(0, depth);
    }

    // 17. ResolveDesignatedApprover — ACTING takes precedence over PRIMARY
    [Fact]
    public async Task ResolveDesignatedApprover_ActiveActing_ReturnsActingManager()
    {
        // Assign PRIMARY first
        await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA, relationship: "PRIMARY"));

        // Assign ACTING — takes precedence
        await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrB, relationship: "ACTING"));

        var (managerId, approvalMethod, depth) = await _repo.ResolveDesignatedApproverAsync(TestEmp);

        Assert.Equal(TestMgrB, managerId);
        Assert.Equal("ACTING_MANAGER", approvalMethod);
        Assert.Equal(0, depth);
    }

    // 18. ResolveDesignatedApprover — no line returns null
    [Fact]
    public async Task ResolveDesignatedApprover_NoLine_ReturnsNull()
    {
        // TestEmp has no reporting line (cleanup runs before each test via InitializeAsync)
        var (managerId, approvalMethod, depth) = await _repo.ResolveDesignatedApproverAsync(TestEmp);

        Assert.Null(managerId);
        Assert.Null(approvalMethod);
        Assert.Equal(0, depth);
    }

    // 19. Import — batch assign creates lines with HR_IMPORT source
    [Fact]
    public async Task Import_BatchAssign_CreatesLinesWithHrImportSource()
    {
        var line = MakeLine(TestEmp, TestMgrA, source: "HR_IMPORT");
        var result = await _repo.AssignAsync(expectedCurrentVersion: null, line);

        Assert.Equal("HR_IMPORT", result.Source);
        Assert.Equal(1, result.Version);
        Assert.Equal(TestMgrA, result.ManagerId);
        Assert.Null(result.EffectiveTo);

        // Verify via direct read
        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "PRIMARY");
        Assert.NotNull(active);
        Assert.Equal("HR_IMPORT", active!.Source);
    }

    // 20. Import — idempotent: second identical assign creates version 2 (repo does not skip)
    [Fact]
    public async Task Import_Idempotent_SecondIdenticalAssignCreatesVersion2()
    {
        // First assign
        var first = await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmp, TestMgrA, source: "HR_IMPORT"));
        Assert.Equal(1, first.Version);

        // Second assign with same manager — repo supersedes (endpoint would skip,
        // but at repository level a second call creates version 2)
        var second = await _repo.AssignAsync(
            expectedCurrentVersion: first.Version,
            MakeLine(TestEmp, TestMgrA, source: "HR_IMPORT"));
        Assert.Equal(2, second.Version);
        Assert.Equal(TestMgrA, second.ManagerId);

        // Only one active line (the second), predecessor closed
        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "PRIMARY");
        Assert.NotNull(active);
        Assert.Equal(2, active!.Version);

        var history = await _repo.GetHistoryAsync(TestEmp);
        Assert.Equal(2, history.Count);
        Assert.Single(history.Where(l => l.EffectiveTo is null));       // 1 active
        Assert.Single(history.Where(l => l.EffectiveTo is not null));   // 1 closed
    }

    // 21. ApprovalPeriod — approve populates routing fields
    [Fact]
    public async Task ApprovalPeriod_ApprovePopulatesRoutingFields()
    {
        // Use seed employee emp002 (org AFD01, HK, OK24) who has a reporting line to mgr01.
        // Use a distant date range to avoid collisions with seed or other test data.
        var periodStart = new DateOnly(2099, 1, 1);
        var periodEnd = new DateOnly(2099, 1, 31);

        // Pre-cleanup: remove any leftover from a previous interrupted run.
        await using (var preConn = new NpgsqlConnection(ConnStr))
        {
            await preConn.OpenAsync();
            await using var preClean = new NpgsqlCommand(
                """
                DELETE FROM approval_audit WHERE period_id IN
                    (SELECT period_id FROM approval_periods WHERE employee_id = 'emp002' AND period_start = @ps AND period_end = @pe);
                DELETE FROM approval_periods WHERE employee_id = 'emp002' AND period_start = @ps AND period_end = @pe
                """, preConn);
            preClean.Parameters.AddWithValue("ps", periodStart);
            preClean.Parameters.AddWithValue("pe", periodEnd);
            await preClean.ExecuteNonQueryAsync();
        }

        var approvalRepo = new ApprovalPeriodRepository(_factory);

        // Create a draft period
        var period = new StatsTid.SharedKernel.Models.ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "emp002",
            OrgId = "AFD01",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        var periodId = await approvalRepo.CreateAsync(period);

        try
        {
            // Submit, then employee-approve, then manager-approve with routing fields
            await approvalRepo.UpdateStatusAsync(periodId, "SUBMITTED", actorId: "emp002");
            await approvalRepo.UpdateStatusAsync(periodId, "EMPLOYEE_APPROVED", actorId: "emp002");
            await approvalRepo.UpdateStatusAsync(
                periodId, "APPROVED",
                actorId: "mgr01",
                designatedApproverId: "mgr01",
                approvalMethod: "DESIGNATED_MANAGER");

            // Read back and verify routing fields
            var readBack = await approvalRepo.GetByIdAsync(periodId);
            Assert.NotNull(readBack);
            Assert.Equal("APPROVED", readBack!.Status);
            Assert.Equal("mgr01", readBack.DesignatedApproverId);
            Assert.Equal("DESIGNATED_MANAGER", readBack.ApprovalMethod);
            Assert.Equal("mgr01", readBack.ApprovedBy);
        }
        finally
        {
            // Cleanup: delete the test approval period
            await using var conn = new NpgsqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cleanupCmd = new NpgsqlCommand(
                "DELETE FROM approval_audit WHERE period_id = @periodId; DELETE FROM approval_periods WHERE period_id = @periodId",
                conn);
            cleanupCmd.Parameters.AddWithValue("periodId", periodId);
            await cleanupCmd.ExecuteNonQueryAsync();
        }
    }
}
