using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Domain event emitted when a <see cref="SegmentManifest"/> is produced for a calculation
/// run. Persisted into the <c>segment_manifests</c> projection (ADR-016 D10, TASK-2001 schema)
/// so retroactive replays and audit queries can reconstruct the exact segmentation that was
/// used for any prior calculation.
///
/// <para>
/// Linkage to downstream artefacts (ADR-016 D10, amended 2026-04-29):
/// <list type="bullet">
///   <item><see cref="StatsTid.SharedKernel.Models.CalculationResult.ManifestId"/> carries the manifest id end-to-end.</item>
///   <item><c>audit_log.payload_jsonb</c> serialises <c>{"manifest_id":"&lt;guid&gt;"}</c> when the
///         manifest id is set on <c>HttpContext.Items["audit:manifest_id"]</c> (TASK-2008).</item>
///   <item>SLS export file content embeds the manifest id (no <c>payroll_export_lines</c> column).</item>
/// </list>
/// </para>
///
/// <para>Registered in <c>EventSerializer.EventTypeMap</c> as <c>"SegmentManifestCreated"</c>.</para>
/// </summary>
public sealed class SegmentManifestCreated : DomainEventBase
{
    public override string EventType => "SegmentManifestCreated";

    public required Guid ManifestId { get; init; }
    public required Guid EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>
    /// Allowed values: <c>forward-calc</c>, <c>retroactive-correction</c>, <c>replay</c>.
    /// (ADR-016 D10; ADR-002 convention: string enums in C# without a DB CHECK constraint.)
    /// </summary>
    public required string CalculationKind { get; init; }

    /// <summary>
    /// Deduped list of <see cref="BoundaryCause"/> names as strings. Stored verbatim into the
    /// <c>boundary_cause_summary TEXT[]</c> column of the projection so the GIN index can
    /// filter by cause without unpacking the segments JSONB.
    /// </summary>
    /// <remarks>
    /// Not marked <c>required</c> so the property defaults to an empty list rather than
    /// <c>null</c>. This keeps the JSON payload self-describing under
    /// <see cref="System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/> and
    /// lets the EventSerializer coverage round-trip test instantiate the type generically.
    /// Producers (TASK-2008) always populate this from <see cref="PlannedCalculation"/>.
    /// </remarks>
    public IReadOnlyList<string> BoundaryCauseSummary { get; init; } = Array.Empty<string>();

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Full segment list — serialised into <c>segments_jsonb JSONB</c> in the projection table.
    /// </summary>
    /// <remarks>
    /// Not marked <c>required</c> for the same reason as
    /// <see cref="BoundaryCauseSummary"/> — see remarks there.
    /// </remarks>
    public IReadOnlyList<PlannedSegment> Segments { get; init; } = Array.Empty<PlannedSegment>();
}
