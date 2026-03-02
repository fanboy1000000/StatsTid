namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Represents a correction line for retroactive payroll re-export.
/// Contains both original and corrected amounts, plus the difference
/// that needs to be sent to the payroll system.
/// </summary>
public sealed class CorrectionExportLine
{
    public required string EmployeeId { get; init; }
    public required string WageType { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string OkVersion { get; init; }
    public required decimal OriginalHours { get; init; }
    public required decimal OriginalAmount { get; init; }
    public required decimal CorrectedHours { get; init; }
    public required decimal CorrectedAmount { get; init; }
    public required decimal DifferenceHours { get; init; }
    public required decimal DifferenceAmount { get; init; }
    public string? SourceRuleId { get; init; }
    public string? SourceTimeType { get; init; }
}
