namespace StatsTid.SharedKernel.Models;

public sealed class ComplianceCheckResult
{
    public required string RuleId { get; init; }
    public required string EmployeeId { get; init; }
    public required bool Success { get; init; }
    public required IReadOnlyList<ComplianceViolation> Violations { get; init; }
    public required IReadOnlyList<ComplianceViolation> Warnings { get; init; }
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
    WEEKLY_MAX_HOURS
}

public enum ComplianceSeverity
{
    WARNING,
    VIOLATION
}
