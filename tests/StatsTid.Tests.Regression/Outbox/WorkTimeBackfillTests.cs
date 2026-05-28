using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S56 / TASK-5603 — backfill replay of <c>WorkTimeRegistered</c> events into
/// the <c>work_time_projection</c> read-model. Exercises the production
/// <see cref="ProjectionBackfillService"/> (single source of truth) the same way
/// <see cref="ProjectionBackfillTests"/> does for time entries. work_time_projection
/// is LATEST-WINS keyed (employee_id, date), so the invariants differ from the
/// append-only projections:
///
/// <list type="bullet">
///   <item>Replay applies WorkTimeRegistered (AppliedWorkTime counter increments).</item>
///   <item>Re-running is idempotent (final state unchanged; equal outbox_id re-applies
///   the same values).</item>
///   <item>A STALE (lower outbox_id) event replayed AFTER a newer row exists does NOT
///   clobber the newer row (the <c>outbox_id&lt;=</c> guard blocks it → SkippedWorkTime).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WorkTimeBackfillTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions IntervalsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private ProjectionBackfillService _service = null!;
    private WorkTimeProjectionRepository _workTimeRepo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
        _service = new ProjectionBackfillService(
            _harness.Factory, NullLogger<ProjectionBackfillService>.Instance);
        _workTimeRepo = new WorkTimeProjectionRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Seed N WorkTimeRegistered events on distinct days; run backfill; assert
    /// AppliedWorkTime == N and the projection holds the replayed state. Re-run;
    /// assert the final row state is unchanged (idempotent — re-applying equal
    /// outbox_id values is a no-op overwrite that still affects 1 row, so we
    /// assert on the resulting STATE rather than the Applied/Skipped split here).
    /// </summary>
    [Fact]
    public async Task Backfill_WorkTime_AppliesAndIsIdempotent()
    {
        const int seedCount = 3;
        var employeeId = "EMP_WTBF_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        for (int i = 0; i < seedCount; i++)
        {
            var evt = new WorkTimeRegistered
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 5, 4).AddDays(i),
                Intervals = new[] { new WorkInterval { Start = "08:00", End = "16:00" } },
                ManualHours = 0m,
            };
            await SeedWorkTimeEventAsync(evt, streamId, streamVersion: i + 1);
        }

        var run1 = await _service.RunAsync();
        Assert.Equal(seedCount, run1.AppliedWorkTime);
        Assert.Equal(0, run1.SkippedWorkTime);

        var rowsAfter1 = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Assert.Equal(seedCount, rowsAfter1.Count);

        // Re-run: latest-wins replay re-applies the same values. State must be unchanged.
        await _service.RunAsync();
        var rowsAfter2 = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Assert.Equal(seedCount, rowsAfter2.Count);
        Assert.All(rowsAfter2, r =>
        {
            var iv = Assert.Single(r.Intervals);
            Assert.Equal("08:00", iv.Start);
            Assert.Equal("16:00", iv.End);
        });
    }

    /// <summary>
    /// Stale-event-does-not-clobber-newer-row: a LIVE newer row (high outbox_id)
    /// already exists for (employee, date); the backfill then replays an OLDER
    /// WorkTimeRegistered event (low outbox_id) for the same day. The
    /// <c>outbox_id&lt;=</c> guard blocks the overwrite (SkippedWorkTime
    /// increments) and the newer row stands. This is the out-of-order replay
    /// safety guarantee.
    /// </summary>
    [Fact]
    public async Task Backfill_StaleEvent_DoesNotClobberNewerLiveRow()
    {
        var employeeId = "EMP_WTBF_STALE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var date = new DateOnly(2026, 5, 20);

        // A live, newer row written directly with a HIGH outbox_id (simulating a
        // post-backfill-window live save).
        var newer = new WorkTimeRegistered
        {
            EmployeeId = employeeId, Date = date,
            Intervals = new[] { new WorkInterval { Start = "09:00", End = "17:00" } },
            ManualHours = 3m,
        };
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _workTimeRepo.UpsertAsync(conn, tx, newer, outboxId: 1_000_000);
            await tx.CommitAsync();
        }

        // An OLDER event in the log for the SAME day (low outbox_id) — backfill
        // will try to replay it but the guard must block the overwrite.
        var stale = new WorkTimeRegistered
        {
            EmployeeId = employeeId, Date = date,
            Intervals = new[] { new WorkInterval { Start = "00:00", End = "01:00" } },
            ManualHours = 99m,
        };
        await SeedWorkTimeEventAsync(stale, streamId, streamVersion: 1, outboxId: 5);

        var run = await _service.RunAsync();
        Assert.Equal(1, run.SkippedWorkTime); // stale replay blocked
        Assert.Equal(0, run.AppliedWorkTime);

        var rows = await _workTimeRepo.GetByEmployeeAndDateRangeAsync(employeeId, date, date);
        var row = Assert.Single(rows);
        Assert.Equal(3m, row.ManualHours); // newer live row survived
        Assert.Equal("09:00", Assert.Single(row.Intervals).Start);
    }

    /// <summary>
    /// Seed a WorkTimeRegistered event into <c>events</c> + a matching
    /// <c>outbox_events</c> row. When <paramref name="outboxId"/> is null the
    /// outbox_id is the BIGSERIAL default; pass an explicit value to control the
    /// latest-wins ordering used by the backfill's outbox_id resolution.
    /// </summary>
    private async Task SeedWorkTimeEventAsync(
        WorkTimeRegistered evt, string streamId, int streamVersion, long? outboxId = null)
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

        if (outboxId.HasValue)
        {
            await using var outboxCmd = new NpgsqlCommand(
                """
                INSERT INTO outbox_events (
                    outbox_id, service_id, stream_id, event_id, event_type, event_payload,
                    correlation_id, actor_id, actor_role, published_at, stream_version)
                VALUES (
                    @oid, 'backend-api', @s, @id, @t, @p::jsonb,
                    NULL, NULL, NULL, NOW(), @v)
                """, conn, tx);
            outboxCmd.Parameters.AddWithValue("oid", outboxId.Value);
            outboxCmd.Parameters.AddWithValue("s", streamId);
            outboxCmd.Parameters.AddWithValue("id", evt.EventId);
            outboxCmd.Parameters.AddWithValue("t", evt.EventType);
            outboxCmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, data);
            outboxCmd.Parameters.AddWithValue("v", streamVersion);
            await outboxCmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var outboxCmd = new NpgsqlCommand(
                """
                INSERT INTO outbox_events (
                    service_id, stream_id, event_id, event_type, event_payload,
                    correlation_id, actor_id, actor_role, published_at, stream_version)
                VALUES (
                    'backend-api', @s, @id, @t, @p::jsonb,
                    NULL, NULL, NULL, NOW(), @v)
                """, conn, tx);
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
