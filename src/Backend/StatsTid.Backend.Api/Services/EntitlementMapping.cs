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
    /// Standard work day hours (37h/week ÷ 5 days). The day-equivalent divisor for converting
    /// absence Hours → day-equivalents (same math as the Skema quota guard, e.g.
    /// <c>SkemaEndpoints</c> :738 / :1076). Single source — no second <c>7.4</c> literal anywhere.
    /// </summary>
    public const decimal StandardDayHours = 7.4m;

    /// <summary>
    /// Absence type → entitlement type mapping (<c>null</c> = no entitlement gating / skip).
    /// Promoted verbatim from <c>SkemaEndpoints</c>; the StringComparer.Ordinal keying is
    /// preserved.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> AbsenceToEntitlementType =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["VACATION"] = "VACATION",
            ["CARE_DAY"] = "CARE_DAY",
            ["CHILD_SICK_DAY"] = "CHILD_SICK",
            ["CHILD_SICK_DAY_2"] = "CHILD_SICK",
            ["CHILD_SICK_DAY_3"] = "CHILD_SICK",
            ["PARENTAL_LEAVE"] = null,
            ["SENIOR_DAY"] = "SENIOR_DAY",
            ["SPECIAL_HOLIDAY_ALLOWANCE"] = "SPECIAL_HOLIDAY",
            ["LEAVE_WITH_PAY"] = null,
            ["LEAVE_WITHOUT_PAY"] = null,
            ["SICK_DAY"] = null
        };

    /// <summary>
    /// Resolve an absence type to its entitlement type (<c>null</c> = no entitlement gating),
    /// reusing <see cref="AbsenceToEntitlementType"/>. Unknown types ⇒ <c>null</c>.
    /// </summary>
    public static string? GetEntitlementType(string absenceType)
        => AbsenceToEntitlementType.TryGetValue(absenceType, out var et) ? et : null;
}
