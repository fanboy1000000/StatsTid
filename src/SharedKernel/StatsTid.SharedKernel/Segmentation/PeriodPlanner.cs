using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Pure planner for temporal period segmentation (ADR-016 D1, D8).
///
/// <para>
/// <strong>Always-invoke contract (D8):</strong> every <c>CalculateAsync</c> call routes
/// through <see cref="Plan"/> first — including non-straddling periods. The planner
/// produces a uniformly-shaped <see cref="PlannedCalculation"/> regardless of segment
/// count so audit + replay determinism are uniform across both straddling and non-straddling
/// cases.
/// </para>
///
/// <para>
/// <strong>Purity:</strong> no I/O, no DB, no HTTP, no filesystem. The planner consumes
/// pre-resolved <see cref="BoundarySources"/> and <see cref="RuleClassification"/> data —
/// the calling service is responsible for hydrating those from infrastructure. This keeps
/// the planner inside <c>StatsTid.SharedKernel</c> and free of any reference to
/// <c>StatsTid.RuleEngine.*</c>, <c>StatsTid.Infrastructure.*</c>, or payroll types.
/// </para>
///
/// <para>
/// <strong>Construction path:</strong> the planner is the sole external caller of
/// <see cref="PlannedCalculation"/>'s internal constructor (the test project also has
/// access via <c>InternalsVisibleTo</c> for the Q9 negative tests). Geometric invariants
/// are enforced inside the ctor; rule-side invariants are enforced here in the planner
/// (ADR-016 D9).
/// </para>
/// </summary>
public static class PeriodPlanner
{
    /// <summary>
    /// Plan a calculation by partitioning <c>[periodStart, periodEnd]</c> into segments
    /// based on boundaries detected in <paramref name="sources"/>, then enforcing rule-side
    /// invariants over the produced <see cref="PlannedCalculation"/>.
    /// </summary>
    /// <param name="employeeId">Employee whose calculation is being planned.</param>
    /// <param name="periodStart">Inclusive start of the calculation period.</param>
    /// <param name="periodEnd">Inclusive end of the calculation period.</param>
    /// <param name="calculationKind">One of <c>forward-calc</c>, <c>retroactive-correction</c>,
    /// <c>replay</c> (ADR-016 D10).</param>
    /// <param name="ruleSet">Resolved rule classifications consumed by this calculation.</param>
    /// <param name="sources">Pre-hydrated boundary sources + non-dated source values for snapshots.</param>
    /// <param name="options">Planner call-site options (e.g., upstream alignment).</param>
    /// <param name="enrollment">ADR-020 D1: optional non-rule snapshot enrollment. When
    /// non-null AND <paramref name="profile"/> is non-null, registered hydrators run per
    /// segment and their results merge into <see cref="SegmentSnapshot.Values"/>. Null
    /// for test-direct call-sites that construct <see cref="BoundarySources"/> directly.</param>
    /// <param name="profile">ADR-020 D1: optional <see cref="EmploymentProfile"/> fed to
    /// each enrollment hydrator. Same profile is re-used across all segments (D1.5
    /// uniform-per-plan binding). Null skips hydrator invocation silently.</param>
    /// <returns>A <see cref="PlannedCalculation"/> ready for evaluation by
    /// <c>PeriodCalculationService.CalculateAsync</c>.</returns>
    /// <exception cref="PlannerInvariantViolation">Thrown when geometric invariants
    /// (in the ctor) or rule-side invariants (here in the planner) are violated.
    /// Also thrown for upstream-alignment violations per ADR-016 D4.</exception>
    public static PlannedCalculation Plan(
        string employeeId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string calculationKind,
        IReadOnlyList<RuleClassification> ruleSet,
        BoundarySources sources,
        PlannerOptions options,
        IPlannerEnrollment? enrollment = null,
        EmploymentProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(calculationKind);
        // Empty/whitespace check on employeeId is asserted by PlannedCalculation's ctor
        // (ADR-016 D10 amendment 2026-05-01) — the planner forwards the value unchanged so
        // the violation message points at the geometric invariant where it belongs.

        if (periodEnd < periodStart)
            throw new PlannerInvariantViolation(
                $"PeriodPlanner.Plan invariant violated: periodEnd ({periodEnd}) is before " +
                $"periodStart ({periodStart}). EmployeeId={employeeId}.");

        // --- D4 upstream-alignment policy ---
        // Apply BEFORE detecting boundaries so the (possibly shrunk) period drives detection.
        // Reject (split-behavior=Reject) rules unconditionally fail when the planner is
        // asked to consider a period; AllowUpstreamAlignment never overrides reject.
        var (effectivePeriodStart, effectivePeriodEnd) = ApplyAlignmentPolicy(
            periodStart, periodEnd, ruleSet, sources, options, employeeId);

        // --- 1. Detect boundaries ---
        var boundaries = BoundaryDetector.Detect(effectivePeriodStart, effectivePeriodEnd, sources);

        // --- 2 + 3. Build contiguous segments ---
        // Segment[0] starts at effectivePeriodStart. If there are no boundaries, single segment
        // covers the full period. We use BoundaryCause.OkTransition as the sentinel for
        // segment 0's cause when no boundaries exist (it is the most-impactful cause and
        // documents intent: "starting context, treated as if the OK version itself anchors
        // the segment").
        //
        // When boundaries exist:
        //   - segment 0 carries the cause of boundary[0] — that is the cause that will end
        //     segment 0 by introducing segment 1. This makes BoundaryCause act as
        //     "what will introduce the NEXT split"; the final segment carries the cause of
        //     the boundary that started it (boundary[N-1]).
        //   - This convention is uniform: every segment's BoundaryCause names the
        //     transition at its leading edge (segment 0's leading edge is the period start
        //     itself, but absent a real boundary we use the cause of the first split).
        var segments = new List<PlannedSegment>(boundaries.Count + 1);

        // Build segment date ranges first (so we can check snapshot-need against the full set).
        var ranges = new List<(DateOnly Start, DateOnly End, BoundaryCause Cause)>(boundaries.Count + 1);
        if (boundaries.Count == 0)
        {
            ranges.Add((effectivePeriodStart, effectivePeriodEnd, BoundaryCause.OkTransition));
        }
        else
        {
            // First segment: [periodStart, boundary[0]-1], cause = boundary[0].Cause.
            // (Tie-break documented in BoundaryDetector.OrderedCauses.)
            ranges.Add((effectivePeriodStart, boundaries[0].Date.AddDays(-1), boundaries[0].Cause));

            // Middle segments: [boundary[i].Date, boundary[i+1].Date-1], cause = boundary[i+1].Cause.
            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                ranges.Add((boundaries[i].Date, boundaries[i + 1].Date.AddDays(-1), boundaries[i + 1].Cause));
            }

            // Final segment: [boundary[N-1].Date, periodEnd], cause = boundary[N-1].Cause.
            ranges.Add((boundaries[^1].Date, effectivePeriodEnd, boundaries[^1].Cause));
        }

