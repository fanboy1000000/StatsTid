namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// What MUST happen when a segmentation boundary falls inside a rule's evaluation span
/// (ADR-016 D2 — second axis of the multi-axis classification triple).
/// </summary>
public enum SplitBehavior
{
    /// <summary>The rule produces correct results when its span is split at any segment
    /// boundary (e.g., per-entry rules, atomic-day window rules).</summary>
    SegmentSafe,

    /// <summary>The rule's natural window must align with the segment boundary; if the
    /// boundary disagrees, the planner either rejects (<see cref="PlannerOptions.AllowUpstreamAlignment"/>
    /// = false) or shrinks the period to the rule's natural edge (true).</summary>
    AlignedWindow,

    /// <summary>The rule produces a correct result when each segment is evaluated
    /// independently and the per-segment results are combined via a per-rule custom
    /// merger (e.g., multi-week norm sums hours per segment then sums totals).</summary>
    Mergeable,

    /// <summary>The rule cannot be split under any circumstance — period straddle is
    /// always a 4xx-class error regardless of <see cref="PlannerOptions.AllowUpstreamAlignment"/>
    /// (ADR-016 D4).</summary>
    Reject,
}
