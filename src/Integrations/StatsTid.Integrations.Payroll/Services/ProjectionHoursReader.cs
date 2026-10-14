using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14508 (rulings R14 and R25) — the production <see cref="IRecordedHoursReader"/>: payroll's
/// read of the employee's recorded hours from the Backend-owned read models, through the existing
/// Infrastructure repositories.
///
/// <para>
/// <b>The only Payroll type that touches the two hours repositories</b> (R25). Under
/// <c>src/Integrations/**</c> no other file may name them — not an endpoint, not
/// <see cref="PeriodCalculationService"/>, not the host's <c>Program.cs</c>. That is why the host
/// registration lives in this file too (<see cref="ProjectionHoursReaderRegistration.AddProjectionHoursReader"/>,
/// ruling R58): the host's own pattern is a bare <c>AddSingleton&lt;Repo&gt;()</c> in <c>Program.cs</c>, and
/// that file also holds the endpoints, so allow-listing it would let an endpoint read the hours tables
/// directly. The guard's allowlist therefore stays this one file.
/// </para>
///
/// <para>
/// <b>Read-only.</b> Payroll never writes these tables (ADR-034 D4 precedent; ADR-016 amendment). Every
/// read below is a plain SELECT; none runs inside a payroll write transaction.
/// </para>
///
/// <para>
/// <b>One snapshot for the current-rows read</b> (S145 TASK-14508 fix-up 2, QUAL-200; reviewed, not
/// pinned — it is a race). <see cref="ReadCurrentAsync"/> takes the time-entry rows and the absence rows
/// from ONE read-only REPEATABLE READ transaction on one connection, so both row sets describe the same
/// committed state of the database. A Skema save writes both projections in one transaction; read on two
/// connections, a save committing between the two reads could be seen half-applied — the hours from
/// before it with the absence from after it, the day paid as both — and the calculation's audit trail
/// would then prove a state the database never held. In one snapshot, a save is seen whole or not at
/// all: a save that commits after the snapshot was taken is simply not seen. That is plain staleness —
/// the calculation used the hours as they stood — which the HR worklist's follow-up of later changes
/// covers, and which the recorded binding of the rows read makes provable: since TASK-14513 (S145) PCS
/// freezes the event ids and digests of exactly these rows in the manifest's <c>PlanInputs</c>, and replay
/// re-reads them by id and refuses on any difference. The snapshot takes no row lock and
/// no advisory lock (only the ACCESS SHARE table lock any read takes), so it never blocks or waits on a
/// save; nothing else runs inside it, and it is disposed as soon as the two reads return. Same pattern as
/// the Skema save's quota pre-check (TASK-14507).
/// </para>
/// </summary>
public sealed class ProjectionHoursReader : IRecordedHoursReader
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly TimeEntryProjectionRepository _timeEntries;
    private readonly AbsenceProjectionRepository _absences;

    public ProjectionHoursReader(
        DbConnectionFactory connectionFactory,
        TimeEntryProjectionRepository timeEntries,
        AbsenceProjectionRepository absences)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(timeEntries);
        ArgumentNullException.ThrowIfNull(absences);
        _connectionFactory = connectionFactory;
        _timeEntries = timeEntries;
        _absences = absences;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Both repositories' date-range reads filter to current rows (<c>superseded_by_outbox_id IS NULL</c>,
    /// S145 TASK-14507) and order by outbox id. The bounds are passed through unchanged: the caller owns
    /// the window (a calculation reads from the day before its opening week's Monday). Both reads run in
    /// ONE read-only REPEATABLE READ snapshot (see the class remarks, QUAL-200).
    /// </remarks>
    public async Task<RecordedHours> ReadCurrentAsync(
        string employeeId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeId);
        if (toInclusive < fromInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toInclusive),
                $"The hours read window is empty: toInclusive ({toInclusive:yyyy-MM-dd}) is before " +
                $"fromInclusive ({fromInclusive:yyyy-MM-dd}).");
        }

        // QUAL-200 — ONE snapshot for both row sets. REPEATABLE READ fixes the snapshot at the first
        // SELECT, so the time-entry read and the absence read below see the same committed state: a
        // Skema save (which writes both projections in one transaction) is seen whole or not at all.
        // SET TRANSACTION READ ONLY is the first statement (it takes no snapshot itself). No row or
        // advisory lock is taken, nothing but these two reads runs inside, and the transaction is
        // disposed (rolled back — it wrote nothing) as soon as they return.
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using (var readOnlyCmd = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
            await readOnlyCmd.ExecuteNonQueryAsync(ct);

        var entries = await _timeEntries.GetByEmployeeAndDateRangeAsync(
            conn, tx, employeeId, fromInclusive, toInclusive, ct);
        var absences = await _absences.GetByEmployeeAndDateRangeAsync(
            conn, tx, employeeId, fromInclusive, toInclusive, ct);
        return new RecordedHours(entries, absences);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The repositories' by-id reads are the only unfiltered reads of the two tables (current OR
    /// superseded rows); an empty id list returns an empty list without a query. Unlike
    /// <see cref="ReadCurrentAsync"/>, these two reads need no shared snapshot: a row read by event id is
    /// immutable in every field the calculation consumes — the only columns that change after insert are
    /// <c>superseded_by_outbox_id</c> (set once, by a later re-save of the cell) and the absence row's
    /// <c>feriedage</c> (the ADR-032 vacation-day revaluation), and neither is a calculation input — so two
    /// reads at different moments return exactly the inputs one snapshot would.
    /// </remarks>
    public async Task<RecordedHours> ReadByEventIdsAsync(
        string employeeId,
        IReadOnlyList<Guid> entryEventIds,
        IReadOnlyList<Guid> absenceEventIds,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeId);
        ArgumentNullException.ThrowIfNull(entryEventIds);
        ArgumentNullException.ThrowIfNull(absenceEventIds);

        var entries = await _timeEntries.GetByEventIdsAsync(employeeId, entryEventIds, ct);
        var absences = await _absences.GetByEventIdsAsync(employeeId, absenceEventIds, ct);
        return new RecordedHours(entries, absences);
    }
}

/// <summary>
/// S145 / TASK-14508 (ruling R58) — the host registration of payroll's hours read, kept in the same file
/// as <see cref="ProjectionHoursReader"/> so that this file stays the only one under
/// <c>src/Integrations/**</c> that names the two hours repositories.
/// </summary>
public static class ProjectionHoursReaderRegistration
{
    /// <summary>
    /// Registers the time-entry and absence read-model repositories and
    /// <see cref="IRecordedHoursReader"/> → <see cref="ProjectionHoursReader"/>, all singletons (the
    /// repositories, and the reader's one-snapshot read, are stateless over the host's
    /// <c>DbConnectionFactory</c>, which the host registers). Idempotent: a second call adds nothing.
    /// </summary>
    public static IServiceCollection AddProjectionHoursReader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TimeEntryProjectionRepository>();
        services.TryAddSingleton<AbsenceProjectionRepository>();
        services.TryAddSingleton<IRecordedHoursReader, ProjectionHoursReader>();
        return services;
    }
}
