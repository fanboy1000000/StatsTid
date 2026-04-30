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

    /// <summary>
    /// Segment manifest id for the calculation run (Sprint 20, ADR-016 D10).
    /// Defaults to <see cref="Guid.Empty"/> when no manifest was produced (e.g. legacy callers,
    /// rules outside the segmentation framework, or planner-bypass paths). Populated end-to-end
    /// so audit log payloads and SLS export rows can be correlated back to the manifest used.
    /// Additive and non-required to preserve compatibility with all existing constructors.
    /// </summary>
    public Guid ManifestId { get; init; }
}

public sealed class CalculationLineItem
{
    public required string TimeType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Rate { get; init; }
    public required DateOnly Date { get; init; }
}
