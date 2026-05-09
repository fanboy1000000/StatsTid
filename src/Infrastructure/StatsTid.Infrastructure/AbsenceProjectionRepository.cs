using Npgsql;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// DB-facing surface for the <c>absences_projection</c> read-model
/// (S27 Phase 4c.6 / TASK-2704). The atomic write paths (TASK-2706 Skema)
/// call <see cref="InsertAsync"/> inside the same transaction that appends
/// to <c>events</c> + enqueues to <c>outbox_events</c>; the caller owns
/// the transaction so the row, the event, and the outbox row commit or
/// roll back atomically (per ADR-018 D3 transactional-outbox contract).
/// Read methods serve the migrated GET endpoints (Skema month, Time
/// absences, Balance summary, Compliance period) and satisfy
/// read-your-write because the projection commits in the same tx as the event.
/// </summary>
public sealed class AbsenceProjectionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public AbsenceProjectionRepository(DbConnectionFactory connectionFactory)
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
        AbsenceRegistered @event, long outboxId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO absences_projection (
                  event_id, employee_id, date, absence_type, hours,
                  agreement_code, ok_version, occurred_at, actor_id, actor_role,
                  correlation_id, outbox_id
              ) VALUES (
                  @eventId, @employeeId, @date, @absenceType, @hours,
                  @agreementCode, @okVersion, @occurredAt, @actorId, @actorRole,
                  @correlationId, @outboxId
              )",
            conn, tx);
        cmd.Parameters.AddWithValue("eventId", @event.EventId);
        cmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
        cmd.Parameters.AddWithValue("date", @event.Date);
        cmd.Parameters.AddWithValue("absenceType", @event.AbsenceType);
        cmd.Parameters.AddWithValue("hours", @event.Hours);
        cmd.Parameters.AddWithValue("agreementCode", @event.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", @event.OkVersion);
        cmd.Parameters.AddWithValue("occurredAt", @event.OccurredAt);
        cmd.Parameters.AddWithValue("actorId", (object?)@event.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)@event.ActorRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlationId", (object?)@event.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outboxId", outboxId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Date-range read backing Skema month, Balance summary, and Compliance
    /// period endpoints. Uses <c>idx_absences_proj_emp_date_outbox</c>
    /// (init.sql:1218-1219). <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering across rows on the same date.
    /// </summary>
    public async Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAndDateRangeAsync(
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, absence_type, hours,
                     agreement_code, ok_version, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id
              FROM absences_projection
              WHERE employee_id = @employeeId AND date >= @start AND date <= @end
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        return await ReadRowsAsync(cmd, ct);
    }

    /// <summary>
    /// Full-stream read backing Time GET <c>/api/time-entries/{employeeId}/absences</c>.
    /// Uses <c>idx_absences_proj_emp_outbox</c> (init.sql:1220-1221),
    /// the no-date-filter index. <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering.
    /// </summary>
    public async Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, absence_type, hours,
                     agreement_code, ok_version, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id
              FROM absences_projection
              WHERE employee_id = @employeeId
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadRowsAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<AbsenceProjectionRow>> ReadRowsAsync(
        NpgsqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<AbsenceProjectionRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(ReadRow(reader));
        return rows;
    }

    private static AbsenceProjectionRow ReadRow(NpgsqlDataReader reader) => new()
    {
        EventId = reader.GetGuid(reader.GetOrdinal("event_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        Date = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date"))),
        AbsenceType = reader.GetString(reader.GetOrdinal("absence_type")),
        Hours = reader.GetDecimal(reader.GetOrdinal("hours")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
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
