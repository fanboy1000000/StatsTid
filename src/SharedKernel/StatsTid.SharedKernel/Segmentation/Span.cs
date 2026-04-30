namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Where a rule's natural computation window sits relative to the calculation period
/// (ADR-016 D2 — first axis of the multi-axis classification triple).
///
/// Adding new values is breaking only for callers that exhaustively pattern-match without
/// a default arm; the planner code uses default-arm dispatch so additions are non-breaking
/// for the framework itself.
/// </summary>
public enum Span
{
    /// <summary>Each registry entry is evaluated independently (e.g., per-day supplements,
    /// per-absence date stamping). Naturally segment-safe.</summary>
    Entry,

    /// <summary>The rule operates on a sub-period window such as a day, week, or 7-day
    /// sliding interval (e.g., daily rest, weekly overtime).</summary>
    Window,

    /// <summary>The rule operates on the full calculation period as a single unit
    /// (e.g., multi-week norm, period-level compliance ceilings).</summary>
    Period,

    /// <summary>The rule's state spans multiple calculation periods (e.g.,
    /// <c>FlexBalanceRule</c> chains carry-state across segments).</summary>
    CrossPeriod,
}