        // --- 4. Gather snapshots per segment ---
        // If any rule in ruleSet has a SnapshotContract, OR a non-rule enrollment is
        // active with a non-null profile (ADR-020 D1), every segment carries a snapshot;
        // otherwise Snapshot is null. Per ADR-016 D5b: a single snapshot per segment is
        // sufficient because non-dated sources are time-invariant within a calculation
        // run; the segment dimension is there for symmetry with future versioned-history
        // work (Phase 4). ADR-020 D1.5: enrollment hydrators run uniformly per plan
        // (same profile reused across segments today; per-segment evolution is a
        // forward-compat seam, not a today binding).
        var anyContract = HasAnySnapshotContract(ruleSet, enrollment);
        var enrollmentActive = enrollment is not null && profile is not null;
        SegmentSnapshot? sharedSnapshot = null;
        if (anyContract || enrollmentActive)
        {
            // Build a merged values dictionary so the externally-visible Values stays an
            // IReadOnlyDictionary; rule-declared non-dated source values land first,
            // then enrollment-injected entries layer on top at well-known keys (e.g.
            // S29 uses "WtmNaturalKey"). Rule-declared keys do not overlap with
            // enrollment keys by design — rules don't declare enrollment contract keys.
            var merged = new Dictionary<string, object?>(sources.NonDatedSourceValues);
            if (enrollmentActive)
            {
                foreach (var (contractKey, hydrator) in enrollment!.GetEnrollments())
                {
                    merged[contractKey] = hydrator(profile!);
                }
            }
            sharedSnapshot = new SegmentSnapshot(merged);
        }

        foreach (var r in ranges)
        {
            segments.Add(new PlannedSegment(r.Start, r.End, r.Cause, sharedSnapshot));
        }

        // --- 5. Construct PlannedCalculation via the internal ctor (geometric invariants here) ---
        var manifestId = Guid.NewGuid();
        var planned = new PlannedCalculation(
            manifestId,
            employeeId,
            effectivePeriodStart,
            effectivePeriodEnd,
            segments,
            calculationKind);

