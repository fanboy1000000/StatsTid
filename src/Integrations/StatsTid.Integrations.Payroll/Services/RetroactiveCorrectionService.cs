using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Service that re-runs PeriodCalculationService for a past period and produces
/// correction lines by diffing against a previous export. Supports retroactive
/// recalculation with full traceability and event sourcing.
///
/// When an OK version transition occurs mid-period (e.g., OK24 → OK26 on Jan 28),
/// the service splits entries by the transition date and recalculates each segment
/// under its respective OK version config (ADR-003: entry-date resolution).
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
        DateOnly? okTransitionDate = null,
        string? previousOkVersion = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<PayrollExportLine> allNewExportLines;
        IReadOnlyList<CalculationResult> allRuleResults;
        decimal? flexDelta = null;

        if (okTransitionDate.HasValue && previousOkVersion is not null)
        {
            // ---------------------------------------------------------------
            // OK version split: recalculate two segments
            // ---------------------------------------------------------------
            var splitResult = await RecalculateWithVersionSplitAsync(
                profile, entries, absences, periodStart, periodEnd,
                previousFlexBalance, okTransitionDate.Value, previousOkVersion,
                authorizationHeader, correlationId, ct);

            if (!splitResult.Success)
            {
                return new RetroactiveCorrectionResult
                {
                    EmployeeId = profile.EmployeeId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    CorrectionLines = [],
                    Success = false,
                    ErrorMessage = splitResult.ErrorMessage
                };
            }

            allNewExportLines = splitResult.ExportLines;
            allRuleResults = splitResult.RuleResults;
            flexDelta = splitResult.FlexDelta;
        }
        else
        {
            // ---------------------------------------------------------------
            // Single version: existing behavior
            // ---------------------------------------------------------------
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

            allNewExportLines = newResult.ExportLines;
            allRuleResults = newResult.RuleResults;

            // Extract flex delta from single-version calculation
            flexDelta = ExtractFlexDelta(newResult.RuleResults);
        }

        // 2. Diff new export lines against previous
        var correctionLines = ProduceCorrectionLines(
            profile, previousExportLines, allNewExportLines, periodStart, periodEnd,
            flexDelta, okTransitionDate, previousOkVersion);

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
                IdempotencyToken = idempotencyToken,
                PreviousOkVersion = previousOkVersion
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
            "Retroactive correction for {EmployeeId} period {PeriodStart}-{PeriodEnd}: {LineCount} correction lines{SplitInfo}",
            profile.EmployeeId, periodStart, periodEnd, correctionLines.Count,
            okTransitionDate.HasValue ? $" (split at {okTransitionDate.Value:yyyy-MM-dd}, {previousOkVersion} → {profile.OkVersion})" : "");

        return new RetroactiveCorrectionResult
        {
            EmployeeId = profile.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CorrectionLines = correctionLines,
            Success = true
        };
    }

    /// <summary>
    /// Recalculates a period split by OK version transition date.
    /// Segment 1: periodStart to (transitionDate - 1 day) under previousOkVersion.
    /// Segment 2: transitionDate to periodEnd under profile.OkVersion.
    /// </summary>
    private async Task<VersionSplitResult> RecalculateWithVersionSplitAsync(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlexBalance,
        DateOnly transitionDate,
        string previousOkVersion,
        string? authorizationHeader,
        Guid? correlationId,
        CancellationToken ct)
    {
        // Split entries by transition date (ADR-003: entry-date resolution)
        var entriesBefore = entries.Where(e => e.Date < transitionDate).ToList();
        var entriesOnOrAfter = entries.Where(e => e.Date >= transitionDate).ToList();
        var absencesBefore = absences.Where(a => a.Date < transitionDate).ToList();
        var absencesOnOrAfter = absences.Where(a => a.Date >= transitionDate).ToList();

        var segmentEnd1 = transitionDate.AddDays(-1);

        // Create profile for old OK version segment
        var oldVersionProfile = new EmploymentProfile
        {
            EmployeeId = profile.EmployeeId,
            AgreementCode = profile.AgreementCode,
            OkVersion = previousOkVersion,
            WeeklyNormHours = profile.WeeklyNormHours,
            EmploymentCategory = profile.EmploymentCategory,
            IsPartTime = profile.IsPartTime,
            PartTimeFraction = profile.PartTimeFraction
        };

        _logger.LogInformation(
            "OK version split for {EmployeeId}: segment 1 [{Start}-{End}] under {OldVersion}, segment 2 [{TransitionDate}-{PeriodEnd}] under {NewVersion}",
            profile.EmployeeId, periodStart, segmentEnd1, previousOkVersion, transitionDate, periodEnd, profile.OkVersion);

        // Calculate segment 1 (old OK version)
        var result1 = await _calculationService.CalculateAsync(
            oldVersionProfile, entriesBefore, absencesBefore, periodStart, segmentEnd1,
            previousFlexBalance, authorizationHeader, correlationId, ct);

        if (!result1.Success)
        {
            return new VersionSplitResult
            {
                Success = false,
                ErrorMessage = $"Segment 1 ({previousOkVersion}) recalculation failed: {result1.ErrorMessage}",
                ExportLines = [],
                RuleResults = []
            };
        }

        // Flex balance from segment 1 feeds into segment 2
        var segment1FlexDelta = ExtractFlexDelta(result1.RuleResults);
        var segment2FlexBalance = previousFlexBalance + (segment1FlexDelta ?? 0m);

        // Calculate segment 2 (new OK version) — uses profile as-is (has new OkVersion)
        var result2 = await _calculationService.CalculateAsync(
            profile, entriesOnOrAfter, absencesOnOrAfter, transitionDate, periodEnd,
            segment2FlexBalance, authorizationHeader, correlationId, ct);

        if (!result2.Success)
        {
            return new VersionSplitResult
            {
                Success = false,
                ErrorMessage = $"Segment 2 ({profile.OkVersion}) recalculation failed: {result2.ErrorMessage}",
                ExportLines = [],
                RuleResults = []
            };
        }

        // Merge export lines and rule results from both segments
        var mergedExportLines = result1.ExportLines.Concat(result2.ExportLines).ToList();
        var mergedRuleResults = result1.RuleResults.Concat(result2.RuleResults).ToList();

        // Total flex delta across both segments
        var segment2FlexDelta = ExtractFlexDelta(result2.RuleResults);
        var totalFlexDelta = (segment1FlexDelta ?? 0m) + (segment2FlexDelta ?? 0m);

        return new VersionSplitResult
        {
            Success = true,
            ExportLines = mergedExportLines,
            RuleResults = mergedRuleResults,
            FlexDelta = totalFlexDelta
        };
    }

    /// <summary>
    /// Extracts the flex balance delta from rule results.
    /// Looks for the FLEX_BALANCE rule result and computes delta from its line items.
    /// Returns null if no flex rule result found.
    /// </summary>
    private static decimal? ExtractFlexDelta(IReadOnlyList<CalculationResult> ruleResults)
    {
        var flexResult = ruleResults.FirstOrDefault(r =>
            r.RuleId.Equals("FLEX_BALANCE", StringComparison.OrdinalIgnoreCase));

        if (flexResult is null || !flexResult.Success)
            return null;

        // The flex rule produces line items representing the delta.
        // Sum all flex line item hours as the delta.
        return flexResult.LineItems.Sum(li => li.Hours);
    }

    private static IReadOnlyList<CorrectionExportLine> ProduceCorrectionLines(
        EmploymentProfile profile,
        IReadOnlyList<PayrollExportLine> previousLines,
        IReadOnlyList<PayrollExportLine> newLines,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal? flexDelta,
        DateOnly? okTransitionDate,
        string? previousOkVersion)
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

                // Determine the OK version for this correction line:
                // - In split mode, use the OkVersion from the new export line (which reflects
                //   the correct version per segment)
                // - In single mode, use profile.OkVersion
                var lineOkVersion = newItems.FirstOrDefault()?.OkVersion
                    ?? prevItems.FirstOrDefault()?.OkVersion
                    ?? profile.OkVersion;

                // Attach flex delta only to flex-related wage types
                var isFlexWageType = sourceItem?.SourceRuleId?.Equals("FLEX_BALANCE", StringComparison.OrdinalIgnoreCase) == true;

                corrections.Add(new CorrectionExportLine
                {
                    EmployeeId = profile.EmployeeId,
                    WageType = wageType,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    OkVersion = lineOkVersion,
                    OriginalHours = originalHours,
                    OriginalAmount = originalAmount,
                    CorrectedHours = correctedHours,
                    CorrectedAmount = correctedAmount,
                    DifferenceHours = diffHours,
                    DifferenceAmount = diffAmount,
                    SourceRuleId = sourceItem?.SourceRuleId,
                    SourceTimeType = sourceItem?.SourceTimeType,
                    FlexDelta = isFlexWageType ? flexDelta : null
                });
            }
        }

        return corrections;
    }

    /// <summary>
    /// Internal result type for version split recalculation.
    /// </summary>
    private sealed class VersionSplitResult
    {
        public required bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public required IReadOnlyList<PayrollExportLine> ExportLines { get; init; }
        public required IReadOnlyList<CalculationResult> RuleResults { get; init; }
        public decimal? FlexDelta { get; init; }
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
