namespace StatsTid.Infrastructure.Temporal;

/// <summary>
/// S138 / TASK-13801 (ADR-040 D8 as amended 2026-09-02) — the PURE routing core behind the two
/// dated-history writers (<c>EmployeeProfileRepository.SupersedeAndCreateAsync</c> and
/// <c>UserAgreementCodeRepository.SupersedeAndCreateAsync</c>). No I/O, no clock, no SQL: the
/// repository locks an employee's timeline, hands the locked rows' intervals plus the requested
/// date to <see cref="Decide(IEnumerable{TemporalInterval}, DateOnly, DateOnly)"/>, and executes
/// exactly the case that comes back. Keeping the decision pure is what lets the whole matrix be
/// pinned without a database (the S137 <c>PeriodPlanner</c> pattern).
///
/// <para>
/// <b>What problem this solves (plain language).</b> Until S138 an employee's profile / agreement
/// history could only be extended at the END: the writer looked at the OPEN row and either edited
/// it (same start date) or closed it and opened a new one (later start date). A correction such as
/// "her part-time fraction actually changed on the 10th, not today" was impossible. This router
/// generalizes the routing to any PAST date: the row COVERING that date is split (or edited in
/// place when it starts on that very date), a date that falls into a GAP gets a row that fills the
/// gap, and a date before the very first row gets a row that ends where the first row begins.
/// End-exclusive intervals <c>[effective_from, effective_to)</c> everywhere (ADR-018 D9): a row
/// closed at <c>d</c> does NOT cover <c>d</c>; the successor does.
/// </para>
///
/// <para>
/// <b>The case table</b> (the letters are the refinement's; "the anchor" is the single row that
/// either STARTS on the requested date or COVERS it):
/// <list type="table">
///   <item><term>A — <see cref="TemporalWriteCase.Create"/></term>
///     <description>No rows at all → INSERT <c>[from, ∞)</c> (the open row).</description></item>
///   <item><term>B' — <see cref="TemporalWriteCase.UpdateInPlace"/></term>
///     <description>A row STARTS exactly at <c>from</c> (open or history) → UPDATE that row in
///     place; its interval is unchanged. Sub-case: a ZERO-WIDTH row <c>[from, from)</c> (a same-day
///     create + soft-delete) is re-extended over the gap that follows it
///     (<see cref="TemporalWriteDecision.ReopensZeroWidthAnchor"/> — ADR-020 D2 Case C's
///     "update-and-reopen"), so the history unique index never blocks a legitimate state.</description></item>
///   <item><term>C' — <see cref="TemporalWriteCase.SplitCovering"/></term>
///     <description>The covering row starts BEFORE <c>from</c> → close it at <c>from</c>, INSERT
///     <c>[from, covering.oldTo)</c>. When the covering row is the OPEN row this is exactly the
///     pre-S138 Case C (the new row is open); when it is a HISTORY row this is D8's insert-between
///     (the new row ends where the covering row used to end; later rows are untouched).</description></item>
///   <item><term>E — <see cref="TemporalWriteCase.InsertBeforeFirst"/></term>
///     <description><c>from</c> precedes the first row → INSERT <c>[from, firstRow.from)</c>; nothing
///     is closed.</description></item>
///   <item><term>G — <see cref="TemporalWriteCase.InsertInGap"/></term>
///     <description><c>from</c> lies in an INTERIOR gap (rows before and after, none covering — the
///     state a DELETE-then-recreate leaves, ADR-020 D2 gap-acknowledging lineage) → INSERT
///     <c>[from, nextRow.from)</c>; nothing is closed. A gap is a fact the admin may correct
///     (refinement Assumption 5; the 422 alternative is one line to switch).</description></item>
///   <item><term>T — <see cref="TemporalWriteCase.InsertTrailing"/></term>
///     <description>Rows exist, none covers <c>from</c>, and no row starts after it (the state a
///     soft-delete leaves) → INSERT <c>[from, ∞)</c>, which becomes the new open row. T is a
///     REPOSITORY-level case for the re-create callers; the profile PUT keeps its "no open row →
///     404" pre-check so an EDIT can never resurrect a deliberately retired profile.</description></item>
///   <item><term><see cref="TemporalWriteCase.RejectedFutureDated"/></term>
///     <description><c>from &gt; today</c>. Future-dating is OUT by owner ruling (ADR-040 D8
///     amendment): the open-ended-row readers — the login token's agreement code, the profile GET's
///     ETag, the <c>users.*</c> caches read at ~200 sites — all treat "the open row" as "current",
///     so a future row would put a not-yet-effective agreement into login tokens (a Security AND a
///     Domain-correctness breach). Refused here defensively; the endpoints also validate.</description></item>
/// </list>
/// Two index facts make the matrix safe: the live partial-unique index (exactly one open row per
/// employee — C'/E/G never open a second, T opens the only one) and the history unique index on
/// <c>(employee, effective_from)</c> (two writers racing to the same date: the second gets a
/// unique-violation, which the repositories surface as a typed conflict — the S35 backstop).
/// </para>
/// </summary>
public static class TemporalWriteRouter
{
    /// <summary>
    /// The future-dating refusal, exposed as a pure predicate so a repository can refuse BEFORE
    /// taking any lock (cheap, nothing to roll back) and the endpoint can map it to 422.
    /// <c>from == today</c> is allowed (a today-dated write is the pre-S138 steady state).
    /// </summary>
    public static bool IsFutureDated(DateOnly requestFrom, DateOnly today) => requestFrom > today;

