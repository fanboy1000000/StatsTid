namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// A contiguous date range within a <see cref="PlannedCalculation"/> over which every
/// input (OK version, agreement-config, position-override) is internally consistent.
///
/// Pure data; no logic. The <see cref="PlannedCalculation"/> ctor enforces that all
/// segments are sorted, non-overlapping, contiguous, and together cover the full
/// calculation period exactly.
///
/// <see cref="Snapshot"/> is <c>null</c> for segments where no registered rule declares
/// a <see cref="SnapshotContract"/> that intersects this segment's date range — the
/// common case for rules that only consume effective-dated sources. The planner sets it
/// when at least one rule's <see cref="SnapshotContract"/> reads non-dated fields whose
/// snapshot must be captured at calculation time (ADR-016 D5b).
/// </summary>
public sealed record PlannedSegment(
    DateOnly StartDate,
    DateOnly EndDate,
    BoundaryCause BoundaryCause,
    SegmentSnapshot? Snapshot);
