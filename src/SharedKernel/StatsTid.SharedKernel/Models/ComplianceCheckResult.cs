namespace StatsTid.SharedKernel.Models;

public sealed class ComplianceCheckResult
{
    public required string RuleId { get; init; }
    public required string EmployeeId { get; init; }
    public required bool Success { get; init; }
    public required IReadOnlyList<ComplianceViolation> Violations { get; init; }
    public required IReadOnlyList<ComplianceViolation> Warnings { get; init; }

    /// <summary>
    /// S147 / QUAL-234 interim (owner ruling OQ-4 = (b), refinement T7) — the days the rest checks
    /// could NOT analyse, so that "no breach" is never shown for a day nobody checked.
    ///
    /// <para>
    /// <b>Why.</b> The 11-hour daily rest check (and the weekly rest-day check beside it) measures
    /// gaps between clock times. A day registered with hours but no start and end time — every day
    /// registered in Skema today — has nothing to measure, so the checks skip it silently and the
    /// result used to look compliant. This list names those days instead; the screens turn it into
    /// one month-level notice ("Hviletid ikke kontrolleret for N dage").
    /// </para>
    ///
    /// <para>
    /// <b>Which days.</b> Each date inside the checked period on which the employee has at least one
    /// entry with hours above zero and NO entry carrying both a start and an end time
    /// (<c>RestPeriodRule</c>). A day with any fully timed entry counts as checked. Dates are
    /// distinct and in ascending order. Empty by default, so every result that does not involve the
    /// rest checks (the hour-ceiling and overtime-governance results, the "no employed day" month)
    /// carries an empty list, never a missing member.
    /// </para>
    ///
    /// <para>
    /// <b>Interim.</b> Retires when the rest check reads Skema's work intervals (decide-track D-2,
    /// S148 stage 0). Payroll never evaluates compliance, so no pay figure or replay marker depends
    /// on it.
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