    /// <summary>
    /// The employment-start floor, exposed as a pure predicate: a profile / agreement row cannot
    /// begin before the employee was employed. <c>null</c> (no recorded hire date — ADR-040 D2's
    /// "unbounded past") never floors. The DATE stays out of any exception message the endpoint
    /// surfaces (date-free 422). Deliberately caller-supplied rather than read from <c>users</c>
    /// by the repository: the admin user-create POST legitimately stores a FUTURE hire date while
    /// writing the first profile row at today, so an unconditional in-repository floor would break
    /// user creation; the EDIT endpoints supply the date, the create paths do not.
    /// </summary>
    public static bool PrecedesEmploymentStart(DateOnly requestFrom, DateOnly? employmentStartDate)
        => employmentStartDate is { } start && requestFrom < start;

    /// <summary>
    /// Routes a write dated <paramref name="requestFrom"/> against the lock-held snapshot
    /// <paramref name="rows"/> (every row of the employee's timeline, any order). Pure.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the rows overlap or two rows share a start
    /// date — a timeline the unique indexes make impossible; surfaced loudly rather than routed.</exception>
    public static TemporalWriteDecision Decide(
        IEnumerable<TemporalInterval> rows, DateOnly requestFrom, DateOnly today)
        => Decide(TimelineSnapshot.Build(rows, requestFrom), requestFrom, today);

