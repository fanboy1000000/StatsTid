namespace StatsTid.Tools.DemoSeed;

/// <summary>
/// Resolves the <c>--reference-date</c> generate argument to a concrete <see cref="DateOnly"/>.
///
/// <para>The generator itself is wall-clock-free by design (all its dates derive from the reference
/// date it is handed). This resolver is the ONLY place a wall-clock reading enters, and only when
/// the caller explicitly opts in with <c>--reference-date rolling</c> — used by the reseed so the
/// demo's activity lands in a RECENT month instead of the fixed pinned month. The default and any
/// explicit ISO date stay deterministic, so <c>generate --scale full</c> (no date) keeps producing
/// the byte-identical committed artifacts.</para>
///
/// <para><b>"rolling" ⇒ the first of <paramref name="today"/>'s month.</b> The activity generator uses
/// <c>referenceDate.AddMonths(-1)</c> (the last COMPLETE month), so anchoring the reference at the
/// first of the current month puts the seeded activity in the PREVIOUS calendar month — a finished
/// month that always passes the app's submit/approve rules and stays ~1 month old, never stale.</para>
/// </summary>
public static class ReferenceDateResolver
{
    /// <summary>The pinned default all committed artifacts and the golden pins are generated at.</summary>
    public static readonly DateOnly PinnedDefault = new(2026, 6, 15);

    /// <summary>
    /// Maps the raw <c>--reference-date</c> value to a concrete date. <c>null</c>/empty or an
    /// unparseable value ⇒ <see cref="PinnedDefault"/> (matches the pre-rolling behaviour);
    /// <c>"rolling"</c> ⇒ the first of <paramref name="today"/>'s month; an ISO date ⇒ that date.
    /// </summary>
    public static DateOnly Resolve(string? arg, DateOnly today) => (arg?.Trim().ToLowerInvariant()) switch
    {
        null or "" => PinnedDefault,
        "rolling" => new DateOnly(today.Year, today.Month, 1),
        var s when DateOnly.TryParse(s, out var d) => d,
        _ => PinnedDefault,
    };
}
