using Npgsql;
using NpgsqlTypes;
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
///
/// <para><b>S145 / TASK-14507 (A0, QUAL-192/193) — a Skema save replaces the cell.</b> A Skema
/// absence cell is one employee, one day, one absence type. Saving it again appends a new event and
/// a new row and marks the earlier Skema row of the cell <c>superseded_by_outbox_id</c> = the new
/// row's <c>outbox_id</c>. Superseded rows are kept for replay (<see cref="GetByEventIdsAsync"/>);
/// every total, including the vacation settlement and the quota, counts CURRENT rows only. An
/// additive row (source <c>TIME_POST</c>) is never marked and never marks.</para>
/// </summary>
public sealed class AbsenceProjectionRepository
{
    /// <summary>
    /// S145 / TASK-14507 — the ONE supersession mark for an absence cell, shared verbatim by the
    /// live <see cref="InsertAsync"/> and the from-events rebuild (<c>ProjectionBackfillService</c>).
    /// It runs BEFORE the new row's INSERT (the partial unique index
    /// <c>ux_absences_proj_current_skema_cell</c> admits one current Skema row per cell). The
    /// clauses mirror the time-entry mark: the cell; Skema rows only; not already retired
    /// (idempotent re-run); only by a LATER write (<c>outbox_id &lt; @outboxId</c>).
    /// <c>absence_type</c> is NOT NULL, so plain equality matches the index exactly.
    /// </summary>
    internal const string MarkSupersededSkemaCellSql = @"
        UPDATE absences_projection
           SET superseded_by_outbox_id = @outboxId
         WHERE employee_id = @employeeId
           AND date = @date
           AND absence_type = @absenceType
           AND source = 'SKEMA'
           AND superseded_by_outbox_id IS NULL
           AND outbox_id < @outboxId";

    // S145 / TASK-14507 — the current Skema rows in a date range: the predecessors a save is about
    // to replace. Used by GetCurrentSkemaRowsAsync below.
    private const string SelectCurrentSkemaRowsSql = @"
        SELECT event_id, employee_id, date, absence_type, hours, feriedage,
               agreement_code, ok_version, occurred_at, actor_id, actor_role,
               correlation_id, outbox_id, source, superseded_by_outbox_id
          FROM absences_projection
         WHERE employee_id = @employeeId AND date >= @start AND date <= @end
           AND source = 'SKEMA'
           AND superseded_by_outbox_id IS NULL
         ORDER BY outbox_id ASC";

