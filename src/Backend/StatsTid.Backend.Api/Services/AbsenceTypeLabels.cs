using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Services;

/// <summary>
/// S147 / TASK-14701 (QUAL-235) — the ONE Danish display-label map for absence codes, keyed by
/// the stored absence-type string (the value <c>absences_projection.absence_type</c> carries).
///
/// <para>
/// <b>Why one map.</b> Before S147 the same table was copied twice — privately in
/// <c>SkemaEndpoints</c> (the Skema catalogue, the visible rows, the row-preferences PUT echo and
/// the save path's 422 messages) and privately in <c>ConfigEndpoints</c> (the admin absence-type
/// visibility list, <c>GET /api/config/{orgId}/absence-types</c>). Two copies drift: adding a
/// code to one surface but not the other is exactly how særlige feriedage ended up with a balance
/// but no row and no admin toggle. Both endpoints now read this map, so a code that gains a label
/// here appears on every absence-type surface at once.
/// </para>
///
/// <para>
/// <b>Order.</b> <see cref="InDisplayOrder"/> is the order the admin visibility list serves (the
/// pre-S147 insertion order of the copied tables, with <c>SPECIAL_HOLIDAY_ALLOWANCE</c> placed
/// beside <c>VACATION</c>). The Skema catalogue's own order comes from its agreement list in
/// <c>SkemaEndpoints</c>, not from here; this map only supplies labels there.
/// </para>
///
/// <para>
/// <b>Keyed by the stored string.</b> S147 / TASK-14708 (QUAL-231): the keys are the SharedKernel
/// <see cref="AbsenceTypes"/> constants, whose values ARE the stored strings, so re-keying was a
/// rename of the literal, never a value change. Every code here is a vocabulary code; the
/// vocabulary's one classification is <see cref="AbsenceClassification"/>.
/// </para>
///
/// <para>
/// <b>One fallback rule.</b> A reader that needs a label calls <see cref="LabelFor"/>, which
/// falls back to the code itself; no reader spells that fallback by hand (S147 / TASK-14708,
/// review follow-up A-2: the six Skema label sites route through it).
/// </para>
///
/// <para>
/// <b>Deliberately <c>public</c>.</b> The Backend.Api assembly declares no
/// <c>InternalsVisibleTo</c>, so <c>internal</c> would make this pure map untestable from a
/// non-Docker Unit fact (the <c>HrFollowUpSettlementEndpoints</c> precedent).
/// </para>
/// </summary>
public static class AbsenceTypeLabels
{
    /// <summary>
    /// Every labelled absence code with its Danish label, in display order. A duplicate code
    /// fails type initialisation (the <see cref="ByCode"/> build below throws), so a copy-paste
    /// slip cannot silently shadow a label.
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> InDisplayOrder =
    [
        new(AbsenceTypes.SickDay, "Sygedag"),
        new(AbsenceTypes.Vacation, "Ferie"),
        // S147 / TASK-14701 (QUAL-235): særlige feriedage (Cirkulære 021-24 §12), the absence code
        // EntitlementMapping maps to the SPECIAL_HOLIDAY entitlement. The label matches the
        // frontend's entitlement label (frontend/src/lib/entitlementConstants.ts).
        new(AbsenceTypes.SpecialHolidayAllowance, "Særlig feriedag"),
        new(AbsenceTypes.CareDay, "Omsorgsdag"),
        new(AbsenceTypes.ChildSick1, "Barns 1. sygedag"),
        new(AbsenceTypes.ChildSick2, "Barns 2. sygedag"),
        new(AbsenceTypes.ChildSick3, "Barns 3. sygedag"),
        new(AbsenceTypes.ParentalLeave, "Barsel"),
        new(AbsenceTypes.SeniorDay, "Seniordag"),
        new(AbsenceTypes.LeaveWithPay, "Tjenestefri m. løn"),
        new(AbsenceTypes.LeaveWithoutPay, "Tjenestefri u. løn"),
    ];

    /// <summary>The same map for lookup, ordinal-keyed (the stored strings are case-exact).</summary>
    public static readonly IReadOnlyDictionary<string, string> ByCode =
        InDisplayOrder.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>
    /// The Danish label for <paramref name="absenceType"/>, or the code itself when it has none
    /// (the fallback every pre-S147 reader of either copied table used). The one place that
    /// rule is spelled: the Skema catalogue, visible rows, row-preferences echo and the save's
    /// 422 messages all call this.
    /// </summary>
    public static string LabelFor(string absenceType)
        => ByCode.TryGetValue(absenceType, out var label) ? label : absenceType;
}
