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
    /// <param name="employmentWindows">ADR-040 D5 (S137): the employee's employment
    /// window(s), resolver-supplied by the caller (the payroll host reads them via
    /// <c>IEmploymentWindowResolver.GetWindowsAsync</c> — spells-proof list shape), used
    /// ONLY to type each segment's <see cref="PlannedSegment.EmploymentStatus"/>.
    /// Semantics: <c>null</c> = windowless caller (pre-S137 call sites and every
    /// employee without window enforcement) — every segment types EMPLOYED, which is
    /// byte-identical to pre-D5 behavior; an EMPTY list = the caller consulted the
    /// resolver and NO window overlaps the period — every segment types NOT_EMPLOYED.
    /// This parameter does NOT introduce boundaries: the corresponding
    /// <see cref="BoundarySources.EmploymentStartedDates"/> /
    /// <see cref="BoundarySources.EmploymentEndedDates"/> must be hydrated by the same
    /// caller from the same windows (fenceposts documented there), or a window edge
    /// interior to the period will not split — and the straddling segment then types
    /// EMPLOYED (fail-safe, see <see cref="ResolveSegmentEmploymentStatus"/>).
    /// The typed statuses ALSO decide the ADR-016 D4 refusal (S137 owner ruling
    /// 2026-09-02, see <see cref="ApplyAlignmentPolicy"/>): a whole-window rule is refused
    /// only when the plan holds ≥ 2 EMPLOYED segments. A windowless caller types every
    /// segment EMPLOYED, so for it any interior boundary still refuses exactly as before
    /// S137; a caller that supplies windows gets hire/leave edges treated as
    /// truncations (the NOT_EMPLOYED side evaluates nothing), not as splits.</param>
    /// <returns>A <see cref="PlannedCalculation"/> ready for evaluation by
    /// <c>PeriodCalculationService.CalculateAsync</c>.</returns>
    /// <exception cref="PlannerInvariantViolation">Thrown when geometric invariants
    /// (in the ctor) or rule-side invariants (here in the planner) are violated.
    /// Also thrown for the ADR-016 D4 split refusal — a <see cref="SplitBehavior.Reject"/>
    /// or (with <see cref="PlannerOptions.AllowUpstreamAlignment"/> false) an
    /// <see cref="SplitBehavior.AlignedWindow"/> rule that would be evaluated in ≥ 2
    /// EMPLOYED segments; see <see cref="ApplyAlignmentPolicy"/>.</exception>
    public static PlannedCalculation Plan(
        string employeeId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string calculationKind,
        IReadOnlyList<RuleClassification> ruleSet,
        BoundarySources sources,
        PlannerOptions options,
        IPlannerEnrollment? enrollment = null,
        EmploymentProfile? profile = null,
        IReadOnlyList<EmploymentWindow>? employmentWindows = null)
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

        // --- 1. Detect boundaries ---
        var boundaries = BoundaryDetector.Detect(periodStart, periodEnd, sources);

        // --- 2 + 3. Build contiguous, TYPED segment ranges ---
        // Segment[0] starts at periodStart. If there are no boundaries, single segment
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
        //
        // Each range is typed EMPLOYED / NOT_EMPLOYED here (ADR-040 D5) because the D4
        // alignment policy below keys on the EMPLOYED count — typing must precede it.
        var ranges = new List<TypedRange>(boundaries.Count + 1);
        if (boundaries.Count == 0)
        {
            ranges.Add(TypeRange(periodStart, periodEnd, BoundaryCause.OkTransition, employmentWindows));
        }
        else
        {
            // First segment: [periodStart, boundary[0]-1], cause = boundary[0].Cause.
            // (Tie-break documented in BoundaryDetector.OrderedCauses.)
            ranges.Add(TypeRange(periodStart, boundaries[0].Date.AddDays(-1), boundaries[0].Cause, employmentWindows));

            // Middle segments: [boundary[i].Date, boundary[i+1].Date-1], cause = boundary[i+1].Cause.
            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                ranges.Add(TypeRange(boundaries[i].Date, boundaries[i + 1].Date.AddDays(-1), boundaries[i + 1].Cause, employmentWindows));
            }

            // Final segment: [boundary[N-1].Date, periodEnd], cause = boundary[N-1].Cause.
            ranges.Add(TypeRange(boundaries[^1].Date, periodEnd, boundaries[^1].Cause, employmentWindows));
        }

        // --- D4 upstream-alignment policy (ADR-016 D4 as ruled 2026-09-02, S137) ---
        // Decided AFTER detection and typing, because the refusal keys on how many EMPLOYED
        // segments a whole-window rule would be EVALUATED in — not on boundary count. Runs
        // before snapshot gathering so a refused plan allocates nothing further.
        ApplyAlignmentPolicy(periodStart, periodEnd, ruleSet, boundaries, ranges, options, employeeId);

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

        // ADR-040 D5 typed segments: NOT_EMPLOYED iff the segment lies outside every
        // supplied employment window. NOT_EMPLOYED segments keep the uniform
        // sharedSnapshot like every other segment — they are SKIPPED at evaluation time,
        // not stripped here: the snapshot is a plan-construction input artifact (hydrated
        // once from caller-supplied inputs), not a per-segment resolver read, so keeping
        // it preserves manifest replay/rebuild semantics (every segment deserializes
        // identically) and changes no bytes for windowless plans (EMPLOYED serializes as
        // an absent key — see PlannedSegment).
        var segments = new List<PlannedSegment>(ranges.Count);
        foreach (var r in ranges)
        {
            segments.Add(new PlannedSegment(r.Start, r.End, r.Cause, sharedSnapshot, r.Status));
        }

        // --- 5. Construct PlannedCalculation via the internal ctor (geometric invariants here) ---
        var manifestId = Guid.NewGuid();
        var planned = new PlannedCalculation(
            manifestId,
            employeeId,
            periodStart,
            periodEnd,
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
    /// A segment date range with its ADR-040 D5 employment typing, built in
    /// <see cref="Plan"/> BEFORE snapshots are gathered so that
    /// <see cref="ApplyAlignmentPolicy"/> can count EMPLOYED ranges.
    /// </summary>
    private readonly record struct TypedRange(
        DateOnly Start,
        DateOnly End,
        BoundaryCause Cause,
        EmploymentWindowStatus Status);

    private static TypedRange TypeRange(
        DateOnly start,
        DateOnly end,
        BoundaryCause cause,
        IReadOnlyList<EmploymentWindow>? employmentWindows)
        => new(start, end, cause, ResolveSegmentEmploymentStatus(start, end, employmentWindows));

    /// <summary>
    /// ADR-016 D4 as applied after the S137 owner ruling (2026-09-02): the planner refuses
    /// a plan when a whole-window rule would be <b>evaluated in two or more EMPLOYED
    /// segments</b> — not when the period merely contains an interior boundary.
    ///
    /// <para>
    /// <b>Why a split is unsafe (the rule D4 protects).</b> An <see cref="SplitBehavior.AlignedWindow"/>
    /// rule (weekly norm, overtime, daily/weekly rest) reasons over a whole window. If the
    /// planner cut that window into two segments, the rule would run twice and produce two
    /// half-results that cannot be combined into one correct answer — which is exactly what
    /// <see cref="MergeStrategy.RejectIfMultipleSegments"/> refuses at merge time. A
    /// <see cref="SplitBehavior.Reject"/> rule declares the same unmergeability
    /// unconditionally. Refusing up front, here, keeps that failure from surfacing as a
    /// half-computed payroll result.
    /// </para>
    ///
    /// <para>
    /// <b>Why an employment edge is NOT a split (ADR-040 D5).</b> A hire or leave date inside
    /// the period does type two segments, but the NOT_EMPLOYED one evaluates nothing — no
    /// rule call, no result, no export line. The rule therefore still runs exactly ONCE, over
    /// a shorter span, and reaches the merger as a single segment. A shorter span is the same
    /// class of input the rule already sees at every month edge (months rarely start on a
    /// Monday). Treating the edge as a truncation is what lets a mid-month leaver's or
    /// starter's month plan and pay under the live rule set; before the ruling, every such
    /// month was refused.
    /// </para>
    ///
    /// <para>
    /// <b>Why windowless callers are behavior-identical.</b> With <c>employmentWindows == null</c>
    /// every segment types EMPLOYED, so "≥ 2 EMPLOYED segments" is exactly "any interior
    /// boundary" — the pre-S137 rule, byte for byte. The retired <c>HasAnyInteriorBoundary</c>
    /// predicate re-listed every source <c>BoundaryDetector.Detect</c> consumes and had to be
    /// kept in step with it by hand; counting the typed ranges the detector actually produced
    /// is complete over every source BY CONSTRUCTION, so there is no mirror to maintain.
    /// </para>
    ///
    /// <para>
    /// <b>What still refuses.</b> Two or more EMPLOYED segments: a profile change (position /
    /// part-time fraction) while employed, or two employment spells inside one period. In the
    /// live rule set that refusal is the registered ADR-016 D4 follow-up (QUAL-149): the
    /// norm/overtime rules refuse a profile-change split until they are reclassified with a
    /// pro-rating merger.
    /// </para>
    ///
    /// <para>
    /// <b>Flag semantics (unchanged since S20).</b> <see cref="SplitBehavior.Reject"/> rules
    /// ignore <see cref="PlannerOptions.AllowUpstreamAlignment"/>. For AlignedWindow rules
    /// with the flag <c>true</c>, D4 specifies "shrink (never expand) to the rule's natural
    /// window edge" — but that edge is rule-specific arithmetic (e.g. "snap to the last week
    /// boundary") that lives in the RuleEngine, not SharedKernel, so the planner passes the
    /// plan through unchanged; concrete shrink wiring lands when a rule exposes its
    /// natural-edge calculator. The planner never expands a period.
    /// </para>
    ///
    /// <para>
    /// <b>Message contract.</b> The thrown message names the rule id, the period, the
    /// EMPLOYED segment count and the interior boundary CAUSES — never a segment date: a
    /// segment start can reveal an employment date, and ADR-040 D7 keeps employment dates out
    /// of anything that can reach a response body. It also states that an employment edge
    /// alone would not have refused, so a reader learns the rule from the error.
    /// </para>
    /// </summary>
    private static void ApplyAlignmentPolicy(
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<RuleClassification> ruleSet,
        IReadOnlyList<(DateOnly Date, BoundaryCause Cause)> boundaries,
        IReadOnlyList<TypedRange> ranges,
        PlannerOptions options,
        string employeeId)
    {
        var employedCount = 0;
        for (int i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Status == EmploymentWindowStatus.EMPLOYED)
                employedCount++;
        }

        // ≤ 1 EMPLOYED segment: either no interior boundary at all, or every boundary is an
        // employment edge that truncates evaluation. Nothing is split; nothing to refuse.
        if (employedCount < 2)
            return;

        var causes = DescribeInteriorBoundaryCauses(boundaries);

        // Reject rules: a genuine split is fatal regardless of AllowUpstreamAlignment.
        var rejectRule = FindFirst(ruleSet, r => r.SplitBehavior == SplitBehavior.Reject);
        if (rejectRule is not null)
        {
            throw new PlannerInvariantViolation(
                $"PeriodPlanner.Plan invariant violated: rule '{rejectRule.RuleId}' has " +
                $"SplitBehavior=Reject, but the calculation period [{periodStart}..{periodEnd}] " +
                $"would evaluate it in {employedCount} EMPLOYED segments (interior boundary " +
                $"causes: {causes}). A Reject rule cannot be evaluated in pieces, and " +
                $"PlannerOptions.AllowUpstreamAlignment never overrides Reject (ADR-016 D4, as " +
                $"ruled 2026-09-02). An employment edge alone (a hire or a leave date) would " +
                $"NOT have refused: it truncates evaluation to one EMPLOYED segment instead of " +
                $"splitting it. EmployeeId={employeeId}.");
        }

        if (!options.AllowUpstreamAlignment)
        {
            var alignedRule = FindFirst(ruleSet, r => r.SplitBehavior == SplitBehavior.AlignedWindow);
            if (alignedRule is not null)
            {
                throw new PlannerInvariantViolation(
                    $"PeriodPlanner.Plan invariant violated: rule '{alignedRule.RuleId}' has " +
                    $"SplitBehavior=AlignedWindow and PlannerOptions.AllowUpstreamAlignment is " +
                    $"false, but the calculation period [{periodStart}..{periodEnd}] would " +
                    $"evaluate it in {employedCount} EMPLOYED segments (interior boundary " +
                    $"causes: {causes}). A whole-window rule cannot be evaluated in two pieces " +
                    $"and merged (ADR-016 D4, as ruled 2026-09-02). An employment edge alone (a " +
                    $"hire or a leave date) would NOT have refused: it truncates evaluation to " +
                    $"one EMPLOYED segment instead of splitting it. EmployeeId={employeeId}.");
            }
        }
        // AllowUpstreamAlignment=true with AlignedWindow rules: shrink-to-natural-edge
        // is rule-specific arithmetic (e.g. "snap to last week boundary") that the
        // planner can't execute generically. For S20 we pass the plan through
        // unchanged when the flag is true; concrete shrink wiring lands when a
        // rule exposes its natural-edge calculator. This preserves the contract that
        // the planner never expands (it does not modify the period upward).
    }

    /// <summary>
    /// Distinct <see cref="BoundaryCause"/> names of the detected interior boundaries, in
    /// date order — the "what typed this plan" part of the D4 refusal message. Causes only,
    /// never the boundary dates (ADR-040 D7).
    /// </summary>
    private static string DescribeInteriorBoundaryCauses(
        IReadOnlyList<(DateOnly Date, BoundaryCause Cause)> boundaries)
    {
        var names = new List<string>(boundaries.Count);
        for (int i = 0; i < boundaries.Count; i++)
        {
            var name = boundaries[i].Cause.ToString();
            if (!names.Contains(name))
                names.Add(name);
        }
        return names.Count == 0 ? "(none)" : string.Join(", ", names);
    }

    /// <summary>
    /// ADR-040 D5 segment typing: a segment is NOT_EMPLOYED iff it lies ENTIRELY outside
    /// every supplied employment window; <c>null</c> windows (windowless caller) type
    /// everything EMPLOYED — pre-D5 behavior by construction.
    ///
    /// <para>
    /// Window semantics are ADR-040 D1/D2: <c>[Start, End]</c> with End INCLUSIVE (the
    /// last day employed); a <c>null</c> side is unbounded. Fenceposts follow directly —
    /// the last employed day belongs to the EMPLOYED segment (the NOT_EMPLOYED suffix
    /// starts at <c>End + 1</c>), and the pre-hire NOT_EMPLOYED prefix ends at
    /// <c>Start − 1</c> (day <c>Start</c> is employed).
    /// </para>
    ///
    /// <para>
    /// A segment that OVERLAPS a window edge types EMPLOYED. This cannot happen when the
    /// caller hydrated <see cref="BoundarySources.EmploymentStartedDates"/> /
    /// <see cref="BoundarySources.EmploymentEndedDates"/> from the same windows (the edge
    /// splits the segment first), so the overlap branch is the FAIL-SAFE for a caller
    /// that supplied windows without the matching boundary dates: mistyping toward
    /// EMPLOYED preserves today's behavior (rules evaluate, lines export), whereas
    /// mistyping toward NOT_EMPLOYED would silently evaluate zero rules — the failure
    /// mode ADR-040 D5 forbids.
    /// </para>
    /// </summary>
    private static EmploymentWindowStatus ResolveSegmentEmploymentStatus(
        DateOnly segmentStart,
        DateOnly segmentEnd,
        IReadOnlyList<EmploymentWindow>? employmentWindows)
    {
        if (employmentWindows is null)
            return EmploymentWindowStatus.EMPLOYED;

        foreach (var window in employmentWindows)
        {
            var endsBeforeWindowOpens = window.Start.HasValue && segmentEnd < window.Start.Value;
            var startsAfterWindowCloses = window.End.HasValue && segmentStart > window.End.Value;
            if (!endsBeforeWindowOpens && !startsAfterWindowCloses)
                return EmploymentWindowStatus.EMPLOYED;
        }

        // Windows were supplied (possibly an empty list — "resolver consulted, nothing
        // overlaps the period") and none covers any day of this segment.
        return EmploymentWindowStatus.NOT_EMPLOYED;
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
/// Positional order is intentionally after <see cref="NonDatedSourceValues"/> so
/// pre-S21 positional construction continues to compile; the
/// <see cref="BoundaryCause.LocalProfileActivation"/> tie-break slot in
/// <c>BoundaryDetector.OrderedCauses</c> still sits between
/// <see cref="BoundaryCause.AgreementConfigPromotion"/> and
/// <see cref="BoundaryCause.PositionOverrideEffective"/> (ADR-017 D9b).</param>
/// <param name="EmploymentStartedDates">ADR-040 D5 (S137): employment-window OPEN dates —
/// each entry is the <c>employment_start_date</c> ITSELF (the first employed day; the
/// pre-hire NOT_EMPLOYED prefix ends at <c>start − 1</c>, ADR-040 D1) and introduces a
/// boundary with cause <see cref="BoundaryCause.EmploymentStarted"/> — the HIGHEST
/// tie-break rank. Hydrated by the caller from
/// <c>IEmploymentWindowResolver.GetWindowsAsync</c> (a list, so spells stay a
/// storage+resolver-only change). Optional trailing param, <c>null</c> = empty (the
/// <see cref="LocalProfileActivations"/> backward-compatibility precedent — every
/// pre-S137 positional construction keeps compiling).</param>
/// <param name="EmploymentEndedDates">ADR-040 D5 (S137): employment-window CLOSE dates —
/// <b>each entry is <c>employment_end_date + 1</c>, the FIRST NOT-employed day</b>,
/// because the window's end is INCLUSIVE (the last day employed, ADR-040 D1) and a
/// boundary date is always the first day of the NEW segment. Passing the end date itself
/// is the classic off-by-one: it would cut the LAST EMPLOYED DAY out of the employed
/// segment. Cause <see cref="BoundaryCause.EmploymentEnded"/>, second-highest tie-break
/// rank. Optional trailing param, <c>null</c> = empty.</param>
/// <param name="EmployeeProfileEffectiveDates">ADR-040 D5 (S137): <c>employee_profiles</c>
/// <c>effective_from</c> dates inside the period (position / part-time-fraction changes)
/// — activates the ADR-016 D5b-reserved <see cref="BoundaryCause.EmployeeProfileChange"/>
/// cause (tie-break slot: after <see cref="BoundaryCause.PositionOverrideEffective"/>,
/// before <see cref="BoundaryCause.EuWtdRulesetVersion"/>). Hydrated by the caller from
/// the profile-history read. Optional trailing param, <c>null</c> = empty.</param>
public sealed record BoundarySources(
    IReadOnlyList<(DateOnly Date, string FromVersion, string ToVersion)> OkTransitions,
    IReadOnlyList<(DateOnly Date, string AgreementCode)> AgreementConfigPromotions,
    IReadOnlyList<(DateOnly Date, string PositionCode)> PositionOverrideEffectiveDates,
    IReadOnlyList<(DateOnly Date, int FromRulesetVersion, int ToRulesetVersion)> EuWtdRulesetTransitions,
    IReadOnlyDictionary<string, object?> NonDatedSourceValues,
    IReadOnlyList<(DateOnly EffectiveFrom, Guid ProfileId)>? LocalProfileActivations = null,
    IReadOnlyList<DateOnly>? EmploymentStartedDates = null,
    IReadOnlyList<DateOnly>? EmploymentEndedDates = null,
    IReadOnlyList<DateOnly>? EmployeeProfileEffectiveDates = null)
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
        Array.Empty<(DateOnly, Guid)>(),
        Array.Empty<DateOnly>(),
        Array.Empty<DateOnly>(),
        Array.Empty<DateOnly>());
}
