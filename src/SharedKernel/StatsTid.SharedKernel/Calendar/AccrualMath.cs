namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// Pure calendar math for Danish monthly vacation accrual (Ferieloven
/// <em>samtidighedsferie</em>, ADR-030 — the ADR-021 D6 MONTHLY_ACCRUAL model).
///
/// <para>Single source of truth (S61 / TASK-6101): the earned-to-date formula was previously
/// triplicated — the authoritative copy in the Rule Engine's <c>AccrualCalculator</c> plus
/// byte-identical Backend-local mirrors in the Balance and Skema endpoints (the Backend may not
/// reference the RuleEngine assembly — PAT-005 makes that boundary HTTP-only). A pure calendar
/// constant with no dependencies is the textbook SharedKernel citizen (same rationale as
/// <see cref="OkVersionResolver"/>), so every caller now delegates here and the math exists in
/// exactly one place.</para>
///
/// <para>Determinism (priority #2/#4): every input is passed explicitly — there is NO I/O,
/// NO wall-clock (<c>DateTime.Now</c>/<c>DateOnly.FromDateTime(DateTime.Today)</c>), and
/// NO mutable state. The result is a pure function of its arguments, so replay/re-derivation
/// is stable by construction (ADR-002).</para>
/// </summary>
public static class AccrualMath
{
    /// <summary>
    /// Gross vacation days earned-to-date (<em>optjent</em>) within the current ferieår.
    ///
    /// Earning is linear-by-month on the ferieår: the annual quota accrues evenly across the
    /// 12 months of the ferieår, so VACATION (25 d) earns ≈ 2,08 d/md and SPECIAL_HOLIDAY
    /// (5 d) earns ≈ 0,42 d/md — both fall out of the generic <paramref name="annualQuota"/>.
    ///
    /// <para><b>Monthly basis (pinned, Q3 default — exact fractional, do NOT round here):</b>
    /// the number of <em>elapsed accrual months</em> is the count of whole month-boundaries
    /// crossed between the accrual start and <paramref name="asOf"/> (inclusive of the start
    /// month, exclusive of an unfinished month). Concretely
    /// <c>monthsElapsed = (asOf.Year*12 + asOf.Month) − (start.Year*12 + start.Month) + 1</c>,
    /// clamped to <c>[0, 12]</c>. Result =
    /// <c>annualQuota × partTimeFraction × monthsElapsed / 12</c> as an exact decimal. Rounding
    /// is a DISPLAY concern handled by callers — never rounded here.</para>
    ///
    /// <para><b>Accrual start (mid-ferieår hires):</b> accrual begins at
    /// <c>max(ferieaarStart, employmentStart)</c>. A null <paramref name="employmentStart"/>
    /// assumes a full ferieår (start at <paramref name="ferieaarStart"/>) — this deliberately
    /// does NOT fail-closed: a missing hire date must not wrongly deny already-earned vacation
    /// (opposite polarity from the S59 DOB age gate, which gates eligibility). An employment
    /// start AFTER <paramref name="asOf"/> yields zero earned.</para>
    /// </summary>
    /// <param name="annualQuota">Full annual entitlement in days (e.g. VACATION 25, SPECIAL_HOLIDAY 5).</param>
    /// <param name="partTimeFraction">Dated employment fraction; 1.0 = full-time.</param>
    /// <param name="ferieaarStart">First day of the current ferieår (e.g. 1 Sep).</param>
    /// <param name="employmentStart">HR-managed hire date; null ⇒ full-ferieår assumption.</param>
    /// <param name="asOf">The consumption as-of date (absence date / month-end). Never wall-clock.</param>
    /// <returns>Exact fractional days earned so far in the ferieår (never negative).</returns>
    public static decimal EarnedToDate(
        decimal annualQuota,
        decimal partTimeFraction,
        DateOnly ferieaarStart,
        DateOnly? employmentStart,
        DateOnly asOf)
    {
        // Accrual begins at the later of ferieår start and employment start.
        var accrualStart = ferieaarStart;
        if (employmentStart.HasValue && employmentStart.Value > accrualStart)
        {
            accrualStart = employmentStart.Value;
        }

        // Whole accrual months crossed, inclusive of the start month. Before the accrual
        // start (asOf earlier than the start month) ⇒ 0; clamped to a full ferieår (12).
        var monthsElapsed = MonthIndex(asOf) - MonthIndex(accrualStart) + 1;
        if (monthsElapsed <= 0)
        {
            return 0m;
        }
        if (monthsElapsed > 12)
        {
            monthsElapsed = 12;
        }

        // Exact fractional — no rounding here (round only for display, elsewhere).
        return annualQuota * partTimeFraction * monthsElapsed / 12m;
    }

