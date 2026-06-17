namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// The single source of truth for resolving an entitlement type's <em>period geometry</em> — the
/// entitlement (accrual) year, the accrual-window start, the taking-window start, and the
/// settlement boundary — from a <c>reset_month</c> and an as-of date (S80 / ADR-033 Slice 2, R10).
///
/// <para><b>Why hoist (S80 Step-0b — both review lenses converged on HOIST):</b> SPECIAL_HOLIDAY
/// (særlige feriedage, the agreement-based "6th week") introduces a genuine <em>third geometry</em>
/// where the accrual year ≠ the taking-window year — neither the <c>reset_month == 9</c> (ferieår)
/// nor the <c>reset_month == 1</c> (calendar) branch expresses it. Before this slice the
/// "Month ≥ reset_month" rule was duplicated across 5–6 sites
/// (<c>SkemaEndpoints</c>, <c>BalanceEndpoints</c> ×2, <c>SettlementCloseService</c> ×2,
/// <c>VacationSettlementService</c>, <c>VacationSettlementEndpoints</c>); leaving it duplicated would
/// turn that into a 5–6-site THREE-geometry divergence ([[cross-process-caller-census]]). Every
/// site now delegates here and the period geometry exists in exactly one place.</para>
///
/// <para><b>Determinism (priority #2/#4):</b> every input is passed explicitly — there is NO I/O,
/// NO wall-clock, and NO mutable state. The result is a pure function of its arguments, so
/// replay/re-derivation is stable by construction (ADR-002). Same SharedKernel-leaf rationale as
/// <see cref="AccrualMath"/> and <see cref="OkVersionResolver"/>.</para>
///
/// <para><b>The three geometries (verified law, Cirkulære 021-24 §12; SPRINT-80 R1/R2/R3):</b>
/// <list type="bullet">
///   <item><description><b>VACATION (reset_month = 9) + every other existing type:</b>
///   BEHAVIOR-IDENTICAL to the pre-S80 sites. The entitlement year of a date <c>d</c> is
///   <c>d.Month &gt;= reset_month ? d.Year : d.Year − 1</c>; the accrual window AND the taking
///   window both start at <c>(entitlementYear, reset_month, 1)</c>; the boundary is the §21 31-Dec
///   deadline of the ferieår-END calendar year (reset_month 1 → 31 Dec E; reset_month 9 → 31 Dec
///   E+1). This is the HARD invariant — a VACATION-unchanged regression test pins it.</description></item>
///   <item><description><b>SPECIAL_HOLIDAY:</b> accrual is the CALENDAR year (1 Jan Y – 31 Dec Y);
///   the taking window is 1 May (Y+1) – 30 Apr (Y+2); the godtgørelse boundary is 30 Apr (Y+2).
///   The accrual year ≠ the taking-window year, and the usage→accrual-year mapping has BOTH halves
///   (Step-0b Reviewer BLOCKER — the taking window spans TWO calendar years): for a usage date
///   <c>d</c>, <c>takingPeriodStart = d.Month &gt;= 5 ? (d.Year, 5, 1) : (d.Year−1, 5, 1)</c>, and
///   <c>entitlementYear = takingPeriodStart.Year − 1</c> — i.e. <b>May–Dec T → accrual year T−1;
///   Jan–Apr T → accrual year T−2</b>. NOT expressible by any raw <c>reset_month</c>.</description></item>
/// </list></para>
/// </summary>
public static class EntitlementPeriodResolver
{
    /// <summary>The SPECIAL_HOLIDAY entitlement-type discriminator (the agreement-based "6th week").</summary>
    public const string SpecialHolidayType = "SPECIAL_HOLIDAY";

    /// <summary>
    /// The first month (1-based) of the SPECIAL_HOLIDAY taking window — 1 May (Cirkulære 021-24
    /// §12 stk.2). A usage date in/after May keys to the taking period that opened THIS May; before
    /// May keys to the taking period that opened LAST May.
    /// </summary>
    public const int SpecialHolidayTakingStartMonth = 5;

