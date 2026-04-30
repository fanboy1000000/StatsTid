using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Pure list-based merger for <see cref="CalculationResult"/> segments produced by per-segment
/// rule evaluation under the temporal segmentation framework (ADR-016 §D3 / §D7).
///
/// Lives in <c>StatsTid.SharedKernel.Segmentation</c> alongside the other planner types so the
/// merge family stays with the result shape (PAT-006). The merger owns the "how to apply" logic;
/// <see cref="MergeStrategy"/> carries only the "which strategy" decision.
///
/// <strong>Invariants:</strong>
/// <list type="bullet">
///   <item>Empty segment list throws <see cref="PlannerInvariantViolation"/> — the planner is
///     contractually obliged to deliver ≥ 1 segment (mirrors <c>PlannedCalculation</c> ctor
///     check).</item>
///   <item>For <see cref="MergeStrategyKind.Concatenate"/>, all segments must share
///     <see cref="CalculationResult.RuleId"/> and <see cref="CalculationResult.EmployeeId"/>;
///     mismatch throws <see cref="PlannerInvariantViolation"/>.</item>
///   <item><see cref="MergeStrategyKind.UnionDedupe"/> is undefined for calculation results
///     (compliance-only; ADR-016 §D3); calling with this strategy throws.</item>
///   <item><see cref="MergeStrategyKind.Custom"/> requires a non-null
///     <c>customMerge</c> delegate; calling without one throws.</item>
/// </list>
///
/// Pure function: no I/O, no logging, no static mutable state.
/// </summary>
public static class CalculationResultMerger
{
    /// <summary>
    /// Apply <paramref name="strategy"/> to merge per-segment <paramref name="segments"/> into
    /// a single <see cref="CalculationResult"/>. See class-level remarks for invariant rules.
    /// </summary>
    /// <param name="segments">Per-segment results in segment order. Must contain ≥ 1 element.</param>
    /// <param name="strategy">Merge strategy to apply (resolved by the planner from the rule's
    /// <c>(span, splitBehavior, family)</c> triple plus any per-rule override).</param>
    /// <param name="customMerge">Required when <paramref name="strategy"/> is
    /// <see cref="MergeStrategyKind.Custom"/>; must be <c>null</c> otherwise (ignored if so).</param>
    /// <returns>The single merged result.</returns>
    /// <exception cref="PlannerInvariantViolation">
    /// Thrown for empty segment lists, cross-segment id mismatch under
    /// <see cref="MergeStrategyKind.Concatenate"/>, multi-segment input under
    /// <see cref="MergeStrategyKind.RejectIfMultipleSegments"/>,
    /// <see cref="MergeStrategyKind.UnionDedupe"/> applied to calculation results, or
    /// <see cref="MergeStrategyKind.Custom"/> without a delegate.
    /// </exception>
    public static CalculationResult Apply(
        IReadOnlyList<CalculationResult> segments,
        MergeStrategy strategy,
        Func<IReadOnlyList<CalculationResult>, CalculationResult>? customMerge = null)
    {
        if (segments is null)
            throw new PlannerInvariantViolation(
                "CalculationResultMerger.Apply called with null segment list — planner invariant violated.");

        if (segments.Count == 0)
            throw new PlannerInvariantViolation(
                "CalculationResultMerger.Apply called with empty segment list — planner invariant violated.");

        return strategy.Kind switch
        {
            MergeStrategyKind.Concatenate => MergeByConcatenate(segments),
            MergeStrategyKind.RejectIfMultipleSegments => MergeByRejectIfMultiple(segments),
            MergeStrategyKind.UnionDedupe => throw new PlannerInvariantViolation(
                $"CalculationResultMerger.Apply: UnionDedupe is not defined for CalculationResult " +
                $"(compliance-only per ADR-016 §D3). RuleId={segments[0].RuleId}."),
            MergeStrategyKind.Custom => MergeByCustom(segments, customMerge),
            _ => throw new PlannerInvariantViolation(
                $"CalculationResultMerger.Apply: unknown MergeStrategyKind '{strategy.Kind}'. " +
                $"RuleId={segments[0].RuleId}."),
        };
    }

