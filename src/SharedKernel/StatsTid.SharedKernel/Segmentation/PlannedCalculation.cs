namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// The output of <c>PeriodPlanner.Plan()</c> — the sole permitted input to
/// <c>PeriodCalculationService.CalculateAsync(PlannedCalculation, …)</c> (ADR-016 D1).
///
/// <strong>Internal constructor:</strong> callers outside this assembly cannot construct a
/// <c>PlannedCalculation</c> directly. The only way to obtain one is through the planner,
/// which enforces all invariants. Bypass attempts fail at compile time; the
/// <c>InternalsVisibleTo</c> attribute on <c>StatsTid.SharedKernel.csproj</c> exposes the
/// ctor to <c>StatsTid.Tests.Unit</c> only — allowing the Q9 negative tests to exercise
/// the throw paths without allowing arbitrary callers through.
///
/// <strong>Ctor-checked invariants (ADR-016 D9, geometric):</strong>
/// <list type="bullet">
///   <item><see cref="Segments"/> must contain at least one segment.</item>
///   <item>Segments must be sorted, non-overlapping, and contiguous:
///     for every consecutive pair (s_i, s_{i+1}): s_i.EndDate &lt; s_{i+1}.StartDate AND
///     s_{i+1}.StartDate == s_i.EndDate.AddDays(1) (no gaps, no overlaps).</item>
///   <item><see cref="Segments"/>[0].StartDate == <see cref="PeriodStart"/> AND
///     <see cref="Segments"/>[^1].EndDate == <see cref="PeriodEnd"/> (segments cover the
///     period exactly — no shrinkage, no extension).</item>
/// </list>
/// Violations throw <see cref="PlannerInvariantViolation"/> with a structured message
/// identifying the violated invariant.
///
/// <strong>Planner-checked invariants (NOT in ctor — wired in TASK-2006):</strong>
/// <list type="bullet">
///   <item>For every rule with a <see cref="SnapshotContract"/>: every segment whose date
///     range intersects the contract's read scope carries a non-null
///     <see cref="PlannedSegment.Snapshot"/>.</item>
///   <item>Every rule in the registry has a non-null resolved <see cref="MergeStrategy"/>
///     (default-derived from its <c>(span, splitBehavior, family)</c> triple, or a
///     per-rule override supplied at registration). TASK-2006 wires the rule registry
///     into the planner so these invariants can be checked before the
///     <c>PlannedCalculation</c> is constructed.</item>
/// </list>
/// These rule-related invariants are intentionally absent from the ctor because the ctor
/// has no access to the rule registry, which is a runtime dependency (not a pure SharedKernel
/// concern). Violations of these invariants are also surfaced as <see cref="PlannerInvariantViolation"/>
/// but are thrown by the planner code in TASK-2004 / TASK-2006, not here.
/// </summary>
public sealed class PlannedCalculation
{
    /// <summary>Unique identifier for this planned calculation run; also the manifest id.</summary>
    public Guid ManifestId { get; }

    /// <summary>The employee whose period is being calculated.</summary>
    public Guid EmployeeId { get; }

    /// <summary>Inclusive start of the calculation period.</summary>
    public DateOnly PeriodStart { get; }

    /// <summary>Inclusive end of the calculation period.</summary>
    public DateOnly PeriodEnd { get; }

    /// <summary>
    /// Ordered list of segments that partition the period. Length ≥ 1.
    /// The ctor guarantees contiguity and full coverage of [PeriodStart, PeriodEnd].
    /// </summary>
    public IReadOnlyList<PlannedSegment> Segments { get; }

    /// <summary>
    /// Allowed values: <c>forward-calc</c>, <c>retroactive-correction</c>, <c>replay</c>.
    /// (ADR-016 D10; ADR-002 convention: string enum in C# without DB CHECK constraint.
    /// No runtime check is asserted here — project convention keeps string enums unchecked
    /// in C#; callers that construct via the planner are type-safe by design.)
    /// </summary>
    public string CalculationKind { get; }

