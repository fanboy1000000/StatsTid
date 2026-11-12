namespace StatsTid.SharedKernel.Models;

public sealed class ComplianceCheckResult
{
    public required string RuleId { get; init; }
    public required string EmployeeId { get; init; }
    public required bool Success { get; init; }
    public required IReadOnlyList<ComplianceViolation> Violations { get; init; }
    public required IReadOnlyList<ComplianceViolation> Warnings { get; init; }

    /// <summary>
    /// The days the rest checks could not fully measure (QUAL-234), so that "no breach" is never
    /// shown for a day whose hours are not all on the clock.
    ///
    /// <para>
    /// <b>Why.</b> The 11-hour daily rest check (and the weekly rest-day check beside it) measures
    /// gaps between the clock intervals the employee types in Skema (ADR-043 D10). A day whose
    /// project hours are not all covered by intervals may hide work the checks cannot place, so the
    /// result would look compliant for it. This list names those days instead; the screens turn it
    /// into one month-level notice ("Hviletid ikke kontrolleret for N dage").
    /// </para>
    ///
    /// <para>
    /// <b>Which days.</b> Each date inside the checked period whose project hours are above zero and
    /// whose interval hours fall short of them, both rounded to hundredths (<c>RestPeriodRule</c>).
    /// A day whose intervals cover its project hours counts as checked. Dates are distinct and in
    /// ascending order. Empty by default, so every result that does not involve the rest checks
    /// (the hour-ceiling and overtime-governance results, the "no employed day" month) carries an
    /// empty list, never a missing member.
    /// </para>
    ///
    /// <para>
    /// <b>Stays (owner ruling R-22).</b> The send check refuses a month unless every day's interval
    /// hours equal its project hours, so the list can be non-empty only for a month still being
    /// edited. Payroll never evaluates compliance, so no pay figure or replay marker depends on it.
    /// </para>
    /// </summary>
    public IReadOnlyList<DateOnly> RestNotCheckedDays { get; init; } = Array.Empty<DateOnly>();
}

public sealed class ComplianceViolation
{
    public required ComplianceViolationType ViolationType { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal ActualValue { get; init; }
    public required decimal ThresholdValue { get; init; }
    public required ComplianceSeverity Severity { get; init; }
    public bool IsVoluntaryExempt { get; init; }
    public required string Message { get; init; }
}

public enum ComplianceViolationType
{
    DAILY_REST,
    WEEKLY_REST,
    MAX_DAILY_HOURS,
    WEEKLY_MAX_HOURS,
    OVERTIME_EXCEEDED,
    OVERTIME_UNAPPROVED
}

public enum ComplianceSeverity
{
    WARNING,
    VIOLATION
}
