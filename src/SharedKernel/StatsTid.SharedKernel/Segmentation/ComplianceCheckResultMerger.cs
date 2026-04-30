using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Pure list-based merger for <see cref="ComplianceCheckResult"/> segments produced by
/// per-segment rule evaluation under the temporal segmentation framework
/// (ADR-016 §D3 / §D7; ADR-015 owns the result shape).
///
/// <strong>Default strategy for compliance is <see cref="MergeStrategyKind.UnionDedupe"/></strong>
/// — every populated <c>(span, split-behavior, compliance)</c> cell in ADR-016's classification
/// inventory uses it. <see cref="MergeStrategyKind.Concatenate"/> is also accepted (semantically
/// equivalent to UnionDedupe with no dedup pass — kept available so callers don't need to think
/// twice when the calc/compliance distinction is not the point of their code path).
///
/// <strong>Dedup key</strong> for <see cref="MergeStrategyKind.UnionDedupe"/>:
/// <c>(ViolationType, Date, ActualValue, ThresholdValue, Severity)</c>. Defined here in the
/// merger rather than as <see cref="ComplianceViolation"/> equality because (a)
/// <c>ComplianceViolation</c> is a domain record consumed in many contexts where structural
/// equality on these five fields would be wrong (e.g. an audit query that wants to see two
/// notifications of the same threshold breach as distinct events), and (b) the dedup semantics
/// are merge-policy state, not value-type identity. <c>Message</c> and <c>IsVoluntaryExempt</c>
/// are deliberately excluded from the key — message is a presentation string and exemption
/// state can drift between segment evaluations on the same underlying breach; including either
/// would cause spurious duplicates in the merged output.
///
/// <strong>Stable ordering:</strong> first-occurrence-wins. The first segment that emitted a
/// given dedup key contributes the <c>ComplianceViolation</c> instance kept in the merged list.
///
/// Pure function: no I/O, no logging, no static mutable state.
/// </summary>
public static class ComplianceCheckResultMerger
{
    /// <summary>
    /// Apply <paramref name="strategy"/> to merge per-segment <paramref name="segments"/> into
    /// a single <see cref="ComplianceCheckResult"/>. See class-level remarks for invariant rules
    /// and the dedup-key definition.
    /// </summary>
    /// <param name="segments">Per-segment results in segment order. Must contain ≥ 1 element.</param>
    /// <param name="strategy">Merge strategy to apply.</param>
    /// <param name="customMerge">Required when <paramref name="strategy"/> is
    /// <see cref="MergeStrategyKind.Custom"/>; must be <c>null</c> otherwise (ignored if so).</param>
    /// <returns>The single merged result.</returns>
    /// <exception cref="PlannerInvariantViolation">
    /// Thrown for empty segment lists, multi-segment input under
    /// <see cref="MergeStrategyKind.RejectIfMultipleSegments"/>, cross-segment id mismatch, or
    /// <see cref="MergeStrategyKind.Custom"/> without a delegate.
    /// </exception>
    public static ComplianceCheckResult Apply(
        IReadOnlyList<ComplianceCheckResult> segments,
        MergeStrategy strategy,
        Func<IReadOnlyList<ComplianceCheckResult>, ComplianceCheckResult>? customMerge = null)
    {
        if (segments is null)
            throw new PlannerInvariantViolation(
                "ComplianceCheckResultMerger.Apply called with null segment list — planner invariant violated.");

        if (segments.Count == 0)
            throw new PlannerInvariantViolation(
                "ComplianceCheckResultMerger.Apply called with empty segment list — planner invariant violated.");

        return strategy.Kind switch
        {
            MergeStrategyKind.UnionDedupe => MergeByUnionDedupe(segments),
            MergeStrategyKind.Concatenate => MergeByConcatenate(segments),
            MergeStrategyKind.RejectIfMultipleSegments => MergeByRejectIfMultiple(segments),
            MergeStrategyKind.Custom => MergeByCustom(segments, customMerge),
            _ => throw new PlannerInvariantViolation(
                $"ComplianceCheckResultMerger.Apply: unknown MergeStrategyKind '{strategy.Kind}'. " +
                $"RuleId={segments[0].RuleId}."),
        };
    }

    /// <summary>
    /// UnionDedupe-strategy implementation (ADR-016 §D3 default for <c>(*, *, compliance)</c>).
    /// Violations and Warnings are deduplicated independently using the
    /// <c>(ViolationType, Date, ActualValue, ThresholdValue, Severity)</c> key. Stable order:
    /// first occurrence wins.
    /// </summary>
    private static ComplianceCheckResult MergeByUnionDedupe(IReadOnlyList<ComplianceCheckResult> segments)
    {
        ValidateIdConsistency(segments, strategyName: "UnionDedupe");

        var first = segments[0];
        var mergedSuccess = true;
        var seenViolations = new HashSet<DedupKey>();
        var seenWarnings = new HashSet<DedupKey>();
        var dedupedViolations = new List<ComplianceViolation>();
        var dedupedWarnings = new List<ComplianceViolation>();

        foreach (var s in segments)
        {
            mergedSuccess = mergedSuccess && s.Success;

            foreach (var v in s.Violations)
            {
                if (seenViolations.Add(KeyOf(v)))
                    dedupedViolations.Add(v);
            }

            foreach (var w in s.Warnings)
            {
                if (seenWarnings.Add(KeyOf(w)))
                    dedupedWarnings.Add(w);
            }
        }

        return new ComplianceCheckResult
        {
            RuleId = first.RuleId,
            EmployeeId = first.EmployeeId,
            Success = mergedSuccess,
            Violations = dedupedViolations,
            Warnings = dedupedWarnings,
        };
    }

