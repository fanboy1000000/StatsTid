using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Immutable read-result row for the <c>work_time_projection</c> table
/// (TASK-5603 / S56 phase 2). One row per (employee, date) holding the latest
/// self-recorded work-time state: the list of work intervals, a day's only
/// work-time record (ADR-043 D10, D-2.4). Returned by
/// <see cref="WorkTimeProjectionRepository.GetByEmployeeAndDateRangeAsync"/>
/// to back the Skema month GET <c>workTime</c> field.
/// </summary>
public sealed class WorkTimeProjectionRow
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<WorkInterval> Intervals { get; init; }
}

/// <summary>
/// The current state of one work-time day as the Skema save's in-lock clear reads it: the day's
/// intervals, the row's <c>outbox_id</c>, and the <c>event_id</c> of the outbox row with that id
/// (the latest <see cref="WorkTimeRegistered"/> of the day, which the clear names).
/// </summary>
public sealed record CurrentWorkTimeDay(
    DateOnly Date, IReadOnlyList<WorkInterval> Intervals, long OutboxId, Guid RegisteredEventId);

/// <summary>
/// DB-facing surface for the <c>work_time_projection</c> read-model
/// (TASK-5603). Unlike <see cref="TimeEntryProjectionRepository"/> /
/// <see cref="AbsenceProjectionRepository"/> (one row per event, keyed by
/// <c>event_id</c>), this projection is keyed by
/// <c>(employee_id, date)</c> and is LATEST-WINS: re-saving a day emits a NEW
/// <see cref="WorkTimeRegistered"/> event and the upsert overwrites the prior
/// row — but only when the incoming <c>outbox_id</c> is &gt;= the stored one,
/// so an out-of-order replay of a stale event can never clobber a newer row.
///
/// <para><b>A cleared day is kept and marked</b> (owner Q2; addendum A1 D-4). Emptying a day (a
/// <see cref="WorkTimeCleared"/>) does not delete its row: <see cref="ClearDaySql"/> sets
/// <c>cleared_by_outbox_id</c> and <c>outbox_id</c> to the clear's own outbox id. The row is current
/// only while <c>cleared_by_outbox_id</c> is NULL, and every reader of the table counts current rows
/// only. Why keep it: the startup rebuild (<c>ProjectionBackfillService</c>) replays registrations from
/// <c>events</c>, which the publisher fills after commit; a deleted row would let an older
/// registration be replayed over the day, while the kept row's advanced <c>outbox_id</c> refuses it
/// through the upsert's guard. Registering the day again overwrites the row and sets the marker back to
/// NULL.</para>
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
    /// <summary>
    /// The ONE clear of a work-time day, shared verbatim by the live clear (<see cref="ClearDayAsync"/>)
    /// and the from-events rebuild (<c>ProjectionBackfillService</c>), the way the hours projections share
    /// their supersession mark. It marks the day cleared and advances its <c>outbox_id</c> to the clear's:
    /// the upsert's guard is <c>outbox_id &lt;=</c>, so a replayed registration carrying the row's old id
    /// would otherwise pass it and un-clear the day. <c>cleared_by_outbox_id IS NULL</c>: only a current day
    /// is cleared, so a re-run changes nothing. <c>outbox_id &lt;= @outboxId</c>: a clear never retires a day
    /// registered after it; live it always holds (the save holds the employee's consumption lock and
    /// enqueued the clear first), and on a rebuild it keeps a replayed clear off a day whose later
    /// re-registration is not yet published.
    /// </summary>
    internal const string ClearDaySql = @"
        UPDATE work_time_projection
           SET cleared_by_outbox_id = @outboxId,
               outbox_id = @outboxId
         WHERE employee_id = @employeeId
           AND date = @date
           AND cleared_by_outbox_id IS NULL
           AND outbox_id <= @outboxId";

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
    /// row (out-of-order replay safety). The conflict path also sets
    /// <c>cleared_by_outbox_id</c> back to NULL: a day registered again after its clear is current
    /// again (the same statement as the rebuild's copy). The caller commits or rolls back; this
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
                  employee_id, date, intervals,
                  occurred_at, actor_id, actor_role, correlation_id, outbox_id
              ) VALUES (
                  @employeeId, @date, @intervals,
                  @occurredAt, @actorId, @actorRole, @correlationId, @outboxId
              )
              ON CONFLICT (employee_id, date) DO UPDATE SET
                  intervals = EXCLUDED.intervals,
                  occurred_at = EXCLUDED.occurred_at,
                  actor_id = EXCLUDED.actor_id,
                  actor_role = EXCLUDED.actor_role,
                  correlation_id = EXCLUDED.correlation_id,
                  outbox_id = EXCLUDED.outbox_id,
                  cleared_by_outbox_id = NULL
              WHERE work_time_projection.outbox_id <= EXCLUDED.outbox_id",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
        cmd.Parameters.AddWithValue("date", @event.Date);
        cmd.Parameters.Add("intervals", NpgsqlDbType.Jsonb).Value = intervalsJson;
        cmd.Parameters.AddWithValue("occurredAt", occurredAt);
        cmd.Parameters.AddWithValue("actorId", (object?)@event.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorRole", (object?)@event.ActorRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlationId", (object?)@event.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outboxId", outboxId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The current rows of the given days, with each row's <c>outbox_id</c> and the <c>event_id</c> of the
    /// outbox row that id belongs to, on the caller's <paramref name="conn"/> + <paramref name="tx"/>. The Skema
    /// save reads the days it clears here, inside the employee's consumption lock and before its first write,
    /// so the <see cref="WorkTimeCleared"/> names the registration it retires before <see cref="ClearDaySql"/>
    /// advances the row's <c>outbox_id</c> to the clear's own. A day with no current row is absent from the
    /// result. A current row whose <c>outbox_id</c> has no outbox row is a broken invariant (every live and
    /// rebuilt row takes its id from one) and throws.
    /// </summary>
    public async Task<IReadOnlyList<CurrentWorkTimeDay>> GetCurrentDaysAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default)
    {
        if (dates.Count == 0)
            return Array.Empty<CurrentWorkTimeDay>();

        await using var cmd = new NpgsqlCommand(
            @"SELECT w.date, w.intervals, w.outbox_id, o.event_id
              FROM work_time_projection w
              LEFT JOIN outbox_events o ON o.outbox_id = w.outbox_id
              WHERE w.employee_id = @employeeId AND w.date = ANY(@dates)
                AND w.cleared_by_outbox_id IS NULL
              ORDER BY w.date ASC",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.Add(new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date)
        {
            Value = dates.ToArray(),
        });

        var days = new List<CurrentWorkTimeDay>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var date = DateOnly.FromDateTime(reader.GetDateTime(0));
            var outboxId = reader.GetInt64(2);
            if (reader.IsDBNull(3))
                throw new InvalidOperationException(
                    $"work_time_projection row ({employeeId}, {date:yyyy-MM-dd}) holds outbox_id {outboxId}, which no outbox row has.");
            var intervals = JsonSerializer.Deserialize<List<WorkInterval>>(reader.GetString(1), IntervalsJsonOptions)
                ?? new List<WorkInterval>();
            days.Add(new CurrentWorkTimeDay(date, intervals, outboxId, reader.GetGuid(3)));
        }
        return days;
    }

    /// <summary>
    /// The live clear of one work-time day (owner Q2; addendum A1 D-4): <see cref="ClearDaySql"/> with the
    /// clear's <paramref name="clearOutboxId"/>, in the caller's transaction. The caller has enqueued the
    /// <see cref="WorkTimeCleared"/> first, holds the employee's consumption lock, and read the day's current
    /// row on this transaction (<see cref="GetCurrentDaysAsync"/>); so exactly one current row is updated.
    /// Anything else is a broken invariant and throws, which rolls the save back.
    /// </summary>
    public async Task ClearDayAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly date, long clearOutboxId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(ClearDaySql, conn, tx);
        cmd.Parameters.AddWithValue("outboxId", clearOutboxId);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        var cleared = await cmd.ExecuteNonQueryAsync(ct);
        if (cleared != 1)
            throw new InvalidOperationException(
                $"Clearing work-time day ({employeeId}, {date:yyyy-MM-dd}) updated {cleared} rows; exactly one current row was expected.");
    }

    /// <summary>
    /// Self-managed-connection date-range read backing the Skema month GET
    /// <c>workTime</c> field. Returns the per-day latest-wins rows (one per
    /// (employee, date)) with <c>intervals</c> deserialized from JSONB.
    /// Read-your-write is satisfied because the atomic POST handler commits the
    /// upsert in the same transaction as the <see cref="WorkTimeRegistered"/>
    /// event enqueue.
    ///
    /// <para>CURRENT days only (<c>cleared_by_outbox_id IS NULL</c>): a cleared day is kept with its
    /// marker set and is no work-time day to any reader. This read also serves the send's allocation
    /// gate, the year overview and the Skema save's worked figure.</para>
    /// </summary>
    public async Task<IReadOnlyList<WorkTimeProjectionRow>> GetByEmployeeAndDateRangeAsync(
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT employee_id, date, intervals
              FROM work_time_projection
              WHERE employee_id = @employeeId AND date >= @start AND date <= @end
                AND cleared_by_outbox_id IS NULL
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
            });
        }
        return rows;
    }
}
