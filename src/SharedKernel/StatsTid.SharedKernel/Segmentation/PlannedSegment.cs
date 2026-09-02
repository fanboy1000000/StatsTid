using System.Text.Json.Serialization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// A contiguous date range within a <see cref="PlannedCalculation"/> over which every
/// input (OK version, agreement-config, position-override, employment state) is
/// internally consistent.
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
///
/// <para>
/// <see cref="EmploymentStatus"/> is the ADR-040 D5 typed-segment state: NOT_EMPLOYED
/// segments evaluate no rules and emit no export lines, but stay in the manifest so
/// non-employment is structurally explicit and auditable — never encoded as "norm 0".
/// Three serialization constraints are LOAD-BEARING here:
/// <list type="bullet">
///   <item>The attribute target MUST be <c>[property:]</c> — on a positional record a
///     bare attribute lands on the constructor PARAMETER, where
///     <c>JsonIgnore</c> does nothing (System.Text.Json reads the attribute from the
///     generated property). The S137 per-writer byte-parity pins
///     (<c>SegmentSerializationParityTests</c>) catch a regression here.</item>
///   <item><c>WhenWritingDefault</c> + the pinned <c>EMPLOYED == 0</c> default
///     (<see cref="EmploymentWindowStatus"/>) make windowless plans serialize
///     BYTE-IDENTICALLY to pre-D5 plans under BOTH manifest writers (the PCS shared
///     options and <c>EventSerializer</c>) — the key is simply absent for EMPLOYED —
///     and make pre-D5 manifests replay to EMPLOYED for free (an absent key
///     deserializes to the parameter default). A NOT_EMPLOYED replay default would
///     make historical replays silently evaluate zero rules (ADR-040 D5).</item>
///   <item>NOT <c>required</c>: a required member would break the
///     <c>EventSerializerCoverageTests</c> generic round-trip over
///     <c>SegmentManifestCreated</c>, and the default is the correctness mechanism
///     anyway.</item>
/// </list>
/// </para>
/// </summary>
public sealed record PlannedSegment(
    DateOnly StartDate,
    DateOnly EndDate,
    BoundaryCause BoundaryCause,
    SegmentSnapshot? Snapshot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    EmploymentWindowStatus EmploymentStatus = EmploymentWindowStatus.EMPLOYED);
