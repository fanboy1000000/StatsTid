using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.ReportingLine;

/// <summary>
/// S74 / ADR-027 Phase 5 (TASK-7401) Docker-gated integration tests for the vikar engine:
/// <see cref="ManagerVikarRepository"/> CRUD + the extended
/// <see cref="ReportingLineRepository.ResolveDesignatedApproverAsync"/> precedence (R3) +
/// the inclusive "til og med" expiry date semantics (R4a).
///
/// <para>
/// Connects to the running PostgreSQL container (init.sql already applied, so
/// <c>manager_vikar</c> + its partial-unique index exist). Uses dedicated test users
/// (<c>tv_*</c>) and cleans up reporting_lines + manager_vikar + users in DisposeAsync.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManagerVikarEngineTests : IAsyncLifetime
{
    private const string ConnStr =
        "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

    private readonly DbConnectionFactory _factory = new(ConnStr);
    private readonly ManagerVikarRepository _vikarRepo;
    private readonly ReportingLineRepository _rlRepo;
    // Real outbox + audit-projection repo so the audit-persistence tests exercise the
    // ADR-026 D2 sync-in-tx path end-to-end (NOT the NoopOutbox, which can't surface a row).
    private readonly PostgresEventStore _realOutbox;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly ManagerVikarCreatedAuditMapper _createdMapper = new();
    private readonly ManagerVikarEndedAuditMapper _endedMapper = new();

    // tv_emp reports PRIMARY to tv_mgr; tv_vik is the stand-in; tv_admin holds an admin ACTING.
    private const string Emp = "tv_emp";
    private const string Mgr = "tv_mgr";
    private const string Vik = "tv_vik";
    private const string AdminActing = "tv_admin";
    private const string TreeRoot = "STY02";

    public ManagerVikarEngineTests()
    {
        _vikarRepo = new ManagerVikarRepository(_factory);
        _rlRepo = new ReportingLineRepository(_factory, _vikarRepo);
        _realOutbox = new PostgresEventStore(_factory, new OutboxServiceContext("backend-api"));
        _auditRepo = new AuditProjectionRepository(_factory);
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await CleanupAsync(conn);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp,   @emp,   '$2a$11$fake', 'TV Emp',   'tv_emp@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@mgr,   @mgr,   '$2a$11$fake', 'TV Mgr',   'tv_mgr@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@vik,   @vik,   '$2a$11$fake', 'TV Vikar', 'tv_vik@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@admin, @admin, '$2a$11$fake', 'TV Admin', 'tv_admin@test.dk', 'STY02', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("vik", Vik);
        cmd.Parameters.AddWithValue("admin", AdminActing);
        await cmd.ExecuteNonQueryAsync();

        // tv_emp → tv_mgr PRIMARY.
        await _rlRepo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = Emp,
            ManagerId = Mgr,
            OrganisationId = TreeRoot,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await CleanupAsync(conn);
    }

    private static async Task CleanupAsync(NpgsqlConnection conn)
    {
        var ids = new[] { Emp, Mgr, Vik, AdminActing };

        // Audit/outbox cleanup FIRST (before the manager_vikar rows are dropped), so the
        // audit-persistence tests are rerun-safe. ManagerVikar audit rows carry the vikar_id
        // in target_resource_id; scope the delete to vikars owned by the test approvers
        // (covers the SYSTEM-actor EXPIRED rows too, which actor_id alone wouldn't catch).
        await using (var del = new NpgsqlCommand(
            """
            DELETE FROM audit_projection
            WHERE event_type IN ('ManagerVikarCreated', 'ManagerVikarEnded')
              AND (actor_id = ANY(@ids)
                   OR target_resource_id IN (
                       SELECT vikar_id::text FROM manager_vikar
                       WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)))
            """, conn))
        {
            del.Parameters.AddWithValue("ids", ids);
            await del.ExecuteNonQueryAsync();
        }
        await using (var del = new NpgsqlCommand(
            "DELETE FROM outbox_events WHERE stream_id = ANY(@streams)", conn))
        {
            del.Parameters.AddWithValue("streams", ids.Select(id => $"reporting-line-{id}").ToArray());
            await del.ExecuteNonQueryAsync();
        }

        await using (var del = new NpgsqlCommand(
            "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)", conn))
        {
            del.Parameters.AddWithValue("ids", ids);
            await del.ExecuteNonQueryAsync();
        }
        await using (var del = new NpgsqlCommand(
            "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)", conn))
        {
            del.Parameters.AddWithValue("ids", ids);
            await del.ExecuteNonQueryAsync();
        }
        await using (var del = new NpgsqlCommand(
            "DELETE FROM users WHERE user_id = ANY(@ids)", conn))
        {
            del.Parameters.AddWithValue("ids", ids);
            await del.ExecuteNonQueryAsync();
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────

    private async Task<ManagerVikar> CreateVikarAsync(
        string absentApprover, string vikarUser, DateOnly untilDate)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var v = await _vikarRepo.CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "ANDET",
            OrganisationId = TreeRoot,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
        return v;
    }

    private static async Task SetUserActiveAsync(string userId, bool active)
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = @active WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("active", active);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task AssignAdminActingAsync(
        ReportingLineRepository repo, string employeeId, string managerId)
    {
        await repo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = employeeId,
            ManagerId = managerId,
            OrganisationId = TreeRoot,
            Relationship = "ACTING",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ManagerVikarRepository CRUD
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_Then_GetActiveByApprover_CoversAsOf_Inclusive()
    {
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var created = await CreateVikarAsync(Mgr, Vik, until);
        Assert.NotEqual(Guid.Empty, created.VikarId);

        // Covered today and on the inclusive until_date; NOT covered the day after.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAsync(Mgr, today));
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAsync(Mgr, until));        // inclusive
        Assert.Null(await _vikarRepo.GetActiveByApproverAsync(Mgr, until.AddDays(1))); // day after = uncovered
    }

    [Fact]
    public async Task Create_SecondActive_ForSameApprover_Throws_PartialUnique()
    {
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        await CreateVikarAsync(Mgr, Vik, until);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            () => CreateVikarAsync(Mgr, AdminActing, until));
    }

    [Fact]
    public async Task CloseByApprover_ThenNoActiveRow()
    {
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        await CreateVikarAsync(Mgr, Vik, until);

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var closed = await _vikarRepo.CloseByApproverAsync(conn, tx, Mgr, DateOnly.FromDateTime(DateTime.UtcNow));
        await tx.CommitAsync();

        Assert.NotNull(closed);
        Assert.NotNull(closed!.EffectiveTo);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    [Fact]
    public async Task GetActiveByVikarUser_ReverseLookup_FindsRow()
    {
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        await CreateVikarAsync(Mgr, Vik, until);

        var rows = await _vikarRepo.GetActiveByVikarUserAsync(Vik);
        Assert.Single(rows);
        Assert.Equal(Mgr, rows[0].AbsentApproverId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Resolver precedence (R3) — the 4 cases + 2 edge cases
    // ════════════════════════════════════════════════════════════════════════════════

    // Case 3: M active, no vikar → DESIGNATED_MANAGER (M).
    [Fact]
    public async Task Resolve_ActiveManager_NoVikar_ReturnsDesignatedManager()
    {
        var (managerId, method, _) = await _rlRepo.ResolveDesignatedApproverAsync(Emp);
        Assert.Equal(Mgr, managerId);
        Assert.Equal("DESIGNATED_MANAGER", method);
    }

    // Case 2: M active + active vikar V (V active) → V wins, ACTING_MANAGER.
    [Fact]
    public async Task Resolve_ActiveManager_WithActiveVikar_ReturnsVikarAsActing()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));

        var (managerId, method, _) = await _rlRepo.ResolveDesignatedApproverAsync(Emp);
        Assert.Equal(Vik, managerId);
        Assert.Equal("ACTING_MANAGER", method);
    }

    // Case 1: per-report admin ACTING beats the vikar (highest precedence).
    [Fact]
    public async Task Resolve_AdminActing_BeatsVikar()
    {
        await AssignAdminActingAsync(_rlRepo, Emp, AdminActing);
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));

        var (managerId, method, _) = await _rlRepo.ResolveDesignatedApproverAsync(Emp);
        Assert.Equal(AdminActing, managerId);
        Assert.Equal("ACTING_MANAGER", method);
    }

    // Edge (b): vikar's user INACTIVE → vikar SKIPPED, falls through to M-if-active.
    [Fact]
    public async Task Resolve_VikarUserInactive_SkipsVikar_FallsThroughToManager()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));
        await SetUserActiveAsync(Vik, false);

        var (managerId, method, _) = await _rlRepo.ResolveDesignatedApproverAsync(Emp);
        Assert.Equal(Mgr, managerId);                  // M is still active → wins
        Assert.Equal("DESIGNATED_MANAGER", method);
    }

    // Edge (a): M INACTIVE but holds an active vikar V (V active) → V wins over escalation.
    [Fact]
    public async Task Resolve_ManagerInactive_WithActiveVikar_VikarWinsOverEscalation()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));
        await SetUserActiveAsync(Mgr, false);          // manager away/inactive

        var (managerId, method, depth) = await _rlRepo.ResolveDesignatedApproverAsync(Emp);
        Assert.Equal(Vik, managerId);
        Assert.Equal("ACTING_MANAGER", method);
        Assert.Equal(0, depth);                        // fired in the SAME iteration, no walk
    }

    // Case 4 + edge (b) combined: M inactive AND vikar's user inactive → escalation
    // (no usable authority anywhere on M's row → walk up; M has no PRIMARY of its own → null).
    [Fact]
    public async Task Resolve_ManagerInactive_VikarUserInactive_Escalates()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));
        await SetUserActiveAsync(Mgr, false);
        await SetUserActiveAsync(Vik, false);

        var (managerId, method, _) = await _rlRepo.ResolveDesignatedApproverAsync(Emp);
        // Mgr has no PRIMARY line of its own → escalation runs out → org-scope fallback.
        Assert.Null(managerId);
        Assert.Null(method);
    }

    // R3 asOf: a vikar whose until_date is BEFORE asOf is not consulted (resolution returns M).
    [Fact]
    public async Task Resolve_VikarExpiredRelativeToAsOf_NotConsulted()
    {
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        await CreateVikarAsync(Mgr, Vik, until);

        // Resolve as-of a date AFTER the vikar's inclusive until_date.
        var (managerId, method, _) = await _rlRepo.ResolveDesignatedApproverAsync(
            Emp, asOf: until.AddDays(1));
        Assert.Equal(Mgr, managerId);
        Assert.Equal("DESIGNATED_MANAGER", method);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  DelegationExpiryService — R4a inclusive "til og med" boundary
    // ════════════════════════════════════════════════════════════════════════════════

    // A vikar whose until_date IS today is STILL active today (inclusive) — NOT expired.
    // A vikar whose until_date is yesterday IS expired and closes (the day after).
    [Fact]
    public async Task Expiry_InclusiveUntilDate_ClosesYesterdayKeepsToday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        // until_date = today → must SURVIVE the sweep (still covered "til og med" today).
        var stillActive = await CreateVikarAsync(Mgr, Vik, today);
        // until_date = yesterday → must be CLOSED by the sweep.
        var shouldExpire = await CreateVikarAsync(AdminActing, Emp, yesterday);

        // Real outbox + real audit repo (NOT NoopOutbox): the expiry close must persist BOTH
        // the outbox event AND the audit_projection row in-tx (ADR-026 D2). A no-op outbox
        // could never surface the missing audit row this test now also guards.
        var service = new DelegationExpiryService(
            _factory, _realOutbox, _vikarRepo, _auditRepo, _endedMapper,
            NullLogger<DelegationExpiryService>.Instance);
        await service.CloseExpiredDelegationsAsync(CancellationToken.None);

        // today's vikar is still open; yesterday's is closed the day after (= today).
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        var expired = await _vikarRepo.GetActiveByApproverAnyDateAsync(AdminActing);
        Assert.Null(expired); // closed → no active row

        // Sanity: the survivor is the row we expect.
        Assert.Equal(stillActive.VikarId,
            (await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr))!.VikarId);

        // ADR-026 D2: the EXPIRED close emitted an audit_projection row for the closed vikar
        // (TENANT_TARGETED, SYSTEM actor, target_resource_id = the vikar_id).
        var row = await GetVikarAuditRowAsync(shouldExpire.VikarId);
        Assert.NotNull(row);
        Assert.Equal("ManagerVikarEnded", row!.Value.EventType);
        Assert.Equal("TENANT_TARGETED", row.Value.VisibilityScope);
        Assert.Equal(TreeRoot, row.Value.TargetOrgId);
        Assert.Equal("SYSTEM", row.Value.ActorId);
        // The survivor (until_date = today) was NOT closed → no ManagerVikarEnded audit row.
        Assert.Null(await GetVikarAuditRowAsync(stillActive.VikarId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ADR-026 D2 audit-projection persistence — the create / end write sites
    // ════════════════════════════════════════════════════════════════════════════════

    // POST /delegate create path (the in-endpoint trio): EnqueueAndReturnIdAsync + Map +
    // InsertAsync in ONE tx → an audit_projection row lands for the ManagerVikarCreated.
    [Fact]
    public async Task Create_WritesAuditProjectionRow_InTx()
    {
        var created = await CreateVikarWithAuditAsync(Mgr, Vik,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));

        var row = await GetVikarAuditRowAsync(created.VikarId);
        Assert.NotNull(row);
        Assert.Equal("ManagerVikarCreated", row!.Value.EventType);
        Assert.Equal("TENANT_TARGETED", row.Value.VisibilityScope);
        Assert.Equal(TreeRoot, row.Value.TargetOrgId);
        Assert.Equal(created.VikarId.ToString(), row.Value.TargetResourceId);
        Assert.Equal(Mgr, row.Value.ActorId); // the approver is the actor on the create path
    }

    // DELETE /delegate revoke path: CloseByApprover + the ManagerVikarEnded audit trio in
    // ONE tx → an audit_projection row lands for the ManagerVikarEnded (REVOKED).
    [Fact]
    public async Task End_Revoke_WritesAuditProjectionRow_InTx()
    {
        var created = await CreateVikarWithAuditAsync(Mgr, Vik,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));

        await using (var conn = _factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var closed = await _vikarRepo.CloseByApproverAsync(conn, tx, Mgr, today);
            Assert.NotNull(closed);

            var endedEvent = new ManagerVikarEnded
            {
                VikarId = closed!.VikarId,
                AbsentApproverId = closed.AbsentApproverId,
                VikarUserId = closed.VikarUserId,
                UntilDate = closed.UntilDate,
                Reason = closed.Reason,
                OrganisationId = closed.OrganisationId,
                EffectiveTo = closed.EffectiveTo!.Value,
                EndReason = "REVOKED",
                RowVersion = closed.Version,
                ActorId = Mgr,
                ActorRole = "LOCAL_LEADER",
            };
            var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{Mgr}", endedEvent);
            var ctx = new AuditProjectionContext(
                ActorId: endedEvent.ActorId,
                ActorPrimaryOrgId: TreeRoot,
                CorrelationId: endedEvent.CorrelationId,
                OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(endedEvent.OccurredAt, DateTimeKind.Utc)),
                ResolvedTargetOrgId: endedEvent.OrganisationId);
            var rowData = _endedMapper.Map(endedEvent, ctx);
            await _auditRepo.InsertAsync(conn, tx, endedEvent.EventId, outboxId, endedEvent.EventType, rowData, ctx);
            await tx.CommitAsync();
        }

        var row = await GetVikarAuditRowAsync(created.VikarId);
        Assert.NotNull(row);
        Assert.Equal("ManagerVikarEnded", row!.Value.EventType);
        Assert.Equal("TENANT_TARGETED", row.Value.VisibilityScope);
        Assert.Equal(TreeRoot, row.Value.TargetOrgId);
    }

    // Atomicity (ADR-018 D3): if the tx ROLLS BACK after enqueue + audit insert, NEITHER the
    // manager_vikar state, NOR the outbox event, NOR the audit_projection row survives.
    [Fact]
    public async Task Create_ForcedRollback_LeavesNoEvent_NoAudit_NoState()
    {
        var vikarId = Guid.NewGuid();
        await using (var conn = _factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

            var created = await _vikarRepo.CreateAsync(conn, tx, new ManagerVikar
            {
                VikarId = vikarId,
                AbsentApproverId = Mgr,
                VikarUserId = Vik,
                UntilDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
                Reason = "ANDET",
                OrganisationId = TreeRoot,
                Version = 1,
                CreatedBy = "TEST",
            });

            var createdEvent = new ManagerVikarCreated
            {
                VikarId = created.VikarId,
                AbsentApproverId = created.AbsentApproverId,
                VikarUserId = created.VikarUserId,
                UntilDate = created.UntilDate,
                Reason = created.Reason,
                OrganisationId = created.OrganisationId,
                RowVersion = created.Version,
                ActorId = Mgr,
                ActorRole = "LOCAL_LEADER",
            };
            var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{Mgr}", createdEvent);
            var ctx = new AuditProjectionContext(
                ActorId: createdEvent.ActorId,
                ActorPrimaryOrgId: TreeRoot,
                CorrelationId: createdEvent.CorrelationId,
                OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(createdEvent.OccurredAt, DateTimeKind.Utc)),
                ResolvedTargetOrgId: createdEvent.OrganisationId);
            var rowData = _createdMapper.Map(createdEvent, ctx);
            await _auditRepo.InsertAsync(conn, tx, createdEvent.EventId, outboxId, createdEvent.EventType, rowData, ctx);

            await tx.RollbackAsync(); // FORCED ROLLBACK — nothing must survive
        }

        // State: no manager_vikar row.
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        // Audit: no audit_projection row for the vikar.
        Assert.Null(await GetVikarAuditRowAsync(vikarId));
        // Event: no outbox_events row for the rolled-back create — the "NoEvent" the test
        // name claims. The enqueue (EnqueueAndReturnIdAsync), the audit InsertAsync, and the
        // state CreateAsync all share ONE tx (ADR-018 D3), so the forced rollback must discard
        // the outbox event TOO, not only the state + audit. (Was unasserted — the test passed
        // green while never proving NoEvent; Step-5a c2 catch.)
        Assert.Equal(0, await GetVikarOutboxCountAsync(vikarId));
    }

    // ── audit-test helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a vikar AND its ManagerVikarCreated audit row in ONE tx — the in-endpoint
    /// ADR-026 D2 trio, mirrored for the integration test (the endpoint lambda itself can't
    /// be invoked here without the full HTTP host).
    /// </summary>
    private async Task<ManagerVikar> CreateVikarWithAuditAsync(
        string absentApprover, string vikarUser, DateOnly untilDate)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var created = await _vikarRepo.CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "ANDET",
            OrganisationId = TreeRoot,
            Version = 1,
            CreatedBy = "TEST",
        });
        var createdEvent = new ManagerVikarCreated
        {
            VikarId = created.VikarId,
            AbsentApproverId = created.AbsentApproverId,
            VikarUserId = created.VikarUserId,
            UntilDate = created.UntilDate,
            Reason = created.Reason,
            OrganisationId = created.OrganisationId,
            RowVersion = created.Version,
            ActorId = absentApprover,
            ActorRole = "LOCAL_LEADER",
        };
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{absentApprover}", createdEvent);
        var ctx = new AuditProjectionContext(
            ActorId: createdEvent.ActorId,
            ActorPrimaryOrgId: TreeRoot,
            CorrelationId: createdEvent.CorrelationId,
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(createdEvent.OccurredAt, DateTimeKind.Utc)),
            ResolvedTargetOrgId: createdEvent.OrganisationId);
        var rowData = _createdMapper.Map(createdEvent, ctx);
        await _auditRepo.InsertAsync(conn, tx, createdEvent.EventId, outboxId, createdEvent.EventType, rowData, ctx);
        await tx.CommitAsync();
        return created;
    }

    private async Task<AuditRowProbe?> GetVikarAuditRowAsync(Guid vikarId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_type, visibility_scope, target_org_id, target_resource_id, actor_id
            FROM audit_projection
            WHERE target_resource_id = @vikarId
            ORDER BY occurred_at DESC, projection_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("vikarId", vikarId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new AuditRowProbe(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>
    /// Counts outbox_events rows for a specific vikar's create event. The vikarId is a fresh
    /// unique Guid per test, so a payload-text match is precise and collision-free. Used by the
    /// forced-rollback atomicity test to prove the outbox event did NOT survive the rollback.
    /// </summary>
    private async Task<int> GetVikarOutboxCountAsync(Guid vikarId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE event_type = 'ManagerVikarCreated'
              AND event_payload::text LIKE '%' || @vikarId || '%'
            """, conn);
        cmd.Parameters.AddWithValue("vikarId", vikarId.ToString());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private readonly record struct AuditRowProbe(
        string EventType, string VisibilityScope, string? TargetOrgId, string? TargetResourceId, string? ActorId);
}
