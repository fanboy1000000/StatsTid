namespace StatsTid.SharedKernel.Models;

public sealed class PayrollExportLine
{
    public required string EmployeeId { get; init; }
    public required string WageType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Amount { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string OkVersion { get; init; }
    public string? SourceRuleId { get; init; }
    public string? SourceTimeType { get; init; }
}
