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

    /// <summary>
    /// Evaluated once across the whole plan with per-day context; never split
    /// (S145 Stage B, B1 — QUAL-149). The rule is not run once per segment: its caller sends the
    /// whole calculation period to the rule engine in one call, and each day is evaluated under
    /// that day's own contract (the week-by-week timeline). A segment boundary therefore never
    /// cuts the rule's window, so the planner does not refuse on this label (it refuses only on
    /// <see cref="Reject"/> and <see cref="AlignedWindow"/>), and exactly one result per rule
    /// reaches the merge step.
    ///
    /// <para>Default merge (ADR-016 D3, as amended in S145): <see cref="MergeStrategy.SinglePlanEvaluation"/>
    /// for the calculation family — one result passes through unchanged; two or more is a
    /// contract violation. The compliance family has no default (its merger has no single-plan
    /// arm), so a compliance registration must supply its strategy explicitly.</para>
    ///
    /// <para>Appended last on purpose: the enum crosses the wire as an integer
    /// (<c>GET /api/rules/classifications</c>), so the existing values keep their ordinals.</para>
    /// </summary>
    TimelineAware,
}
