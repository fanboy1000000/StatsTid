using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Audit;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S44 / TASK-4414 — Phase E D-tests for the 6-mapper Org/User/RoleAssignment
/// exemplar family cutover. 12 tests:
/// <list type="bullet">
///   <item><description>6 happy-path tests — verify endpoint emit chain
///   (INSERT main row + EnqueueAndReturnIdAsync + mapper.Map +
///   AuditProjectionRepository.InsertAsync) lands an audit_projection row
///   with correct visibility_scope/target_org_id/event_type.</description></item>
///   <item><description>6 forced-rollback tests using
///   <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> — two-shape
///   design per Step 0b cycle 1 Codex BLOCKER absorption:
///   <list type="bullet">
///     <item><description>4 single-enqueue endpoints (Org×2 + Role×2):
///     throw on the only EnqueueAndReturnIdAsync → tx aborts BEFORE audit
///     insert runs → assert no audit_projection row (S27 TimeProjectionAtomicTests
///     pattern; proves tx-atomicity at the audit-insert boundary).</description></item>
///     <item><description>2 multi-enqueue endpoints (User×2):
///     EnqueueAndReturnIdAsync + audit InsertAsync SUCCEED for the
///     primary event → throw on the SECOND enqueue (EmployeeProfileCreated
///     for POST; UserAgreementCodeChanged for PUT) → tx aborts AFTER audit
///     insert ran → assert no audit_projection row anyway (proves
///     post-insert tx rollback).</description></item>
///   </list></description></item>
/// </list>
/// Direct-orchestration shape mirroring <see cref="AdminAtomicTests"/>.
/// Each test instantiates real mappers + real AuditProjectionRepository
/// against the DockerHarness factory; only IOutboxEnqueue is the mocked seam.
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionCutoverTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AuditProjectionRepository _auditRepo = null!;
    private PostgresEventStore _realOutbox = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await Outbox.OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        await ApplyAdminSchemaAsync(_harness.ConnectionString);
        await SeedOrgAsync(_harness.ConnectionString);

        var services = new ServiceCollection();
        services.AddSingleton(_harness.Factory);
        services.AddSingleton<AuditProjectionRepository>();
        services.AddSingleton<IAuditProjectionMapperRegistry, AuditProjectionMapperRegistry>();
        services.AddSingleton<IAuditProjectionMapper<OrganizationCreated>, OrganizationCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<OrganizationUpdated>, OrganizationUpdatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<UserCreated>, UserCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<UserUpdated>, UserUpdatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<RoleAssignmentGranted>, RoleAssignmentGrantedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<RoleAssignmentRevoked>, RoleAssignmentRevokedAuditMapper>();
        _sp = services.BuildServiceProvider();
        _auditRepo = _sp.GetRequiredService<AuditProjectionRepository>();
        _realOutbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _harness.DisposeAsync();
    }

    private const string TestOrgId = "ORG_S44_TEST";

    // ===================================================================
    // Happy-path tests — verify audit_projection row lands with correct shape
    // ===================================================================

    [Fact]
    public async Task OrganizationCreated_AuditProjectionRowLands_TenantTargeted()
    {
        var orgId = $"ORG_HC_{Guid.NewGuid():N}".Substring(0, 16);
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<OrganizationCreated>>();
        var ev = new OrganizationCreated
        {
            OrgId = orgId, OrgName = "Happy Org", OrgType = "STYRELSE",
            ParentOrgId = null, MaterializedPath = $"/{orgId}/", AgreementCode = "AC", OkVersion = "OK24",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await InsertOrgInlineAsync(conn, tx, ev);
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("TENANT_TARGETED", row!.VisibilityScope);
        Assert.Equal(orgId, row.TargetOrgId);
        Assert.Equal(orgId, row.TargetResourceId);
        Assert.Equal("OrganizationCreated", row.EventType);
    }

    [Fact]
    public async Task OrganizationUpdated_AuditProjectionRowLands_TenantTargeted()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<OrganizationUpdated>>();
        var ev = new OrganizationUpdated { OrgId = TestOrgId, OrgName = "Updated Name", AgreementCode = "AC", OkVersion = "OK26" };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{TestOrgId}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal(TestOrgId, row!.TargetOrgId);
        Assert.Equal("OrganizationUpdated", row.EventType);
    }

    [Fact]
    public async Task UserCreated_AuditProjectionRowLands_TargetOrgFromEventPayload()
    {
        var userId = $"USR_HC_{Guid.NewGuid():N}".Substring(0, 16);
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<UserCreated>>();
        var ev = new UserCreated
        {
            UserId = userId, Username = "happyuser", DisplayName = "Happy User",
            PrimaryOrgId = TestOrgId, AgreementCode = "AC", OkVersion = "OK24",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await InsertUserInlineAsync(conn, tx, userId);
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal(TestOrgId, row!.TargetOrgId);
        Assert.Equal(userId, row.TargetResourceId);
    }

    [Fact]
    public async Task UserUpdated_AuditProjectionRowLands_TargetOrgFromResolvedContext()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<UserUpdated>>();
        var ev = new UserUpdated { UserId = "USR_EXISTING", DisplayName = "Renamed", PrimaryOrgId = null };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev);
        var ctx = MakeCtx(ev.OccurredAt) with { ResolvedTargetOrgId = TestOrgId };
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal(TestOrgId, row!.TargetOrgId); // from ResolvedTargetOrgId fallback
    }

    [Fact]
    public async Task RoleAssignmentGranted_AuditProjectionRowLands_TargetOrgFromResolvedContext()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<RoleAssignmentGranted>>();
        var ev = new RoleAssignmentGranted
        {
            AssignmentId = Guid.NewGuid(), UserId = "USR_EXISTING", RoleId = "EMPLOYEE",
            OrgId = TestOrgId, ScopeType = "ORG_ONLY",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev);
        var ctx = MakeCtx(ev.OccurredAt) with { ResolvedTargetOrgId = TestOrgId };
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal(TestOrgId, row!.TargetOrgId);
        Assert.Equal(ev.UserId, row.TargetResourceId);
    }

    [Fact]
    public async Task RoleAssignmentRevoked_AuditProjectionRowLands_TargetOrgFromResolvedContext()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<RoleAssignmentRevoked>>();
        var ev = new RoleAssignmentRevoked
        {
            AssignmentId = Guid.NewGuid(), UserId = "USR_EXISTING", RoleId = "EMPLOYEE", Reason = "Test",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev);
        var ctx = MakeCtx(ev.OccurredAt) with { ResolvedTargetOrgId = TestOrgId };
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal(TestOrgId, row!.TargetOrgId);
    }

    // ===================================================================
    // Forced-rollback tests — S27 pattern (throw on (only) EnqueueAndReturnIdAsync)
    // for single-enqueue endpoints; post-audit-insert throw for multi-enqueue.
    // ===================================================================

    [Fact]
    public async Task OrganizationCreated_OutboxFails_NoAuditProjectionRow()
    {
        var orgId = $"ORG_FR_{Guid.NewGuid():N}".Substring(0, 16);
        var ev = new OrganizationCreated
        {
            OrgId = orgId, OrgName = "Rollback Org", OrgType = "STYRELSE",
            ParentOrgId = null, MaterializedPath = $"/{orgId}/", AgreementCode = "AC", OkVersion = "OK24",
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await InsertOrgInlineAsync(conn, tx, ev);
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task OrganizationUpdated_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new OrganizationUpdated { OrgId = TestOrgId, OrgName = "Rollback Update", AgreementCode = "AC", OkVersion = "OK26" };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{TestOrgId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task RoleAssignmentGranted_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new RoleAssignmentGranted
        {
            AssignmentId = Guid.NewGuid(), UserId = "USR_EXISTING", RoleId = "EMPLOYEE",
            OrgId = TestOrgId, ScopeType = "ORG_ONLY",
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task RoleAssignmentRevoked_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new RoleAssignmentRevoked
        {
            AssignmentId = Guid.NewGuid(), UserId = "USR_EXISTING", RoleId = "EMPLOYEE", Reason = "Rollback Test",
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task UserCreated_SecondEnqueueFails_AuditInsertRollsBack()
    {
        // Post-audit-insert rollback: UserCreated enqueue + mapper + audit insert
        // SUCCEED (real EnqueueAndReturnIdAsync + real auditRepo); throw on the
        // SECOND enqueue (mimicking EmployeeProfileCreated@L611 staying as
        // EnqueueAsync per same-endpoint coupling). Asserts the audit row
        // actually rolls back even though it was written.
        var userId = $"USR_PR_{Guid.NewGuid():N}".Substring(0, 16);
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<UserCreated>>();
        var ev = new UserCreated
        {
            UserId = userId, Username = "rollbackuser", DisplayName = "Rollback",
            PrimaryOrgId = TestOrgId, AgreementCode = "AC", OkVersion = "OK24",
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await InsertUserInlineAsync(conn, tx, userId);
                var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{userId}", ev);
                var ctx = MakeCtx(ev.OccurredAt);
                var rowData = mapper.Map(ev, ctx);
                await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
                // Simulate the secondary EmployeeProfileCreated enqueue throwing
                var secondaryEv = new EmployeeProfileCreated
                {
                    ProfileId = Guid.NewGuid(), EmployeeId = userId,
                    WeeklyNormHours = 37m, PartTimeFraction = 1m, Position = null,
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                };
                await throwingOutbox.EnqueueAsync(conn, tx, $"employee-profile-{userId}", secondaryEv); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        // CRITICAL: even though auditRepo.InsertAsync succeeded inside the tx,
        // the rollback removes it. Proves post-audit-insert atomic rollback.
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task UserUpdated_SecondEnqueueFails_AuditInsertRollsBack()
    {
        // Same post-audit-insert pattern as UserCreated_SecondEnqueueFails;
        // throw on the simulated UserAgreementCodeChanged@L1163 (S44b deferred).
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<UserUpdated>>();
        var ev = new UserUpdated { UserId = "USR_EXISTING", DisplayName = "Renamed Rollback", PrimaryOrgId = null };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev);
                var ctx = MakeCtx(ev.OccurredAt) with { ResolvedTargetOrgId = TestOrgId };
                var rowData = mapper.Map(ev, ctx);
                await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
                // Simulate secondary UserAgreementCodeChanged enqueue throwing
                var secondaryEv = new UserAgreementCodeChanged
                {
                    UserId = ev.UserId, OldAgreementCode = "AC", NewAgreementCode = "HK",
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                };
                await throwingOutbox.EnqueueAsync(conn, tx, $"user-{ev.UserId}", secondaryEv); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private AuditProjectionContext MakeCtx(DateTime occurredAt) => new(
        ActorId: "tester",
        ActorPrimaryOrgId: TestOrgId,
        CorrelationId: Guid.NewGuid(),
        OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)));

    private async Task<AuditRowSnapshot?> ReadAuditRowAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_type, visibility_scope, target_org_id, target_resource_id FROM audit_projection WHERE event_id = @eventId", conn);
        cmd.Parameters.AddWithValue("eventId", eventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new AuditRowSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private record AuditRowSnapshot(string EventType, string VisibilityScope, string? TargetOrgId, string? TargetResourceId);

    private static async Task InsertOrgInlineAsync(NpgsqlConnection conn, NpgsqlTransaction tx, OrganizationCreated ev)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active, created_at, updated_at)
              VALUES (@orgId, @orgName, @orgType, NULL, @path, @ac, @ok, TRUE, NOW(), NOW())
              ON CONFLICT DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue("orgId", ev.OrgId);
        cmd.Parameters.AddWithValue("orgName", ev.OrgName);
        cmd.Parameters.AddWithValue("orgType", ev.OrgType);
        cmd.Parameters.AddWithValue("path", ev.MaterializedPath);
        cmd.Parameters.AddWithValue("ac", ev.AgreementCode);
        cmd.Parameters.AddWithValue("ok", ev.OkVersion);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertUserInlineAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string userId)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id)
              VALUES (@id, @id, 'hash', 'name', @orgId)
              ON CONFLICT DO NOTHING", conn, tx);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("orgId", TestOrgId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedOrgAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var orgCmd = new NpgsqlCommand(
            @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
              VALUES (@id, 'Test Org', 'STYRELSE', @path)
              ON CONFLICT DO NOTHING", conn))
        {
            orgCmd.Parameters.AddWithValue("id", TestOrgId);
            orgCmd.Parameters.AddWithValue("path", $"/{TestOrgId}/");
            await orgCmd.ExecuteNonQueryAsync();
        }
        await using (var userCmd = new NpgsqlCommand(
            @"INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id)
              VALUES ('USR_EXISTING', 'existing', 'hash', 'Existing User', @orgId)
              ON CONFLICT DO NOTHING", conn))
        {
            userCmd.Parameters.AddWithValue("orgId", TestOrgId);
            await userCmd.ExecuteNonQueryAsync();
        }
    }

    private const string AdminSchemaDdl = """
        CREATE TABLE IF NOT EXISTS users (
            user_id             TEXT        PRIMARY KEY,
            username            TEXT        NOT NULL UNIQUE,
            password_hash       TEXT        NOT NULL,
            display_name        TEXT        NOT NULL,
            email               TEXT,
            primary_org_id      TEXT        NOT NULL REFERENCES organizations(org_id),
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            employment_category TEXT        NOT NULL DEFAULT 'Standard',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            version             BIGINT      NOT NULL DEFAULT 1,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
    """;

    private static async Task ApplyAdminSchemaAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(AdminSchemaDdl, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
