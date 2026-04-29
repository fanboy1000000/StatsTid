namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Call-site options passed to the planner when constructing a <see cref="PlannedCalculation"/>.
///
/// All properties have defaults that express the strictest correct behaviour (ADR-016 D4):
/// <see cref="AllowUpstreamAlignment"/> defaults to <c>false</c> so that any
/// <c>aligned-window</c> rule whose natural window edge disagrees with the calculation
/// period boundary causes the planner to return a structured error rather than silently
/// shrinking the period.
///
/// Add new options here as needed. Do NOT add fields that are not yet specified — this
/// record is intentionally minimal for S20.
/// </summary>
public sealed record PlannerOptions
{
    /// <summary>
    /// Singleton instance with all defaults; use as the default argument for callers
    /// that have no special requirements.
    /// </summary>
    public static readonly PlannerOptions Default = new();

    /// <summary>
    /// When <c>false</c> (default), the planner returns a structured error if an
    /// <c>aligned-window</c> rule's natural window edge disagrees with the period boundary.
    ///
    /// When <c>true</c>, the planner shrinks (never expands) the period to the rule's
    /// natural window edge and annotates the manifest with <c>boundary_realigned</c>.
    /// Expansion is excluded — shrink is more predictable than extend. <c>(reject, *, *)</c>
    /// rules always reject regardless of this flag (ADR-016 D4).
    /// </summary>
    public bool AllowUpstreamAlignment { get; init; } = false;
}
