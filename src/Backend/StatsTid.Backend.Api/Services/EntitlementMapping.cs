using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Services;

/// <summary>
/// S65 / TASK-6502 — shared Backend.Api home for two constants that were previously private
/// to <c>SkemaEndpoints</c> and must now be consumed by BOTH the Skema endpoints and the new
/// Balance year-overview read, with NO second literal / second copy of the map (Step-0b Codex
/// W1). Promoted as-is (values unchanged) so the existing Skema behavior is preserved
/// byte-for-byte.
///
/// <para>
/// <b>Why the map matters for year-overview <c>afholdt</c> (Step-0b Codex W1).</b> The
/// <c>absences_projection</c> rows carry the ABSENCE type (e.g. <c>SPECIAL_HOLIDAY_ALLOWANCE</c>,
/// the three <c>CHILD_SICK_DAY*</c> variants), NOT the entitlement type. Aggregating
/// <c>afholdt</c> by filtering the projection's <c>absence_type</c> directly against an
/// entitlement-type string (e.g. <c>"SPECIAL_HOLIDAY"</c>) would silently zero the Feriefridage
/// row. The aggregation MUST resolve each row's <c>absence_type</c> THROUGH
/// <see cref="AbsenceToEntitlementType"/> first (e.g. <c>SPECIAL_HOLIDAY_ALLOWANCE</c> →
/// <c>SPECIAL_HOLIDAY</c>).
/// </para>
/// </summary>
internal static class EntitlementMapping
{
    /// <summary>
    /// Standard work day hours (37h/week ÷ 5 days). Single source — no second <c>7.4</c> literal.
    ///
    /// <para>
    /// <b>S66 / ADR-032 — role NARROWED.</b> This constant is NO LONGER the general consumption
    /// divisor. Vacation consumption (feriedage) now divides absence <c>Hours</c> by the employee's
    /// REAL per-day norm (<c>fullDayHours</c> from <c>ConsumptionCalculator</c>/<c>DailyNormCalculator</c>),
    /// so a half-time full day (3.7h) consumes a full feriedag. The remaining live roles of this
    /// 7.4 constant are exactly three:
    /// <list type="number">
    ///   <item><description>the LEGACY backfill convention — <c>hours / 7.4</c> for pre-S66
    ///     serialized events lacking a <c>Feriedage</c> field (init.sql / ProjectionBackfillService),
    ///     kept for byte-stability with already-persisted history;</description></item>
    ///   <item><description>the NON-entitlement weekend cap — a non-entitlement absence row (e.g.
    ///     a sick hour) on a zero-norm day is bounded by this flat 7.4 (ADR-032 D3);</description></item>
    ///   <item><description>the ANNUAL_ACTIVITY (academic) fallback BASE — <c>7.4 × partTimeFraction</c>
    ///     is the <c>fullDayHours</c> used when a per-weekday norm is not meaningful (ADR-032 D3).</description></item>
    /// </list>
    /// It is NOT the WEEKLY_HOURS-case consumption divisor anymore.
    /// </para>
    /// </summary>
    public const decimal StandardDayHours = 7.4m;

    /// <summary>
    /// Absence type → entitlement type mapping (<c>null</c> = no entitlement gating / skip).
    /// Promoted verbatim from <c>SkemaEndpoints</c>; the StringComparer.Ordinal keying is
    /// preserved. S147 / TASK-14708 (QUAL-231): the KEYS are the SharedKernel
    /// <see cref="AbsenceTypes"/> constants (the stored spelling — same strings as before, so
    /// no lookup changes); the VALUES are entitlement types, a different vocabulary, and stay
    /// as written.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> AbsenceToEntitlementType =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AbsenceTypes.Vacation] = "VACATION",
            [AbsenceTypes.CareDay] = "CARE_DAY",
            [AbsenceTypes.ChildSick1] = "CHILD_SICK",
            [AbsenceTypes.ChildSick2] = "CHILD_SICK",
            [AbsenceTypes.ChildSick3] = "CHILD_SICK",
            [AbsenceTypes.ParentalLeave] = null,
            [AbsenceTypes.SeniorDay] = "SENIOR_DAY",
            [AbsenceTypes.SpecialHolidayAllowance] = "SPECIAL_HOLIDAY",
            [AbsenceTypes.LeaveWithPay] = null,
            [AbsenceTypes.LeaveWithoutPay] = null,
            [AbsenceTypes.SickDay] = null
        };

    /// <summary>
    /// Resolve an absence type to its entitlement type (<c>null</c> = no entitlement gating),
    /// reusing <see cref="AbsenceToEntitlementType"/>. Unknown types ⇒ <c>null</c>.
    /// </summary>
    public static string? GetEntitlementType(string absenceType)
        => AbsenceToEntitlementType.TryGetValue(absenceType, out var et) ? et : null;
}