    /// <summary>
    /// Concatenate-strategy implementation for compliance results — equivalent to
    /// <see cref="MergeByUnionDedupe"/> minus the dedup pass. Provided for symmetry with
    /// <see cref="CalculationResultMerger"/> so callers selecting Concatenate explicitly get
    /// the simple-concat semantics they asked for.
    /// </summary>
    private static ComplianceCheckResult MergeByConcatenate(IReadOnlyList<ComplianceCheckResult> segments)
    {
        ValidateIdConsistency(segments, strategyName: "Concatenate");

        var first = segments[0];
        var mergedSuccess = true;
        var mergedViolations = new List<ComplianceViolation>();
        var mergedWarnings = new List<ComplianceViolation>();

        foreach (var s in segments)
        {
            mergedSuccess = mergedSuccess && s.Success;
            mergedViolations.AddRange(s.Violations);
            mergedWarnings.AddRange(s.Warnings);
        }

        return new ComplianceCheckResult
        {
            RuleId = first.RuleId,
            EmployeeId = first.EmployeeId,
            Success = mergedSuccess,
            Violations = mergedViolations,
            Warnings = mergedWarnings,
        };
    }

    /// <summary>
    /// Reject-if-multiple-segments implementation. Single-segment input passes through
    /// unchanged; multi-segment input is a planner contract violation.
    /// </summary>
    private static ComplianceCheckResult MergeByRejectIfMultiple(IReadOnlyList<ComplianceCheckResult> segments)
    {
        if (segments.Count == 1)
            return segments[0];

        var ruleId = segments[0].RuleId;
        throw new PlannerInvariantViolation(
            $"ComplianceCheckResultMerger.RejectIfMultipleSegments: rule '{ruleId}' produced " +
            $"{segments.Count} segments but its merge strategy requires exactly one — " +
            $"the planner contract was violated — an aligned-window rule reached merge with multiple segments.");
    }

    /// <summary>
    /// Custom-strategy delegation; throws if no delegate was supplied.
    /// </summary>
    private static ComplianceCheckResult MergeByCustom(
        IReadOnlyList<ComplianceCheckResult> segments,
        Func<IReadOnlyList<ComplianceCheckResult>, ComplianceCheckResult>? customMerge)
    {
        if (customMerge is null)
            throw new PlannerInvariantViolation(
                $"ComplianceCheckResultMerger.Custom: rule '{segments[0].RuleId}' selected the Custom " +
                $"strategy but no customMerge delegate was supplied — Custom strategy requires " +
                $"customMerge delegate; rule registration omitted the per-rule override.");

        return customMerge(segments);
    }

    /// <summary>
    /// Cross-segment id consistency check shared by Concatenate and UnionDedupe.
    /// All segments must share <see cref="ComplianceCheckResult.RuleId"/> and
    /// <see cref="ComplianceCheckResult.EmployeeId"/> — the same rule applied to the same
    /// employee per segment.
    /// </summary>
    private static void ValidateIdConsistency(
        IReadOnlyList<ComplianceCheckResult> segments,
        string strategyName)
    {
        var first = segments[0];
        for (int i = 1; i < segments.Count; i++)
        {
            var s = segments[i];
            if (!string.Equals(s.RuleId, first.RuleId, StringComparison.Ordinal))
                throw new PlannerInvariantViolation(
                    $"ComplianceCheckResultMerger.{strategyName}: RuleId mismatch across segments " +
                    $"(segment[0].RuleId='{first.RuleId}', segment[{i}].RuleId='{s.RuleId}'). " +
                    $"All segments must share the same rule.");

            if (!string.Equals(s.EmployeeId, first.EmployeeId, StringComparison.Ordinal))
                throw new PlannerInvariantViolation(
                    $"ComplianceCheckResultMerger.{strategyName}: EmployeeId mismatch across segments " +
                    $"(segment[0].EmployeeId='{first.EmployeeId}', segment[{i}].EmployeeId='{s.EmployeeId}'). " +
                    $"RuleId='{first.RuleId}'.");
        }
    }

    /// <summary>
    /// Project a <see cref="ComplianceViolation"/> onto its dedup key. See class-level remarks
    /// for the rationale on which fields are included / excluded.
    /// </summary>
    private static DedupKey KeyOf(ComplianceViolation v) =>
        new(v.ViolationType, v.Date, v.ActualValue, v.ThresholdValue, v.Severity);

    /// <summary>
    /// Internal value-type dedup key. <c>record struct</c> gives structural equality and
    /// hash-code generation for free, with no heap allocations in the dedup loop.
    /// </summary>
    private readonly record struct DedupKey(
        ComplianceViolationType ViolationType,
        DateOnly Date,
        decimal ActualValue,
        decimal ThresholdValue,
        ComplianceSeverity Severity);
}
