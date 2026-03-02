namespace StatsTid.SharedKernel.Models;

public sealed class PeriodCalculationResult
{
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required List<CalculationResult> RuleResults { get; init; }
    public required List<PayrollExportLine> ExportLines { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
