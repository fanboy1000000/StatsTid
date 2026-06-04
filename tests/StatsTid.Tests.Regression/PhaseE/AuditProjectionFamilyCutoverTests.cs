using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Audit;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S44b — Phase E D-tests for the 4-family cutover:
/// AgreementConfig (GLOBAL_TENANT_VISIBLE), Period (TENANT_TARGETED),
/// Overtime (TENANT_TARGETED), UserAgreementCodeChanged (TENANT_TARGETED).
/// 9 tests: 4 happy-path (one per family), 4 forced-rollback (one per family),
/// 1 dual-emit (publish-with-supersession).
/// Direct-orchestration shape mirroring <see cref="AuditProjectionCutoverTests"/>.
/// Each test instantiates real mappers + real AuditProjectionRepository
/// against the DockerHarness factory; only IOutboxEnqueue is the mocked seam
/// in forced-rollback tests.
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionFamilyCutoverTests : IAsyncLifetime
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

        // Seed the org FK target for the TENANT_TARGETED audit rows. The happy-path
        // tests write audit_projection.target_org_id = TestOrgId; without this row
        // InsertAsync hit 23503 (audit_projection_target_org_id_fkey). Mirrors the
        // per-class seed pattern in AuditProjectionSyncInTxTests/CatalogCloseTests.
        await using (var seedConn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await seedConn.OpenAsync();
            await using var seedCmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@id, 'S44B Test Org', 'STYRELSE', @path)
                  ON CONFLICT DO NOTHING", seedConn);
            seedCmd.Parameters.AddWithValue("id", TestOrgId);
            seedCmd.Parameters.AddWithValue("path", $"/{TestOrgId}/");
            await seedCmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton(_harness.Factory);
        services.AddSingleton<AuditProjectionRepository>();
        services.AddSingleton<IAuditProjectionMapper<AgreementConfigCreated>, AgreementConfigCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<AgreementConfigPublished>, AgreementConfigPublishedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<AgreementConfigArchived>, AgreementConfigArchivedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<PeriodSubmitted>, PeriodSubmittedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<OvertimePreApprovalCreated>, OvertimePreApprovalCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<UserAgreementCodeChanged>, UserAgreementCodeChangedAuditMapper>();
        _sp = services.BuildServiceProvider();
        _auditRepo = _sp.GetRequiredService<AuditProjectionRepository>();
        _realOutbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _harness.DisposeAsync();
    }

    private const string TestOrgId = "ORG_S44B_TEST";

    // ===================================================================
    // Happy-path tests — one per family
    // ===================================================================

    [Fact]
    public async Task AgreementConfigCreated_AuditProjectionRowLands_GlobalTenantVisible()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<AgreementConfigCreated>>();
        var ev = new AgreementConfigCreated { ConfigId = Guid.NewGuid(), AgreementCode = "AC", OkVersion = "OK24" };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{ev.ConfigId}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("GLOBAL_TENANT_VISIBLE", row!.VisibilityScope);
        Assert.Null(row.TargetOrgId);
        Assert.Equal(ev.ConfigId.ToString(), row.TargetResourceId);
        Assert.Equal("AgreementConfigCreated", row.EventType);
    }

    [Fact]
    public async Task PeriodSubmitted_AuditProjectionRowLands_TenantTargeted()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<PeriodSubmitted>>();
        var ev = new PeriodSubmitted
        {
            PeriodId = Guid.NewGuid(), EmployeeId = "EMP_TEST", OrgId = TestOrgId,
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            PeriodType = "MONTHLY",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"approval-{ev.EmployeeId}-{ev.PeriodStart:yyyy-MM-dd}", ev);
        var ctx = MakeCtx(ev.OccurredAt, resolvedTargetOrgId: TestOrgId);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("TENANT_TARGETED", row!.VisibilityScope);
        Assert.Equal(TestOrgId, row.TargetOrgId);
        Assert.Equal(ev.PeriodId.ToString(), row.TargetResourceId);
        Assert.Equal("PeriodSubmitted", row.EventType);
    }

    [Fact]
    public async Task OvertimePreApprovalCreated_AuditProjectionRowLands_TenantTargeted()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<OvertimePreApprovalCreated>>();
        var ev = new OvertimePreApprovalCreated
        {
            EmployeeId = "EMP_TEST",
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            MaxHours = 10m, Status = "PENDING",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"overtime-preapproval-{ev.EventId}", ev);
        var ctx = MakeCtx(ev.OccurredAt, resolvedTargetOrgId: TestOrgId);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("TENANT_TARGETED", row!.VisibilityScope);
        Assert.Equal(TestOrgId, row.TargetOrgId);
        Assert.Equal(ev.EmployeeId, row.TargetResourceId);
        Assert.Equal("OvertimePreApprovalCreated", row.EventType);
    }

    [Fact]
    public async Task UserAgreementCodeChanged_AuditProjectionRowLands_TenantTargeted()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<UserAgreementCodeChanged>>();
        var ev = new UserAgreementCodeChanged
        {
            UserId = "USR_TEST", OldAgreementCode = "AC", NewAgreementCode = "HK",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"user-{ev.UserId}", ev);
        var ctx = MakeCtx(ev.OccurredAt, resolvedTargetOrgId: TestOrgId);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("TENANT_TARGETED", row!.VisibilityScope);
        Assert.Equal(TestOrgId, row.TargetOrgId);
        Assert.Equal(ev.UserId, row.TargetResourceId);
        Assert.Equal("UserAgreementCodeChanged", row.EventType);
    }

    // ===================================================================
    // Forced-rollback tests — one per family
    // ===================================================================

    [Fact]
    public async Task AgreementConfigCreated_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new AgreementConfigCreated { ConfigId = Guid.NewGuid(), AgreementCode = "AC", OkVersion = "OK24" };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{ev.ConfigId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task PeriodSubmitted_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new PeriodSubmitted
        {
            PeriodId = Guid.NewGuid(), EmployeeId = "EMP_TEST", OrgId = TestOrgId,
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            PeriodType = "MONTHLY",
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"approval-{ev.EmployeeId}-{ev.PeriodStart:yyyy-MM-dd}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task OvertimePreApprovalCreated_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new OvertimePreApprovalCreated
        {
            EmployeeId = "EMP_TEST",
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            MaxHours = 10m, Status = "PENDING",
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"overtime-preapproval-{ev.EventId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task UserAgreementCodeChanged_OutboxFails_NoAuditProjectionRow()
    {
        var ev = new UserAgreementCodeChanged
        {
            UserId = "USR_TEST", OldAgreementCode = "AC", NewAgreementCode = "HK",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
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

    // ===================================================================
    // Dual-emit test — publish-with-supersession
    // ===================================================================

    [Fact]
    public async Task AgreementConfigPublished_DualEmit_BothAuditRowsLand()
    {
        var publishMapper = _sp.GetRequiredService<IAuditProjectionMapper<AgreementConfigPublished>>();
        var archivedMapper = _sp.GetRequiredService<IAuditProjectionMapper<AgreementConfigArchived>>();

        var archivedId = Guid.NewGuid();
        var publishedEv = new AgreementConfigPublished
        {
            ConfigId = Guid.NewGuid(), AgreementCode = "AC", OkVersion = "OK24",
            ArchivedConfigId = archivedId,
        };
        var archivedEv = new AgreementConfigArchived
        {
            ConfigId = archivedId, AgreementCode = "AC", OkVersion = "OK24",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Emit published event + audit row
        var outboxId1 = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{publishedEv.ConfigId}", publishedEv);
        var ctx1 = MakeCtx(publishedEv.OccurredAt);
        var rowData1 = publishMapper.Map(publishedEv, ctx1);
        await _auditRepo.InsertAsync(conn, tx, publishedEv.EventId, outboxId1, publishedEv.EventType, rowData1, ctx1);

        // Emit archived event + audit row (superseded config) in same tx
        var outboxId2 = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{archivedId}", archivedEv);
        var ctx2 = MakeCtx(archivedEv.OccurredAt);
        var rowData2 = archivedMapper.Map(archivedEv, ctx2);
        await _auditRepo.InsertAsync(conn, tx, archivedEv.EventId, outboxId2, archivedEv.EventType, rowData2, ctx2);

        await tx.CommitAsync();

        // Assert BOTH audit_projection rows exist
        var publishedRow = await ReadAuditRowAsync(publishedEv.EventId);
        Assert.NotNull(publishedRow);
        Assert.Equal("GLOBAL_TENANT_VISIBLE", publishedRow!.VisibilityScope);
        Assert.Null(publishedRow.TargetOrgId);
        Assert.Equal(publishedEv.ConfigId.ToString(), publishedRow.TargetResourceId);
        Assert.Equal("AgreementConfigPublished", publishedRow.EventType);

        var archivedRow = await ReadAuditRowAsync(archivedEv.EventId);
        Assert.NotNull(archivedRow);
        Assert.Equal("GLOBAL_TENANT_VISIBLE", archivedRow!.VisibilityScope);
        Assert.Null(archivedRow.TargetOrgId);
        Assert.Equal(archivedId.ToString(), archivedRow.TargetResourceId);
        Assert.Equal("AgreementConfigArchived", archivedRow.EventType);
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private AuditProjectionContext MakeCtx(DateTime occurredAt, string? resolvedTargetOrgId = null) => new(
        ActorId: "tester",
        ActorPrimaryOrgId: TestOrgId,
        CorrelationId: Guid.NewGuid(),
        OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)),
        ResolvedTargetOrgId: resolvedTargetOrgId);

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
}