    /// <summary>
    /// Concatenate-strategy implementation (ADR-016 §D3 default for
    /// <c>(entry, segment-safe, calculation)</c>).
    ///
    /// <list type="bullet">
    ///   <item><c>RuleId</c> / <c>EmployeeId</c> taken from segment 0; all other segments must
    ///     match (planner contract — same rule applied to same employee per segment).</item>
    ///   <item><c>LineItems</c> is the in-order concatenation across all segments.</item>
    ///   <item><c>Success</c> = AND of segment <c>Success</c> values.</item>
    ///   <item><c>ErrorMessage</c> = first non-null across segments (or null).</item>
    ///   <item>Norm-period metadata (<c>NormPeriodWeeks</c>, <c>NormHoursTotal</c>,
    ///     <c>ActualHoursTotal</c>, <c>Deviation</c>, <c>NormFulfilled</c>) is intentionally
    ///     not aggregated under Concatenate — those fields belong to per-rule custom mergers
    ///     for <c>(period, mergeable)</c> norm rules. Set null on the merged result so a
    ///     downstream consumer cannot mistake an arbitrary segment's norm value for a
    ///     period-wide value.</item>
    /// </list>
    /// </summary>
    private static CalculationResult MergeByConcatenate(IReadOnlyList<CalculationResult> segments)
    {
        var first = segments[0];
        var ruleId = first.RuleId;
        var employeeId = first.EmployeeId;

        // Validate id consistency across segments.
        for (int i = 1; i < segments.Count; i++)
        {
            var s = segments[i];
            if (!string.Equals(s.RuleId, ruleId, StringComparison.Ordinal))
                throw new PlannerInvariantViolation(
                    $"CalculationResultMerger.Concatenate: RuleId mismatch across segments " +
                    $"(segment[0].RuleId='{ruleId}', segment[{i}].RuleId='{s.RuleId}'). " +
                    $"All segments must share the same rule under Concatenate.");

            if (!string.Equals(s.EmployeeId, employeeId, StringComparison.Ordinal))
                throw new PlannerInvariantViolation(
                    $"CalculationResultMerger.Concatenate: EmployeeId mismatch across segments " +
                    $"(segment[0].EmployeeId='{employeeId}', segment[{i}].EmployeeId='{s.EmployeeId}'). " +
                    $"RuleId='{ruleId}'.");
        }

        // Aggregate.
        var mergedSuccess = true;
        string? mergedError = null;
        var mergedLineItems = new List<CalculationLineItem>();

        foreach (var s in segments)
        {
            mergedSuccess = mergedSuccess && s.Success;
            mergedError ??= s.ErrorMessage;
            mergedLineItems.AddRange(s.LineItems);
        }

        return new CalculationResult
        {
            RuleId = ruleId,
            EmployeeId = employeeId,
            Success = mergedSuccess,
            LineItems = mergedLineItems,
            ErrorMessage = mergedError,
            // Norm-period metadata intentionally NOT aggregated under Concatenate — see XML doc.
            NormPeriodWeeks = null,
            NormHoursTotal = null,
            ActualHoursTotal = null,
            Deviation = null,
            NormFulfilled = null,
        };
    }

    /// <summary>
    /// Reject-if-multiple-segments implementation (ADR-016 §D3 default for
    /// <c>(window, aligned-window, *)</c>).
    ///
    /// Single-segment input passes through unchanged; multi-segment input is a planner
    /// contract violation — an aligned-window rule should never reach merge with more than
    /// one segment because the planner is supposed to either align the window or reject the
    /// period outright.
    /// </summary>
    private static CalculationResult MergeByRejectIfMultiple(IReadOnlyList<CalculationResult> segments)
    {
        if (segments.Count == 1)
            return segments[0];

        var ruleId = segments[0].RuleId;
        throw new PlannerInvariantViolation(
            $"CalculationResultMerger.RejectIfMultipleSegments: rule '{ruleId}' produced " +
            $"{segments.Count} segments but its merge strategy requires exactly one — " +
            $"the planner contract was violated — an aligned-window rule reached merge with multiple segments.");
    }

    /// <summary>
    /// Custom-strategy delegation. The planner is expected to register a per-rule custom merge
    /// delegate at rule-registration time for <c>(period, mergeable, calculation)</c> rules
    /// and for cross-period carry-state rules (e.g. <c>FlexBalanceRule</c>).
    /// </summary>
    private static CalculationResult MergeByCustom(
        IReadOnlyList<CalculationResult> segments,
        Func<IReadOnlyList<CalculationResult>, CalculationResult>? customMerge)
    {
        if (customMerge is null)
            throw new PlannerInvariantViolation(
                $"CalculationResultMerger.Custom: rule '{segments[0].RuleId}' selected the Custom " +
                $"strategy but no customMerge delegate was supplied — Custom strategy requires " +
                $"customMerge delegate; rule registration omitted the per-rule override.");

        return customMerge(segments);
    }
}
