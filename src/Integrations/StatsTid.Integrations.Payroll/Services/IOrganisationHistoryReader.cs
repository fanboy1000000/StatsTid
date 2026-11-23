namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Payroll's one question about the employee's organisation history (ADR-040 D4): on which days inside a
/// window did the employee's institution change? <see cref="PeriodCalculationService"/> asks it once per
/// calculation, beside the <c>employee_profiles</c> and <c>user_agreement_codes</c> date reads, and cuts the
/// month there: each part then resolves its own institution's record and local profile (ADR-043 D12).
///
/// <para>
/// <b>Why it matters (plain language).</b> An employee who moves from one institution to another on the 15th
/// works the first half of the month under the first institution's local agreement profile and the second
/// half under the new one's. Each part asks its own institution's local-profile history for activation dates (a
/// boundary source), and payroll resolves each part's configuration with that institution and sends it to the
/// engine, so each part is priced under its own institution's rules: paid figures depend on these dates. A month
/// planned as one piece would ask only one of them, so a transfer date splits the month, through the existing
/// <c>EmployeeProfileChange</c> boundary (no boundary kind of its own).
/// </para>
///
/// <para>
/// <b>The rows it reads.</b> <c>user_organisations</c> rows are half-open <c>[effective_from, effective_to)</c>;
/// a person's first row starts at the SQL literal <c>0001-01-01</c>, which Npgsql reads back as
/// <see cref="DateOnly.MinValue"/> (the reasoning is on <c>UserOrganisationRepository.InsertFirstRowAsync</c>).
/// The window's bounds are always real dates, so a "since always" row is never selected, which is right: it is
/// no change inside any period. Coverage is decided per real day or compared in C#, never by SQL equality
/// between two rows' start dates, and nothing resolves at <see cref="DateOnly.MinValue"/>.
/// </para>
///
/// <para>
/// <b>Why an interface (the <see cref="IPriorPeriodApprovalReader"/> precedent).</b> Many calculation fixtures
/// run with no database; a fixture not about transfers passes a fake answering "no transfer dates" and says so
/// in a comment. The dependency is optional in the service's constructor but never silent: a calculation or a
/// planning call on a service without it throws <see cref="InvalidOperationException"/> naming the missing
/// registration, because a month planned without its transfer cuts would be priced under the wrong
/// institution's rules. Production registers <see cref="PostgresOrganisationHistoryReader"/>.
/// </para>
/// </summary>
public interface IOrganisationHistoryReader
{
    /// <summary>
    /// The distinct <c>user_organisations.effective_from</c> dates of <paramref name="employeeId"/> with
    /// <c>afterExclusive &lt; effective_from &lt;= toInclusive</c>, ascending; empty when none. Every row counts,
    /// open or closed: a closed row's start is still a day the institution changed. Read on a connection of its
    /// own, outside any transaction, taking no lock.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> GetEffectiveFromDatesAsync(
        string employeeId, DateOnly afterExclusive, DateOnly toInclusive, CancellationToken ct = default);
}
