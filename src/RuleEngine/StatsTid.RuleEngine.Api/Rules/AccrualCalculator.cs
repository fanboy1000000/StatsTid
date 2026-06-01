namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure rule-engine math for Danish monthly vacation accrual (Ferieloven
/// <em>samtidighedsferie</em>, ADR-030 — activates the ADR-021 D6 MONTHLY_ACCRUAL model).
///
/// Determinism (priority #2/#4): every input is passed explicitly — there is NO I/O,
/// NO wall-clock (<c>DateTime.Now</c>/<c>DateOnly.FromDateTime(DateTime.Today)</c>), and
/// NO mutable state. The result is a pure function of its arguments, so replay/re-derivation
/// is stable by construction (ADR-002).
/// </summary>
public static class AccrualCalculator
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

    /// <summary>Absolute month ordinal so month arithmetic crosses year boundaries cleanly.</summary>
    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month;
}
