namespace StatsTid.Infrastructure;

/// <summary>
/// S146 / TASK-14602 (QUAL-198) — the dated part-time fraction of MANY employees on ONE date, in
/// one bounded read. Built for the manager's team overview, which shows every roster employee's
/// "hours expected this month" and, since QUAL-198, must show each person's OWN norm: weekdays ×
/// <c>DayNorm.ForWeekday(weekly norm, that person's fraction at month end)</c>.
///
/// <para>
/// <b>Why an interface.</b> The team overview treats this read as best-effort, like its
/// <c>awayToday</c> read: if it fails, every row falls back to fraction 1.0 and the failure is
/// logged, rather than the whole page answering 500. That fallback is only provable if a test host
/// can make the read fail on demand — so the read sits behind this interface, registered once in
/// the Backend host's <c>Program.cs</c>, and a test host swaps it with
/// <c>services.RemoveAll&lt;IPartTimeFractionBatchReader&gt;()</c> followed by
/// <c>services.AddSingleton&lt;IPartTimeFractionBatchReader&gt;(new ThrowingReader())</c>
/// (the <c>ComplianceEmploymentWindowTests</c> idiom).
/// </para>
///
/// <para>
/// <b>Contract.</b> The result holds an entry for every requested employee who has an
/// <c>employee_profiles</c> row covering <c>asOfDate</c> under the end-exclusive predicate
/// (ADR-018 D9) — the same predicate <see cref="EmploymentProfileResolver"/> uses — and NO entry
/// for an employee with no covering row. The caller decides what a missing entry means (the team
/// overview applies the same graceful <c>1.0</c> fallback as <c>/summary</c>, ADR-023 D3). It is a
/// pure read: no writes, no events, no clock (the date is the caller's), no employment dates
/// returned.
/// </para>
/// </summary>
public interface IPartTimeFractionBatchReader
{
    /// <summary>
    /// Returns <c>employee_id → part_time_fraction</c> for the <c>employee_profiles</c> row covering
    /// <paramref name="asOfDate"/>, for each of <paramref name="employeeIds"/> that has one. An empty
    /// input returns an empty map without touching the database.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetFractionsAtAsync(
        IReadOnlyCollection<string> employeeIds, DateOnly asOfDate, CancellationToken ct = default);
}
