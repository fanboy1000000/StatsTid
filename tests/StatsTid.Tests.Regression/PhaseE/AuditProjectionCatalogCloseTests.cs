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
/// S44c — Phase E D-tests for the 6-family catalog close:
/// PositionOverride (GLOBAL_TENANT_VISIBLE), WageTypeMapping (GLOBAL_TENANT_VISIBLE),
/// EntitlementConfig (GLOBAL_TENANT_VISIBLE), EmployeeProfile (TENANT_TARGETED),
/// LocalAgreementProfileChanged (TENANT_TARGETED), RoleConfigOverride (GLOBAL_TENANT_VISIBLE, mapper-only).
/// 10 tests: 5 happy-path (one per endpoint-active family), 4 forced-rollback
/// (one per endpoint-active family), 1 mapper-only (RoleConfigOverride — no emit site).
/// Direct-orchestration shape mirroring <see cref="AuditProjectionFamilyCutoverTests"/>.
/// Each test instantiates real mappers + real AuditProjectionRepository
/// against the DockerHarness factory; only IOutboxEnqueue is the mocked seam
/// in forced-rollback tests.
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionCatalogCloseTests : IAsyncLifetime
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

        var services = new ServiceCollection();
        services.AddSingleton(_harness.Factory);
        services.AddSingleton<AuditProjectionRepository>();
        services.AddSingleton<IAuditProjectionMapper<PositionOverrideCreated>, PositionOverrideCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<WageTypeMappingCreated>, WageTypeMappingCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<EntitlementConfigCreated>, EntitlementConfigCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<EmployeeProfileCreated>, EmployeeProfileCreatedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<LocalAgreementProfileChanged>, LocalAgreementProfileChangedAuditMapper>();
        services.AddSingleton<IAuditProjectionMapper<RoleConfigOverrideCreated>, RoleConfigOverrideCreatedAuditMapper>();
        _sp = services.BuildServiceProvider();
        _auditRepo = _sp.GetRequiredService<AuditProjectionRepository>();
        _realOutbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _harness.DisposeAsync();
    }

    private const string TestOrgId = "ORG_S44C_TEST";

    // ===================================================================
    // Happy-path tests — one per endpoint-active family
    // ===================================================================

    [Fact]
    public async Task PositionOverrideCreated_AuditRowLands_GlobalTenantVisible()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<PositionOverrideCreated>>();
        var ev = new PositionOverrideCreated { OverrideId = Guid.NewGuid(), AgreementCode = "AC", OkVersion = "OK24", PositionCode = "CHEF" };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"position-override-{ev.OverrideId}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("GLOBAL_TENANT_VISIBLE", row!.VisibilityScope);
        Assert.Null(row.TargetOrgId);
        Assert.Equal(ev.OverrideId.ToString(), row.TargetResourceId);
        Assert.Equal("PositionOverrideCreated", row.EventType);
    }

    [Fact]
    public async Task WageTypeMappingCreated_AuditRowLands_GlobalTenantVisible()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<WageTypeMappingCreated>>();
        var ev = new WageTypeMappingCreated { TimeType = "NORMAL", WageType = "SLS_0100", AgreementCode = "AC", OkVersion = "OK24", Position = "" };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"wage-type-mapping-{ev.TimeType}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("GLOBAL_TENANT_VISIBLE", row!.VisibilityScope);
        Assert.Null(row.TargetOrgId);
        Assert.Equal($"{ev.TimeType}:{ev.AgreementCode}:{ev.OkVersion}:{ev.Position}", row.TargetResourceId);
        Assert.Equal("WageTypeMappingCreated", row.EventType);
    }

    [Fact]
    public async Task EntitlementConfigCreated_AuditRowLands_GlobalTenantVisible()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<EntitlementConfigCreated>>();
        var ev = new EntitlementConfigCreated
        {
            ConfigId = Guid.NewGuid(), EntitlementType = "VACATION", AgreementCode = "AC", OkVersion = "OK24",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), RowVersion = 1,
            AnnualQuota = 25m, AccrualModel = "MONTHLY", ResetMonth = 1,
            CarryoverMax = 5m, ProRateByPartTime = true, IsPerEpisode = false,
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"entitlement-config-{ev.ConfigId}", ev);
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("GLOBAL_TENANT_VISIBLE", row!.VisibilityScope);
        Assert.Null(row.TargetOrgId);
        Assert.Equal(ev.ConfigId.ToString(), row.TargetResourceId);
        Assert.Equal("EntitlementConfigCreated", row.EventType);
    }

    [Fact]
    public async Task EmployeeProfileCreated_AuditRowLands_TenantTargeted()
    {
        // Seed org row for FK satisfaction on TENANT_TARGETED rows
        await SeedOrgRowAsync(TestOrgId);

        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<EmployeeProfileCreated>>();
        var ev = new EmployeeProfileCreated
        {
            ProfileId = Guid.NewGuid(), EmployeeId = "EMP_TEST",
            WeeklyNormHours = 37m, PartTimeFraction = 1m,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-profile-{ev.EmployeeId}", ev);
        var ctx = MakeCtx(ev.OccurredAt, resolvedTargetOrgId: TestOrgId);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("TENANT_TARGETED", row!.VisibilityScope);
        Assert.Equal(TestOrgId, row.TargetOrgId);
        Assert.Equal(ev.EmployeeId, row.TargetResourceId);
        Assert.Equal("EmployeeProfileCreated", row.EventType);
    }

    [Fact]
    public async Task LocalAgreementProfileChanged_AuditRowLands_TenantTargeted()
    {
        // Seed org row for FK satisfaction on TENANT_TARGETED rows
        await SeedOrgRowAsync(TestOrgId);

        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<LocalAgreementProfileChanged>>();
        var ev = new LocalAgreementProfileChanged
        {
            ProfileId = Guid.NewGuid(), OrgId = TestOrgId,
            AgreementCode = "AC", OkVersion = "OK24",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"local-agreement-profile-{ev.ProfileId}", ev);
        var ctx = MakeCtx(ev.OccurredAt, resolvedTargetOrgId: ev.OrgId);
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        var row = await ReadAuditRowAsync(ev.EventId);
        Assert.NotNull(row);
        Assert.Equal("TENANT_TARGETED", row!.VisibilityScope);
        Assert.Equal(TestOrgId, row.TargetOrgId);
        Assert.Equal(ev.ProfileId.ToString(), row.TargetResourceId);
        Assert.Equal("LocalAgreementProfileChanged", row.EventType);
    }

    // ===================================================================
    // Forced-rollback tests — one per endpoint-active family
    // ===================================================================

    [Fact]
    public async Task PositionOverrideCreated_OutboxFails_NoAuditRow()
    {
        var ev = new PositionOverrideCreated { OverrideId = Guid.NewGuid(), AgreementCode = "AC", OkVersion = "OK24", PositionCode = "CHEF" };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"position-override-{ev.OverrideId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task WageTypeMappingCreated_OutboxFails_NoAuditRow()
    {
        var ev = new WageTypeMappingCreated { TimeType = "NORMAL", WageType = "SLS_0100", AgreementCode = "AC", OkVersion = "OK24", Position = "" };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"wage-type-mapping-{ev.TimeType}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task EntitlementConfigCreated_OutboxFails_NoAuditRow()
    {
        var ev = new EntitlementConfigCreated
        {
            ConfigId = Guid.NewGuid(), EntitlementType = "VACATION", AgreementCode = "AC", OkVersion = "OK24",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), RowVersion = 1,
            AnnualQuota = 25m, AccrualModel = "MONTHLY", ResetMonth = 1,
            CarryoverMax = 5m, ProRateByPartTime = true, IsPerEpisode = false,
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"entitlement-config-{ev.ConfigId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    [Fact]
    public async Task EmployeeProfileCreated_OutboxFails_NoAuditRow()
    {
        var ev = new EmployeeProfileCreated
        {
            ProfileId = Guid.NewGuid(), EmployeeId = "EMP_TEST",
            WeeklyNormHours = 37m, PartTimeFraction = 1m,
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
                await throwingOutbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-profile-{ev.EmployeeId}", ev); // THROWS
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }

    // ===================================================================
    // Mapper-only test — verify mapper shape for events without emit sites
    // ===================================================================

    [Fact]
    public void RoleConfigOverrideCreated_MapperReturnsCorrectShape()
    {
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<RoleConfigOverrideCreated>>();
        var ev = new RoleConfigOverrideCreated
        {
            OverrideId = Guid.NewGuid(), EmploymentCategory = "Standard",
            AgreementCode = "AC", OkVersion = "OK24",
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var ctx = MakeCtx(ev.OccurredAt);
        var rowData = mapper.Map(ev, ctx);

        Assert.Equal(AuditVisibilityScope.GlobalTenantVisible, rowData.VisibilityScope);
        Assert.Null(rowData.TargetOrgId);
        Assert.Equal($"{ev.EmploymentCategory}:{ev.AgreementCode}:{ev.OkVersion}", rowData.TargetResourceId);
        Assert.False(string.IsNullOrEmpty(rowData.DetailsJson));
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

    /// <summary>
    /// Seeds a stub <c>organizations</c> row so the
    /// <c>audit_projection.target_org_id</c> FK is satisfied for
    /// TENANT_TARGETED rows. Idempotent via <c>ON CONFLICT DO NOTHING</c>.
    /// </summary>
    private async Task SeedOrgRowAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
              VALUES (@orgId, 'Test Org', 'MINISTRY', @orgId)
              ON CONFLICT (org_id) DO NOTHING", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await cmd.ExecuteNonQueryAsync();
    }

    private record AuditRowSnapshot(string EventType, string VisibilityScope, string? TargetOrgId, string? TargetResourceId);
}
