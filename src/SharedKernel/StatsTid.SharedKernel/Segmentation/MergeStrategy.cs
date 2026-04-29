namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Describes how a rule's per-segment results should be combined into a single output when
/// a <see cref="PlannedCalculation"/> has more than one segment (ADR-016 D3 / D7).
///
/// Design choice — discriminated-union via <see cref="MergeStrategyKind"/> rather than an
/// abstract base with concrete subclasses: the full set of named strategies is closed and
/// small (4 values); a kind enum + single record is cheaper to pattern-match over than a
/// virtual-dispatch hierarchy, and the kind is directly serialisable into the manifest JSONB
/// without a custom converter.
///
/// The <em>application</em> of a strategy (i.e. the actual merging of result lists) is
/// intentionally NOT on this type — it lives in <c>CalculationResultMerger</c> and
/// <c>ComplianceCheckResultMerger</c> (TASK-2005) so that merging stays in the family
/// that owns the result shape (PAT-006 / ADR-015) rather than here in the metadata record.
/// This record is the "which strategy" decision; the mergers are the "how to apply it"
/// implementations.
/// </summary>
public sealed record MergeStrategy
{
    /// <summary>
    /// Concatenate per-segment results into a single list.
    /// Default for <c>(entry, segment-safe, calculation)</c> rules.
    /// </summary>
    public static readonly MergeStrategy Concatenate =
        new(MergeStrategyKind.Concatenate);

    /// <summary>
    /// Reject any attempt to apply this strategy when there is more than one segment.
    /// Represents a planner-contract violation: an <c>aligned-window</c> rule should
    /// never reach merge because the planner guarantees window alignment or rejects the
    /// period outright.
    /// Default for <c>(window, aligned-window, *)</c> rules.
    /// </summary>
    public static readonly MergeStrategy RejectIfMultipleSegments =
        new(MergeStrategyKind.RejectIfMultipleSegments);

    /// <summary>
    /// Union the per-segment compliance findings and deduplicate by rule-check identity.
    /// Default for <c>(*, *, compliance)</c> rules.
    /// </summary>
    public static readonly MergeStrategy UnionDedupe =
        new(MergeStrategyKind.UnionDedupe);

    /// <summary>
    /// Per-rule custom merge logic — required for <c>(period, mergeable, calculation)</c>
    /// rules and for rules with carry-state across segments (e.g. <c>FlexBalanceRule</c>).
    /// The actual merge delegate lives in the concrete merger class (TASK-2005); this record
    /// carries the kind marker so the planner can identify which rules need the custom path.
    /// </summary>
    public static readonly MergeStrategy Custom =
        new(MergeStrategyKind.Custom);

    /// <summary>Discriminant for pattern-matching in merger implementations.</summary>
    public MergeStrategyKind Kind { get; }

    private MergeStrategy(MergeStrategyKind kind) => Kind = kind;

    // Equality is structural via the record's synthesised members on Kind.
}

/// <summary>
/// Discriminant values for <see cref="MergeStrategy"/>.
/// </summary>
public enum MergeStrategyKind
{
    Concatenate,
    RejectIfMultipleSegments,
    UnionDedupe,
    Custom,
}
