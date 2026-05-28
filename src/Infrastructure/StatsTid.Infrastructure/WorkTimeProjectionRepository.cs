using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Immutable read-result row for the <c>work_time_projection</c> table
/// (TASK-5603 / S56 phase 2). One row per (employee, date) holding the latest
/// self-recorded work-time state: the list of work intervals plus the manual
/// daily-hours scalar. Returned by
/// <see cref="WorkTimeProjectionRepository.GetByEmployeeAndDateRangeAsync"/>
/// to back the Skema month GET <c>workTime</c> field.
/// </summary>
public sealed class WorkTimeProjectionRow
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<WorkInterval> Intervals { get; init; }
    public required decimal ManualHours { get; init; }
}

/// <summary>
/// DB-facing surface for the <c>work_time_projection</c> read-model
/// (TASK-5603). Unlike <see cref="TimeEntryProjectionRepository"/> /
/// <see cref="AbsenceProjectionRepository"/> (append-only, one row per event,
/// keyed by <c>event_id</c>), this projection is keyed by
/// <c>(employee_id, date)</c> and is LATEST-WINS: re-saving a day emits a NEW
/// <see cref="WorkTimeRegistered"/> event and the upsert overwrites the prior
/// row — but only when the incoming <c>outbox_id</c> is &gt;= the stored one,
/// so an out-of-order replay of a stale event can never clobber a newer row.
///
/// <para>
/// The atomic write path (TASK-5603 Skema save) calls <see cref="UpsertAsync"/>
/// inside the same transaction that enqueues to <c>outbox_events</c>; the
/// caller passes the <c>outbox_id</c> returned by
/// <c>IOutboxEnqueue.EnqueueAndReturnIdAsync</c> earlier in the same tx so the
/// projection commits or rolls back atomically with the event (ADR-018 D3/D13)
/// and read-your-write is preserved without waiting for the publisher drain.
/// </para>
///
/// <para>
/// <c>intervals</c> is persisted as JSONB (the <see cref="WorkInterval"/> list
/// serialized with camelCase property names: <c>[{"start":"08:00","end":"12:00"}]</c>).
/// </para>
/// </summary>
public sealed class WorkTimeProjectionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    // camelCase to match the rest of the API surface (WorkInterval.Start/End →
    // start/end). The shape is deterministic so it round-trips cleanly through
    // the GET deserialize path.
    private static readonly JsonSerializerOptions IntervalsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public WorkTimeProjectionRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// In-transaction LATEST-WINS upsert for the atomic POST handler. Keyed
    /// <c>(employee_id, date)</c>. The caller passes the <paramref name="outboxId"/>
    /// returned earlier in the same transaction by
    /// <c>IOutboxEnqueue.EnqueueAndReturnIdAsync</c>; the
    /// <c>WHERE work_time_projection.outbox_id &lt;= EXCLUDED.outbox_id</c> guard
    /// on the conflict path ensures a stale/older event never overwrites a newer
    /// row (out-of-order replay safety). The caller commits or rolls back; this
    /// method does NOT.
    /// </summary>
    public async Task UpsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WorkTimeRegistered @event, long outboxId, CancellationToken ct = default)
    {
        var intervalsJson = JsonSerializer.Serialize(@event.Intervals, IntervalsJsonOptions);
        var occurredAt = @event.OccurredAt.Kind == DateTimeKind.Utc
            ? @event.OccurredAt
            : DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc);

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO work_time_projection (
                  employee_id, date, intervals, manual_hours,
                  occurred_at, actor_id, actor_role, correlation_id, outbox_id
              ) VALUES (
                  @employeeId, @date, @intervals, @manualHours,
                  @occurredAt, @actorId, @actorRole, @correlationId, @outboxId
              )
              ON CONFLICT (employee_id, date) DO UPDATE SET
                  intervals = EXCLUDED.intervals,
                  manual_hours = EXCLUDED.manual_hours,
                  occurred_at = EXCLUDED.occurred_at,
                  actor_id = EXCLUDED.actor_id,
                  actor_role = EXCLUDED.actor_role,
                  correlation_id = EXCLUDED.correlation_id,
                  outbox_id = EXCLUDED.outbox_id
              WHERE work_time_projection.outbox_id <= EXCLUDED.outbox_id",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
        cmd.Parameters.AddWithValue("date", @event.Date);
        cmd.Parameters.Add("intervals", NpgsqlDbType.Jsonb).Value = intervalsJson;
        cmd.Parameters.AddWithValue("manualHours", @event.ManualHours);
        cmd.Parameters.AddWithValue("occurredAt", occurredAt);
        cmd.Parameters.AddWithValue("actorId", (object?)@event.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)@event.ActorRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlationId", (object?)@event.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outboxId", outboxId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Self-managed-connection date-range read backing the Skema month GET
    /// <c>workTime</c> field. Returns the per-day latest-wins rows (one per
    /// (employee, date)) with <c>intervals</c> deserialized from JSONB.
    /// Read-your-write is satisfied because the atomic POST handler commits the
    /// upsert in the same transaction as the <see cref="WorkTimeRegistered"/>
    /// event enqueue.
    /// </summary>
    public async Task<IReadOnlyList<WorkTimeProjectionRow>> GetByEmployeeAndDateRangeAsync(
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT employee_id, date, intervals, manual_hours
              FROM work_time_projection
              WHERE employee_id = @employeeId AND date >= @start AND date <= @end
              ORDER BY date ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);

        var rows = new List<WorkTimeProjectionRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var intervalsJson = reader.GetString(reader.GetOrdinal("intervals"));
            var intervals = JsonSerializer.Deserialize<List<WorkInterval>>(intervalsJson, IntervalsJsonOptions)
                ?? new List<WorkInterval>();
            rows.Add(new WorkTimeProjectionRow
            {
                EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
                Date = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date"))),
                Intervals = intervals,
                ManualHours = reader.GetDecimal(reader.GetOrdinal("manual_hours")),
            });
        }
        return rows;
    }
}
