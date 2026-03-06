namespace StatsTid.SharedKernel.Models;

public sealed class CalculationResult
{
    public required string RuleId { get; init; }
    public required string EmployeeId { get; init; }
    public required bool Success { get; init; }
    public required List<CalculationLineItem> LineItems { get; init; }
    public string? ErrorMessage { get; init; }

    // Norm period metadata (populated by NormCheckRule)
    public int? NormPeriodWeeks { get; init; }
    public decimal? NormHoursTotal { get; init; }
    public decimal? ActualHoursTotal { get; init; }
    public decimal? Deviation { get; init; }
    public bool? NormFulfilled { get; init; }
}

public sealed class CalculationLineItem
{
    public required string TimeType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Rate { get; init; }
    public required DateOnly Date { get; init; }
}
