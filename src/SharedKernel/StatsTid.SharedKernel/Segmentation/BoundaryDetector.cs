namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Pure helper used by <see cref="PeriodPlanner"/> to identify segment boundaries
/// inside a calculation period (ADR-016 D1, D5).
///
/// A "boundary" here is the date on which a new segment starts (i.e., the first day
/// the rules see a new OK version / agreement-config / position-override / EU WTD
/// ruleset / employment state / employee-profile row — the last two per ADR-040 D5).
/// Boundaries are sorted ascending and deduped; if multiple causes coincide
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
    /// <b>Documentation-only:</b> the EXECUTABLE tie-break is the literal foreach order
    /// in <see cref="Detect"/> combined with first-write-wins <see cref="AddIfAbsent"/>.
    /// Keep this array synced with that order — a divergence is a doc bug, not a
    /// behavior change.
    ///
    /// Order rationale (ADR-017 D9b, extended by ADR-040 D5):
    /// <list type="number">
    ///   <item><see cref="BoundaryCause.EmploymentStarted"/> — employment-window causes
    ///     outrank everything: non-employment is the strongest fact about a date. A hire
    ///     on 2026-04-01 coincides with the OK24→OK26 transition and must record as the
    ///     hire (ADR-040 D5).</item>
    ///   <item><see cref="BoundaryCause.EmploymentEnded"/> — same rationale; start
    ///     outranks end so a same-day re-hire signature would record as the start.</item>
    ///   <item><see cref="BoundaryCause.OkTransition"/> — most-impactful non-employment
    ///     cause; if an OK transition coincides with anything below, attribute the
    ///     segment to OK.</item>
    ///   <item><see cref="BoundaryCause.AgreementConfigPromotion"/> — DRAFT→ACTIVE
    ///     promotions are the next-most-impactful for downstream rules.</item>
    ///   <item><see cref="BoundaryCause.LocalProfileActivation"/> — local-agreement-profile
    ///     activation effective-from (ADR-017 D9b, S21); per-org scope, sits below
    ///     agreement-level promotions but above per-position overrides.</item>
    ///   <item><see cref="BoundaryCause.PositionOverrideEffective"/> — per-position
    ///     scope; affects fewer rules.</item>
    ///   <item><see cref="BoundaryCause.EmployeeProfileChange"/> — per-employee scope
    ///     (position / part-time-fraction effective dates, ADR-040 D5 activation of the
    ///     ADR-016 D5b reservation); slots after per-position overrides per the D5
    ///     ruling.</item>
    ///   <item><see cref="BoundaryCause.EuWtdRulesetVersion"/> — narrow compliance
    ///     scope; lowest-impact.</item>
    /// </list>
    /// </summary>
    private static readonly BoundaryCause[] OrderedCauses =
    {
        BoundaryCause.EmploymentStarted,
        BoundaryCause.EmploymentEnded,
        BoundaryCause.OkTransition,
        BoundaryCause.AgreementConfigPromotion,
        BoundaryCause.LocalProfileActivation,
        BoundaryCause.PositionOverrideEffective,
        BoundaryCause.EmployeeProfileChange,
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

        // EmploymentStartedDates (ADR-040 D5). Employment-window causes run FIRST — they
        // outrank everything (a hire on 2026-04-01, the OK24→OK26 date, must record as
        // EmploymentStarted, not OkTransition). Nullable for backward compatibility with
        // pre-S137 callers; null is treated as the empty list. Entries are the
        // employment_start_date itself — the first employed day (D1 fencepost).
        if (sources.EmploymentStartedDates is { } employmentStarts)
        {
            foreach (var d in employmentStarts)
            {
                if (IsInsidePeriod(d, periodStart, periodEnd))
                    AddIfAbsent(byDate, d, BoundaryCause.EmploymentStarted);
            }
        }

        // EmploymentEndedDates (ADR-040 D5). Entries are employment_end_date + 1 — the
        // FIRST NOT-employed day, because the window's end is INCLUSIVE (D1) and a
        // boundary date is the first day of the NEW segment. The caller applies the +1
        // when hydrating (documented on BoundarySources).
        if (sources.EmploymentEndedDates is { } employmentEnds)
        {
            foreach (var d in employmentEnds)
            {
                if (IsInsidePeriod(d, periodStart, periodEnd))
                    AddIfAbsent(byDate, d, BoundaryCause.EmploymentEnded);
            }
        }

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

        // EmployeeProfileEffectiveDates (ADR-040 D5 activation of the ADR-016 D5b-reserved
        // EmployeeProfileChange cause): employee_profiles effective_from dates (position /
        // part-time-fraction changes) now split segments. Tie-break slot per the D5 ruling:
        // after PositionOverrideEffective, before EuWtdRulesetVersion. Nullable for
        // backward compatibility with pre-S137 callers; null is treated as the empty list.
        if (sources.EmployeeProfileEffectiveDates is { } profileEffectiveDates)
        {
            foreach (var d in profileEffectiveDates)
            {
                if (IsInsidePeriod(d, periodStart, periodEnd))
                    AddIfAbsent(byDate, d, BoundaryCause.EmployeeProfileChange);
            }
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
