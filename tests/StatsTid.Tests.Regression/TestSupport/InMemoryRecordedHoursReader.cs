using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S145 / TASK-14508-T (R14, R25) — the in-memory <see cref="IRecordedHoursReader"/> the test suites
/// give <see cref="PeriodCalculationService"/>.
///
/// <para>
/// Plain language: until S145 a test handed the month's hours to the calculation as arguments. Payroll
/// now reads the hours itself, so a test hands the SAME hours to this reader instead, and the reader
/// answers payroll's read with them. Each test therefore keeps exactly its former meaning.
/// </para>
///
/// <para>
/// <b>Fidelity to the former inputs.</b> The reader returns every row it was given, in the order given,
/// and applies NO date window and no employee filter: the old arguments reached the calculation unfiltered (its per-segment date
/// filter did the narrowing), so an unfiltered reader reproduces them exactly. Each
/// <see cref="TimeEntry"/> becomes a <see cref="TimeEntryProjectionRow"/> that maps back to the same
/// input: <c>EventId</c> = the entry's <c>SourceStintId</c> when it has one (the production mapping sets
/// <c>SourceStintId</c> from the row's event id), else a fresh deterministic id; <c>OccurredAt</c> = the
/// entry's <c>RegisteredAt</c> (production maps <c>RegisteredAt</c> from <c>OccurredAt</c>); every other
/// field is copied. <c>OutboxId</c> ascends in the order given. Absences map field for field.
/// </para>
///
/// <para>
/// <b>Caveat (Step 5a).</b> "Same input" holds for an existing non-null <c>SourceStintId</c>, which survives.
/// A formerly NULL one becomes a deterministic event id here, so PCS's mapping hands it a
/// <c>SourceStintId</c> it did not have before. A test that needs the normalizer's missing-id fallback
/// must therefore not rely on this reader.
/// </para>
///
/// <para>
/// Built with no rows it is the "empty reader" (a calculation on no hours; a replay that needs a reader
/// to exist but reads nothing).
/// </para>
/// </summary>
internal sealed class InMemoryRecordedHoursReader : IRecordedHoursReader
{
    private readonly List<TimeEntryProjectionRow> _entries = new();
    private readonly List<AbsenceProjectionRow> _absences = new();
    private long _outbox;

    public InMemoryRecordedHoursReader(
        IEnumerable<TimeEntry>? entries = null,
        IEnumerable<AbsenceEntry>? absences = null)
    {
        if (entries is not null)
            foreach (var e in entries) Add(e);
        if (absences is not null)
            foreach (var a in absences) Add(a);
    }

    /// <summary>Adds one time entry as a projection row.</summary>
    public InMemoryRecordedHoursReader Add(TimeEntry e)
    {
        var outbox = ++_outbox;
        _entries.Add(new TimeEntryProjectionRow
        {
            EventId = e.SourceStintId ?? DeterministicId(outbox),
            EmployeeId = e.EmployeeId,
            Date = e.Date,
            Hours = e.Hours,
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            TaskId = e.TaskId,
            ActivityType = e.ActivityType,
            AgreementCode = e.AgreementCode,
            OkVersion = e.OkVersion,
            VoluntaryUnsocialHours = e.VoluntaryUnsocialHours,
            OccurredAt = e.RegisteredAt,
            OutboxId = outbox,
        });
        return this;
    }

    /// <summary>Adds one absence as a projection row.</summary>
    public InMemoryRecordedHoursReader Add(AbsenceEntry a)
    {
        var outbox = ++_outbox;
        _absences.Add(new AbsenceProjectionRow
        {
            EventId = DeterministicId(outbox),
            EmployeeId = a.EmployeeId,
            Date = a.Date,
            AbsenceType = a.AbsenceType,
            Hours = a.Hours,
            AgreementCode = a.AgreementCode,
            OkVersion = a.OkVersion,
            OccurredAt = DateTime.UnixEpoch,
            OutboxId = outbox,
        });
        return this;
    }

    public Task<RecordedHours> ReadCurrentAsync(
        string employeeId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct) =>
        Task.FromResult(new RecordedHours(
            _entries.OrderBy(r => r.OutboxId).ToList(),
            _absences.OrderBy(r => r.OutboxId).ToList()));

    public Task<RecordedHours> ReadByEventIdsAsync(
        string employeeId,
        IReadOnlyList<Guid> entryEventIds,
        IReadOnlyList<Guid> absenceEventIds,
        CancellationToken ct) =>
        Task.FromResult(new RecordedHours(
            _entries.Where(r => entryEventIds.Contains(r.EventId)).ToList(),
            _absences.Where(r => absenceEventIds.Contains(r.EventId)).ToList()));

    // Deterministic, unique per reader row: the same test always reads the same event ids.
    private static Guid DeterministicId(long outbox)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(outbox).CopyTo(bytes, 0);
        bytes[15] = 0x14;
        return new Guid(bytes);
    }
}