    /// <summary>
    /// The resolved period geometry for an entitlement type as-of (or for) a given year.
    /// All four dates are derived together from one <c>reset_month</c> so callers never re-derive a
    /// half of the geometry.
    /// </summary>
    /// <param name="EntitlementYear">
    /// The accrual ("optjenings") year the date/usage belongs to. For VACATION this is the ferieår
    /// start year; for SPECIAL_HOLIDAY it is the calendar accrual year (≠ the taking-window year).
    /// </param>
    /// <param name="AccrualStart">
    /// The first day accrual begins for <see cref="EntitlementYear"/> — the value fed to
    /// <see cref="AccrualMath.EarnedToDate"/> as <c>ferieaarStart</c>. SPECIAL_HOLIDAY ⇒ 1 Jan
    /// (R1 calendar accrual); VACATION ⇒ 1 Sep; calendar types ⇒ 1 Jan.
    /// </param>
    /// <param name="TakingStart">
    /// The first day of the window in which <see cref="EntitlementYear"/>'s days may be TAKEN.
    /// VACATION/calendar types take in the same year they accrue (TakingStart == AccrualStart);
    /// SPECIAL_HOLIDAY takes 1 May of the year AFTER the accrual year.
    /// </param>
    /// <param name="Boundary">
    /// The settlement boundary for <see cref="EntitlementYear"/> — the last day of the
    /// taking/afholdelses window. VACATION/calendar ⇒ the §21 31-Dec deadline of the ferieår-END
    /// year; SPECIAL_HOLIDAY ⇒ 30 Apr (EntitlementYear + 2) (R3).
    /// </param>
    /// <param name="AccrualEnd">
    /// The LAST day on which the entitlement year's days ACCRUE — the close of the 12-month accrual
    /// window opened by <see cref="AccrualStart"/>. DISTINCT from <see cref="Boundary"/> (the later
    /// settlement/afholdelses deadline): VACATION accrual ends 31 Aug (E+1) yet its settlement
    /// boundary is 31 Dec (E+1); SPECIAL_HOLIDAY accrual ends 31 Dec (Y) yet its boundary is
    /// 30 Apr (Y+2). The earned-at-boundary read MUST feed this (not Boundary) to
    /// <see cref="AccrualMath.EarnedToDate"/>, which clamps elapsed months from the accrual start
    /// (= hire date for a mid-period hire) but does NOT cap the asOf at the accrual-window end — so
    /// the later Boundary would over-count a mid-period hire (S80 / TASK-8001 BLOCKER 1).
    /// </param>
    public readonly record struct EntitlementPeriod(
        int EntitlementYear,
        DateOnly AccrualStart,
        DateOnly AccrualEnd,
        DateOnly TakingStart,
        DateOnly Boundary);

    /// <summary>
    /// Resolve the full period geometry for the entitlement <em>year that a usage / as-of date
    /// belongs to</em> (SkemaEndpoints / BalanceEndpoints booking + balance keying — R2).
    ///
    /// <para>For VACATION + every non-SPECIAL_HOLIDAY type this reproduces the pre-S80
    /// "Month ≥ reset_month" entitlement-year keying EXACTLY. For SPECIAL_HOLIDAY it applies the
    /// two-calendar-year taking-window mapping (May–Dec T → accrual T−1; Jan–Apr T → accrual T−2).</para>
    /// </summary>
    /// <param name="entitlementType">The entitlement type string (e.g. <c>"VACATION"</c>, <c>"SPECIAL_HOLIDAY"</c>).</param>
    /// <param name="resetMonth">The config's <c>reset_month</c> (1–12). Ignored for SPECIAL_HOLIDAY (its geometry is fixed by law).</param>
    /// <param name="asOf">The usage / absence / month-end date whose entitlement year is being resolved.</param>
    public static EntitlementPeriod Resolve(string entitlementType, int resetMonth, DateOnly asOf)
    {
        if (IsSpecialHoliday(entitlementType))
        {
            // R2 — the taking window spans TWO calendar years (1 May Y+1 .. 30 Apr Y+2), so the
            // usage→accrual-year mapping has BOTH halves. May–Dec keys to the taking period that
            // opened THIS May; Jan–Apr keys to the one that opened LAST May. The accrual year is
            // the year BEFORE the taking-window opens.
            var takingPeriodStart = asOf.Month >= SpecialHolidayTakingStartMonth
                ? new DateOnly(asOf.Year, SpecialHolidayTakingStartMonth, 1)
                : new DateOnly(asOf.Year - 1, SpecialHolidayTakingStartMonth, 1);
            var entitlementYear = takingPeriodStart.Year - 1;
            return BuildSpecialHoliday(entitlementYear);
        }

        // VACATION + every other existing type — the pre-S80 "Month ≥ reset_month" keying, unchanged.
        var year = asOf.Month >= resetMonth ? asOf.Year : asOf.Year - 1;
        return BuildResetMonth(resetMonth, year);
    }

