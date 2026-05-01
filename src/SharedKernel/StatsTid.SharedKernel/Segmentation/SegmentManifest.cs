namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// The persisted (projection) form of a segmented calculation run. Built from a
/// <see cref="PlannedCalculation"/> after all segments have been evaluated, and persisted
/// via the <c>SegmentManifestCreated</c> domain event (TASK-2007) into the
/// <c>segment_manifests</c> projection table (ADR-016 D10, TASK-2001 schema).
///
/// This is NOT the same shape as <see cref="PlannedCalculation"/> — it is the audit-query
/// projection that lives in the DB and is used for replay reconstruction. A public
/// constructor is intentional: the manifest is the projection's view of what the planner
/// produced; it does not carry the planner's runtime invariants.
///
/// Shape spec (ADR-016 D10):
/// <list type="bullet">
///   <item><c>manifest_id</c> — correlates to the <c>SegmentManifestCreated</c> event Id.</item>
///   <item><c>calculation_kind</c> — one of <c>forward-calc</c> / <c>retroactive-correction</c> / <c>replay</c>.</item>
///   <item><c>boundary_cause_summary</c> — deduped list of <see cref="BoundaryCause"/> names.</item>
///   <item><c>segments_jsonb</c> — the full segment list serialised into the DB JSONB column.</item>
/// </list>
/// </summary>
public sealed record SegmentManifest(
    Guid ManifestId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    // EmployeeId is stored as string to align with every other employee_id column in the
    // schema (ADR-016 D10 amendment 2026-05-01). The projection table uses TEXT NOT NULL
    // so audit-query joins to other employee-keyed tables work without UUID derivation.
    string EmployeeId,

    /// <summary>
    /// Allowed values: <c>forward-calc</c>, <c>retroactive-correction</c>, <c>replay</c>.
    /// (ADR-016 D10; ADR-002 convention: string enums in C# without a DB CHECK constraint.)
    /// </summary>
    string CalculationKind,

    /// <summary>
    /// Deduped list of <see cref="BoundaryCause"/> names as strings — stored verbatim in
    /// the <c>boundary_cause_summary TEXT[]</c> column so the GIN index can filter by cause
    /// without a join. Values correspond to <see cref="BoundaryCause"/> enum member names.
    /// </summary>
    IReadOnlyList<string> BoundaryCauseSummary,

    DateTimeOffset CreatedAt,

    /// <summary>
    /// Full segment list — serialised into <c>segments_jsonb JSONB</c> in the projection table.
    /// </summary>
    IReadOnlyList<PlannedSegment> Segments);
