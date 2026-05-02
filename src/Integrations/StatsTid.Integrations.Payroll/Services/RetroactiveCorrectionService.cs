using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Service that re-runs PeriodCalculationService for a past period and produces
/// correction lines by diffing against a previous export. Supports retroactive
/// recalculation with full traceability and event sourcing.
///
/// <para>
/// Segmentation across boundaries (OK-version transitions, agreement-config
/// promotions, position-override effective dates, EU-WTD ruleset transitions,
/// etc.) is now driven entirely by the planner inside
/// <see cref="PeriodCalculationService"/>. This service does NOT segment
/// the period locally any more — it forwards the period as-is to the
/// calculation service, which calls <c>PeriodPlanner.Plan</c> and produces
/// a per-segment, per-line OK-stamped result (S20 wave 1+2 / TASK-2009 /
/// ADR-016 D6).
/// </para>
///
/// <para>
/// <strong>ADR-013 (no-cascade) preservation:</strong> enforced by the planner's
/// geometric bound — <c>PeriodPlanner.Plan</c> never expands the input window
/// (D4 alignment policy can only shrink it; <c>aligned-window</c> rules either
/// pass or reject) — combined with <c>FlexBalanceRule</c>'s chained-balance
/// hand-off across in-period segments. The planner produces exactly one
/// <c>PlannedCalculation</c> confined to <c>[periodStart, periodEnd]</c>; per-segment
/// rule evaluation never consults a neighbouring period. The merge strategies
/// (RejectIfMultipleSegments for aligned-window rules, UnionDedupe for compliance,
/// Concatenate / Custom for calc rules) are the merge mechanism — not the
/// no-cascade enforcement mechanism.
/// </para>
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

    /// <summary>
    /// Recalculates a past period and produces correction lines by diffing against
    /// the previously-exported lines. All segmentation is delegated to the planner
    /// inside <see cref="PeriodCalculationService"/>.
    ///
    /// <para>
    /// <paramref name="okTransitionDate"/> and <paramref name="previousOkVersion"/>
    /// are kept on the public signature for backward compatibility (the
    /// <c>/api/payroll/recalculate</c> endpoint contract), but they no longer drive
    /// a manual two-segment recalculation. The planner derives segmentation from
    /// <see cref="OkVersionResolver"/> on its own. The two parameters are now
    /// purely informational and feed into <see cref="OkVersionCanonicalization.Resolve"/>
    /// so the audit event records the canonical (date-resolved) OK versions
    /// instead of (potentially stale) caller-supplied values.
    /// </para>
    ///
    /// <para>
    /// <strong>ADR-013 (no-cascade)</strong>: the planner produces one
    /// <c>PlannedCalculation</c> bounded by <c>[periodStart, periodEnd]</c>;
    /// nothing in the call graph cascades into neighbouring periods. ADR-013's
    /// constraint is enforced by the planner's geometric bound (Plan never
    /// expands the input window) and FlexBalanceRule's in-period chained-balance
    /// hand-off — NOT by this service, and NOT by any merge strategy. The merge
    /// strategies in PCS handle within-period segment merging only.
    /// </para>
    /// <para>
    /// <strong>ADR-016 D6</strong>: the previous local two-segment
    /// recalculation (<c>RecalculateWithVersionSplitAsync</c>) was retired by
    /// TASK-2009; segmentation is the planner's responsibility, end-to-end.
    /// </para>
    /// </summary>
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
        // Coherent-pair validation: either both split inputs are present or neither.
        // Mismatched inputs indicate a caller bug and must not silently fall through.
        // This guard is independent of segmentation — it pins the public contract of
        // the /api/payroll/recalculate endpoint and is unaffected by the planner-driven
        // rewrite.
        if (okTransitionDate.HasValue ^ previousOkVersion is not null)
        {
            return new RetroactiveCorrectionResult
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CorrectionLines = [],
                Success = false,
                ErrorMessage = "OkTransitionDate and PreviousOkVersion must both be provided or both omitted."
            };
        }

        // Canonicalise OK versions from the period/transition dates so the audit event
        // reflects what the calculation actually used (ADR-003 / TASK-1904). The pure
        // resolution lives in OkVersionCanonicalization so the per-branch behaviour can
        // be unit-tested without standing up the service stack — Codex first-pass S19
        // review flagged that pinning OkVersionResolver alone left the service-level
        // choice (resolver vs. profile.OkVersion) untested.
        var canonical = OkVersionCanonicalization.Resolve(
            profile.OkVersion, periodStart, okTransitionDate, previousOkVersion);
        string canonicalCurrentOkVersion = canonical.CurrentOkVersion;
        string? canonicalPreviousOkVersion = canonical.PreviousOkVersion;

        if (canonical.PreviousDrifted)
        {
            _logger.LogWarning(
                "Caller-supplied PreviousOkVersion '{Supplied}' differs from date-resolved '{Resolved}' for transition {Transition}. Using resolved value.",
                previousOkVersion, canonical.PreviousOkVersion, okTransitionDate);
        }
        if (canonical.CurrentDrifted)
        {
            _logger.LogWarning(
                "Caller-supplied profile.OkVersion '{Supplied}' differs from date-resolved '{Resolved}' (path={Path}). Using resolved value in audit event.",
                profile.OkVersion, canonical.CurrentOkVersion,
                okTransitionDate.HasValue && previousOkVersion is not null ? "split" : "single-version");
        }

        // ---------------------------------------------------------------
        // Planner-driven recalculation (S20 wave 1+2 / ADR-016 D6).
        //
        // The shim overload of CalculateAsync internally calls
        // PeriodPlanner.Plan(...) and returns a PeriodCalculationResult whose
        // ExportLines are already per-segment, per-line OK-stamped. There is
        // no need (and it would be wrong) to segment locally here — the
        // planner detects boundaries from OkVersionResolver and produces 1
        // segment for non-straddling periods or 2+ segments for straddling
        // ones. The previously-hand-coded two-segment recalculation
        // (RecalculateWithVersionSplitAsync) was retired by TASK-2009.
        //
        // The shim overload is [Obsolete]-marked because TASK-2010 will
        // migrate the export boundary to construct PlannedCalculation
        // directly. We are inside the migration path here, so the call is
        // intentional and the warning is suppressed locally only — do NOT
        // suppress at file scope.
        // ---------------------------------------------------------------
