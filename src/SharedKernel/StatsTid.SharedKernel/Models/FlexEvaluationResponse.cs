namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Typed response DTO for the flex evaluation endpoint.
/// Includes standard CalculationResult fields (PAT-006) plus flex-specific fields.
/// </summary>
public sealed class FlexEvaluationResponse
{
    // Standard fields (PAT-006: unified response format)
    public required string RuleId { get; init; }
    public required string EmployeeId { get; init; }
    public required bool Success { get; init; }
    public required List<CalculationLineItem> LineItems { get; init; }
    public string? ErrorMessage { get; init; }

    // Flex-specific fields
    public required decimal PreviousBalance { get; init; }
    public required decimal NewBalance { get; init; }
    public required decimal Delta { get; init; }
    public required decimal WorkedHours { get; init; }
    public required decimal AbsenceNormCredits { get; init; }
    public required decimal EffectiveNorm { get; init; }
    public required decimal ExcessForPayout { get; init; }
}