    // EA MD-13 (mutation only): the mark without the source = SKEMA restriction.
    private const string EaMd13MarkAllSql = @"UPDATE absences_projection SET superseded_by_outbox_id = @outboxId WHERE employee_id = @employeeId AND date = @date AND absence_type = @absenceType AND superseded_by_outbox_id IS NULL AND outbox_id < @outboxId";

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
    ///
    /// <para>S145 / TASK-14507 — the row's <c>source</c> is <see cref="HoursSource.Resolve"/> of the
    /// event's <c>Source</c> (null is <c>TIME_POST</c>). For a <c>SKEMA</c> row the cell's current
    /// Skema row is marked superseded FIRST (<see cref="MarkSupersededSkemaCellSql"/>), then the new
    /// row is inserted, both in the caller's transaction. A <c>TIME_POST</c> row skips the mark.
    /// The balance consequence of a replacement (the signed quota delta) is the caller's: this
    /// method writes the projection only.</para>
    /// </summary>
    public async Task InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        AbsenceRegistered @event, long outboxId, CancellationToken ct = default)
    {
        var source = HoursSource.Resolve(@event.Source);
        if (source.Length >= 0) // EA MD-13
        {
            await using var markCmd = new NpgsqlCommand(EaMd13MarkAllSql, conn, tx);
            markCmd.Parameters.AddWithValue("outboxId", outboxId);
            markCmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
            markCmd.Parameters.AddWithValue("date", @event.Date);
            markCmd.Parameters.AddWithValue("absenceType", @event.AbsenceType);
            await markCmd.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO absences_projection (
                  event_id, employee_id, date, absence_type, hours, feriedage,
                  agreement_code, ok_version, occurred_at, actor_id, actor_role,
                  correlation_id, outbox_id, source
              ) VALUES (
                  @eventId, @employeeId, @date, @absenceType, @hours, @feriedage,
                  @agreementCode, @okVersion, @occurredAt, @actorId, @actorRole,
                  @correlationId, @outboxId, @source
              )",
            conn, tx);
        cmd.Parameters.AddWithValue("eventId", @event.EventId);
        cmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
        cmd.Parameters.AddWithValue("date", @event.Date);
        cmd.Parameters.AddWithValue("absenceType", @event.AbsenceType);
        cmd.Parameters.AddWithValue("hours", @event.Hours);
        // ADR-032 D2: write the authoritative per-absence feriedage verbatim from the
        // event payload. Nullable passthrough — NO fallback computed here: the live
        // write path always supplies it (post-TASK-6603); a null payload (legacy emitter)
        // stays NULL until the legacy backfill (init.sql) / ProjectionBackfillService
        // materializes it. One valuation site, no drift.
        cmd.Parameters.AddWithValue("feriedage", (object?)@event.Feriedage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("agreementCode", @event.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", @event.OkVersion);
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
    /// period endpoints. Uses <c>idx_absences_proj_emp_date_outbox</c>
    /// (init.sql:1218-1219). <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering across rows on the same date.
    ///
    /// <para>S145 / TASK-14507 (R20) — CURRENT rows only (<c>superseded_by_outbox_id IS NULL</c>):
    /// a re-saved Skema cell is counted once, at its latest value. This also scopes the ADR-032 D4
    /// revaluation enumeration (<c>EmployeeProfileEndpoints</c>) to current rows, which is the set
    /// <c>entitlement_balances.used</c> counts.</para>
    /// </summary>
    public async Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAndDateRangeAsync(
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
    /// can take the absence rows and the time-entry rows from ONE snapshot (one REPEATABLE READ
    /// transaction): a Skema save writes both projections in one transaction, and two reads on two
    /// connections could otherwise see it half-applied. Same SQL, same filter, same order (the one query
    /// below serves both overloads). The caller owns the transaction; this method neither commits nor
    /// rolls back. Precedent: <see cref="GetCurrentSkemaRowsAsync"/>.
    /// </summary>
    public Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAndDateRangeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        return QueryCurrentByDateRangeAsync(conn, tx, employeeId, start, end, ct);
    }

    // The ONE current-rows date-range query behind both GetByEmployeeAndDateRangeAsync overloads
    // (a null tx only from the self-connecting overload, which runs it outside any transaction).
    private static async Task<IReadOnlyList<AbsenceProjectionRow>> QueryCurrentByDateRangeAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id, source, superseded_by_outbox_id
              FROM absences_projection
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
    /// Full-stream read backing Time GET <c>/api/time-entries/{employeeId}/absences</c>.
    /// Uses <c>idx_absences_proj_emp_outbox</c> (init.sql:1220-1221),
    /// the no-date-filter index. <c>ORDER BY outbox_id ASC</c> preserves
    /// per-employee monotonic ordering.
    ///
    /// <para>S145 / TASK-14507 (R20) — CURRENT rows only, like
    /// <see cref="GetByEmployeeAndDateRangeAsync"/>.</para>
    /// </summary>
    public async Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id, source, superseded_by_outbox_id
              FROM absences_projection
              WHERE employee_id = @employeeId
                AND superseded_by_outbox_id IS NULL
              ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadRowsAsync(cmd, ct);
    }

    /// <summary>
    /// S145 / TASK-14507 (A0 constraint 5) — the rows with exactly these event ids, current OR
    /// superseded, ordered by <c>outbox_id</c>. The ONLY unfiltered read of the table; it exists so
    /// a calculation can be replayed from the rows it recorded even after a later re-save of the
    /// cell. An id with no row is simply absent from the result. Never use this read to total
    /// hours or feriedage.
    /// </summary>
    public async Task<IReadOnlyList<AbsenceProjectionRow>> GetByEventIdsAsync(
        string employeeId, IReadOnlyList<Guid> eventIds, CancellationToken ct = default)
    {
        if (eventIds.Count == 0)
            return Array.Empty<AbsenceProjectionRow>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, actor_id, actor_role,
                     correlation_id, outbox_id, source, superseded_by_outbox_id
              FROM absences_projection
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

    /// <summary>
    /// S145 / TASK-14507 (R23) — the CURRENT Skema rows of the employee dated in
    /// <c>[start, end]</c>: the rows a Skema save is about to replace, read so the save can move the
    /// quota by the SIGNED difference (new feriedage minus the stored feriedage of the replaced row)
    /// instead of adding the full new value again. Additive (<c>TIME_POST</c>) rows are never
    /// replaced, so they are not returned.
    ///
    /// <para>Reads on the caller's <paramref name="conn"/> + <paramref name="tx"/>. The Skema save
    /// calls it twice: in its advisory pre-check, on a short read-only REPEATABLE READ snapshot that
    /// the pre-check's balance reads share (so the two agree); and AFTER it took the employee's
    /// consumption lock, inside the write transaction (the authoritative read).</para>
    /// </summary>
    public async Task<IReadOnlyList<AbsenceProjectionRow>> GetCurrentSkemaRowsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(SelectCurrentSkemaRowsSql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
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
        // ADR-032 D2: nullable feriedage passthrough (pre-S66 rows read NULL until backfilled).
        Feriedage = reader.IsDBNull(reader.GetOrdinal("feriedage"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("feriedage")),
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
        OutboxId = reader.GetInt64(reader.GetOrdinal("outbox_id")),
        Source = reader.GetString(reader.GetOrdinal("source")),
        SupersededByOutboxId = reader.IsDBNull(reader.GetOrdinal("superseded_by_outbox_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("superseded_by_outbox_id"))
    };
}
