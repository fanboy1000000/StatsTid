namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Metadata record describing which non-effective-dated source fields a rule reads.
/// Consumed by the planner to know which fields to snapshot at calculation time so that
/// replay against a <see cref="SegmentManifest"/> remains deterministic even after the
/// live DB has changed (ADR-016 D5b).
///
/// Field-path convention: dotted dot-notation, PascalCase, with the source object as the
/// root segment and the field name as the leaf — for example:
/// <c>"EmployeeProfile.WeeklyNormHours"</c>, <c>"EmployeeProfile.AgreementCode"</c>,
/// <c>"WageTypeMappings"</c> (whole collection, no sub-field), <c>"EntitlementConfig.QuotaHours"</c>.
///
/// Using strings rather than expression trees keeps this type free of any dependency on
/// specific source types — the planner resolves the actual values from whatever
/// infrastructure the calling service exposes; the contract here is purely a declaration
/// of intent that the planner uses to build <see cref="SegmentSnapshot"/> instances.
/// </summary>
public sealed record SnapshotContract(
    string RuleId,
    IReadOnlyList<string> NonDatedSourceFields);
