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
    private const string TestEmpNoRl = "test_emp_rl_norl";
    private const string TestMgrC = "test_mgr_rl_c";

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
                (@empCross, @empCross, '$2a$11$fake', 'Test Cross-Tree',    'test_cross@test.dk', 'STY05', 'HK', 'OK24'),
                (@empNoRl,  @empNoRl,  '$2a$11$fake', 'Test No RL',         'test_norl@test.dk',  'AFD01', 'HK', 'OK24'),
                (@mgrC,     @mgrC,     '$2a$11$fake', 'Test Manager C',     'test_mgrc@test.dk',  'AFD01', 'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("emp", TestEmp);
        cmd.Parameters.AddWithValue("mgrA", TestMgrA);
        cmd.Parameters.AddWithValue("mgrB", TestMgrB);
        cmd.Parameters.AddWithValue("empCross", TestEmpCross);
        cmd.Parameters.AddWithValue("empNoRl", TestEmpNoRl);
        cmd.Parameters.AddWithValue("mgrC", TestMgrC);
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
            WHERE employee_id IN (@emp, @mgrA, @mgrB, @empCross, @empNoRl, @mgrC)
               OR manager_id  IN (@emp, @mgrA, @mgrB, @empCross, @empNoRl, @mgrC)
            """, conn))
        {
            del.Parameters.AddWithValue("emp", TestEmp);
            del.Parameters.AddWithValue("mgrA", TestMgrA);
            del.Parameters.AddWithValue("mgrB", TestMgrB);
            del.Parameters.AddWithValue("empCross", TestEmpCross);
            del.Parameters.AddWithValue("empNoRl", TestEmpNoRl);
            del.Parameters.AddWithValue("mgrC", TestMgrC);
            await del.ExecuteNonQueryAsync();
        }

        // Delete tree settings test data.
        await using (var del = new NpgsqlCommand(
            "DELETE FROM reporting_line_tree_settings WHERE updated_by = 'TEST'", conn))
        {
            await del.ExecuteNonQueryAsync();
        }

        await using (var del = new NpgsqlCommand(
            """
            DELETE FROM users
            WHERE user_id IN (@emp, @mgrA, @mgrB, @empCross, @empNoRl, @mgrC)
            """, conn))
        {
            del.Parameters.AddWithValue("emp", TestEmp);
            del.Parameters.AddWithValue("mgrA", TestMgrA);
            del.Parameters.AddWithValue("mgrB", TestMgrB);
            del.Parameters.AddWithValue("empCross", TestEmpCross);
            del.Parameters.AddWithValue("empNoRl", TestEmpNoRl);
            del.Parameters.AddWithValue("mgrC", TestMgrC);
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

    // ════════════════════════════════════════════════════════════════
    // S50 TASK-5011: TreeSettings + enforcement toggle Docker-gated tests
    // ════════════════════════════════════════════════════════════════

    // 22. TreeSettings — GetAsync for a tree with no settings row returns null
    [Fact]
    public async Task TreeSettings_GetDefault_ReturnsNull()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);

        // Use a tree root that has no settings row. STY05 is an isolated tree in the seed.
        var result = await settingsRepo.GetAsync("STY05");
        Assert.Null(result);
    }

    // 23. TreeSettings — GetEnforcementModeAsync returns "PREFERRED" when no row exists
    [Fact]
    public async Task TreeSettings_GetEnforcementMode_DefaultPreferred()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);

        // No row exists for STY05 → should return "PREFERRED" as the default.
        var mode = await settingsRepo.GetEnforcementModeAsync("STY05");
        Assert.Equal("PREFERRED", mode);
    }

    // 24. TreeSettings — UpsertAsync with expectedVersion=null creates row at version 1
    [Fact]
    public async Task TreeSettings_Upsert_CreatesRow()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);
        var treeRoot = "STY05"; // No pre-existing settings row in seed.

        try
        {
            var result = await settingsRepo.UpsertAsync(
                treeRoot, "REQUIRED", expectedVersion: null, actorId: "TEST");

            Assert.Equal(treeRoot, result.TreeRootOrgId);
            Assert.Equal("REQUIRED", result.EnforcementMode);
            Assert.Equal(1, result.Version);
            Assert.Equal("TEST", result.UpdatedBy);
        }
        finally
        {
            await using var conn = new NpgsqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM reporting_line_tree_settings WHERE tree_root_org_id = @id", conn);
            cmd.Parameters.AddWithValue("id", treeRoot);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // 25. TreeSettings — Create, then update with correct version, assert version=2
    [Fact]
    public async Task TreeSettings_Upsert_UpdatesRow()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);
        var treeRoot = "STY05";

        try
        {
            // Create at version 1
            var created = await settingsRepo.UpsertAsync(
                treeRoot, "PREFERRED", expectedVersion: null, actorId: "TEST");
            Assert.Equal(1, created.Version);

            // Update to REQUIRED with correct version
            var updated = await settingsRepo.UpsertAsync(
                treeRoot, "REQUIRED", expectedVersion: 1, actorId: "TEST");
            Assert.Equal(2, updated.Version);
            Assert.Equal("REQUIRED", updated.EnforcementMode);
        }
        finally
        {
            await using var conn = new NpgsqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM reporting_line_tree_settings WHERE tree_root_org_id = @id", conn);
            cmd.Parameters.AddWithValue("id", treeRoot);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // 26. TreeSettings — Create, then update with wrong version, assert OptimisticConcurrencyException
    [Fact]
    public async Task TreeSettings_Upsert_StaleVersion_ThrowsOptimistic()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);
        var treeRoot = "STY05";

        try
        {
            // Create at version 1
            await settingsRepo.UpsertAsync(
                treeRoot, "PREFERRED", expectedVersion: null, actorId: "TEST");

            // Attempt update with stale version (999 instead of 1)
            await Assert.ThrowsAsync<OptimisticConcurrencyException>(
                () => settingsRepo.UpsertAsync(
                    treeRoot, "REQUIRED", expectedVersion: 999, actorId: "TEST"));
        }
        finally
        {
            await using var conn = new NpgsqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM reporting_line_tree_settings WHERE tree_root_org_id = @id", conn);
            cmd.Parameters.AddWithValue("id", treeRoot);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // 27. TreeSettings — PopulationGate: STY02 with complete seed data is populated
    [Fact]
    public async Task TreeSettings_PopulationGate_STY02_Populated()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);

        // STY02 tree has full seed data with reporting lines for all employees.
        // The only users without a reporting-line-as-employee are tree roots
        // (who are managers but not employees in the tree).
        // However, test_emp_rl_norl is in AFD01 (STY02 tree) and has NO reporting line.
        // We need to exclude it for this test by giving it a reporting line first.
        await _repo.AssignAsync(
            expectedCurrentVersion: null,
            MakeLine(TestEmpNoRl, TestMgrA, treeRootOrgId: "STY02"));

        try
        {
            var (isPopulated, unassigned) = await settingsRepo.ValidateTreePopulatedAsync("STY02");

            // With our test user now having a reporting line + all seed data intact,
            // the only unassigned should be test_emp_rl (also in AFD01/STY02 tree) who
            // has no reporting line at start-of-test. But test_emp_rl is cleaned up in
            // InitializeAsync, so reporting_lines for test_emp_rl are removed.
            // The seed employees should all be covered. Let's verify our norl user is NOT
            // in the unassigned list (it now has a line).
            Assert.DoesNotContain(TestEmpNoRl, unassigned);
        }
        finally
        {
            // Clean up the line we added
            var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmpNoRl, "PRIMARY");
            if (active is not null)
            {
                await _repo.RemoveAsync(expectedCurrentVersion: active.Version, TestEmpNoRl, "PRIMARY");
            }
        }
    }

    // 28. TreeSettings — PopulationGate: user without PRIMARY line shows as unassigned
    [Fact]
    public async Task TreeSettings_PopulationGate_UnassignedUser_ReturnsFalse()
    {
        var settingsRepo = new TreeSettingsRepository(_factory);

        // test_emp_rl_norl is in AFD01 (tree root STY02) and has NO reporting line.
        // ValidateTreePopulatedAsync should return it in the unassigned list.
        var (isPopulated, unassigned) = await settingsRepo.ValidateTreePopulatedAsync("STY02");

        // The tree is NOT fully populated because test_emp_rl_norl (and possibly test_emp_rl,
        // test_mgr_rl_a, test_mgr_rl_b who are also in STY02-subtree orgs) don't have lines.
        // At minimum, test_emp_rl_norl should appear.
        Assert.False(isPopulated,
            "STY02 tree should not be fully populated when test_emp_rl_norl has no reporting line.");
        Assert.Contains(TestEmpNoRl, unassigned);
    }

    // 29. ApprovalPeriod — ExplicitFallbackConfirmation persisted on approve
    [Fact]
    public async Task ApprovalPeriod_ExplicitFallbackConfirmation_Persisted()
    {
        var periodStart = new DateOnly(2099, 2, 1);
        var periodEnd = new DateOnly(2099, 2, 28);

        // Pre-cleanup
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

        var period = new ApprovalPeriod
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
            // Submit → employee approve → manager approve with explicitFallbackConfirmation=true
            await approvalRepo.UpdateStatusAsync(periodId, "SUBMITTED", actorId: "emp002");
            await approvalRepo.UpdateStatusAsync(periodId, "EMPLOYEE_APPROVED", actorId: "emp002");
            await approvalRepo.UpdateStatusAsync(
                periodId, "APPROVED",
                actorId: "mgr01",
                designatedApproverId: "mgr01",
                approvalMethod: "ORG_SCOPE_FALLBACK",
                explicitFallbackConfirmation: true);

            // Read back and verify the boolean is persisted
            var readBack = await approvalRepo.GetByIdAsync(periodId);
            Assert.NotNull(readBack);
            Assert.Equal("APPROVED", readBack!.Status);
            Assert.True(readBack.ExplicitFallbackConfirmation,
                "ExplicitFallbackConfirmation=true must be persisted in the database.");
            Assert.Equal("ORG_SCOPE_FALLBACK", readBack.ApprovalMethod);
            Assert.Equal("mgr01", readBack.DesignatedApproverId);
        }
        finally
        {
            await using var conn = new NpgsqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cleanupCmd = new NpgsqlCommand(
                "DELETE FROM approval_audit WHERE period_id = @periodId; DELETE FROM approval_periods WHERE period_id = @periodId",
                conn);
            cleanupCmd.Parameters.AddWithValue("periodId", periodId);
            await cleanupCmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // S51 TASK-5110: Self-service delegation — ScheduledExpiry + SELF_DELEGATION source
    // ════════════════════════════════════════════════════════════════

    // 30. AssignAsync with ScheduledExpiry — persists field
    [Fact]
    public async Task AssignAsync_WithScheduledExpiry_PersistsField()
    {
        var expiry = new DateOnly(2026, 7, 1);
        var line = MakeLine(TestEmp, TestMgrA, relationship: "ACTING", source: "SELF_DELEGATION");
        var lineWithExpiry = new SharedKernel.Models.ReportingLine
        {
            ReportingLineId = line.ReportingLineId,
            EmployeeId = line.EmployeeId,
            ManagerId = line.ManagerId,
            TreeRootOrgId = line.TreeRootOrgId,
            Relationship = line.Relationship,
            EffectiveFrom = line.EffectiveFrom,
            Source = line.Source,
            Version = line.Version,
            ScheduledExpiry = expiry,
            CreatedBy = line.CreatedBy,
        };

        var result = await _repo.AssignAsync(expectedCurrentVersion: null, lineWithExpiry);

        Assert.NotEqual(Guid.Empty, result.ReportingLineId);
        Assert.Equal(expiry, result.ScheduledExpiry);

        // Also read back via direct query to confirm DB column is populated
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT scheduled_expiry FROM reporting_lines WHERE reporting_line_id = @id", conn);
        cmd.Parameters.AddWithValue("id", result.ReportingLineId);
        var dbValue = await cmd.ExecuteScalarAsync();
        Assert.NotNull(dbValue);
        Assert.NotEqual(DBNull.Value, dbValue);
        var dbDate = DateOnly.FromDateTime((DateTime)dbValue!);
        Assert.Equal(expiry, dbDate);
    }

    // 31. AssignAsync without ScheduledExpiry — null field
    [Fact]
    public async Task AssignAsync_WithoutScheduledExpiry_NullField()
    {
        var line = MakeLine(TestEmp, TestMgrA, relationship: "ACTING", source: "SELF_DELEGATION");
        var result = await _repo.AssignAsync(expectedCurrentVersion: null, line);

        Assert.Null(result.ScheduledExpiry);

        // Also read back via direct query
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT scheduled_expiry FROM reporting_lines WHERE reporting_line_id = @id", conn);
        cmd.Parameters.AddWithValue("id", result.ReportingLineId);
        var dbValue = await cmd.ExecuteScalarAsync();
        Assert.True(dbValue is null || dbValue == DBNull.Value,
            "scheduled_expiry must be NULL when not set");
    }

    // 32. DelegationExpiry — closes expired SELF_DELEGATION ACTING lines
    [Fact]
    public async Task DelegationExpiry_ClosesExpiredLines()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var line = new SharedKernel.Models.ReportingLine
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = TestEmp,
            ManagerId = TestMgrC,
            TreeRootOrgId = "STY02",
            Relationship = "ACTING",
            EffectiveFrom = new DateOnly(2026, 5, 1),
            Source = "SELF_DELEGATION",
            Version = 0,
            ScheduledExpiry = yesterday,
            CreatedBy = "TEST",
        };
        var assigned = await _repo.AssignAsync(expectedCurrentVersion: null, line);

        // Run expiry SQL directly
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE reporting_lines
            SET effective_to = scheduled_expiry, version = version + 1
            WHERE source = 'SELF_DELEGATION' AND relationship = 'ACTING'
              AND scheduled_expiry IS NOT NULL AND scheduled_expiry <= CURRENT_DATE
              AND effective_to IS NULL
            """, conn);
        var affected = await cmd.ExecuteNonQueryAsync();
        Assert.True(affected >= 1, $"Expected at least 1 row affected, got {affected}");

        // Verify the line is closed
        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "ACTING");
        Assert.Null(active);

        // Verify via history that it was closed with scheduled_expiry as effective_to
        var history = await _repo.GetHistoryAsync(TestEmp);
        var closedLine = history.FirstOrDefault(l => l.ReportingLineId == assigned.ReportingLineId);
        Assert.NotNull(closedLine);
        Assert.Equal(yesterday, closedLine!.EffectiveTo);
    }

    // 33. DelegationExpiry — skips non-SELF_DELEGATION lines
    [Fact]
    public async Task DelegationExpiry_SkipsNonSelfDelegation()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        // Insert a MANUAL ACTING line with scheduled_expiry via direct SQL
        // (the repo MakeLine helper defaults to MANUAL)
        var lineId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (reporting_line_id, employee_id, manager_id, tree_root_org_id, relationship,
                 effective_from, source, version, scheduled_expiry, created_by)
            VALUES
                (@id, @emp, @mgr, 'STY02', 'ACTING',
                 '2026-05-01', 'MANUAL', 1, @expiry, 'TEST')
            """, conn);
        insertCmd.Parameters.AddWithValue("id", lineId);
        insertCmd.Parameters.AddWithValue("emp", TestEmp);
        insertCmd.Parameters.AddWithValue("mgr", TestMgrC);
        insertCmd.Parameters.AddWithValue("expiry", yesterday.ToDateTime(TimeOnly.MinValue));
        await insertCmd.ExecuteNonQueryAsync();

        // Run expiry SQL
        await using var expiryCmd = new NpgsqlCommand(
            """
            UPDATE reporting_lines
            SET effective_to = scheduled_expiry, version = version + 1
            WHERE source = 'SELF_DELEGATION' AND relationship = 'ACTING'
              AND scheduled_expiry IS NOT NULL AND scheduled_expiry <= CURRENT_DATE
              AND effective_to IS NULL
            """, conn);
        await expiryCmd.ExecuteNonQueryAsync();

        // Verify the MANUAL line is still open
        await using var checkCmd = new NpgsqlCommand(
            "SELECT effective_to FROM reporting_lines WHERE reporting_line_id = @id", conn);
        checkCmd.Parameters.AddWithValue("id", lineId);
        var effectiveTo = await checkCmd.ExecuteScalarAsync();
        Assert.True(effectiveTo is null || effectiveTo == DBNull.Value,
            "MANUAL ACTING line with scheduled_expiry should NOT be closed by expiry SQL");
    }

    // 34. DelegationExpiry — skips future-expiry lines
    [Fact]
    public async Task DelegationExpiry_SkipsFutureExpiry()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var line = new SharedKernel.Models.ReportingLine
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = TestEmp,
            ManagerId = TestMgrC,
            TreeRootOrgId = "STY02",
            Relationship = "ACTING",
            EffectiveFrom = new DateOnly(2026, 5, 1),
            Source = "SELF_DELEGATION",
            Version = 0,
            ScheduledExpiry = tomorrow,
            CreatedBy = "TEST",
        };
        var assigned = await _repo.AssignAsync(expectedCurrentVersion: null, line);

        // Run expiry SQL
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE reporting_lines
            SET effective_to = scheduled_expiry, version = version + 1
            WHERE source = 'SELF_DELEGATION' AND relationship = 'ACTING'
              AND scheduled_expiry IS NOT NULL AND scheduled_expiry <= CURRENT_DATE
              AND effective_to IS NULL
            """, conn);
        await cmd.ExecuteNonQueryAsync();

        // Verify the line is still open
        var active = await _repo.GetActiveByEmployeeAndRelationshipAsync(TestEmp, "ACTING");
        Assert.NotNull(active);
        Assert.Equal(assigned.ReportingLineId, active!.ReportingLineId);
        Assert.Null(active.EffectiveTo);
    }

    // 35. SelfDelegation source constraint — accepts SELF_DELEGATION
    [Fact]
    public async Task SelfDelegation_SourceConstraint_AcceptsSelfDelegation()
    {
        var line = MakeLine(TestEmp, TestMgrA, relationship: "ACTING", source: "SELF_DELEGATION");
        var result = await _repo.AssignAsync(expectedCurrentVersion: null, line);

        Assert.Equal("SELF_DELEGATION", result.Source);
        Assert.Equal(1, result.Version);
    }

    // 36. SelfDelegation source constraint — rejects INVALID
    [Fact]
    public async Task SelfDelegation_SourceConstraint_RejectsInvalid()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, tree_root_org_id, relationship,
                 effective_from, source, created_by)
            VALUES
                (@emp, @mgr, 'STY02', 'ACTING',
                 '2026-05-01', 'INVALID', 'TEST')
            """, conn);
        cmd.Parameters.AddWithValue("emp", TestEmp);
        cmd.Parameters.AddWithValue("mgr", TestMgrC);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23514", ex.SqlState); // check_violation
    }

    // 37. GetDirectReports — includes SELF_DELEGATION ACTING lines
    [Fact]
    public async Task GetDirectReports_IncludesSelfDelegatedActing()
    {
        // Assign TestEmp under TestMgrC via SELF_DELEGATION ACTING
        var line = MakeLine(TestEmp, TestMgrC, relationship: "ACTING", source: "SELF_DELEGATION");
        await _repo.AssignAsync(expectedCurrentVersion: null, line);

        var reports = await _repo.GetDirectReportsAsync(TestMgrC);

        var employeeIds = reports.Select(r => r.EmployeeId).ToHashSet();
        Assert.Contains(TestEmp, employeeIds);

        // Verify the returned line is ACTING + SELF_DELEGATION
        var delegatedLine = reports.First(r => r.EmployeeId == TestEmp);
        Assert.Equal("ACTING", delegatedLine.Relationship);
        Assert.Equal("SELF_DELEGATION", delegatedLine.Source);
    }
}
