using StatsTid.SharedKernel.Calendar;

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
    /// The one <c>--reference-date</c> value that consumes a clock reading. Named once so the
    /// switch below and the <see cref="TimeProvider"/> overload's lazy guard cannot drift apart —
    /// if they did, the overload would either read the clock needlessly or (worse) skip reading it
    /// on a branch that needs it.
    /// </summary>
    private const string RollingArg = "rolling";

    /// <summary>
    /// Maps the raw <c>--reference-date</c> value to a concrete date. <c>null</c>/empty or an
    /// unparseable value ⇒ <see cref="PinnedDefault"/> (matches the pre-rolling behaviour);
    /// <c>"rolling"</c> ⇒ the first of <paramref name="today"/>'s month; an ISO date ⇒ that date.
    /// </summary>
    public static DateOnly Resolve(string? arg, DateOnly today) => (arg?.Trim().ToLowerInvariant()) switch
    {
        null or "" => PinnedDefault,
        RollingArg => new DateOnly(today.Year, today.Month, 1),
        var s when DateOnly.TryParse(s, out var d) => d,
        _ => PinnedDefault,
    };

    /// <summary>
    /// The CLI's entry point: same mapping as <see cref="Resolve(string?, DateOnly)"/>, but "today"
    /// is read from <paramref name="timeProvider"/> as the <b>Copenhagen</b> calendar day.
    ///
    /// <para><b>S142 / TASK-14210 (owner ruling OQ-10, 2026-09-16) — why this overload exists.</b>
    /// StatsTid's users are all Danish, so a "today" used to pick a BUSINESS date must be the Danish
    /// calendar day. This site previously read <c>DateTime.Today</c>, the MACHINE-LOCAL day: correct
    /// on a Danish developer's laptop purely because the machine's zone happened to be Copenhagen,
    /// and silently wrong on a UTC CI box. The dependency is now stated rather than assumed.</para>
    ///
    /// <para><b>The failure window is narrow, which is exactly why it needs a pin.</b> Only the
    /// <c>rolling</c> branch reads the clock, and it consumes only the year and month, so a one-day
    /// skew changes the answer ONLY at a month boundary — roughly two hours on twelve nights a year.
    /// Pinned by <c>ReferenceDateResolverTests</c> at 2026-07-31 22:30Z, where Copenhagen (CEST,
    /// +02:00) is already 1 August while UTC is still 31 July.</para>
    ///
    /// <para><b>Determinism is untouched.</b> The default (no <c>--reference-date</c>) and any
    /// explicit ISO date short-circuit to <see cref="PinnedDefault"/> / that date without ever
    /// calling <see cref="CopenhagenBusinessDate.Today"/> — the committed artifacts and the golden
    /// pin never traverse the clock path at all.</para>
    /// </summary>
    public static DateOnly Resolve(string? arg, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var normalized = arg?.Trim().ToLowerInvariant();

        // The clock is read LAZILY, on the `rolling` branch only. C# evaluates arguments eagerly,
        // so `Resolve(arg, CopenhagenBusinessDate.Today(tp))` would read the clock on EVERY run and
        // discard the reading on the deterministic ones. Discarding it would be harmless in fact
        // (the helper is pure), but it would make the determinism claim an argument about what the
        // code does with a value rather than a property you can see: with this shape, the default
        // and explicit-ISO paths — the ones that produce the committed artifact and the golden pin —
        // provably never touch a clock at all. `rolling` is the only consumer of `today`.
        return normalized == RollingArg
            ? Resolve(normalized, CopenhagenBusinessDate.Today(timeProvider))
            : Resolve(normalized, PinnedDefault);
    }
}