        // --- 6. Rule-side invariants (NOT in ctor — see PlannedCalculation XML doc) ---
        EnforceRuleInvariants(planned, ruleSet);

        return planned;
    }

    /// <summary>
    /// Reconstruct a <see cref="PlannedCalculation"/> from a persisted
    /// <see cref="SegmentManifest"/> — the replay primitive (ADR-016 D10).
    ///
    /// Replay does NOT consult the live DB: snapshots inside <c>manifest.Segments</c> are
    /// the source of truth. The reconstructed <see cref="PlannedCalculation"/> carries
    /// <c>manifest.ManifestId</c> verbatim — replay does not mint a new id.
    /// </summary>
    /// <exception cref="PlannerInvariantViolation">Thrown when the manifest's segment list
    /// violates geometric invariants (caught by the ctor) or when rule-side invariants are
    /// violated against the supplied <paramref name="ruleSet"/>.</exception>
    public static PlannedCalculation FromManifest(
        SegmentManifest manifest,
        IReadOnlyList<RuleClassification> ruleSet)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(ruleSet);

        // Use manifest.Segments directly (typed). Geometric invariants fire in the ctor
        // if the persisted segments were corrupted (gap/overlap/coverage).
        var planned = new PlannedCalculation(
            manifest.ManifestId,
            manifest.EmployeeId,
            manifest.PeriodStart,
            manifest.PeriodEnd,
            manifest.Segments,
            manifest.CalculationKind);

        // Re-run rule-side invariants against the supplied ruleSet. Note: the manifest
        // was persisted with a snapshot per segment if its rules had contracts at the time
        // of original calculation; re-asserting against today's ruleSet protects against
        // a manifest being replayed after the rule registry has changed in incompatible
        // ways (e.g. a rule grew a new SnapshotContract but the manifest's snapshot dict
        // doesn't carry the new field).
        EnforceRuleInvariants(planned, ruleSet);

        return planned;
    }

    // --- Helpers -----------------------------------------------------------

    /// <summary>
    /// ADR-016 D4: when AllowUpstreamAlignment=false and an aligned-window rule's natural
    /// edge disagrees with the period boundary, throw. When true, shrink (never expand)
    /// the period to the rule's natural window edge. Reject rules ignore the flag.
    ///
    /// This implementation cannot inspect a rule's "natural window edge" without knowing
    /// concrete rule semantics — that knowledge lives in the RuleEngine, not SharedKernel.
    /// We therefore restrict the check to two operationally tractable cases:
    /// <list type="number">
    ///   <item>Reject rules + a non-empty boundary list → unconditional throw.</item>
    ///   <item>AlignedWindow rules + a non-empty boundary list with AllowUpstreamAlignment=false
    ///     → throw with the offending rule(s) listed (the planner has no way to "shrink" to
    ///     the rule's natural edge without the rule's window-arithmetic, so when the flag is
    ///     true we still pass through here unchanged — actual shrinking is a future
    ///     enhancement when concrete rules expose their natural-edge calculator).</item>
    /// </list>
    /// </summary>
    private static (DateOnly Start, DateOnly End) ApplyAlignmentPolicy(
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<RuleClassification> ruleSet,
        BoundarySources sources,
        PlannerOptions options,
        string employeeId)
    {
        // Reject rules: any boundary inside the period is fatal.
        var hasInteriorBoundary = HasAnyInteriorBoundary(periodStart, periodEnd, sources);
        if (hasInteriorBoundary)
        {
            var rejectRule = FindFirst(ruleSet, r => r.SplitBehavior == SplitBehavior.Reject);
            if (rejectRule is not null)
            {
                throw new PlannerInvariantViolation(
                    $"PeriodPlanner.Plan invariant violated: rule '{rejectRule.RuleId}' has " +
                    $"SplitBehavior=Reject but the calculation period [{periodStart}..{periodEnd}] " +
                    $"contains one or more interior boundaries. " +
                    $"EmployeeId={employeeId}.");
            }

            if (!options.AllowUpstreamAlignment)
            {
                var alignedRule = FindFirst(ruleSet, r => r.SplitBehavior == SplitBehavior.AlignedWindow);
                if (alignedRule is not null)
                {
                    throw new PlannerInvariantViolation(
                        $"PeriodPlanner.Plan invariant violated: rule '{alignedRule.RuleId}' has " +
                        $"SplitBehavior=AlignedWindow but the calculation period " +
                        $"[{periodStart}..{periodEnd}] contains interior boundaries and " +
                        $"PlannerOptions.AllowUpstreamAlignment is false (ADR-016 D4). " +
                        $"EmployeeId={employeeId}.");
                }
            }
            // AllowUpstreamAlignment=true with AlignedWindow rules: shrink-to-natural-edge
            // is rule-specific arithmetic (e.g. "snap to last week boundary") that the
            // planner can't execute generically. For S20 we pass the period through
            // unchanged when the flag is true; concrete shrink wiring lands when a
            // rule exposes its natural-edge calculator. This preserves the contract that
            // the planner never expands (it does not modify the period upward).
        }

        return (periodStart, periodEnd);
    }

    private static bool HasAnyInteriorBoundary(DateOnly periodStart, DateOnly periodEnd, BoundarySources sources)
    {
        foreach (var t in sources.OkTransitions)
            if (t.Date > periodStart && t.Date <= periodEnd) return true;
        foreach (var t in sources.AgreementConfigPromotions)
            if (t.Date > periodStart && t.Date <= periodEnd) return true;
        if (sources.LocalProfileActivations is { } profiles)
        {
            foreach (var t in profiles)
                if (t.EffectiveFrom > periodStart && t.EffectiveFrom <= periodEnd) return true;
        }
        foreach (var t in sources.PositionOverrideEffectiveDates)
            if (t.Date > periodStart && t.Date <= periodEnd) return true;
        foreach (var t in sources.EuWtdRulesetTransitions)
            if (t.Date > periodStart && t.Date <= periodEnd) return true;
        return false;
    }

    private static bool HasAnySnapshotContract(
        IReadOnlyList<RuleClassification> ruleSet,
        IPlannerEnrollment? enrollment)
    {
        for (int i = 0; i < ruleSet.Count; i++)
        {
            if (ruleSet[i].SnapshotContract is not null)
                return true;
        }
        // ADR-020 D1 component 3: the gate also fires when a non-rule consumer has
        // registered any enrollment. The hydrator-invocation gate (enrollmentActive
        // at the call-site) additionally requires a non-null profile to actually run
        // the hydrator; this method answers the rule-side question alone.
        if (enrollment is not null && enrollment.GetEnrollments().Count > 0)
            return true;
        return false;
    }

    private static RuleClassification? FindFirst(
        IReadOnlyList<RuleClassification> ruleSet,
        Func<RuleClassification, bool> predicate)
    {
        for (int i = 0; i < ruleSet.Count; i++)
        {
            if (predicate(ruleSet[i]))
                return ruleSet[i];
        }
        return null;
    }

    /// <summary>
    /// ADR-016 D9 rule-side invariants:
    /// <list type="bullet">
    ///   <item>For every rule with a <see cref="SnapshotContract"/>: every intersecting
    ///     segment carries a non-null <see cref="PlannedSegment.Snapshot"/>.</item>
    ///   <item>Every rule has a non-null resolved <see cref="MergeStrategy"/>.</item>
    ///   <item>Each declared <see cref="SnapshotContract.NonDatedSourceFields"/> entry is
    ///     present as a key in the snapshot dictionary (we do not assert non-null values —
    ///     a non-dated source can legitimately be null when the field is unset).</item>
    /// </list>
    /// </summary>
    private static void EnforceRuleInvariants(
        PlannedCalculation planned,
        IReadOnlyList<RuleClassification> ruleSet)
    {
        for (int i = 0; i < ruleSet.Count; i++)
        {
            var rule = ruleSet[i];

            // Non-null MergeStrategy: the type system enforces this at the RuleClassification
            // ctor (non-nullable), but we re-assert here so a manually-constructed list that
            // somehow nulls the field is caught. Belt + braces; the cost is one null check.
            if (rule.MergeStrategy is null)
                throw new PlannerInvariantViolation(
                    $"PeriodPlanner invariant violated: rule '{rule.RuleId}' has a null " +
                    $"MergeStrategy. Every rule must have a resolved merge strategy at " +
                    $"registration (ADR-016 D3 / D9). ManifestId={planned.ManifestId}.");

            // Snapshot completeness for rules that declare a SnapshotContract.
            if (rule.SnapshotContract is { } contract)
            {
                for (int s = 0; s < planned.Segments.Count; s++)
                {
                    var seg = planned.Segments[s];

                    if (seg.Snapshot is null)
                        throw new PlannerInvariantViolation(
                            $"PeriodPlanner invariant violated: rule '{rule.RuleId}' has a " +
                            $"SnapshotContract but segment {s} ([{seg.StartDate}..{seg.EndDate}]) " +
                            $"carries a null Snapshot. ManifestId={planned.ManifestId}.");

                    foreach (var field in contract.NonDatedSourceFields)
                    {
                        if (!seg.Snapshot.Values.ContainsKey(field))
                            throw new PlannerInvariantViolation(
                                $"PeriodPlanner invariant violated: rule '{rule.RuleId}' " +
                                $"declares non-dated field '{field}' but segment {s} " +
                                $"([{seg.StartDate}..{seg.EndDate}]) Snapshot does not carry " +
                                $"that key. ManifestId={planned.ManifestId}.");
                    }
                }
            }
        }
    }
}

