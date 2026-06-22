using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.PhaseE;

[Trait("Category", "Docker")]
public sealed class AuditProjectionCrossProcessTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AuditProjectionRepository _auditRepo = null!;
    private PostgresEventStore _realOutbox = null!;
    private RetroactiveCorrectionRequestedAuditMapper _mapper = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);

        // Seed the org FK target for the TENANT_TARGETED audit row. The happy-path
        // test writes audit_projection.target_org_id = TestOrgId (ResolvedTargetOrgId);
        // without this row InsertAsync hit 23503 (audit_projection_target_org_id_fkey).
        await using (var seedConn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await seedConn.OpenAsync();
            await using var seedCmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@id, 'Cross-Process Test Org', 'ORGANISATION', @path)
                  ON CONFLICT DO NOTHING", seedConn);
            seedCmd.Parameters.AddWithValue("id", TestOrgId);
            seedCmd.Parameters.AddWithValue("path", $"/{TestOrgId}/");
            await seedCmd.ExecuteNonQueryAsync();
        }

        _auditRepo = new AuditProjectionRepository(_harness.Factory);
        _realOutbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("payroll"));
        _mapper = new RetroactiveCorrectionRequestedAuditMapper();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    private const string TestOrgId = "ORG_CROSS_PROC";
    private const string TestEmployeeId = "EMP_CROSS_PROC";

    [Fact]
    public async Task RetroactiveCorrectionRequested_AuditRowLands_TenantTargeted()
    {
        var ev = new RetroactiveCorrectionRequested
        {
            EmployeeId = TestEmployeeId,
            OriginalPeriodStart = new DateOnly(2026, 4, 1),
            OriginalPeriodEnd = new DateOnly(2026, 4, 30),
            AgreementCode = "AC",
            OkVersion = "OK24",
            Reason = "Test correction",
            CorrectedByActorId = "admin-test",
            CorrectionLineCount = 3,
            TotalDifferenceHours = 2.5m,
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(
            conn, tx, $"retro-correction-{TestEmployeeId}-2026-04-01", ev);
        var ctx = new AuditProjectionContext(
            ActorId: "admin-test",
            ActorPrimaryOrgId: TestOrgId,
            CorrelationId: Guid.NewGuid(),
            OccurredAt: new DateTimeOffset(ev.OccurredAt),
            ResolvedTargetOrgId: TestOrgId);
        var rowData = _mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        await using var readConn = new NpgsqlConnection(_harness.ConnectionString);
        await readConn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_type, visibility_scope, target_org_id, target_resource_id FROM audit_projection WHERE event_id = @id",
            readConn);
        cmd.Parameters.AddWithValue("id", ev.EventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("RetroactiveCorrectionRequested", reader.GetString(0));
        Assert.Equal("TENANT_TARGETED", reader.GetString(1));
        Assert.Equal(TestOrgId, reader.GetString(2));
        Assert.Equal(TestEmployeeId, reader.GetString(3));
    }

    [Fact]
    public async Task RetroactiveCorrectionRequested_OutboxFails_NoAuditRow()
    {
        var ev = new RetroactiveCorrectionRequested
        {
            EmployeeId = TestEmployeeId,
            OriginalPeriodStart = new DateOnly(2026, 5, 1),
            OriginalPeriodEnd = new DateOnly(2026, 5, 31),
            AgreementCode = "AC",
            OkVersion = "OK24",
            Reason = "Rollback test",
            CorrectedByActorId = "admin-test",
            CorrectionLineCount = 1,
            TotalDifferenceHours = 1.0m,
        };
        var throwingOutbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await throwingOutbox.EnqueueAndReturnIdAsync(
                    conn, tx, $"retro-correction-{TestEmployeeId}-2026-05-01", ev);
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }
}