    /// <summary>
    /// Routes against a pre-built <see cref="TimelineSnapshot"/> (the anchor row, the first row's
    /// start, the next row's start). The primitive the matrix tests pin directly.
    /// </summary>
    public static TemporalWriteDecision Decide(
        TimelineSnapshot snapshot, DateOnly requestFrom, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (IsFutureDated(requestFrom, today))
        {
            return new TemporalWriteDecision(
                TemporalWriteCase.RejectedFutureDated, requestFrom, NewEffectiveTo: null,
                Anchor: null, AnchorIsOpen: false, ReopensZeroWidthAnchor: false);
        }

        // A — an empty timeline.
        if (snapshot.FirstRowFrom is null)
        {
            if (snapshot.Anchor is not null || snapshot.NextRowFrom is not null)
                throw new InvalidOperationException(
                    "Inconsistent timeline snapshot: an anchor or a next row was supplied without a first row.");
            return new TemporalWriteDecision(
                TemporalWriteCase.Create, requestFrom, NewEffectiveTo: null,
                Anchor: null, AnchorIsOpen: false, ReopensZeroWidthAnchor: false);
        }

        if (snapshot.Anchor is { } anchor)
        {
            if (anchor.From > requestFrom)
                throw new InvalidOperationException(
                    "Inconsistent timeline snapshot: the anchor row starts after the requested date.");

            // B' — a row starts exactly on the requested date: edit it in place.
            if (anchor.From == requestFrom)
            {
                // A zero-width row [from, from) does not cover `from` (end-exclusive) — it is the
                // trace of a same-day create + soft-delete. Editing it in place must also give it
                // back an interval, otherwise the edit is invisible; re-extend it to the next row's
                // start (or reopen it when nothing follows). ADR-020 D2 Case C, generalized.
                var isZeroWidth = anchor.To == requestFrom;
                var newTo = isZeroWidth ? snapshot.NextRowFrom : anchor.To;
                return new TemporalWriteDecision(
                    TemporalWriteCase.UpdateInPlace, requestFrom, newTo,
                    Anchor: anchor, AnchorIsOpen: anchor.To is null, ReopensZeroWidthAnchor: isZeroWidth);
            }

            // C' — the covering row starts before the requested date: split it there.
            if (anchor.To is { } to && to <= requestFrom)
                throw new InvalidOperationException(
                    "Inconsistent timeline snapshot: the anchor row does not cover the requested date.");
            return new TemporalWriteDecision(
                TemporalWriteCase.SplitCovering, requestFrom, NewEffectiveTo: anchor.To,
                Anchor: anchor, AnchorIsOpen: anchor.To is null, ReopensZeroWidthAnchor: false);
        }

        // No anchor: the date falls outside every row. Three shapes, told apart by neighbours.
        if (requestFrom < snapshot.FirstRowFrom.Value)
        {
            // E — before the first row: the new row ends where the first row begins.
            return new TemporalWriteDecision(
                TemporalWriteCase.InsertBeforeFirst, requestFrom, NewEffectiveTo: snapshot.FirstRowFrom,
                Anchor: null, AnchorIsOpen: false, ReopensZeroWidthAnchor: false);
        }

        if (snapshot.NextRowFrom is { } next)
        {
            // G — an interior gap: the new row ends where the next row begins.
            return new TemporalWriteDecision(
                TemporalWriteCase.InsertInGap, requestFrom, NewEffectiveTo: next,
                Anchor: null, AnchorIsOpen: false, ReopensZeroWidthAnchor: false);
        }

        // T — a trailing gap (every row ended before `from`, nothing follows): the new row is open.
        return new TemporalWriteDecision(
            TemporalWriteCase.InsertTrailing, requestFrom, NewEffectiveTo: null,
            Anchor: null, AnchorIsOpen: false, ReopensZeroWidthAnchor: false);
    }

    /// <summary>
    /// Maps a decision to the result-level <see cref="TemporalWriteKind"/> the endpoints consume
    /// (the same case letter reads as two kinds when the anchor is open vs history — C' on the open
    /// row is the classic supersession, C' on a history row is D8's insert-between). The same-values
    /// no-op is decided by the repository (it needs the row's FIELDS), so it is not produced here.
    /// </summary>
    public static TemporalWriteKind KindOf(TemporalWriteDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision.Case switch
        {
            TemporalWriteCase.Create => TemporalWriteKind.Created,
            TemporalWriteCase.UpdateInPlace => TemporalWriteKind.Updated,
            TemporalWriteCase.SplitCovering when decision.AnchorIsOpen => TemporalWriteKind.Superseded,
            TemporalWriteCase.SplitCovering => TemporalWriteKind.Inserted,
            TemporalWriteCase.InsertBeforeFirst => TemporalWriteKind.InsertedBeforeFirst,
            TemporalWriteCase.InsertInGap => TemporalWriteKind.InsertedInGap,
            TemporalWriteCase.InsertTrailing => TemporalWriteKind.InsertedTrailing,
            TemporalWriteCase.RejectedFutureDated => throw new InvalidOperationException(
                "A rejected decision has no write kind; the repository must throw before mapping."),
            _ => throw new InvalidOperationException($"Unhandled TemporalWriteCase '{decision.Case}'."),
        };
    }
}

