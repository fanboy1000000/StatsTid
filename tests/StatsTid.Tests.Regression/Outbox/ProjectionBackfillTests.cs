using System.Data;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S27 / TASK-2710 Slot 5 — Projection backfill idempotency.
///
/// <para>
/// Pins the load-bearing invariant of <c>tools/ProjectionBackfill</c>'s
/// <c>ON CONFLICT (event_id) DO NOTHING</c> idempotency: re-running the backfill against
/// a database whose projections are already populated (e.g. from a prior backfill run, or
/// from sync-in-tx writes that landed concurrently with the backfill query) MUST NOT
/// duplicate rows. Per <c>tools/ProjectionBackfill/Program.cs:14-17</c>, no TRUNCATE is
/// performed (live POST handlers may be racing the backfill in production), so idempotency
/// is the correctness guarantee.
/// </para>
///
/// <para>
/// Replicates the backfill SQL inline rather than invoking <c>StatsTid.Tools.ProjectionBackfill.Program</c>
/// directly because the tool is an Exe with top-level statements and exposes no callable
/// surface. The pertinent contract under test is "the SELECT/INSERT pair is idempotent",
/// not "the tool's CLI argv parsing works" — the SQL is the load-bearing component and is
/// duplicated here verbatim from <c>Program.cs:90-116</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectionBackfillTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Seed N events directly into <c>events</c> + <c>outbox_events</c> (mark them as
    /// published with <c>stream_version</c> set). Run the backfill SQL programmatically;
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

        // Run backfill (replicates Program.cs:118-281 SELECT + INSERT path).
        var run1 = await RunBackfillAsync();
        Assert.Equal(seedCount, run1.InsertedTime);
        Assert.Equal(0, run1.ConflictsTime);

        // Verify projection has N rows for this employee.
        var rowCountAfterRun1 = await CountProjectionRowsAsync("time_entries_projection", employeeId);
        Assert.Equal((long)seedCount, rowCountAfterRun1);

        // Re-run — every row should ON CONFLICT DO NOTHING.
        var run2 = await RunBackfillAsync();
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

    /// <summary>
    /// Inline replication of <c>tools/ProjectionBackfill/Program.cs:118-281</c>'s
    /// SELECT + INSERT pipeline. Returns counters for assertion. Matches the production
    /// SQL byte-for-byte (any drift in <c>Program.cs</c> SQL must be mirrored here).
    /// </summary>
    private async Task<BackfillCounts> RunBackfillAsync()
    {
        const string SelectSql = @"
            SELECT
                events.event_id,
                events.event_type,
                events.data,
                events.stream_version,
                outbox_events.outbox_id,
                events.stored_at
            FROM events
            LEFT JOIN outbox_events ON events.event_id = outbox_events.event_id
            WHERE events.event_type IN ('TimeEntryRegistered', 'AbsenceRegistered')
            ORDER BY events.stream_id, events.stream_version
        ";

        const string InsertTimeSql = @"
            INSERT INTO time_entries_projection (
                event_id, employee_id, date, hours, start_time, end_time,
                task_id, activity_type, agreement_code, ok_version,
                voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                correlation_id, outbox_id
            ) VALUES (
                @eventId, @employeeId, @date, @hours, @startTime, @endTime,
                @taskId, @activityType, @agreementCode, @okVersion,
                @voluntaryUnsocialHours, @occurredAt, @actorId, @actorRole,
                @correlationId, @outboxId
            )
            ON CONFLICT (event_id) DO NOTHING
        ";

        const string InsertAbsSql = @"
            INSERT INTO absences_projection (
                event_id, employee_id, date, absence_type, hours,
                agreement_code, ok_version, occurred_at,
                actor_id, actor_role, correlation_id, outbox_id
            ) VALUES (
                @eventId, @employeeId, @date, @absenceType, @hours,
                @agreementCode, @okVersion, @occurredAt,
                @actorId, @actorRole, @correlationId, @outboxId
            )
            ON CONFLICT (event_id) DO NOTHING
        ";

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);

        var rows = new List<(Guid EventId, string EventType, string Data, int StreamVersion, long? OutboxId)>();
        await using (var selectCmd = new NpgsqlCommand(SelectSql, conn, tx))
        await using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((
                    EventId: reader.GetGuid(0),
                    EventType: reader.GetString(1),
                    Data: reader.GetString(2),
                    StreamVersion: reader.GetInt32(3),
                    OutboxId: reader.IsDBNull(4) ? null : reader.GetInt64(4)));
            }
        }

        var counts = new BackfillCounts();
        foreach (var row in rows)
        {
            var outboxId = row.OutboxId ?? row.StreamVersion;
            var domainEvent = EventSerializer.Deserialize(row.EventType, row.Data);
            if (domainEvent is TimeEntryRegistered te)
            {
                await using var cmd = new NpgsqlCommand(InsertTimeSql, conn, tx);
                cmd.Parameters.AddWithValue("eventId", te.EventId);
                cmd.Parameters.AddWithValue("employeeId", te.EmployeeId);
                cmd.Parameters.AddWithValue("date", te.Date);
                cmd.Parameters.AddWithValue("hours", te.Hours);
                cmd.Parameters.AddWithValue("startTime", (object?)te.StartTime ?? DBNull.Value);
                cmd.Parameters.AddWithValue("endTime", (object?)te.EndTime ?? DBNull.Value);
                cmd.Parameters.AddWithValue("taskId", (object?)te.TaskId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("activityType", (object?)te.ActivityType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("agreementCode", te.AgreementCode);
                cmd.Parameters.AddWithValue("okVersion", te.OkVersion);
                cmd.Parameters.AddWithValue("voluntaryUnsocialHours", te.VoluntaryUnsocialHours);
                cmd.Parameters.AddWithValue("occurredAt",
                    te.OccurredAt.Kind == DateTimeKind.Utc ? te.OccurredAt : DateTime.SpecifyKind(te.OccurredAt, DateTimeKind.Utc));
                cmd.Parameters.AddWithValue("actorId", (object?)te.ActorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("actorRole", (object?)te.ActorRole ?? DBNull.Value);
                cmd.Parameters.AddWithValue("correlationId", (object?)te.CorrelationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("outboxId", outboxId);
                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 1) counts.InsertedTime++;
                else counts.ConflictsTime++;
            }
            else if (domainEvent is AbsenceRegistered ab)
            {
                await using var cmd = new NpgsqlCommand(InsertAbsSql, conn, tx);
                cmd.Parameters.AddWithValue("eventId", ab.EventId);
                cmd.Parameters.AddWithValue("employeeId", ab.EmployeeId);
                cmd.Parameters.AddWithValue("date", ab.Date);
                cmd.Parameters.AddWithValue("absenceType", ab.AbsenceType);
                cmd.Parameters.AddWithValue("hours", ab.Hours);
                cmd.Parameters.AddWithValue("agreementCode", ab.AgreementCode);
                cmd.Parameters.AddWithValue("okVersion", ab.OkVersion);
                cmd.Parameters.AddWithValue("occurredAt",
                    ab.OccurredAt.Kind == DateTimeKind.Utc ? ab.OccurredAt : DateTime.SpecifyKind(ab.OccurredAt, DateTimeKind.Utc));
                cmd.Parameters.AddWithValue("actorId", (object?)ab.ActorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("actorRole", (object?)ab.ActorRole ?? DBNull.Value);
                cmd.Parameters.AddWithValue("correlationId", (object?)ab.CorrelationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("outboxId", outboxId);
                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 1) counts.InsertedAbs++;
                else counts.ConflictsAbs++;
            }
        }

        await tx.CommitAsync();
        return counts;
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

    private sealed class BackfillCounts
    {
        public int InsertedTime;
        public int InsertedAbs;
        public int ConflictsTime;
        public int ConflictsAbs;
    }
}
