using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14508 (rulings R14 and R25) — the ONE way payroll obtains an employee's recorded hours.
///
/// <para>
/// <b>Why it exists (plain language).</b> Until S145 the two calculating routes were handed the month's
/// hours in the request body. Nobody sent them in practice, and being handed them made two things
/// impossible: noticing that a caller had left out a day (an absent entry looks exactly like a day not
/// worked — for example a context day of the month's first week, which lies in the previous month), and
/// proving a payslip later (a replay that has to be re-fed its inputs proves nothing). Payroll now reads
/// the recorded hours itself, through this seam, and a request that still carries hours is refused.
/// </para>
///
/// <para>
/// <b>What it reads.</b> The two hours read models the Backend writes (time entries and absences, one
/// row per registration), under a declared READ-ONLY cross-context
/// contract (the ADR-034 D4 precedent, recorded in ADR-016's amendment): payroll never writes them. Each
/// row is keyed by the immutable event id of the registration it came from and ordered by the global
/// outbox id; the hours a row carries never change. A re-saved Skema cell keeps its earlier row, marked
/// superseded, so a fresh calculation reads the CURRENT rows while a replay can still re-read by id.
/// </para>
///
/// <para>
/// <b>Why an interface.</b> The production implementation is <see cref="ProjectionHoursReader"/>, and it
/// is the only Payroll type allowed to touch the two read models' repositories (R25; TASK-14514's
/// hours-repository access guard holds this in CI once it lands). Those repositories are concrete classes over a database connection, so
/// a unit-level calculation could not otherwise be fed; a test supplies an in-memory reader instead. Same
/// shape as the <see cref="IRuleClassificationProvider"/> seam.
/// </para>
///
/// <para>
/// <b>Never silent.</b> <see cref="PeriodCalculationService"/> takes this as an optional constructor
/// dependency (unit fixtures that only plan need none), but any calculation or replay without one throws
/// <see cref="InvalidOperationException"/> naming the registration before it reads anything: a calculation
/// on no hours would pay the month as if nothing had been worked.
/// </para>
/// </summary>
public interface IRecordedHoursReader
{
    /// <summary>
    /// The employee's CURRENT rows dated in [<paramref name="fromInclusive"/>, <paramref name="toInclusive"/>]:
    /// the current row of every Skema cell (a re-saved cell's earlier row is superseded and not returned)
    /// plus every additive time-entry registration. Each list is ordered by outbox id and returned exactly
    /// as read; the caller keeps the rows as read so it can bind and digest what it consumed.
    /// </summary>
    Task<RecordedHours> ReadCurrentAsync(
        string employeeId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct);

    /// <summary>
    /// The employee's rows with exactly these event ids, CURRENT OR SUPERSEDED, ordered by outbox id: the
    /// replay read, which must find the rows a calculation recorded even after the employee later re-saved
    /// the cell. An id with no row is simply absent from the result; the caller decides whether that is a
    /// refusal. Never use this read to total hours for a fresh calculation.
    /// </summary>
    Task<RecordedHours> ReadByEventIdsAsync(
        string employeeId,
        IReadOnlyList<Guid> entryEventIds,
        IReadOnlyList<Guid> absenceEventIds,
        CancellationToken ct);
}

/// <summary>
/// S145 / TASK-14508 — the recorded hours one read returned: the time-entry rows and the absence rows,
/// exactly as read (event id, hours, dates, outbox id). They are kept unmapped so that the calculation
/// that consumed them can bind and digest precisely those rows (TASK-14513).
/// </summary>
/// <param name="Entries">The time-entry rows, ordered by outbox id.</param>
/// <param name="Absences">The absence rows, ordered by outbox id.</param>
public sealed record RecordedHours(
    IReadOnlyList<TimeEntryProjectionRow> Entries,
    IReadOnlyList<AbsenceProjectionRow> Absences);
