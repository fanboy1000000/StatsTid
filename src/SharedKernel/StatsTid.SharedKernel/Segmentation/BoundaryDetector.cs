namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Pure helper used by <see cref="PeriodPlanner"/> to identify segment boundaries
/// inside a calculation period (ADR-016 D1, D5).
///
/// A "boundary" here is the date on which a new segment starts (i.e., the first day
/// the rules see a new OK version / agreement-config / position-override / EU WTD
/// ruleset). Boundaries are sorted ascending and deduped; if multiple causes coincide
/// on the same date, the first one encountered (in iteration order over the sources)
/// wins for that segment's <see cref="BoundaryCause"/>. The deterministic order documented
/// in <see cref="OrderedCauses"/> is what defines "first encountered".
///
/// Pure data; no I/O, no allocation outside the returned list.
/// </summary>
internal static class BoundaryDetector
{
    /// <summary>
    /// Iteration order for <see cref="BoundaryCause"/> values when multiple causes
    /// coincide on the same date. Documented here so the tie-break is deterministic
    /// across runs and easy to reason about in tests.
    ///
    /// Order rationale:
    /// <list type="number">
    ///   <item><see cref="BoundaryCause.OkTransition"/> — most-impactful cause; if an
    ///     OK transition coincides with anything, attribute the segment to OK.</item>
    ///   <item><see cref="BoundaryCause.AgreementConfigPromotion"/> — DRAFT→ACTIVE
    ///     promotions are the next-most-impactful for downstream rules.</item>
    ///   <item><see cref="BoundaryCause.LocalProfileActivation"/> — local-agreement-profile
    ///     activation effective-from (ADR-017 D9b, S21); per-org scope, sits below
    ///     agreement-level promotions but above per-position overrides.</item>
    ///   <item><see cref="BoundaryCause.PositionOverrideEffective"/> — per-position
    ///     scope; affects fewer rules.</item>
    ///   <item><see cref="BoundaryCause.EuWtdRulesetVersion"/> — narrow compliance
    ///     scope; lowest-impact.</item>
    /// </list>
    /// </summary>
    private static readonly BoundaryCause[] OrderedCauses =
    {
        BoundaryCause.OkTransition,
        BoundaryCause.AgreementConfigPromotion,
        BoundaryCause.LocalProfileActivation,
        BoundaryCause.PositionOverrideEffective,
        BoundaryCause.EuWtdRulesetVersion,
    };

    /// <summary>
    /// Collect boundary dates from <paramref name="sources"/> falling <strong>strictly
    /// inside</strong> <c>(periodStart, periodEnd]</c>. Boundaries at <paramref name="periodStart"/>
    /// are not splits (they are part of the first segment's starting context).
    /// Boundaries at <paramref name="periodEnd"/> are unreachable in this convention because
    /// a segment starting at <paramref name="periodEnd"/> would still produce a single-day
    /// final segment <c>[periodEnd, periodEnd]</c>; we treat the upper bound as inclusive
    /// so the last boundary can validly be <paramref name="periodEnd"/> itself.
    ///
    /// Returns an ordered list of <c>(Date, Cause)</c> deduplicated by date with cause
    /// tie-break per <see cref="OrderedCauses"/>.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, BoundaryCause Cause)> Detect(
        DateOnly periodStart,
        DateOnly periodEnd,
        BoundarySources sources)
    {
        // Per-cause buckets keyed by date. We populate in OrderedCauses order so that
        // a later cause cannot overwrite an earlier (higher-priority) cause on the same date.
        var byDate = new SortedDictionary<DateOnly, BoundaryCause>();

        // OkTransitions
        foreach (var t in sources.OkTransitions)
        {
            if (IsInsidePeriod(t.Date, periodStart, periodEnd))
                AddIfAbsent(byDate, t.Date, BoundaryCause.OkTransition);
        }

        // AgreementConfigPromotions
        foreach (var t in sources.AgreementConfigPromotions)
        {
            if (IsInsidePeriod(t.Date, periodStart, periodEnd))
                AddIfAbsent(byDate, t.Date, BoundaryCause.AgreementConfigPromotion);
        }

        // LocalProfileActivations (ADR-017 D9b, S21). Nullable for backward compatibility
        // with pre-S21 callers that construct BoundarySources without specifying the field;
        // null is treated as the empty list (no profile-activation boundaries).
        if (sources.LocalProfileActivations is { } profileActivations)
        {
            foreach (var t in profileActivations)
            {
                if (IsInsidePeriod(t.EffectiveFrom, periodStart, periodEnd))
                    AddIfAbsent(byDate, t.EffectiveFrom, BoundaryCause.LocalProfileActivation);
            }
        }

        // PositionOverrideEffectiveDates
        foreach (var t in sources.PositionOverrideEffectiveDates)
        {
            if (IsInsidePeriod(t.Date, periodStart, periodEnd))
                AddIfAbsent(byDate, t.Date, BoundaryCause.PositionOverrideEffective);
        }

        // EuWtdRulesetTransitions
        foreach (var t in sources.EuWtdRulesetTransitions)
        {
            if (IsInsidePeriod(t.Date, periodStart, periodEnd))
                AddIfAbsent(byDate, t.Date, BoundaryCause.EuWtdRulesetVersion);
        }

        var result = new List<(DateOnly Date, BoundaryCause Cause)>(byDate.Count);
        foreach (var kv in byDate)
            result.Add((kv.Key, kv.Value));

        return result;
    }

    /// <summary>
    /// A boundary date <c>d</c> introduces a split when <c>periodStart &lt; d &lt;= periodEnd</c>.
    /// At <c>d == periodStart</c>, the date is the period's own start — no split.
    /// </summary>
    private static bool IsInsidePeriod(DateOnly date, DateOnly periodStart, DateOnly periodEnd)
        => date > periodStart && date <= periodEnd;

    private static void AddIfAbsent(
        SortedDictionary<DateOnly, BoundaryCause> map,
        DateOnly date,
        BoundaryCause cause)
    {
        if (!map.ContainsKey(date))
            map[date] = cause;
        // If the date is already in the map, the earlier (higher-priority) cause wins
        // because we iterate causes in OrderedCauses order. No overwrite.
    }
}
