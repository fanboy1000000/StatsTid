namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Thrown when <see cref="PlannedCalculation"/>'s internal constructor detects a geometric
/// invariant violation (empty segment list, gap, overlap, or coverage mismatch) or when the
/// planner detects a rule-related invariant violation (missing snapshot for a segment that
/// intersects a <see cref="SnapshotContract"/>, or a rule without a resolved
/// <see cref="MergeStrategy"/>).
///
/// The <paramref name="message"/> must name the specific invariant that was violated and,
/// where applicable, the offending rule id — so test assertions can pin the exact failure mode.
///
/// <para>
/// <b>Structured split-refusal members (S144).</b> The ADR-016 D4 split refusal — a
/// whole-window rule (Reject / AlignedWindow) would be evaluated in two or more EMPLOYED
/// segments — is the one violation a caller may legitimately render to a user ("this month
/// needs manual handling"), so it also carries its facts as members: the refusing rule's id,
/// the EMPLOYED segment count and the distinct interior boundary causes. Never a date
/// (ADR-040 D7). Every other throw site uses the message-only constructors, leaving the
/// members null / empty, so <see cref="IsSplitRefusal"/> is false for a bug (a geometric or
/// snapshot violation) and a caller cannot dress a bug up as a refusable month. The type stays
/// sealed and un-subclassed: callers and tests assert the exact type.
/// </para>
/// </summary>
public sealed class PlannerInvariantViolation : InvalidOperationException
{
    public PlannerInvariantViolation(string message)
        : base(message)
    {
    }

    public PlannerInvariantViolation(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// ADR-016 D4 split refusal: the message plus its structured facts. Used ONLY by the two
    /// D4 throw sites in <see cref="PeriodPlanner"/> (Reject and AlignedWindow).
    /// </summary>
    public PlannerInvariantViolation(
        string message,
        string splitRefusalRuleId,
        int employedSegmentCount,
        IReadOnlyList<BoundaryCause> interiorBoundaryCauses)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(splitRefusalRuleId);
        ArgumentNullException.ThrowIfNull(interiorBoundaryCauses);
        SplitRefusalRuleId = splitRefusalRuleId;
        EmployedSegmentCount = employedSegmentCount;
        InteriorBoundaryCauses = interiorBoundaryCauses.ToArray();
    }

    /// <summary>The id of the whole-window rule that refused the split; null when not a split refusal.</summary>
    public string? SplitRefusalRuleId { get; }

    /// <summary>How many EMPLOYED segments the period would have been evaluated in; null when not a split refusal.</summary>
    public int? EmployedSegmentCount { get; }

    /// <summary>
    /// The distinct causes of the interior boundaries, in date order (first occurrence);
    /// empty when not a split refusal. Causes only, never dates (ADR-040 D7).
    /// </summary>
    public IReadOnlyList<BoundaryCause> InteriorBoundaryCauses { get; } = Array.Empty<BoundaryCause>();

    /// <summary>True only for the ADR-016 D4 split refusal (the structured constructor).</summary>
    public bool IsSplitRefusal => EmployedSegmentCount is not null;
}