/// <summary>
/// Pre-hydrated boundary sources for the planner (ADR-016 D5).
///
/// <strong>Pure data:</strong> no infrastructure imports. The calling service hydrates
/// these from DB / config sources before calling <see cref="PeriodPlanner.Plan"/>.
///
/// Each effective-dated list is interpreted as: "on this date, the source transitions
/// from the previous value to the new value". The <c>From*</c>/<c>To*</c> fields are
/// retained on the tuples for audit / future use; the planner currently uses only the
/// <c>Date</c> field to introduce a segment boundary, but the surrounding context is
/// kept on the wire so downstream consumers (e.g. SLS export per-line stamping) can
/// resolve the right value per segment without a second round-trip.
/// </summary>
/// <param name="OkTransitions">OK collective-agreement version transitions (e.g. OK24→OK26).</param>
/// <param name="AgreementConfigPromotions">DRAFT→ACTIVE agreement-config promotions (ADR-014).</param>
/// <param name="PositionOverrideEffectiveDates">Position-override effective-from dates (ADR-013, S11/S14).</param>
/// <param name="EuWtdRulesetTransitions">EU WTD compliance ruleset version bumps (ADR-015, S16).</param>
/// <param name="NonDatedSourceValues">Snapshot dictionary for non-effective-dated sources
/// (employee profile fields, wage-type mappings, entitlement-policy rows). Keyed by the
/// dotted field path declared in <see cref="SnapshotContract.NonDatedSourceFields"/>.</param>
/// <param name="LocalProfileActivations">Local-agreement-profile activation effective-from
/// dates (ADR-017, S21). Each <c>(EffectiveFrom, ProfileId)</c> introduces a boundary at
/// <c>EffectiveFrom</c> with cause <see cref="BoundaryCause.LocalProfileActivation"/>.
/// Defaults to <c>null</c> for backward compatibility with pre-S21 call sites; the planner
/// and detector treat <c>null</c> as the empty list (no profile-activation boundaries).
/// Positional order is intentionally last (after <see cref="NonDatedSourceValues"/>) so
/// existing positional construction continues to compile; the
/// <see cref="BoundaryCause.LocalProfileActivation"/> tie-break slot in
/// <c>BoundaryDetector.OrderedCauses</c> still sits between
/// <see cref="BoundaryCause.AgreementConfigPromotion"/> and
/// <see cref="BoundaryCause.PositionOverrideEffective"/> (ADR-017 D9b).</param>
public sealed record BoundarySources(
    IReadOnlyList<(DateOnly Date, string FromVersion, string ToVersion)> OkTransitions,
    IReadOnlyList<(DateOnly Date, string AgreementCode)> AgreementConfigPromotions,
    IReadOnlyList<(DateOnly Date, string PositionCode)> PositionOverrideEffectiveDates,
    IReadOnlyList<(DateOnly Date, int FromRulesetVersion, int ToRulesetVersion)> EuWtdRulesetTransitions,
    IReadOnlyDictionary<string, object?> NonDatedSourceValues,
    IReadOnlyList<(DateOnly EffectiveFrom, Guid ProfileId)>? LocalProfileActivations = null)
{
    /// <summary>
    /// Convenience empty instance — useful for tests and for the common
    /// no-boundary-sources case (single-segment plan).
    /// </summary>
    public static readonly BoundarySources Empty = new(
        Array.Empty<(DateOnly, string, string)>(),
        Array.Empty<(DateOnly, string)>(),
        Array.Empty<(DateOnly, string)>(),
        Array.Empty<(DateOnly, int, int)>(),
        new Dictionary<string, object?>(),
        Array.Empty<(DateOnly, Guid)>());
}