    /// <summary>
    /// Resolve the full period geometry for an <em>already-known</em> entitlement (accrual) year —
    /// the settlement-service entry point (<c>SettlementCloseService</c>, <c>VacationSettlementService</c>,
    /// <c>VacationSettlementEndpoints</c>), which start from a stored <c>entitlement_year</c> rather
    /// than from a usage date.
    ///
    /// <para>For VACATION + non-SPECIAL_HOLIDAY types this reproduces the pre-S80 ferieår
    /// [start, end] + boundary derivation EXACTLY. For SPECIAL_HOLIDAY it yields the 1 Jan accrual
    /// start, the 1 May (Y+1) taking start, and the 30 Apr (Y+2) boundary.</para>
    /// </summary>
    public static EntitlementPeriod ResolveForYear(string entitlementType, int resetMonth, int entitlementYear)
        => IsSpecialHoliday(entitlementType)
            ? BuildSpecialHoliday(entitlementYear)
            : BuildResetMonth(resetMonth, entitlementYear);

    /// <summary>True iff the type is SPECIAL_HOLIDAY (case-sensitive, ordinal — the canonical seed value).</summary>
    public static bool IsSpecialHoliday(string entitlementType)
        => string.Equals(entitlementType, SpecialHolidayType, StringComparison.Ordinal);

    // ── geometry builders ────────────────────────────────────────────────

    /// <summary>
    /// SPECIAL_HOLIDAY (R1/R2/R3): calendar accrual (1 Jan Y), taking window opens 1 May (Y+1),
    /// godtgørelse boundary 30 Apr (Y+2). No carryover, no §34, no §22.
    /// </summary>
    private static EntitlementPeriod BuildSpecialHoliday(int entitlementYear) => new(
        EntitlementYear: entitlementYear,
        AccrualStart: new DateOnly(entitlementYear, 1, 1),
        // Accrual closes 31 Dec of the calendar accrual year (12 months from 1 Jan); the godtgørelse
        // boundary 30 Apr (Y+2) is the LATER taking-window close — see Boundary.
        AccrualEnd: new DateOnly(entitlementYear, 12, 31),
        TakingStart: new DateOnly(entitlementYear + 1, SpecialHolidayTakingStartMonth, 1),
        Boundary: new DateOnly(entitlementYear + 2, 4, 30));

    /// <summary>
    /// The pre-S80 reset_month geometry (VACATION reset_month 9; calendar types reset_month 1):
    /// accrual == taking, both start at <c>(entitlementYear, reset_month, 1)</c>; the boundary is
    /// the §21 31-Dec deadline of the ferieår-END calendar year (reset_month 1 → 31 Dec E; reset_month
    /// 9 → 31 Dec E+1). Byte-identical to the IsBoundaryPassed / ResolveDeadlineAndCapAsync / D9
    /// derivations the consuming sites used before.
    /// </summary>
    private static EntitlementPeriod BuildResetMonth(int resetMonth, int entitlementYear)
    {
        DateOnly accrualStart;
        DateOnly ferieaarEnd;
        if (resetMonth == 1)
        {
            accrualStart = new DateOnly(entitlementYear, 1, 1);
            ferieaarEnd = new DateOnly(entitlementYear, 12, 31);
        }
        else
        {
            accrualStart = new DateOnly(entitlementYear, resetMonth, 1);
            ferieaarEnd = accrualStart.AddYears(1).AddDays(-1);
        }
        // The §21 31-Dec deadline of the ferieår-END year (the existing settlement boundary).
        var boundary = new DateOnly(ferieaarEnd.Year, 12, 31);
        return new EntitlementPeriod(
            EntitlementYear: entitlementYear,
            AccrualStart: accrualStart,
            // The accrual window CLOSES at ferieaarEnd (31 Aug E+1 for VACATION reset_month 9;
            // 31 Dec E for calendar reset_month 1) — earlier than (VACATION) or equal to (calendar)
            // the §21 31-Dec settlement boundary. This is the asOf the earned-at-boundary read needs.
            AccrualEnd: ferieaarEnd,
            TakingStart: accrualStart,   // VACATION/calendar: accrue & take in the same window.
            Boundary: boundary);
    }
}