    /// <summary>
    /// Cumulative <b>piecewise</b> vacation days earned-to-date (<em>optjent</em>) when the employment
    /// fraction CHANGES within the ferieår (ADR-030 D8 — supersedes the single-fraction sub-decision
    /// of D1 for accrual reads; the single-fraction <see cref="EarnedToDate"/> stays for non-accrual
    /// callers and is the byte-equality oracle below).
    ///
    /// <para>Danish <em>samtidighedsferie</em> earns concurrently: each elapsed accrual month earns
    /// at the part-time fraction IN EFFECT that month, and the months are summed —
    /// <c>annualQuota × Σ(fraction_i) / 12</c>. This is exactly the construction that avoids the S61
    /// <c>/series</c> non-monotonicity bug, where applying a single per-point fraction retroactively
    /// to ALL of a point's elapsed months made the curve drop when the fraction fell; summing
    /// per-month fractions can never decrease as months are added.</para>
    ///
    /// <para><b>Window — IDENTICAL to <see cref="EarnedToDate"/> (load-bearing):</b> accrual starts at
    /// <c>max(ferieaarStart, employmentStart)</c>; the elapsed-month count
    /// (<c>MonthIndex(asOf) − MonthIndex(accrualStart) + 1</c>) is clamped to <c>[0, 12]</c>. The
    /// 12-cap is what stops a fraction change in month 13+ (or an <paramref name="asOf"/> past the
    /// ferieår end) from over-summing past a full ferieår — the same invariant the single-fraction
    /// method relies on, applied BEFORE the per-month loop.</para>
    ///
    /// <para><b>Month anchor = month-START whole-month policy:</b> accrual month <c>i ∈ [0, n)</c> is
    /// anchored at the 1st of that accrual month
    /// (<c>new DateOnly(accrualStart.Year, accrualStart.Month, 1).AddMonths(i)</c>); its fraction is
    /// the period covering that anchor (half-open <c>[From, To)</c> via
    /// <see cref="FractionPeriod.Covers"/>). A change that takes effect mid-month therefore applies
    /// from the NEXT month — intra-month pro-ration is deliberately out of scope (it would diverge
    /// from <c>/summary</c> and the Skema quota guard, which are whole-month).</para>
    ///
    /// <para><b>Gap polarity</b> (no period covers an anchor): if
    /// <paramref name="fractionHistory"/> is empty the employee is treated as full-time
    /// (<c>1.0</c>) — the ONLY case that yields 1.0. If any period exists, an anchor BEFORE the
    /// earliest <c>From</c> carries the earliest (first-known) fraction backward, and an anchor after
    /// all periods (defensive — the live period is normally open-ended) carries the last-known
    /// fraction forward. A pre-history in-window month must never inflate toward full-time.</para>
    ///
    /// <para><b>Constant short-circuit (byte-equality guarantee):</b> when every resolved fraction
    /// equals the first, the legacy expression <c>annualQuota × f0 × n / 12m</c> is returned verbatim,
    /// so a constant-fraction employee is bit-identical to <see cref="EarnedToDate"/>. Otherwise the
    /// fractions are summed first, then a single <c>× / 12m</c>. Because <c>part_time_fraction</c> is
    /// <c>NUMERIC(4,3)</c> (scale ≤ 3), summing ≤ 12 such values is exact in <c>decimal</c> — no
    /// rounding error and no rounding applied (rounding stays a display concern, ADR-030 D1/D5).</para>
    ///
    /// <para>Pure and deterministic (ADR-002 / ADR-030 D5): no I/O, no wall-clock, no mutable state;
    /// a pure function of its arguments. Never returns negative.</para>
    /// </summary>
    /// <param name="annualQuota">Full annual entitlement in days (e.g. VACATION 25, SPECIAL_HOLIDAY 5).</param>
    /// <param name="ferieaarStart">First day of the current ferieår (e.g. 1 Sep).</param>
    /// <param name="employmentStart">HR-managed hire date; null ⇒ full-ferieår assumption.</param>
    /// <param name="asOf">The consumption as-of date (absence date / month-end). Never wall-clock.</param>
    /// <param name="fractionHistory">Ordered-by-date dated fraction periods (half-open
    /// <c>[From, To)</c>); empty ⇒ full-time across the window.</param>
    /// <returns>Exact fractional days earned so far in the ferieår (never negative).</returns>
    public static decimal EarnedToDatePiecewise(
        decimal annualQuota,
        DateOnly ferieaarStart,
        DateOnly? employmentStart,
        DateOnly asOf,
        IReadOnlyList<FractionPeriod> fractionHistory)
    {
        // Accrual begins at the later of ferieår start and employment start — identical to EarnedToDate.
        var accrualStart = ferieaarStart;
        if (employmentStart.HasValue && employmentStart.Value > accrualStart)
        {
            accrualStart = employmentStart.Value;
        }

        // IDENTICAL windowed [0,12] clamp as EarnedToDate, applied BEFORE summation so a fraction
        // change beyond month 12 (or an asOf past ferieår end) can never over-sum past a full ferieår.
        var clampedMonths = MonthIndex(asOf) - MonthIndex(accrualStart) + 1;
        if (clampedMonths <= 0)
        {
            return 0m;
        }
        if (clampedMonths > 12)
        {
            clampedMonths = 12;
        }

        // First-of-accrual-month anchor; each successive month is + i months from it.
        var firstAnchor = new DateOnly(accrualStart.Year, accrualStart.Month, 1);

        var f0 = ResolveFraction(firstAnchor, fractionHistory);
        var sum = f0;
        var allEqualF0 = true;
        for (var i = 1; i < clampedMonths; i++)
        {
            var fraction = ResolveFraction(firstAnchor.AddMonths(i), fractionHistory);
            if (fraction != f0)
            {
                allEqualF0 = false;
            }
            sum += fraction;
        }

        // Constant-fraction short-circuit: return the legacy expression VERBATIM for byte-equality
        // with EarnedToDate (the single-source guard counts files, so this second '/ 12m' is fine —
        // it stays inside AccrualMath.cs).
        if (allEqualF0)
        {
            return annualQuota * f0 * clampedMonths / 12m;
        }

        // Σf is exact (NUMERIC(4,3) scale over ≤12 additions); one multiply + one divide, no rounding.
        return annualQuota * sum / 12m;
    }

