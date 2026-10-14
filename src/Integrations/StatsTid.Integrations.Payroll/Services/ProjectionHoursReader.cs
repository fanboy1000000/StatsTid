using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// <b>Read-only.</b> Payroll never writes these tables (ADR-034 D4 precedent; ADR-016 amendment). Both
/// reads below are plain SELECTs on their own connection, outside any payroll transaction.
/// </para>
///
/// <para>
/// <b>Two reads, two snapshots.</b> The entry and absence rows are read one after the other, each on its
/// own connection, so a save landing between them can be seen by one read and not the other. The
/// calculation keeps the rows it actually received, so what it consumed stays provable either way.
/// </para>
/// </summary>
public sealed class ProjectionHoursReader : IRecordedHoursReader
{
    private readonly TimeEntryProjectionRepository _timeEntries;
    private readonly AbsenceProjectionRepository _absences;

    public ProjectionHoursReader(
        TimeEntryProjectionRepository timeEntries,
        AbsenceProjectionRepository absences)
    {
        ArgumentNullException.ThrowIfNull(timeEntries);
        ArgumentNullException.ThrowIfNull(absences);
        _timeEntries = timeEntries;
        _absences = absences;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Both repositories' date-range reads filter to current rows (<c>superseded_by_outbox_id IS NULL</c>,
    /// S145 TASK-14507) and order by outbox id. The bounds are passed through unchanged: the caller owns
    /// the window (a calculation reads from the day before its opening week's Monday).
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

        var entries = await _timeEntries.GetByEmployeeAndDateRangeAsync(employeeId, fromInclusive, toInclusive, ct);
        var absences = await _absences.GetByEmployeeAndDateRangeAsync(employeeId, fromInclusive, toInclusive, ct);
        return new RecordedHours(entries, absences);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The repositories' by-id reads are the only unfiltered reads of the two tables (current OR
    /// superseded rows); an empty id list returns an empty list without a query.
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
    /// repositories are stateless over the host's <c>DbConnectionFactory</c>, which the host registers).
    /// Idempotent: a second call adds nothing.
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
