using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Service that re-runs PeriodCalculationService for a past period and produces
/// correction lines by diffing against a previous export. Supports retroactive
/// recalculation with full traceability and event sourcing.
/// </summary>
public sealed class RetroactiveCorrectionService
{
    private readonly PeriodCalculationService _calculationService;
    private readonly IEventStore _eventStore;
    private readonly ILogger<RetroactiveCorrectionService> _logger;

    public RetroactiveCorrectionService(
        PeriodCalculationService calculationService,
        IEventStore eventStore,
        ILogger<RetroactiveCorrectionService> logger)
    {
        _calculationService = calculationService;
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<RetroactiveCorrectionResult> RecalculateAsync(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlexBalance,
        IReadOnlyList<PayrollExportLine> previousExportLines,
        string reason,
        string actorId,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        Guid? idempotencyToken = null,
        CancellationToken ct = default)
    {
        // 1. Re-run calculation for the period
        var newResult = await _calculationService.CalculateAsync(
            profile, entries, absences, periodStart, periodEnd,
            previousFlexBalance, authorizationHeader, correlationId, ct);

        if (!newResult.Success)
        {
            return new RetroactiveCorrectionResult
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CorrectionLines = [],
                Success = false,
                ErrorMessage = $"Recalculation failed: {newResult.ErrorMessage}"
            };
        }

        // 2. Diff new export lines against previous
        var correctionLines = ProduceCorrectionLines(
            profile, previousExportLines, newResult.ExportLines, periodStart, periodEnd);

        // 3. Emit RetroactiveCorrectionRequested event (non-fatal if fails)
        try
        {
            var correctionEvent = new RetroactiveCorrectionRequested
            {
                EmployeeId = profile.EmployeeId,
                OriginalPeriodStart = periodStart,
                OriginalPeriodEnd = periodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = profile.OkVersion,
                Reason = reason,
                CorrectedByActorId = actorId,
                CorrectionLineCount = correctionLines.Count,
                TotalDifferenceHours = correctionLines.Sum(l => Math.Abs(l.DifferenceHours)),
                CorrelationId = correlationId,
                IdempotencyToken = idempotencyToken
            };

            await _eventStore.AppendAsync(
                $"retro-correction-{profile.EmployeeId}-{periodStart:yyyy-MM-dd}",
                correctionEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit RetroactiveCorrectionRequested event for {EmployeeId}", profile.EmployeeId);
        }

        _logger.LogInformation(
            "Retroactive correction for {EmployeeId} period {PeriodStart}-{PeriodEnd}: {LineCount} correction lines",
            profile.EmployeeId, periodStart, periodEnd, correctionLines.Count);

        return new RetroactiveCorrectionResult
        {
            EmployeeId = profile.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CorrectionLines = correctionLines,
            Success = true
        };
    }

    private static IReadOnlyList<CorrectionExportLine> ProduceCorrectionLines(
        EmploymentProfile profile,
        IReadOnlyList<PayrollExportLine> previousLines,
        IReadOnlyList<PayrollExportLine> newLines,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var corrections = new List<CorrectionExportLine>();

        // Index previous lines by wage type
        var previousByWageType = previousLines
            .GroupBy(l => l.WageType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Index new lines by wage type
        var newByWageType = newLines
            .GroupBy(l => l.WageType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // All wage types from both sets
        var allWageTypes = previousByWageType.Keys
            .Union(newByWageType.Keys)
            .Distinct();

        foreach (var wageType in allWageTypes)
        {
            var prevItems = previousByWageType.GetValueOrDefault(wageType, []);
            var newItems = newByWageType.GetValueOrDefault(wageType, []);

            var originalHours = prevItems.Sum(l => l.Hours);
            var originalAmount = prevItems.Sum(l => l.Amount);
            var correctedHours = newItems.Sum(l => l.Hours);
            var correctedAmount = newItems.Sum(l => l.Amount);
            var diffHours = correctedHours - originalHours;
            var diffAmount = correctedAmount - originalAmount;

            // Only produce correction line if there's a difference
            if (diffHours != 0 || diffAmount != 0)
            {
                // Pick traceability from new lines if available, else from previous
                var sourceItem = newItems.FirstOrDefault() ?? prevItems.FirstOrDefault();

                corrections.Add(new CorrectionExportLine
                {
                    EmployeeId = profile.EmployeeId,
                    WageType = wageType,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    OkVersion = profile.OkVersion,
                    OriginalHours = originalHours,
                    OriginalAmount = originalAmount,
                    CorrectedHours = correctedHours,
                    CorrectedAmount = correctedAmount,
                    DifferenceHours = diffHours,
                    DifferenceAmount = diffAmount,
                    SourceRuleId = sourceItem?.SourceRuleId,
                    SourceTimeType = sourceItem?.SourceTimeType
                });
            }
        }

        return corrections;
    }
}

public sealed class RetroactiveCorrectionResult
{
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required IReadOnlyList<CorrectionExportLine> CorrectionLines { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
