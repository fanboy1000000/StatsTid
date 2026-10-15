using Npgsql;
using NpgsqlTypes;
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
///
/// <para><b>S145 / TASK-14507 (A0, QUAL-192) — a Skema save replaces the cell.</b> In plain terms:
/// a Skema cell (one employee, one day, one task) holds ONE number. Saving it again appends a new
/// event (events stay append-only) and a new row, and marks the earlier Skema row of the cell
/// <c>superseded_by_outbox_id</c> = the new row's <c>outbox_id</c>. Superseded rows are kept, so
/// a calculation can still be replayed from the exact rows it read (<see cref="GetByEventIdsAsync"/>),
/// but every total counts CURRENT rows only: both default reads below filter
/// <c>superseded_by_outbox_id IS NULL</c>. A <c>POST /api/time-entries</c> row (source
/// <c>TIME_POST</c>) is a separate additive registration: it is never marked and never marks.</para>
/// </summary>
public sealed class TimeEntryProjectionRepository
{
    /// <summary>
    /// S145 / TASK-14507 — the ONE supersession mark for a time-entry cell, shared verbatim by the
    /// live <see cref="InsertAsync"/> and the from-events rebuild (<c>ProjectionBackfillService</c>)
    /// so both reach the same current set. It runs BEFORE the new row's INSERT (mark, then insert,
    /// never the reverse: the partial unique index <c>ux_time_entries_proj_current_skema_cell</c>
    /// admits one current Skema row per cell, so an insert-first would collide with the row it is
    /// about to replace).
    ///
    /// <para>The predicate, clause by clause: the cell (employee, date, task); <c>task_id IS NOT
    /// DISTINCT FROM</c> rather than <c>=</c>, so a NULL task matches a NULL task exactly as the
    /// index's <c>NULLS NOT DISTINCT</c> does (the save refuses an empty project code, so this is
    /// a backstop); <c>source = 'SKEMA'</c>, so a Skema save never retires an additive Time-POST row;
    /// <c>superseded_by_outbox_id IS NULL</c>, so an already-retired row keeps the id of the save
    /// that really replaced it (idempotent on a rebuild re-run); <c>outbox_id &lt; @outboxId</c>, so
    /// a row is only ever retired by a LATER write (always true on the live path, which holds the
    /// employee's consumption lock; load-bearing on a rebuild).</para>
    /// </summary>
    internal const string MarkSupersededSkemaCellSql = @"
        UPDATE time_entries_projection
           SET superseded_by_outbox_id = @outboxId
         WHERE employee_id = @employeeId
           AND date = @date
           AND task_id IS NOT DISTINCT FROM @taskId
           AND source = 'SKEMA'
           AND superseded_by_outbox_id IS NULL
           AND outbox_id < @outboxId";

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
    ///
    /// <para>S145 / TASK-14507 — the row's <c>source</c> is <see cref="HoursSource.Resolve"/> of the
    /// event's <c>Source</c> (null, i.e. every <c>POST /api/time-entries</c> event and every pre-S145
    /// event, is <c>TIME_POST</c>). For a <c>SKEMA</c> row the cell's current Skema row is marked
    /// superseded FIRST (<see cref="MarkSupersededSkemaCellSql"/>), then the new row is inserted, both
    /// in the caller's transaction. A caller posting several cells processes them in posted order, so
    /// the same cell posted twice in one save ends with the later value current. A <c>TIME_POST</c>
    /// row skips the mark: it is never marked and never marks.</para>
    /// </summary>
    public async Task InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        TimeEntryRegistered @event, long outboxId, CancellationToken ct = default)
    {
        var source = HoursSource.Resolve(@event.Source);
        if (string.Equals(source, HoursSource.Skema, StringComparison.Ordinal))
        {
            await using var markCmd = new NpgsqlCommand(MarkSupersededSkemaCellSql, conn, tx);
            markCmd.Parameters.AddWithValue("outboxId", outboxId);
            markCmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
            markCmd.Parameters.AddWithValue("date", @event.Date);
            // Typed: a NULL task must still bind as text for IS NOT DISTINCT FROM.
            markCmd.Parameters.Add(new NpgsqlParameter("taskId", NpgsqlDbType.Text)
            {
                Value = (object?)@event.TaskId ?? DBNull.Value,
            });
            await markCmd.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO time_entries_projection (
                  event_id, employee_id, date, hours, start_time, end_time,
                  task_id, activity_type, agreement_code, ok_version,
                  voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                  correlation_id, outbox_id, source
              ) VALUES (
                  @eventId, @employeeId, @date, @hours, @startTime, @endTime,
                  @taskId, @activityType, @agreementCode, @okVersion,
                  @voluntaryUnsocialHours, @occurredAt, @actorId, @actorRole,
                  @correlationId, @outboxId, @source
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
        cmd.Parameters.AddWithValue("source", source);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Date-range read backing Skema month, Balance summary, and Compliance
    /// period endpoints. Uses <c>idx_time_entries_proj_emp_date_outbox</c>
    /// (init.sql:1199-1200). <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering across rows on the same date.
    ///
    /// <para>S145 / TASK-14507 (R20) — CURRENT rows only (<c>superseded_by_outbox_id IS NULL</c>):
    /// a re-saved Skema cell is counted once, at its latest value. Every caller totals or displays
    /// hours, so none may see a replaced row; replay reads by event id instead
    /// (<see cref="GetByEventIdsAsync"/>).</para>
    /// </summary>
    public async Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEmployeeAndDateRangeAsync(
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await QueryCurrentByDateRangeAsync(conn, tx: null, employeeId, start, end, ct);
    }

    /// <summary>
    /// S145 / TASK-14508 fix-up 2 (QUAL-200) — the same CURRENT-rows date-range read as
    /// <see cref="GetByEmployeeAndDateRangeAsync(string, DateOnly, DateOnly, CancellationToken)"/>, on the
    /// caller's <paramref name="conn"/> + <paramref name="tx"/>. It exists so that payroll's hours read
    /// can take the time-entry rows and the absence rows from ONE snapshot (one REPEATABLE READ
    /// transaction): a Skema save writes both projections in one transaction, and two reads on two
    /// connections could otherwise see it half-applied. Same SQL, same filter, same order (the one query
    /// below serves both overloads). The caller owns the transaction; this method neither commits nor
    /// rolls back. Precedent: <c>AbsenceProjectionRepository.GetCurrentSkemaRowsAsync</c>.
    /// </summary>
    public Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEmployeeAndDateRangeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        return QueryCurrentByDateRangeAsync(conn, tx, employeeId, start, end, ct);
    }

    // The ONE current-rows date-range query behind both GetByEmployeeAndDateRangeAsync overloads
    // (a null tx only from the self-connecting overload, which runs it outside any transaction).
    private static async Task<IReadOnlyList<TimeEntryProjectionRow>> QueryCurrentByDateRangeAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, hours, start_time, end_time,
                     task_id, activity_type, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id, source, superseded_by_outbox_id
              FROM time_entries_projection
              WHERE employee_id = @employeeId AND date >= @start AND date <= @end
                AND superseded_by_outbox_id IS NULL
              ORDER BY outbox_id ASC",
            conn, tx);
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
    ///
    /// <para>S145 / TASK-14507 (R20) — CURRENT rows only, like
    /// <see cref="GetByEmployeeAndDateRangeAsync"/>.</para>
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
                     correlation_id, outbox_id, source, superseded_by_outbox_id
              FROM time_entries_projection
              WHERE employee_id = @employeeId
                AND superseded_by_outbox_id IS NULL
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadRowsAsync(cmd, ct);
    }

    /// <summary>
    /// S145 / TASK-14507 (A0 constraint 5) — the rows with exactly these event ids, current OR
    /// superseded, ordered by <c>outbox_id</c>. This is the ONLY read of the table that does not
    /// filter to current rows, and it exists for replay: a calculation records the event ids of the
    /// rows it read, and replaying it must find those rows even after the employee later re-saved
    /// the cell. Auditability must not depend on nobody editing afterwards. An id with no row is
    /// simply absent from the result (the caller decides whether that is a refusal). Never use this
    /// read to total hours.
    /// </summary>
    public async Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEventIdsAsync(
        string employeeId, IReadOnlyList<Guid> eventIds, CancellationToken ct = default)
    {
        if (eventIds.Count == 0)
            return Array.Empty<TimeEntryProjectionRow>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, hours, start_time, end_time,
                     task_id, activity_type, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id, source, superseded_by_outbox_id
              FROM time_entries_projection
              WHERE employee_id = @employeeId AND event_id = ANY(@eventIds)
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.Add(new NpgsqlParameter("eventIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = eventIds.ToArray(),
        });
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
        OutboxId = reader.GetInt64(reader.GetOrdinal("outbox_id")),
        Source = reader.GetString(reader.GetOrdinal("source")),
        SupersededByOutboxId = reader.IsDBNull(reader.GetOrdinal("superseded_by_outbox_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("superseded_by_outbox_id"))
    };
}
