namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Holds a snapshot of the non-effective-dated source data (employee profile fields,
/// wage-type mappings, entitlement-policy rows) that a rule reads via its
/// <see cref="SnapshotContract"/> for a single segment (ADR-016 D5b).
///
/// The <see cref="Values"/> dictionary is keyed by the dotted field path declared in
/// <see cref="SnapshotContract.NonDatedSourceFields"/> — for example:
/// <c>"EmployeeProfile.WeeklyNormHours"</c> or <c>"WageTypeMappings"</c>.
/// Values are typed as <c>object?</c> to accommodate any field type; the planner
/// and consumers are responsible for casting to the expected CLR type.
///
/// Snapshot is separate from <see cref="PlannedSegment"/> rather than nested inside it so
/// that segments without any <see cref="SnapshotContract"/> consumers (i.e. all rules on the
/// segment use only effective-dated sources) can carry a null <c>Snapshot</c> on
/// <see cref="PlannedSegment"/> without allocating an empty dictionary — keeping the common
/// case allocation-free. If SegmentSnapshot were nested, callers would need to check both
/// for null-segment and empty-values; the separate type makes the null check a single
/// <c>PlannedSegment.Snapshot is null</c> test.
/// </summary>
public sealed record SegmentSnapshot(IReadOnlyDictionary<string, object?> Values);