/// <summary>
/// One row's validity interval under end-exclusive semantics: covers every day <c>d</c> with
/// <c>From &lt;= d</c> and (<c>To is null</c> or <c>d &lt; To</c>). <c>To == From</c> is a
/// zero-width row (covers nothing).
/// </summary>
public readonly record struct TemporalInterval(DateOnly From, DateOnly? To)
{
    /// <summary>True when the row is the OPEN (live) row.</summary>
    public bool IsOpen => To is null;

    /// <summary>End-exclusive coverage test.</summary>
    public bool Covers(DateOnly day) => From <= day && (To is null || day < To.Value);
}

/// <summary>
/// The three facts the router needs about a locked timeline relative to a requested date:
/// the ANCHOR (the row that starts exactly on the date, else the row that covers it; null when
/// the date falls outside every row), the FIRST row's start (null = empty timeline) and the NEXT
/// row's start (the smallest <c>effective_from</c> strictly after the date; null = none).
/// </summary>
public sealed record TimelineSnapshot(
    TemporalInterval? Anchor,
    DateOnly? FirstRowFrom,
    DateOnly? NextRowFrom)
{
    /// <summary>
    /// Derives the snapshot from a full row set (any order). Pure; validates the two shape
    /// invariants the unique indexes guarantee (distinct starts, no overlaps) and fails loudly on
    /// a violation instead of routing a corrupt timeline.
    /// </summary>
    public static TimelineSnapshot Build(IEnumerable<TemporalInterval> rows, DateOnly requestFrom)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var ordered = rows.OrderBy(r => r.From).ToList();
        if (ordered.Count == 0)
            return new TimelineSnapshot(Anchor: null, FirstRowFrom: null, NextRowFrom: null);

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            if (prev.From == cur.From)
                throw new InvalidOperationException(
                    $"Corrupt timeline: two rows start on {cur.From:yyyy-MM-dd} (history unique index violated).");
            // prev is open, or ends after cur begins → the two rows overlap.
            if (prev.To is null || prev.To.Value > cur.From)
                throw new InvalidOperationException(
                    $"Corrupt timeline: the row starting {prev.From:yyyy-MM-dd} overlaps the row starting {cur.From:yyyy-MM-dd}.");
        }

        // Precedence: a row STARTING on the date wins (B'), even a zero-width one; else the row
        // COVERING the date (C'). At most one of each can exist after the checks above.
        TemporalInterval? anchor = null;
        foreach (var row in ordered)
        {
            if (row.From == requestFrom) { anchor = row; break; }
        }
        if (anchor is null)
        {
            foreach (var row in ordered)
            {
                if (row.Covers(requestFrom)) { anchor = row; break; }
            }
        }

        DateOnly? next = null;
        foreach (var row in ordered)
        {
            if (row.From > requestFrom) { next = row.From; break; }
        }

        return new TimelineSnapshot(anchor, ordered[0].From, next);
    }
}

/// <summary>
/// The router's verdict: which case fires and the interval the written row must occupy
/// (<see cref="NewEffectiveTo"/> null = the row is open). <see cref="Anchor"/> identifies the row
/// the repository must UPDATE (B') or CLOSE (C') — matched back to the locked row by its start
/// date, which the history unique index makes a key.
/// </summary>
public sealed record TemporalWriteDecision(
    TemporalWriteCase Case,
    DateOnly NewEffectiveFrom,
    DateOnly? NewEffectiveTo,
    TemporalInterval? Anchor,
    bool AnchorIsOpen,
    bool ReopensZeroWidthAnchor)
{
    /// <summary>True when the row this write produces / re-extends is the OPEN row.</summary>
    public bool ProducesOpenRow => Case != TemporalWriteCase.RejectedFutureDated && NewEffectiveTo is null;

    /// <summary>True for the write cases that INSERT a new row (A, C', E, G, T).</summary>
    public bool InsertsRow => Case is TemporalWriteCase.Create
        or TemporalWriteCase.SplitCovering
        or TemporalWriteCase.InsertBeforeFirst
        or TemporalWriteCase.InsertInGap
        or TemporalWriteCase.InsertTrailing;
}

