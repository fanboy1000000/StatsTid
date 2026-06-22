using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Audit;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S43 / TASK-4306 — Phase E Test #2 (Projection backfill idempotency).
///
/// <para>
/// Strengthened per Step 0b cycle 1 BLOCKER B2 absorption. Earlier draft
/// asserted "run twice, counts equal" which would pass vacuously when no
/// mappers are registered (the case Sub-Sprint 1 ships in). This test
/// instead seeds a synthetic source event, registers an inline test
/// <see cref="IAuditProjectionMapper{TEvent}"/>, instantiates the production
/// <see cref="AuditProjectionBackfillService"/> via DI (NOT inline SQL
/// replay per S27 SSOT discipline + Step 0b cycle 1 Reviewer W2 absorption),
/// and asserts insert-once-then-conflict.
/// </para>
///
/// <para>
/// Failure modes the test catches:
/// </para>
/// <list type="bullet">
///   <item><description>Scan path broken (events not picked up by SELECT)</description></item>
///   <item><description>Mapper path broken (registry doesn't resolve / Map throws)</description></item>
///   <item><description>Insert path broken (InsertAsync doesn't ON CONFLICT
///   DO NOTHING properly on re-run)</description></item>
///   <item><description>Counter accuracy (Inserted vs Conflicts vs NoMapper
///   split for the seeded event)</description></item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionBackfillIdempotencyTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AuditProjectionBackfillService _service = null!;
    private AuditProjectionRepository _repository = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await Outbox.OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);

        // Seed the organization row the synthetic OrganizationCreated event
        // refers to — also serves as FK target for any TENANT_TARGETED
        // catalog rows (this test uses GLOBAL_TENANT_VISIBLE so the FK isn't
        // exercised, but the row pre-exists for symmetry with production
        // ordering where the event lands after the row is committed).
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES ('ORG_TEST_BF', 'Backfill Test Org', 'ORGANISATION', '/ORG_TEST_BF/')
                  ON CONFLICT DO NOTHING", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // DI container: registry + repository + backfill service + inline
        // test mapper for OrganizationCreated + RegisteredAuditEventType
        // marker so the backfill scans OrganizationCreated events
        // (Step 7a cycle 1 Codex W1 absorption — backfill filters by
        // RegisteredEventTypeNames; absence of the marker means fast-path
        // no-op).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_harness.Factory);
        services.AddSingleton<AuditProjectionRepository>();
        services.AddSingleton<IAuditProjectionMapperRegistry, AuditProjectionMapperRegistry>();
        services.AddSingleton<AuditProjectionBackfillService>();
        services.AddSingleton<IAuditProjectionMapper<OrganizationCreated>, TestOrganizationCreatedMapper>();
        services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationCreated), nameof(OrganizationCreated)));
        _sp = services.BuildServiceProvider();

        _service = _sp.GetRequiredService<AuditProjectionBackfillService>();
        _repository = _sp.GetRequiredService<AuditProjectionRepository>();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _harness.DisposeAsync();
    }

    /// <summary>
    /// Seed 1 synthetic OrganizationCreated event + matching outbox row;
    /// run backfill; assert exactly 1 audit_projection row inserted.
    /// Re-run; assert second run conflicts (no duplicate). Final count = 1.
    /// </summary>
    [Fact]
    public async Task Backfill_WithSeededEventAndMapper_InsertsOnce_ReRunConflicts()
    {
        var evt = new OrganizationCreated
        {
            OrgId = "ORG_TEST_BF",
            OrgName = "Backfill Test Org",
            OrgType = "ORGANISATION",
            MaterializedPath = "/ORG_TEST_BF/",
            AgreementCode = "AC",
            OkVersion = "OK24",
        };
        await SeedEventAndOutboxRowAsync(evt, $"org-{evt.OrgId}", streamVersion: 1);

        // Run 1: expect 1 insert, 0 conflicts, 0 noMapper.
        var run1 = await _service.RunAsync();
        Assert.Equal(1, run1.Scanned);
        Assert.Equal(1, run1.Inserted);
        Assert.Equal(0, run1.Conflicts);
        Assert.Equal(0, run1.NoMapper);
        Assert.Equal(0, run1.NullOutboxSkipped);
        Assert.Equal(0, run1.UnknownEventTypes);
        Assert.Equal(0, run1.DeserializationErrors);

        // Verify row landed exactly once.
        Assert.Equal(1L, await _repository.CountAsync());
        Assert.Equal(1L, await _repository.CountByEventIdAsync(evt.EventId));

        // Run 2: expect 1 conflict (ON CONFLICT DO NOTHING short-circuit), no new insert.
        var run2 = await _service.RunAsync();
        Assert.Equal(1, run2.Scanned);
        Assert.Equal(0, run2.Inserted);
        Assert.Equal(1, run2.Conflicts);
        Assert.Equal(0, run2.NoMapper);

        // Row count UNCHANGED — no duplicate.
        Assert.Equal(1L, await _repository.CountAsync());
        Assert.Equal(1L, await _repository.CountByEventIdAsync(evt.EventId));
    }

    /// <summary>
    /// Sub-Sprint 1 default state: backfill with NO RegisteredAuditEventType
    /// markers triggers the fast-path no-op exit (scanned=0; no events
    /// scanned at all per Step 7a cycle 1 Codex W1 absorption). Sub-Sprint 2
    /// progressively adds RegisteredAuditEventType + IAuditProjectionMapper
    /// pairs and re-runs pick them up idempotently.
    /// </summary>
    [Fact]
    public async Task Backfill_WithoutRegisteredEventTypes_FastPathsNoOp()
    {
        // Bring up a sibling service with NO mappers + NO marker registrations.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_harness.Factory);
        services.AddSingleton<AuditProjectionRepository>();
        services.AddSingleton<IAuditProjectionMapperRegistry, AuditProjectionMapperRegistry>();
        services.AddSingleton<AuditProjectionBackfillService>();
        await using var emptySp = services.BuildServiceProvider();
        var emptyService = emptySp.GetRequiredService<AuditProjectionBackfillService>();

        var evt = new OrganizationCreated
        {
            OrgId = "ORG_TEST_NM",
            OrgName = "NoMapper Org",
            OrgType = "ORGANISATION",
            MaterializedPath = "/ORG_TEST_NM/",
            AgreementCode = "AC",
            OkVersion = "OK24",
        };
        // Pre-seed the FK target.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@id, 'NoMapper Org', 'ORGANISATION', '/ORG_TEST_NM/')
                  ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("id", evt.OrgId);
            await cmd.ExecuteNonQueryAsync();
        }
        await SeedEventAndOutboxRowAsync(evt, $"org-{evt.OrgId}", streamVersion: 1);

        var run = await emptyService.RunAsync();
        Assert.Equal(0, run.Scanned);
        Assert.Equal(0, run.Inserted);
        Assert.Equal(0, run.Conflicts);
        Assert.Equal(0, run.NoMapper);
    }

    /// <summary>
    /// Test-local mapper for OrganizationCreated → GLOBAL_TENANT_VISIBLE
    /// audit row. Used only by Test #2; no production registration.
    /// </summary>
    private sealed class TestOrganizationCreatedMapper : IAuditProjectionMapper<OrganizationCreated>
    {
        public AuditProjectionRowData Map(OrganizationCreated @event, AuditProjectionContext context)
        {
            return new AuditProjectionRowData(
                VisibilityScope: AuditVisibilityScope.GlobalTenantVisible,
                TargetOrgId: null,
                TargetResourceId: @event.OrgId,
                DetailsJson: $"{{\"org_id\":\"{@event.OrgId}\",\"org_name\":\"{@event.OrgName}\",\"org_type\":\"{@event.OrgType}\"}}");
        }
    }

    /// <summary>
    /// Insert an event + matching published outbox_events row. Mirrors
    /// <see cref="StatsTid.Tests.Regression.Outbox.ProjectionBackfillTests"/>
    /// seeding pattern verbatim except we use OrganizationCreated as the
    /// synthetic source.
    /// </summary>
    private async Task SeedEventAndOutboxRowAsync(
        OrganizationCreated evt, string streamId, int streamVersion)
    {
        var data = EventSerializer.Serialize(evt);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var ensureCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("s", streamId);
            await ensureCmd.ExecuteNonQueryAsync();
        }

        await using (var eventsCmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at, actor_id, actor_role, correlation_id)
            VALUES (@id, @s, @v, @t, @d::jsonb, @o, NULL, NULL, NULL)
            """, conn, tx))
        {
            eventsCmd.Parameters.AddWithValue("id", evt.EventId);
            eventsCmd.Parameters.AddWithValue("s", streamId);
            eventsCmd.Parameters.AddWithValue("v", streamVersion);
            eventsCmd.Parameters.AddWithValue("t", evt.EventType);
            eventsCmd.Parameters.AddWithValue("d", NpgsqlDbType.Text, data);
            eventsCmd.Parameters.AddWithValue("o", DateTime.SpecifyKind(evt.OccurredAt, DateTimeKind.Utc));
            await eventsCmd.ExecuteNonQueryAsync();
        }

        await using (var outboxCmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_events (
                service_id, stream_id, event_id, event_type, event_payload,
                correlation_id, actor_id, actor_role, published_at, stream_version)
            VALUES (
                'backend-api', @s, @id, @t, @p::jsonb,
                NULL, NULL, NULL, NOW(), @v)
            """, conn, tx))
        {
            outboxCmd.Parameters.AddWithValue("s", streamId);
            outboxCmd.Parameters.AddWithValue("id", evt.EventId);
            outboxCmd.Parameters.AddWithValue("t", evt.EventType);
            outboxCmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, data);
            outboxCmd.Parameters.AddWithValue("v", streamVersion);
            await outboxCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}