    /// <summary>
    /// Resolves the part-time fraction in effect at <paramref name="anchor"/> (the 1st of an accrual
    /// month) under the gap polarity documented on <see cref="EarnedToDatePiecewise"/>: the covering
    /// period if any; else carry the earliest fraction backward / the last fraction forward; else
    /// (empty history only) full-time 1.0.
    /// </summary>
    private static decimal ResolveFraction(DateOnly anchor, IReadOnlyList<FractionPeriod> history)
    {
        if (history.Count == 0)
        {
            // Only the all-empty case is full-time — never when any period exists.
            return 1.0m;
        }

        // Direct hit on a covering half-open [From, To) period.
        foreach (var period in history)
        {
            if (period.Covers(anchor))
            {
                return period.Fraction;
            }
        }

        // Gap: find the earliest (smallest From) and latest (largest From) known periods, then carry
        // the appropriate end's fraction. No ordering of `history` is assumed.
        var earliest = history[0];
        var latest = history[0];
        for (var i = 1; i < history.Count; i++)
        {
            if (history[i].From < earliest.From) earliest = history[i];
            if (history[i].From > latest.From) latest = history[i];
        }

        // Front gap (before any period started) ⇒ carry the first-known fraction backward (NEVER 1.0
        // when periods exist). Otherwise (after all periods — defensive) ⇒ carry the last forward.
        return anchor < earliest.From ? earliest.Fraction : latest.Fraction;
    }

    /// <summary>Absolute month ordinal so month arithmetic crosses year boundaries cleanly.</summary>
    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month;
}
