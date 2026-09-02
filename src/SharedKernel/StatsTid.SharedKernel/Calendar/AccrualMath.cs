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
    ///
    /// <para><b>Accrual end (leavers — S137 / ADR-040 D9):</b> accrual stops at the last employed
    /// day. When <paramref name="employmentEnd"/> has a value and <paramref name="asOf"/> falls
    /// AFTER it, <paramref name="asOf"/> is clamped to <paramref name="employmentEnd"/> BEFORE the
    /// month arithmetic, so the running balance a leaver (or HR) sees stops rising at the leave
    /// date instead of accruing all the way to the ferieår end. The clamp lives HERE, inside the
    /// single-source math, never at a call site (the S61 single-source guard forbids call-site
    /// formula work). Idempotent by construction when <c>asOf &lt;= employmentEnd</c> — an in-window
    /// as-of is untouched, which is why the settlement rails that already valuate AT the end date
    /// (ADR-033) are unaffected when they opt in. A null <paramref name="employmentEnd"/> means
    /// open-ended employment — today's behaviour, byte-identical for every pre-S137 caller. An
    /// <paramref name="employmentEnd"/> BEFORE the accrual start yields zero earned (nothing was
    /// earned in a ferieår the person had already left — the mirror of "start after asOf ⇒ 0").</para>
    /// </summary>
    /// <param name="annualQuota">Full annual entitlement in days (e.g. VACATION 25, SPECIAL_HOLIDAY 5).</param>
    /// <param name="partTimeFraction">Dated employment fraction; 1.0 = full-time.</param>
    /// <param name="ferieaarStart">First day of the current ferieår (e.g. 1 Sep).</param>
    /// <param name="employmentStart">HR-managed hire date; null ⇒ full-ferieår assumption.</param>
    /// <param name="asOf">The consumption as-of date (absence date / month-end). Never wall-clock.</param>
    /// <param name="employmentEnd">HR-managed LAST employed day (INCLUSIVE, ADR-040 D1); null ⇒
    /// open-ended (no end-cap — the pre-S137 behaviour). Optional and trailing so every existing
    /// caller compiles unchanged and opts in explicitly.</param>
    /// <returns>Exact fractional days earned so far in the ferieår (never negative).</returns>
    public static decimal EarnedToDate(
        decimal annualQuota,
        decimal partTimeFraction,
        DateOnly ferieaarStart,
        DateOnly? employmentStart,
        DateOnly asOf,
        DateOnly? employmentEnd = null)
    {
        // S137 / ADR-040 D9 — end-cap: nothing accrues after the last employed day. Clamp the
        // as-of to the spell end BEFORE counting months; a null end is open-ended (no-op).
        if (employmentEnd.HasValue && asOf > employmentEnd.Value)
        {
            asOf = employmentEnd.Value;
        }

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

    /// <summary>Absolute month ordinal so month arithmetic crosses year boundaries cleanly.</summary>
    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month;
}
