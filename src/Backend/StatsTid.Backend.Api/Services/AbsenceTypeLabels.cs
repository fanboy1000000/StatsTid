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
/// <b>Keyed by the stored string.</b> TASK-14708 (S147 wave 2a) moves the codes to the
/// SharedKernel constants; the keys stay the stored strings, so re-keying is a rename of the
/// literal, never a value change.
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
        new("SICK_DAY", "Sygedag"),
        new("VACATION", "Ferie"),
        // S147 / TASK-14701 (QUAL-235): særlige feriedage (Cirkulære 021-24 §12), the absence code
        // EntitlementMapping maps to the SPECIAL_HOLIDAY entitlement. The label matches the
        // frontend's entitlement label (frontend/src/lib/entitlementConstants.ts).
        new("SPECIAL_HOLIDAY_ALLOWANCE", "Særlig feriedag"),
        new("CARE_DAY", "Omsorgsdag"),
        new("CHILD_SICK_DAY", "Barns 1. sygedag"),
        new("CHILD_SICK_DAY_2", "Barns 2. sygedag"),
        new("CHILD_SICK_DAY_3", "Barns 3. sygedag"),
        new("PARENTAL_LEAVE", "Barsel"),
        new("SENIOR_DAY", "Seniordag"),
        new("LEAVE_WITH_PAY", "Tjenestefri m. løn"),
        new("LEAVE_WITHOUT_PAY", "Tjenestefri u. løn"),
    ];

    /// <summary>The same map for lookup, ordinal-keyed (the stored strings are case-exact).</summary>
    public static readonly IReadOnlyDictionary<string, string> ByCode =
        InDisplayOrder.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>
    /// The Danish label for <paramref name="absenceType"/>, or the code itself when it has none
    /// (the fallback every pre-S147 reader of either copied table used).
    /// </summary>
    public static string LabelFor(string absenceType)
        => ByCode.TryGetValue(absenceType, out var label) ? label : absenceType;
}