    /// <summary>
    /// Internal constructor — the only way external callers can exercise this is via
    /// <c>PeriodPlanner.Plan()</c> (TASK-2004). <c>StatsTid.Tests.Unit</c> can invoke it
    /// directly via <c>InternalsVisibleTo</c> for the Q9 invariant negative tests.
    ///
    /// Throws <see cref="PlannerInvariantViolation"/> if any geometric invariant is violated.
    /// </summary>
    internal PlannedCalculation(
        Guid manifestId,
        Guid employeeId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<PlannedSegment> segments,
        string calculationKind)
    {
        // --- Geometric invariant 1: at least one segment ---
        if (segments.Count < 1)
            throw new PlannerInvariantViolation(
                $"PlannedCalculation invariant violated: Segments must contain at least one segment " +
                $"(EmployeeId={employeeId}, PeriodStart={periodStart}, PeriodEnd={periodEnd}).");

        // --- Geometric invariant 2: segments are contiguous (no gaps, no overlaps) ---
        for (int i = 0; i < segments.Count - 1; i++)
        {
            var current = segments[i];
            var next = segments[i + 1];

            if (next.StartDate != current.EndDate.AddDays(1))
                throw new PlannerInvariantViolation(
                    $"PlannedCalculation invariant violated: Segments are not contiguous at index {i}/{i + 1}. " +
                    $"Segment[{i}].EndDate={current.EndDate}, Segment[{i + 1}].StartDate={next.StartDate} " +
                    $"(expected {current.EndDate.AddDays(1)}). " +
                    $"EmployeeId={employeeId}, PeriodStart={periodStart}, PeriodEnd={periodEnd}.");
        }

        // --- Geometric invariant 3: segments cover the period exactly ---
        if (segments[0].StartDate != periodStart)
            throw new PlannerInvariantViolation(
                $"PlannedCalculation invariant violated: First segment StartDate ({segments[0].StartDate}) " +
                $"does not equal PeriodStart ({periodStart}). " +
                $"EmployeeId={employeeId}.");

        if (segments[^1].EndDate != periodEnd)
            throw new PlannerInvariantViolation(
                $"PlannedCalculation invariant violated: Last segment EndDate ({segments[^1].EndDate}) " +
                $"does not equal PeriodEnd ({periodEnd}). " +
                $"EmployeeId={employeeId}.");

        ManifestId = manifestId;
        EmployeeId = employeeId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Segments = segments;
        CalculationKind = calculationKind;
    }

    /// <summary>
    /// Internal factory that reconstructs a <see cref="PlannedCalculation"/> from a
    /// persisted <see cref="SegmentManifest"/> — the replay primitive (ADR-016 D10).
    ///
    /// Runs identical geometric invariants as the main ctor, so any manifest that was
    /// persisted with a corrupted segment list is caught here before replay proceeds.
    ///
    /// <strong>Note to TASK-2004:</strong> the manifest-to-plan segment reconstruction
    /// (populating <see cref="PlannedSegment.Snapshot"/> from the manifest's stored snapshot
    /// data and verifying <see cref="SnapshotContract"/> completeness) belongs to the
    /// <c>PeriodPlanner.FromManifest</c> logic in TASK-2004. This factory's signature must
    /// be present in S20 TASK-2003 so that TASK-2008 (<c>PeriodCalculationService.ReplayAsync</c>)
    /// can compile against it; the body delegates to the planner once that is available.
    /// </summary>
    internal static PlannedCalculation FromManifest(SegmentManifest manifest)
    {
        // TODO (TASK-2004): replace this stub with full reconstruction.
        // The planner owns the logic for mapping SegmentManifest → PlannedCalculation,
        // including re-hydrating SegmentSnapshot values and verifying SnapshotContract
        // completeness for each segment. Calling this method before TASK-2004 lands will
        // throw at runtime to make the missing dependency visible.
        throw new NotImplementedException(
            "PlannedCalculation.FromManifest is a stub pending TASK-2004 (PeriodPlanner). " +
            $"ManifestId={manifest.ManifestId}");
    }
}