#pragma warning disable CS0618 // RetroactiveCorrectionService intentionally calls the [Obsolete] shim overload during the TASK-2009 / TASK-2010 migration; remove once the service constructs PlannedCalculation explicitly.
        var newResult = await _calculationService.CalculateAsync(
            profile, entries, absences, periodStart, periodEnd,
            previousFlexBalance, authorizationHeader, correlationId, ct);
#pragma warning restore CS0618

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

        // Flex delta is extracted from the merged rule results; FlexBalanceRule's
        // own chained-balance hand-off (in-period across segments) means
        // newResult.RuleResults already carries the across-segment net delta.
        decimal? flexDelta = ExtractFlexDelta(newResult.RuleResults);

        // Diff new export lines against previous. ProduceCorrectionLines already
        // honours each line's per-line OkVersion (S14), which is exactly what the
        // planner-driven path emits — so OK-version-aware diffing works whether
        // the planner produced 1 or N segments.
        var correctionLines = ProduceCorrectionLines(
            profile, previousExportLines, newResult.ExportLines, periodStart, periodEnd,
            flexDelta, okTransitionDate, canonicalPreviousOkVersion);

        // Emit RetroactiveCorrectionRequested event (non-fatal if fails).
        // Persist the canonical (date-resolved) OK versions and the profile Position
        // so the audit trail reflects what actually ran, not what the caller sent.
        // ManifestId joins this event to SegmentManifestCreated for the actual
        // N-segment plan (closes Codex P2 / Reviewer audit-event NOTE: the
        // OkVersion/PreviousOkVersion pair only describes the canonicalized
        // 2-version view — full segmentation is recoverable via the manifest).
        var manifestId = newResult.RuleResults.FirstOrDefault()?.ManifestId ?? Guid.Empty;
        try
        {
            var correctionEvent = new RetroactiveCorrectionRequested
            {
                EmployeeId = profile.EmployeeId,
                OriginalPeriodStart = periodStart,
                OriginalPeriodEnd = periodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = canonicalCurrentOkVersion,
                Reason = reason,
                CorrectedByActorId = actorId,
                CorrectionLineCount = correctionLines.Count,
                TotalDifferenceHours = correctionLines.Sum(l => Math.Abs(l.DifferenceHours)),
                CorrelationId = correlationId,
                IdempotencyToken = idempotencyToken,
                PreviousOkVersion = canonicalPreviousOkVersion,
                Position = profile.Position,
                ManifestId = manifestId
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
            okTransitionDate.HasValue ? $" (split at {okTransitionDate.Value:yyyy-MM-dd}, {canonicalPreviousOkVersion} → {canonicalCurrentOkVersion})" : "");

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
