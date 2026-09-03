namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S137 / ADR-040 D1 — one employment spell as a pair of dates: <c>[Start, End]</c> with
/// the end date INCLUSIVE (the last day employed — the ADR-033 / S70 R1 semantics D1
/// pins). A <c>null</c> side is unbounded (D2): <c>null</c> Start = employed since the
/// beginning of time, <c>null</c> End = open-ended employment; a both-<c>null</c> window
/// covers every date.
///
/// <para>
/// This is the SPELLS-PROOF shape (ADR-040 D1): consumers receive a LIST of these from
/// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentWindowResolver.GetWindowsAsync"/>
/// — 0-or-1 entries while storage is the single <c>users</c>-row window, a genuine list
/// when the deferred spells increment (re-hire) lands. Growing to spells then changes
/// storage + resolver only, never this type or its consumers.
/// </para>
///
/// <para>
/// <b>Fencepost warning for boundary hydration (the codebase's recurring
/// inclusive/exclusive hazard):</b> a segment-boundary date is the FIRST day of the NEW
/// segment, so when translating a window into <see cref="Segmentation.BoundarySources"/>
/// entries, <c>EmploymentStarted</c>'s boundary is <see cref="Start"/> itself (the first
/// employed day) but <c>EmploymentEnded</c>'s boundary is <see cref="End"/><c> + 1</c>
/// (the first NOT-employed day) — never <see cref="End"/> itself, which is still employed.
/// <see cref="FirstNotEmployedDay"/> IS that boundary date.
/// </para>
///
/// <para>
/// <b>S138 / TASK-13806 — the date arithmetic lives HERE, once.</b> Before this sprint the
/// window∩range test was hand-rolled in three places (the resolver's range filter, the
/// planner's segment typing, the compliance check's "first employed day of the month") —
/// three chances to get an inclusive/exclusive comparison wrong. The helpers below are the
/// single implementation those call sites now share; each is total over the D1/D2
/// semantics (a <c>null</c> side never needs a special case at the call site) and every
/// comparison is pinned by the fencepost matrix in <c>EmploymentWindowTests</c>.
/// </para>
/// </summary>
/// <param name="Start">First employed day; <c>null</c> = unbounded into the past (ADR-040 D2).</param>
/// <param name="End">Last employed day (INCLUSIVE, ADR-040 D1); <c>null</c> = open-ended.</param>
public sealed record EmploymentWindow(DateOnly? Start, DateOnly? End)
{
    /// <summary>
    /// The first day NOT employed — <see cref="End"/><c> + 1</c> — which is exactly the
    /// <c>EmploymentEnded</c> segment-boundary date ("boundary date = first day of the NEW
    /// segment"). <c>null</c> when the window is open-ended (D2) and also when
    /// <see cref="End"/> is <see cref="DateOnly.MaxValue"/>: a sentinel 9999-12-31 end has no
    /// day after it, and "employed through the end of time" is the open-ended answer anyway
    /// (never an <see cref="ArgumentOutOfRangeException"/> from <c>AddDays</c>).
    /// </summary>
    public DateOnly? FirstNotEmployedDay
        => End is { } end && end < DateOnly.MaxValue ? end.AddDays(1) : null;

    /// <summary>
    /// Does this window share at least ONE day with the closed range
    /// <c>[from, to]</c> (both ends INCLUSIVE)? A <c>null</c> side is unbounded (D2), so a
    /// both-<c>null</c> window overlaps every range. The D1 fenceposts fall out directly:
    /// a window ending ON <paramref name="from"/> overlaps (that day is still employed), a
    /// window starting ON <paramref name="to"/> overlaps (day <c>Start</c> is employed),
    /// while a window ending the day BEFORE <paramref name="from"/> does not.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="to"/> is before <paramref name="from"/>. Fail-loud on purpose: an
    /// inverted range is a caller's date-arithmetic bug, and a quiet <c>false</c> here would
    /// read as the legitimate domain answer "no employed day" — the same stance the
    /// resolver's range query takes.
    /// </exception>
    public bool Overlaps(DateOnly from, DateOnly to)
    {
        ThrowIfInverted(from, to);
        return (!Start.HasValue || Start.Value <= to)
            && (!End.HasValue || End.Value >= from);
    }

    /// <summary>
    /// The intersection of this window with the closed range <c>[from, to]</c>, as a CLOSED
    /// window (<c>[max(Start, from), min(End, to)]</c> — both sides of the result are
    /// non-<c>null</c>), or <c>null</c> when the window contributes no day to the range.
    /// A <c>null</c> side clips to the range edge (D2); the range's own end stays INCLUSIVE
    /// (D1), so a window ending ON <paramref name="to"/> clips to a window that still ends
    /// on <paramref name="to"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="to"/> is before <paramref name="from"/> (see <see cref="Overlaps"/>).
    /// </exception>
    public EmploymentWindow? ClipTo(DateOnly from, DateOnly to)
    {
        ThrowIfInverted(from, to);
        var clippedStart = Start is { } s && s > from ? s : from;
        var clippedEnd = End is { } e && e < to ? e : to;
        return clippedStart <= clippedEnd
            ? new EmploymentWindow(clippedStart, clippedEnd)
            : null;
    }

    /// <summary>
    /// The first employed day inside <c>[from, to]</c> — the clipped start — or <c>null</c>
    /// when this window contributes no day to the range. Equivalent to
    /// <c>ClipTo(from, to)?.Start</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="to"/> is before <paramref name="from"/> (see <see cref="Overlaps"/>).
    /// </exception>
    public DateOnly? FirstEmployedDayWithin(DateOnly from, DateOnly to)
        => ClipTo(from, to)?.Start;

    /// <summary>
    /// The UNION form over a spells-proof LIST: the earliest first-employed-day across all
    /// <paramref name="windows"/> within <c>[from, to]</c>, or <c>null</c> when the union of
    /// (each window ∩ the range) is EMPTY — "known: no employed day in this range". Windows
    /// that contribute no day are ignored; list order does not matter. An empty list yields
    /// <c>null</c> (the resolver's "consulted, nothing overlaps" answer — never "no
    /// information", per the <c>IEmploymentWindowResolver</c> contract). This is what the
    /// compliance check asks to find the profile as-of date for a month (ADR-040 D10).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="to"/> is before <paramref name="from"/> (see <see cref="Overlaps"/>).
    /// </exception>
    public static DateOnly? FirstEmployedDayWithin(
        IReadOnlyList<EmploymentWindow> windows, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ThrowIfInverted(from, to);

        DateOnly? first = null;
        foreach (var window in windows)
        {
            if (window.FirstEmployedDayWithin(from, to) is { } day
                && (first is null || day < first.Value))
            {
                first = day;
            }
        }
        return first;
    }

    private static void ThrowIfInverted(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            // Deliberately NO employment date in the message (ADR-040 D7): the range is the
            // caller's own input, so echoing it reveals nothing about the window.
            throw new ArgumentException(
                $"Range is inverted: to ({to:yyyy-MM-dd}) is before from ({from:yyyy-MM-dd}).",
                nameof(to));
        }
    }
}
