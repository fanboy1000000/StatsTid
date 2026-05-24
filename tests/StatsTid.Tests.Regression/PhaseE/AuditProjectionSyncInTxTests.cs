using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S44f — Phase E Test #3 (Sync-in-TX outbox ID linkage).
///
/// <para>
/// Validates the ADR-018 D13 sync-in-tx projection contract at the
/// <c>audit_projection</c> table: the <c>outbox_id</c> column in the
/// audit row must exactly match the <c>outbox_id</c> column in the
/// <c>outbox_events</c> row for the same <c>event_id</c>.
/// </para>
///
/// <list type="bullet">
///   <item><description>Test 3a (commit path): emit an event, enqueue via
///   <c>EnqueueAndReturnIdAsync</c>, map, insert audit row, COMMIT —
///   assert outbox_id linkage matches across both tables.</description></item>
///   <item><description>Test 3b (rollback path): same setup but ROLLBACK
///   instead of COMMIT — assert neither outbox row NOR audit_projection
///   row exists for the event_id.</description></item>
/// </list>
///
/// <para>
/// Docker-gated (requires a live PostgreSQL container).
/// Uses <see cref="OrganizationCreatedAuditMapper"/> as the test mapper
/// (simple, already proven in prior D-tests).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionSyncInTxTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AuditProjectionRepository _auditRepo = null!;
    private PostgresEventStore _realOutbox = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);

        // Seed the organization FK target for TENANT_TARGETED rows.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES ('ORG_SYNC_TEST', 'Sync Test Org', 'STYRELSE', '/ORG_SYNC_TEST/')
                  ON CONFLICT DO NOTHING", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton(_harness.Factory);
        services.AddSingleton<AuditProjectionRepository>();
        services.AddSingleton<IAuditProjectionMapper<OrganizationCreated>, OrganizationCreatedAuditMapper>();
        _sp = services.BuildServiceProvider();
        _auditRepo = _sp.GetRequiredService<AuditProjectionRepository>();
        _realOutbox = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _harness.DisposeAsync();
    }

    /// <summary>
    /// Committed path: the <c>outbox_id</c> in <c>audit_projection</c> must
    /// equal the <c>outbox_id</c> in <c>outbox_events</c> for the same
    /// <c>event_id</c>. This proves the sync-in-tx linkage is intact —
    /// both rows were inserted inside the same transaction with the same
    /// outbox_id value returned by <c>EnqueueAndReturnIdAsync</c>.
    /// </summary>
    [Fact]
    public async Task CommittedAuditRow_HasMatchingOutboxRow()
    {
        var orgId = $"ORG_CM_{Guid.NewGuid():N}"[..16];
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<OrganizationCreated>>();
        var ev = new OrganizationCreated
        {
            OrgId = orgId,
            OrgName = "Sync Commit Org",
            OrgType = "STYRELSE",
            ParentOrgId = null,
            MaterializedPath = $"/{orgId}/",
            AgreementCode = "AC",
            OkVersion = "OK24",
        };

        // Seed org FK target for audit_projection.target_org_id
        await using (var seedConn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await seedConn.OpenAsync();
            await using var seedCmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@orgId, @orgName, @orgType, @path)
                  ON CONFLICT DO NOTHING", seedConn);
            seedCmd.Parameters.AddWithValue("orgId", orgId);
            seedCmd.Parameters.AddWithValue("orgName", ev.OrgName);
            seedCmd.Parameters.AddWithValue("orgType", ev.OrgType);
            seedCmd.Parameters.AddWithValue("path", ev.MaterializedPath);
            await seedCmd.ExecuteNonQueryAsync();
        }

        // Atomic tx: enqueue + map + audit insert + commit
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", ev);
        var ctx = new AuditProjectionContext(
            ActorId: "tester",
            ActorPrimaryOrgId: orgId,
            CorrelationId: Guid.NewGuid(),
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(ev.OccurredAt, DateTimeKind.Utc)));
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);
        await tx.CommitAsync();

        // Verify: outbox_id in audit_projection matches outbox_id in outbox_events
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();

        await using var auditCmd = new NpgsqlCommand(
            "SELECT outbox_id FROM audit_projection WHERE event_id = @eventId", verifyConn);
        auditCmd.Parameters.AddWithValue("eventId", ev.EventId);
        var auditOutboxId = (long?)await auditCmd.ExecuteScalarAsync();
        Assert.NotNull(auditOutboxId);

        await using var outboxCmd = new NpgsqlCommand(
            "SELECT outbox_id FROM outbox_events WHERE event_id = @eventId", verifyConn);
        outboxCmd.Parameters.AddWithValue("eventId", ev.EventId);
        var outboxOutboxId = (long?)await outboxCmd.ExecuteScalarAsync();
        Assert.NotNull(outboxOutboxId);

        Assert.Equal(outboxOutboxId, auditOutboxId);
        Assert.Equal(outboxId, auditOutboxId);
    }

    /// <summary>
    /// Rolled-back path: neither <c>outbox_events</c> nor
    /// <c>audit_projection</c> should contain a row for the event_id.
    /// This proves the atomic-tx rollback removes both the outbox row
    /// and the audit projection row.
    /// </summary>
    [Fact]
    public async Task RolledBackAuditRow_NeitherOutboxNorAuditExists()
    {
        var orgId = $"ORG_RB_{Guid.NewGuid():N}"[..16];
        var mapper = _sp.GetRequiredService<IAuditProjectionMapper<OrganizationCreated>>();
        var ev = new OrganizationCreated
        {
            OrgId = orgId,
            OrgName = "Sync Rollback Org",
            OrgType = "STYRELSE",
            ParentOrgId = null,
            MaterializedPath = $"/{orgId}/",
            AgreementCode = "AC",
            OkVersion = "OK24",
        };

        // Note: we do NOT seed the org FK target here — the whole tx
        // will be rolled back so the FK is never checked at commit.

        // Atomic tx: enqueue + map + audit insert + ROLLBACK
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var outboxId = await _realOutbox.EnqueueAndReturnIdAsync(conn, tx, $"org-{orgId}", ev);
        var ctx = new AuditProjectionContext(
            ActorId: "tester",
            ActorPrimaryOrgId: orgId,
            CorrelationId: Guid.NewGuid(),
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(ev.OccurredAt, DateTimeKind.Utc)));
        var rowData = mapper.Map(ev, ctx);
        await _auditRepo.InsertAsync(conn, tx, ev.EventId, outboxId, ev.EventType, rowData, ctx);

        // Explicit rollback instead of commit
        await tx.RollbackAsync();

        // Verify: no outbox row exists
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();

        await using var outboxCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_id = @eventId", verifyConn);
        outboxCmd.Parameters.AddWithValue("eventId", ev.EventId);
        var outboxCount = Convert.ToInt64(await outboxCmd.ExecuteScalarAsync());
        Assert.Equal(0L, outboxCount);

        // Verify: no audit_projection row exists
        Assert.Equal(0L, await _auditRepo.CountByEventIdAsync(ev.EventId));
    }
}