/// <summary>The routing cases — see the <see cref="TemporalWriteRouter"/> case table.</summary>
public enum TemporalWriteCase
{
    /// <summary>A — empty timeline; INSERT the open row.</summary>
    Create,
    /// <summary>B' — a row starts exactly on the date; UPDATE it in place.</summary>
    UpdateInPlace,
    /// <summary>C' — the covering row starts earlier; close it at the date and INSERT the remainder.</summary>
    SplitCovering,
    /// <summary>E — before the first row; INSERT up to the first row's start.</summary>
    InsertBeforeFirst,
    /// <summary>G — inside an interior gap; INSERT up to the next row's start.</summary>
    InsertInGap,
    /// <summary>T — trailing gap; INSERT the new open row.</summary>
    InsertTrailing,
    /// <summary>The date is after today — refused (owner ruling: future-dating is Increment 4).</summary>
    RejectedFutureDated,
}

/// <summary>
/// Result-level discriminator carried by both writer results (finer than the coarse
/// <c>Outcome</c> enums the pre-S138 endpoints switch on, which stay event-oriented).
/// </summary>
public enum TemporalWriteKind
{
    /// <summary>A — a brand-new open row on an empty timeline.</summary>
    Created,
    /// <summary>B' — the row starting on the date was edited in place (open or history — the
    /// result's covering pre-image says which: <c>EffectiveTo == null</c> means it was the open row).</summary>
    Updated,
    /// <summary>C' on the OPEN row — the classic cross-day supersession (predecessor closed, new open row).</summary>
    Superseded,
    /// <summary>C' on a HISTORY row — D8's insert-between: the covering row was shortened and a
    /// history row <c>[from, oldTo)</c> inserted; later rows untouched.</summary>
    Inserted,
    /// <summary>E — a history row inserted before the first row.</summary>
    InsertedBeforeFirst,
    /// <summary>G — a history row inserted into an interior gap.</summary>
    InsertedInGap,
    /// <summary>T — a new open row inserted after a trailing gap (repository-level re-create).</summary>
    InsertedTrailing,
    /// <summary>The request equalled the covering row field-for-field: nothing was written.</summary>
    NoOp,
}

/// <summary>
/// Why a dated write was refused before touching any row. Thrown by both writers; the endpoints
/// map it to a 422. Messages are deliberately DATE-FREE (the employment-start floor must not leak
/// the hire date to the wire).
/// </summary>
public sealed class TemporalWriteRejectedException : Exception
{
    public TemporalWriteRejection Reason { get; }

    public TemporalWriteRejectedException(TemporalWriteRejection reason, string aggregateDescription)
        : base(MessageFor(reason, aggregateDescription))
    {
        Reason = reason;
    }

    private static string MessageFor(TemporalWriteRejection reason, string aggregateDescription) => reason switch
    {
        TemporalWriteRejection.FutureDated =>
            $"This {aggregateDescription} change cannot be dated in the future; future-dating is not supported yet (ADR-040 D8 amendment).",
        TemporalWriteRejection.PrecedesEmploymentStart =>
            $"This {aggregateDescription} change cannot be dated before the employee's employment start.",
        TemporalWriteRejection.NoRecordedEmploymentCategory =>
            $"This {aggregateDescription} change needs an employment category: no recorded row covers or precedes that date.",
        _ => $"This {aggregateDescription} change was rejected ({reason}).",
    };
}

/// <summary>The refusal reasons of <see cref="TemporalWriteRejectedException"/>.</summary>
public enum TemporalWriteRejection
{
    /// <summary><c>from &gt; today</c>.</summary>
    FutureDated,
    /// <summary><c>from &lt; users.employment_start_date</c> (when the caller supplied the floor).</summary>
    PrecedesEmploymentStart,
    /// <summary>
    /// S138 Step-5a (Reviewer BLOCKER): the write would land BEFORE every recorded row (router
    /// case E) and the caller named no employment category, so nothing records what held then.
    /// Distinct from <see cref="PrecedesEmploymentStart"/> — the two overlap in practice (a date
    /// before the first profile row is usually also before the hire date), so collapsing them
    /// would leave a production 422 undiagnosable. Guessing is the alternative this refusal
    /// exists to avoid: substituting the live value would MISLABEL history, which is precisely
    /// what S138 retired the read-side COALESCE to prevent.
    /// </summary>
    NoRecordedEmploymentCategory,
}
