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
}
