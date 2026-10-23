namespace StatsTid.SharedKernel.Models;

public sealed class PeriodCalculationResult
{
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required IReadOnlyList<CalculationResult> RuleResults { get; init; }
    public required IReadOnlyList<PayrollExportLine> ExportLines { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>S146 flex ledger: the balance the period opened at. Optional; null when not computed.</summary>
    public decimal? OpeningFlexBalance { get; init; }

    /// <summary>S146 flex ledger: the balance the period closed at. Optional; null when not computed.</summary>
    public decimal? ClosingFlexBalance { get; init; }

    /// <summary>
    /// S146 flex ledger: where the opening came from (<c>LEDGER_START</c>, <c>CHAINED</c>,
    /// <c>REHIRE</c>, or <c>UNTRACKED</c> - allowed on a result, never stored on a record).
    /// Optional; null when not computed.
    /// </summary>
    public string? FlexProvenance { get; init; }
}
