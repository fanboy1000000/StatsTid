using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S27 / TASK-2710 Slot 5 — Projection backfill idempotency.
///
/// <para>
/// Pins the load-bearing invariant of <see cref="ProjectionBackfillService"/>'s
/// <c>ON CONFLICT (event_id) DO NOTHING</c> idempotency: re-running the backfill against
/// a database whose projections are already populated (e.g. from a prior backfill run, or
/// from sync-in-tx writes that landed concurrently with the backfill query) MUST NOT
/// duplicate rows. Per <see cref="ProjectionBackfillService"/>'s class doc, no TRUNCATE is
/// performed (live POST handlers may be racing the backfill in production), so idempotency
/// is the correctness guarantee.
/// </para>
///
/// <para>
/// Post-S27 Step 7a cycle 1 BLOCKER fix: invokes the production
/// <see cref="ProjectionBackfillService"/> directly (single source of truth) rather than
/// duplicating the SELECT/INSERT SQL inline. This eliminates the third site that had a
/// drift surface flagged by the TASK-2710 Reviewer (NOTE-2). The console app at
/// <c>tools/ProjectionBackfill</c> and Backend.Api startup also delegate to the same service.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectionBackfillTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private ProjectionBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
        _service = new ProjectionBackfillService(
            _harness.Factory,
            NullLogger<ProjectionBackfillService>.Instance);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Seed N events directly into <c>events</c> + <c>outbox_events</c> (mark them as
    /// published with <c>stream_version</c> set). Run the production backfill service;
    /// assert N rows in the projection table. Re-run; assert STILL N rows (no duplicates
    /// from <c>ON CONFLICT (event_id) DO NOTHING</c>).
    /// </summary>
    [Fact]
    public async Task Backfill_TimeEntries_IsIdempotent_ReRunDoesNotDuplicate()
    {
        const int seedCount = 5;
        var employeeId = "EMP_BF_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        // Seed N TimeEntryRegistered events directly into events + outbox_events
        // (already-published rows representing pre-S27 history that needs backfilling).
        var seededEventIds = new List<Guid>();
        for (int i = 0; i < seedCount; i++)
        {
            var evt = new TimeEntryRegistered
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 5, 1).AddDays(i),
                Hours = 7.4m,
                TaskId = $"PROJ-BF-{i}",
                ActivityType = "NORMAL",
                AgreementCode = "HK",
                OkVersion = "OK24",
            };
            var streamVersion = i + 1;
            await SeedEventAndOutboxRowAsync(evt, streamId, streamVersion);
            seededEventIds.Add(evt.EventId);
        }

        // Run the production backfill via the canonical service (single source of truth).
        var run1 = await _service.RunAsync();
        Assert.Equal(seedCount, run1.InsertedTime);
        Assert.Equal(0, run1.ConflictsTime);

        // Verify projection has N rows for this employee.
        var rowCountAfterRun1 = await CountProjectionRowsAsync("time_entries_projection", employeeId);
        Assert.Equal((long)seedCount, rowCountAfterRun1);

        // Re-run — every row should ON CONFLICT DO NOTHING.
        var run2 = await _service.RunAsync();
        Assert.Equal(0, run2.InsertedTime);
        Assert.Equal(seedCount, run2.ConflictsTime);

        // Row count UNCHANGED — no duplicates.
        var rowCountAfterRun2 = await CountProjectionRowsAsync("time_entries_projection", employeeId);
        Assert.Equal((long)seedCount, rowCountAfterRun2);

        // Each seeded event_id appears exactly once in the projection.
        foreach (var evtId in seededEventIds)
        {
            var c = await CountProjectionByEventIdAsync("time_entries_projection", evtId);
            Assert.Equal(1L, c);
        }
    }

    /// <summary>
    /// Insert a TimeEntryRegistered event into <c>events</c> + a matching
    /// <c>outbox_events</c> row marked published with <c>stream_version</c> set. Mirrors
    /// the post-publisher-drain steady-state that the backfill is designed to handle.
    /// </summary>
    private async Task SeedEventAndOutboxRowAsync(
        TimeEntryRegistered evt, string streamId, int streamVersion)
    {
        var data = EventSerializer.Serialize(evt);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Ensure stream row exists (FK target for events.stream_id).
        await using (var ensureCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("s", streamId);
            await ensureCmd.ExecuteNonQueryAsync();
        }

        // Insert into events. occurred_at = evt.OccurredAt (already UTC).
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

        // Insert matching outbox_events row marked PUBLISHED with stream_version set.
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

    private async Task<long> CountProjectionRowsAsync(string tableName, string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE employee_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountProjectionByEventIdAsync(string tableName, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
