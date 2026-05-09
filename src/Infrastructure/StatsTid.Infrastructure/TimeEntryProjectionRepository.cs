using Npgsql;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// DB-facing surface for the <c>time_entries_projection</c> read-model
/// (S27 Phase 4c.6 / TASK-2704). The atomic write paths (TASK-2706 Skema,
/// TASK-2707 Time) call <see cref="InsertAsync"/> inside the same transaction
/// that appends to <c>events</c> + enqueues to <c>outbox_events</c>; the
/// caller owns the transaction so the row, the event, and the outbox row
/// commit or roll back atomically (per ADR-018 D3 transactional-outbox
/// contract). Read methods serve the migrated GET endpoints (Skema month,
/// Time entries, Balance summary, Compliance period) and satisfy
/// read-your-write because the projection commits in the same tx as the event.
/// </summary>
public sealed class TimeEntryProjectionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public TimeEntryProjectionRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// In-transaction insert for atomic POST handlers. The caller passes the
    /// <paramref name="outboxId"/> returned earlier in the same transaction
    /// by <c>IOutboxEnqueue.EnqueueAndReturnIdAsync</c> (TASK-2703); this
    /// keeps per-employee monotonic ordering aligned with the global outbox
    /// sequence. The caller commits or rolls back; this method does NOT.
    /// </summary>
    public async Task InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        TimeEntryRegistered @event, long outboxId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO time_entries_projection (
                  event_id, employee_id, date, hours, start_time, end_time,
                  task_id, activity_type, agreement_code, ok_version,
                  voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                  correlation_id, outbox_id
              ) VALUES (
                  @eventId, @employeeId, @date, @hours, @startTime, @endTime,
                  @taskId, @activityType, @agreementCode, @okVersion,
                  @voluntaryUnsocialHours, @occurredAt, @actorId, @actorRole,
                  @correlationId, @outboxId
              )",
            conn, tx);
        cmd.Parameters.AddWithValue("eventId", @event.EventId);
        cmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
        cmd.Parameters.AddWithValue("date", @event.Date);
        cmd.Parameters.AddWithValue("hours", @event.Hours);
        cmd.Parameters.AddWithValue("startTime", (object?)@event.StartTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("endTime", (object?)@event.EndTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("taskId", (object?)@event.TaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activityType", (object?)@event.ActivityType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("agreementCode", @event.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", @event.OkVersion);
        cmd.Parameters.AddWithValue("voluntaryUnsocialHours", @event.VoluntaryUnsocialHours);
        cmd.Parameters.AddWithValue("occurredAt", @event.OccurredAt);
        cmd.Parameters.AddWithValue("actorId", (object?)@event.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)@event.ActorRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlationId", (object?)@event.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outboxId", outboxId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Date-range read backing Skema month, Balance summary, and Compliance
    /// period endpoints. Uses <c>idx_time_entries_proj_emp_date_outbox</c>
    /// (init.sql:1199-1200). <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering across rows on the same date.
    /// </summary>
    public async Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEmployeeAndDateRangeAsync(
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, hours, start_time, end_time,
                     task_id, activity_type, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id
              FROM time_entries_projection
              WHERE employee_id = @employeeId AND date >= @start AND date <= @end
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        return await ReadRowsAsync(cmd, ct);
    }

    /// <summary>
    /// Full-stream read backing Time GET <c>/api/time-entries/{employeeId}</c>.
    /// Uses <c>idx_time_entries_proj_emp_outbox</c> (init.sql:1201-1202),
    /// the no-date-filter index. <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering.
    /// </summary>
    public async Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEmployeeAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, hours, start_time, end_time,
                     task_id, activity_type, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id
              FROM time_entries_projection
              WHERE employee_id = @employeeId
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadRowsAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<TimeEntryProjectionRow>> ReadRowsAsync(
        NpgsqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<TimeEntryProjectionRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(ReadRow(reader));
        return rows;
    }

    private static TimeEntryProjectionRow ReadRow(NpgsqlDataReader reader) => new()
    {
        EventId = reader.GetGuid(reader.GetOrdinal("event_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        Date = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date"))),
        Hours = reader.GetDecimal(reader.GetOrdinal("hours")),
        StartTime = reader.IsDBNull(reader.GetOrdinal("start_time"))
            ? null
            : reader.GetFieldValue<TimeOnly>(reader.GetOrdinal("start_time")),
        EndTime = reader.IsDBNull(reader.GetOrdinal("end_time"))
            ? null
            : reader.GetFieldValue<TimeOnly>(reader.GetOrdinal("end_time")),
        TaskId = reader.IsDBNull(reader.GetOrdinal("task_id"))
            ? null
            : reader.GetString(reader.GetOrdinal("task_id")),
        ActivityType = reader.IsDBNull(reader.GetOrdinal("activity_type"))
            ? null
            : reader.GetString(reader.GetOrdinal("activity_type")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        VoluntaryUnsocialHours = reader.GetBoolean(reader.GetOrdinal("voluntary_unsocial_hours")),
        OccurredAt = reader.GetDateTime(reader.GetOrdinal("occurred_at")),
        ActorId = reader.IsDBNull(reader.GetOrdinal("actor_id"))
            ? null
            : reader.GetString(reader.GetOrdinal("actor_id")),
        ActorRole = reader.IsDBNull(reader.GetOrdinal("actor_role"))
            ? null
            : reader.GetString(reader.GetOrdinal("actor_role")),
        CorrelationId = reader.IsDBNull(reader.GetOrdinal("correlation_id"))
            ? null
            : reader.GetGuid(reader.GetOrdinal("correlation_id")),
        OutboxId = reader.GetInt64(reader.GetOrdinal("outbox_id"))
    };
}
