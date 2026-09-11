using StatsTid.Infrastructure.Temporal;

namespace StatsTid.Tests.Unit.Infrastructure;

/// <summary>
/// S138 / TASK-13801 — the DB-free matrix for <see cref="TemporalWriteRouter"/>, the pure routing
/// core behind both dated-history writers (<c>EmployeeProfileRepository</c> /
/// <c>UserAgreementCodeRepository</c>). Every case letter of the refinement (A / B' / C' / E / G /
/// T) is pinned against live, history, gap and empty timelines, with the fenceposts at every row
/// boundary under end-exclusive <c>[from, to)</c> semantics — a row closed on day <c>d</c> does NOT
/// cover <c>d</c>; the successor does.
///
/// <para>
/// <b>Why this matters (plain language).</b> The router decides what the SQL does: which row is
/// closed, which is edited, and exactly which interval the new row gets. An off-by-one here would
/// let two rows overlap (two answers to "what was her fraction on the 10th?") or leave a day
/// uncovered. The sweep test at the bottom is the safety net: for every calendar day in a mixed
/// fixture (history, a zero-width row, gaps, an open row) the post-write timeline must still have
/// no overlaps, at most one open row, and the requested day covered by the row that was written.
/// </para>
///
/// <para>
/// <b>S141 / TASK-14112 (ADR-040 D8 amendment) — HR can now schedule a change ahead of time.</b>
/// Sprint 141 lifts the future-dating refusal this class used to pin. Because this router is PURE
/// (no I/O, no database), the tests below are the only S141 pins that can run — and genuinely fail
/// — TODAY, before the fix lands: they need no Docker. <see cref="TemporalWriteRouter"/> itself is
/// being edited in a SIBLING worktree this wave (TASK-14102), so most of the tests below are
/// RED-first: they assert the POST-fix behaviour and currently fail against the guard still in
/// place, and are expected to turn GREEN once that sibling task merges. One shape (labelled below)
/// is the exception and is already green — its comment says so explicitly, because a green result
/// there must never be mistaken for evidence the fix landed.
/// </para>
/// </summary>
public sealed class TemporalWriteRouterTests
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static TemporalInterval Row(DateOnly from, DateOnly? to) => new(from, to);

    private static TemporalWriteDecision Decide(DateOnly from, params TemporalInterval[] rows)
        => TemporalWriteRouter.Decide(rows, from, Today);

    // ------------------------------------------------------------------
    // Future refusal — the owner ruling as it stood BEFORE S141 (ADR-040 D8 amendment).
    // ------------------------------------------------------------------

    [Fact]
    public void KindOf_StillThrows_ForARejectedDecision_HandBuilt()
    {
        // TemporalWriteCase.RejectedFutureDated and KindOf's defensive throw for it are kept as
        // live symbols this sprint — the router agent (TASK-14102, a sibling worktree) was told to
        // leave them in place, unreferenced, so this file keeps compiling while it edits the SAME
        // production file this test class pins. Once TASK-14102 removes the guard inside Decide()
        // (see the S141 shapes below), Decide() never PRODUCES this case again for any caller, so
        // this test builds the decision BY HAND instead of routing one through Decide() — it keeps
        // proving KindOf's defensive branch without depending on a code path S141 is deliberately
        // retiring.
        var rejected = new TemporalWriteDecision(
            TemporalWriteCase.RejectedFutureDated, Today, NewEffectiveTo: null,
            Anchor: null, AnchorIsOpen: false, ReopensZeroWidthAnchor: false);

        Assert.Throws<InvalidOperationException>(() => TemporalWriteRouter.KindOf(rejected));
    }

    [Fact]
    public void FutureDate_OnEmptyTimeline_CreatesTheOpenRow_S141()
    {
        // REPLACES the "on empty timeline" half of the old (pre-S141) FutureDate_IsRejected test
        // — that assertion is no longer true once HR can schedule ahead, so it is replaced with a
        // real assertion about the new behaviour rather than deleted outright (no test in this
        // sprint may be deleted without a replacement). A future date on an EMPTY timeline (a new
        // hire whose very first row is dated ahead) is plain Case A — the same "insert the open
        // row" the router already does for a past- or today-dated first write. No new case: the
        // existing one, reached from a date it used to refuse.
        //
        // RED today: the guard inside Decide() (TemporalWriteRouter.cs:107) intercepts ANY
        // from > today before the case table runs at all, so this currently comes back
        // RejectedFutureDated rather than Create. GREEN once TASK-14102 removes that guard.
        var future = Today.AddDays(30);
        var decision = Decide(future);

        Assert.Equal(TemporalWriteCase.Create, decision.Case);
        Assert.Equal(future, decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.True(decision.ProducesOpenRow);
        Assert.Equal(TemporalWriteKind.Created, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void Today_IsNotFuture_AndRoutesNormally()
    {
        Assert.False(TemporalWriteRouter.IsFutureDated(Today, Today));
        Assert.True(TemporalWriteRouter.IsFutureDated(Today.AddDays(1), Today));
        Assert.False(TemporalWriteRouter.IsFutureDated(Today.AddDays(-1), Today));

        var decision = Decide(Today);
        Assert.Equal(TemporalWriteCase.Create, decision.Case);
    }

    // ------------------------------------------------------------------
    // S141 / TASK-14112 — the five scheduling-matrix shapes named in
    // REFINEMENT-s141-increment4-and-the-settlement-anchor.md section B3. Every shape below
    // reaches an EXISTING case letter (SplitCovering or UpdateInPlace) — no new router case is
    // added anywhere here; B3's whole point is that the case table already covers a scheduled
    // write, and only the guard at the top of Decide() was stopping it from being reached. Read
    // in order, these are HR's actual workflow: schedule a change (1), schedule a LATER correction
    // on top of it (2) or an EARLIER one (3), come back the day the first one takes effect and
    // edit something else (4 — already green; see its own comment), or fix a typo in the date HR
    // picked (5).
    // ------------------------------------------------------------------

    [Fact]
    public void S141_FutureWrite_OnOpenRow_SplitsAndSupersedes()
    {
        // Shape 1 — "HR schedules a change." A write dated after today, against a timeline whose
        // open row started in the past, is exactly the pre-S141 Case C' with the guard out of the
        // way: close the open row at the future date, open a new row from there. Kind stays
        // Superseded — the SAME kind a same-day supersession already produces; only the DATE moved.
        //
        // RED today: from > Today trips the Decide() guard before the anchor is even looked at, so
        // this currently comes back RejectedFutureDated. GREEN once TASK-14102 removes the guard.
        var scheduledStart = D(11, 1); // Nov 1 — safely after Today (Sep 3).
        var decision = Decide(scheduledStart, Row(D(1, 1), null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.True(decision.AnchorIsOpen);
        Assert.Equal(scheduledStart, decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.Equal(TemporalWriteKind.Superseded, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void S141_SecondFutureWrite_AfterTheFirst_SplitsTheScheduledOpenRow_StillSuperseded()
    {
        // Shape 2 — "HR schedules a SECOND, LATER change on top of the first." The timeline
        // already holds a scheduled-but-not-yet-effective open row (Nov 1); a write dated even
        // later (Dec 1) finds that future row as its anchor, and the anchor IS open, so this is
        // C' on an open row again: Superseded. The router never asks "is the anchor's own start
        // date in the future" — only "does the anchor cover or start on the requested date" — so
        // this shape needs no separate reasoning from shape 1's.
        //
        // RED today: Dec 1 > Today trips the guard regardless of what the fixture contains.
        var firstScheduledStart = D(11, 1);
        var secondScheduledStart = D(12, 1);
        var decision = Decide(secondScheduledStart, Row(firstScheduledStart, null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.True(decision.AnchorIsOpen);
        Assert.Equal(secondScheduledStart, decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.Equal(TemporalWriteKind.Superseded, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void S141_FutureWrite_BeforeAnAlreadyScheduledOne_SplitsTheHistoryRow_IsInserted()
    {
        // Shape 3 — "HR schedules an EARLIER change than one already on the books." The timeline
        // holds today's row [Jan 1, Nov 1) plus the Nov-1 scheduled row; a write dated Oct 1 (still
        // future, but before Nov 1) lands inside [Jan 1, Nov 1) — a HISTORY row once Nov 1 exists,
        // because that row now has an end date. So the anchor is NOT open, and C' on a non-open
        // anchor is D8's Kind.Inserted, not Superseded: the Nov-1 row is left completely alone —
        // this is the mechanism (B3's revaluation rule) that protects a LATER scheduled change's
        // already-booked absences: the upper bound is the next row's start, never this write's date.
        //
        // RED today: Oct 1 > Today (Sep 3) trips the guard before the anchor lookup ever runs.
        var alreadyScheduledStart = D(11, 1);
        var earlierScheduledStart = D(10, 1);
        var decision = Decide(
            earlierScheduledStart, Row(D(1, 1), alreadyScheduledStart), Row(alreadyScheduledStart, null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.False(decision.AnchorIsOpen);
        Assert.Equal(earlierScheduledStart, decision.NewEffectiveFrom);
        Assert.Equal(alreadyScheduledStart, decision.NewEffectiveTo);
        Assert.Equal(TemporalWriteKind.Inserted, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void S141_TodayDatedWrite_WhileAFutureRowExists_IsInserted_NotSuperseded_AlreadyGreen()
    {
        // Shape 4 — "HR comes back TODAY and edits something else, while a change is scheduled for
        // later." This is the shape the refinement calls out as the one every PRE-S141 test gets
        // wrong by assuming: with a future row present, TODAY is no longer inside the open row —
        // it falls inside the HISTORY row that now ends where the scheduled row begins. So a
        // today-dated write routes C' on a NON-open anchor: Kind.Inserted, exactly like shape 3,
        // just with `from == Today` instead of a future date.
        //
        // HONESTY NOTE (stated per the task, not left implicit): this assertion is ALREADY GREEN
        // today, because Today is not > Today, so the Decide() guard never fires for it — the
        // router has always treated "the row covering today, when it happens to be a history row"
        // as an ordinary history row. A green result here is NOT evidence the S141 fix has landed;
        // it is a regression pin against a case the router already got right by construction. This
        // test would pass identically before and after TASK-14102/TASK-14104's changes — do not
        // read it as proof of either. It earns its place because it is, per the refinement, "the
        // second-most-likely write in the whole feature" (the day-after correction), and no
        // pre-S141 test names it: every pre-S141 today-dated test asserts Superseded, which
        // silently assumed no future row could ever exist.
        var scheduledStart = D(11, 1);
        var decision = Decide(Today, Row(D(1, 1), scheduledStart), Row(scheduledStart, null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.False(decision.AnchorIsOpen);
        Assert.Equal(Today, decision.NewEffectiveFrom);
        Assert.Equal(scheduledStart, decision.NewEffectiveTo);
        Assert.Equal(TemporalWriteKind.Inserted, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void S141_SecondWrite_AtTheSameScheduledDate_IsAbsorbed_AsAnUpdate()
    {
        // Shape 5 — "HR corrects a typo in a change that is not in force yet." A second write
        // dated EXACTLY the same as the already-scheduled row's start hits B' (the anchor STARTS
        // on the requested date), not C' — an in-place edit of the scheduled row itself,
        // Kind.Updated. This is the "correct a scheduled change" path the picker UX will produce
        // whenever HR reopens a not-yet-effective row and saves again at the same date.
        //
        // RED today: the scheduled date itself is still > Today, so the guard fires before B' is
        // ever reached.
        var scheduledStart = D(11, 1);
        var decision = Decide(scheduledStart, Row(scheduledStart, null));

        Assert.Equal(TemporalWriteCase.UpdateInPlace, decision.Case);
        Assert.True(decision.AnchorIsOpen);
        Assert.Equal(scheduledStart, decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.Equal(TemporalWriteKind.Updated, TemporalWriteRouter.KindOf(decision));
    }

    // ------------------------------------------------------------------
    // The employment-start floor — pure predicate, caller-supplied date.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null, false)]          // no recorded hire date → never floors (ADR-040 D2 unbounded past)
    [InlineData("2026-02-01", false)]  // from == start → allowed (day `start` is employed)
    [InlineData("2026-02-02", true)]   // from < start → refused
    [InlineData("2026-01-15", false)]  // from > start → allowed
    public void PrecedesEmploymentStart_Fenceposts(string? start, bool expected)
    {
        var from = D(2, 1);
        DateOnly? startDate = start is null ? null : DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, TemporalWriteRouter.PrecedesEmploymentStart(from, startDate));
    }

    // ------------------------------------------------------------------
    // A — empty timeline.
    // ------------------------------------------------------------------

    [Fact]
    public void A_EmptyTimeline_InsertsTheOpenRow()
    {
        var from = D(3, 10);
        var decision = Decide(from);

        Assert.Equal(TemporalWriteCase.Create, decision.Case);
        Assert.Equal(from, decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.Null(decision.Anchor);
        Assert.True(decision.ProducesOpenRow);
        Assert.True(decision.InsertsRow);
        Assert.Equal(TemporalWriteKind.Created, TemporalWriteRouter.KindOf(decision));
    }

    // ------------------------------------------------------------------
    // B' — a row STARTS on the requested date (open, history, zero-width).
    // ------------------------------------------------------------------

    [Fact]
    public void B_OpenRowStartsAtFrom_UpdatesInPlace_IntervalUnchanged()
    {
        var decision = Decide(D(1, 1), Row(D(1, 1), null));

        Assert.Equal(TemporalWriteCase.UpdateInPlace, decision.Case);
        Assert.Equal(D(1, 1), decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.True(decision.AnchorIsOpen);
        Assert.False(decision.ReopensZeroWidthAnchor);
        Assert.True(decision.ProducesOpenRow);
        Assert.False(decision.InsertsRow);
        Assert.Equal(TemporalWriteKind.Updated, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void B_HistoryRowStartsAtFrom_UpdatesInPlace_KeepsItsEnd()
    {
        var decision = Decide(D(1, 1), Row(D(1, 1), D(3, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.UpdateInPlace, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
        Assert.False(decision.AnchorIsOpen);
        Assert.False(decision.ReopensZeroWidthAnchor);
        Assert.False(decision.ProducesOpenRow);
        Assert.Equal(Row(D(1, 1), D(3, 1)), decision.Anchor);
    }

    [Fact]
    public void B_ZeroWidthRow_WithALaterRow_IsReopenedUpToTheNextRow()
    {
        // [Jan1, Jan1) is the trace of a same-day create + soft-delete; editing it must give it an
        // interval again or the edit is invisible (ADR-020 D2 Case C, generalized).
        var decision = Decide(D(1, 1), Row(D(1, 1), D(1, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.UpdateInPlace, decision.Case);
        Assert.True(decision.ReopensZeroWidthAnchor);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
        Assert.False(decision.AnchorIsOpen);
        Assert.False(decision.ProducesOpenRow);
    }

    [Fact]
    public void B_ZeroWidthRow_AsTheLastRow_IsReopenedAsTheOpenRow()
    {
        var decision = Decide(D(2, 1), Row(D(1, 1), D(2, 1)), Row(D(2, 1), D(2, 1)));

        Assert.Equal(TemporalWriteCase.UpdateInPlace, decision.Case);
        Assert.True(decision.ReopensZeroWidthAnchor);
        Assert.Null(decision.NewEffectiveTo);
        Assert.True(decision.ProducesOpenRow);
    }

    // ------------------------------------------------------------------
    // C' — the covering row starts BEFORE the requested date (open and history).
    // ------------------------------------------------------------------

    [Fact]
    public void C_OpenCoveringRow_IsSplit_NewRowIsOpen()
    {
        var decision = Decide(D(3, 10), Row(D(1, 1), null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.Equal(D(3, 10), decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.True(decision.AnchorIsOpen);
        Assert.True(decision.ProducesOpenRow);
        Assert.True(decision.InsertsRow);
        Assert.Equal(TemporalWriteKind.Superseded, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void C_HistoryCoveringRow_IsSplit_NewRowEndsWhereItUsedTo()
    {
        var decision = Decide(D(2, 10), Row(D(1, 1), D(3, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
        Assert.False(decision.AnchorIsOpen);
        Assert.False(decision.ProducesOpenRow);
        Assert.Equal(Row(D(1, 1), D(3, 1)), decision.Anchor);
        Assert.Equal(TemporalWriteKind.Inserted, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void C_Fencepost_LastCoveredDay_StillSplitsTheCoveringRow()
    {
        // Feb 28 is the last day [Jan1, Mar1) covers.
        var decision = Decide(D(2, 28), Row(D(1, 1), D(3, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.SplitCovering, decision.Case);
        Assert.Equal(Row(D(1, 1), D(3, 1)), decision.Anchor);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
    }

    [Fact]
    public void Fencepost_CloseDateBelongsToTheSuccessor_RoutesToItsInPlaceEdit()
    {
        // Mar 1 is NOT covered by [Jan1, Mar1); the row starting Mar 1 is the anchor.
        var decision = Decide(D(3, 1), Row(D(1, 1), D(3, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.UpdateInPlace, decision.Case);
        Assert.Equal(Row(D(3, 1), null), decision.Anchor);
        Assert.True(decision.AnchorIsOpen);
    }

    // ------------------------------------------------------------------
    // E — before the first row.
    // ------------------------------------------------------------------

    [Fact]
    public void E_BeforeFirstRow_InsertsUpToTheFirstRow()
    {
        var decision = Decide(D(1, 10), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.InsertBeforeFirst, decision.Case);
        Assert.Equal(D(1, 10), decision.NewEffectiveFrom);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
        Assert.Null(decision.Anchor);
        Assert.False(decision.ProducesOpenRow);
        Assert.True(decision.InsertsRow);
        Assert.Equal(TemporalWriteKind.InsertedBeforeFirst, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void E_BeforeFirstRow_WhenFirstRowIsHistory_StillEndsAtTheFirstRow()
    {
        var decision = Decide(D(2, 1), Row(D(3, 1), D(6, 1)), Row(D(6, 1), null));

        Assert.Equal(TemporalWriteCase.InsertBeforeFirst, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
    }

    [Fact]
    public void E_Fencepost_DayBeforeFirstRow()
    {
        var decision = Decide(D(2, 28), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.InsertBeforeFirst, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
    }

    // ------------------------------------------------------------------
    // G — an interior gap (rows on both sides, none covering).
    // ------------------------------------------------------------------

    [Fact]
    public void G_InteriorGap_InsertsUpToTheNextRow()
    {
        var decision = Decide(D(2, 10), Row(D(1, 1), D(2, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.InsertInGap, decision.Case);
        Assert.Equal(D(2, 10), decision.NewEffectiveFrom);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
        Assert.Null(decision.Anchor);
        Assert.False(decision.ProducesOpenRow);
        Assert.Equal(TemporalWriteKind.InsertedInGap, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void G_Fencepost_GapStartIsTheCloseDate_NotCoveredByThePredecessor()
    {
        var decision = Decide(D(2, 1), Row(D(1, 1), D(2, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.InsertInGap, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
    }

    [Fact]
    public void G_Fencepost_LastGapDay()
    {
        var decision = Decide(D(2, 28), Row(D(1, 1), D(2, 1)), Row(D(3, 1), null));

        Assert.Equal(TemporalWriteCase.InsertInGap, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
    }

    [Fact]
    public void G_InteriorGap_WithNoOpenRowAtAll_StillInsertsUpToTheNextRow()
    {
        var decision = Decide(D(2, 10), Row(D(1, 1), D(2, 1)), Row(D(3, 1), D(4, 1)));

        Assert.Equal(TemporalWriteCase.InsertInGap, decision.Case);
        Assert.Equal(D(3, 1), decision.NewEffectiveTo);
    }

    // ------------------------------------------------------------------
    // T — a trailing gap (the state a soft-delete leaves).
    // ------------------------------------------------------------------

    [Fact]
    public void T_TrailingGap_InsertsTheNewOpenRow()
    {
        var decision = Decide(D(3, 10), Row(D(1, 1), D(2, 1)));

        Assert.Equal(TemporalWriteCase.InsertTrailing, decision.Case);
        Assert.Equal(D(3, 10), decision.NewEffectiveFrom);
        Assert.Null(decision.NewEffectiveTo);
        Assert.Null(decision.Anchor);
        Assert.True(decision.ProducesOpenRow);
        Assert.True(decision.InsertsRow);
        Assert.Equal(TemporalWriteKind.InsertedTrailing, TemporalWriteRouter.KindOf(decision));
    }

    [Fact]
    public void T_Fencepost_OnTheCloseDate_IsAlreadyTheTrailingGap()
    {
        var decision = Decide(D(2, 1), Row(D(1, 1), D(2, 1)));

        Assert.Equal(TemporalWriteCase.InsertTrailing, decision.Case);
    }

    [Fact]
    public void T_AfterSeveralClosedRows()
    {
        var decision = Decide(Today, Row(D(1, 1), D(2, 1)), Row(D(2, 1), D(4, 1)));

        Assert.Equal(TemporalWriteCase.InsertTrailing, decision.Case);
        Assert.Null(decision.NewEffectiveTo);
    }

    // ------------------------------------------------------------------
    // Snapshot construction + the shape invariants the unique indexes guarantee.
    // ------------------------------------------------------------------

    [Fact]
    public void Snapshot_ComputesAnchorFirstAndNext()
    {
        var snapshot = TimelineSnapshot.Build(
            new[] { Row(D(6, 1), null), Row(D(3, 1), D(6, 1)) }, D(4, 1));

        Assert.Equal(Row(D(3, 1), D(6, 1)), snapshot.Anchor);
        Assert.Equal(D(3, 1), snapshot.FirstRowFrom);
        Assert.Equal(D(6, 1), snapshot.NextRowFrom);
    }

    [Fact]
    public void Snapshot_NextRow_IsStrictlyAfterTheDate()
    {
        // A row starting ON the date is the anchor, not "next".
        var snapshot = TimelineSnapshot.Build(
            new[] { Row(D(1, 1), D(3, 1)), Row(D(3, 1), null) }, D(3, 1));

        Assert.Equal(Row(D(3, 1), null), snapshot.Anchor);
        Assert.Null(snapshot.NextRowFrom);
    }

    [Fact]
    public void Snapshot_EmptyTimeline()
    {
        var snapshot = TimelineSnapshot.Build(Array.Empty<TemporalInterval>(), D(1, 1));

        Assert.Null(snapshot.Anchor);
        Assert.Null(snapshot.FirstRowFrom);
        Assert.Null(snapshot.NextRowFrom);
    }

    [Fact]
    public void Snapshot_RejectsOverlappingRows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimelineSnapshot.Build(new[] { Row(D(1, 1), D(3, 1)), Row(D(2, 1), null) }, D(2, 10)));
    }

    [Fact]
    public void Snapshot_RejectsAnOpenRowFollowedByAnotherRow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimelineSnapshot.Build(new[] { Row(D(1, 1), null), Row(D(3, 1), null) }, D(2, 10)));
    }

    [Fact]
    public void Snapshot_RejectsDuplicateStartDates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimelineSnapshot.Build(new[] { Row(D(1, 1), D(2, 1)), Row(D(1, 1), D(3, 1)) }, D(1, 15)));
    }

    [Fact]
    public void Decide_RejectsInconsistentHandBuiltSnapshots()
    {
        // An anchor without any row.
        Assert.Throws<InvalidOperationException>(() => TemporalWriteRouter.Decide(
            new TimelineSnapshot(Row(D(1, 1), null), FirstRowFrom: null, NextRowFrom: null), D(1, 1), Today));

        // An anchor that starts after the requested date.
        Assert.Throws<InvalidOperationException>(() => TemporalWriteRouter.Decide(
            new TimelineSnapshot(Row(D(3, 1), null), FirstRowFrom: D(3, 1), NextRowFrom: null), D(2, 1), Today));

        // An anchor that ends on or before the requested date without starting on it.
        Assert.Throws<InvalidOperationException>(() => TemporalWriteRouter.Decide(
            new TimelineSnapshot(Row(D(1, 1), D(2, 1)), FirstRowFrom: D(1, 1), NextRowFrom: null), D(2, 10), Today));
    }

    // ------------------------------------------------------------------
    // Kind mapping — the result-level discriminator the endpoints switch on.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(TemporalWriteCase.Create, false, TemporalWriteKind.Created)]
    [InlineData(TemporalWriteCase.UpdateInPlace, true, TemporalWriteKind.Updated)]
    [InlineData(TemporalWriteCase.UpdateInPlace, false, TemporalWriteKind.Updated)]
    [InlineData(TemporalWriteCase.SplitCovering, true, TemporalWriteKind.Superseded)]
    [InlineData(TemporalWriteCase.SplitCovering, false, TemporalWriteKind.Inserted)]
    [InlineData(TemporalWriteCase.InsertBeforeFirst, false, TemporalWriteKind.InsertedBeforeFirst)]
    [InlineData(TemporalWriteCase.InsertInGap, false, TemporalWriteKind.InsertedInGap)]
    [InlineData(TemporalWriteCase.InsertTrailing, false, TemporalWriteKind.InsertedTrailing)]
    public void KindOf_MapsEveryWriteCase(TemporalWriteCase @case, bool anchorIsOpen, TemporalWriteKind expected)
    {
        var decision = new TemporalWriteDecision(
            @case, D(1, 1), NewEffectiveTo: null, Anchor: null, AnchorIsOpen: anchorIsOpen,
            ReopensZeroWidthAnchor: false);

        Assert.Equal(expected, TemporalWriteRouter.KindOf(decision));
    }

    // ------------------------------------------------------------------
    // The safety net — sweep every day of a mixed fixture and check the post-write shape.
    // ------------------------------------------------------------------

    [Fact]
    public void Sweep_EveryDay_PostWriteTimelineHasNoOverlap_OneOpenRow_AndCoversTheDay()
    {
        // history · gap · history · zero-width · gap · open
        var fixture = new[]
        {
            Row(D(1, 10), D(2, 1)),
            Row(D(3, 1), D(4, 1)),
            Row(D(4, 1), D(4, 1)),
            Row(D(6, 1), null),
        };

        var seenCases = new HashSet<TemporalWriteCase>();
        for (var day = new DateOnly(2025, 12, 1); day <= Today; day = day.AddDays(1))
        {
            var decision = TemporalWriteRouter.Decide(fixture, day, Today);
            seenCases.Add(decision.Case);
            Assert.NotEqual(TemporalWriteCase.RejectedFutureDated, decision.Case);
            Assert.Equal(day, decision.NewEffectiveFrom);
            Assert.True(decision.NewEffectiveTo is null || decision.NewEffectiveTo.Value > day,
                $"{day:yyyy-MM-dd}: the produced row must cover the requested day");

            var after = ApplyDecision(fixture, decision);

            // No overlaps / duplicate starts — Build() throws on either.
            var snapshot = TimelineSnapshot.Build(after, day);
            Assert.Equal(after.Count, after.Select(r => r.From).Distinct().Count());
            Assert.True(after.Count(r => r.IsOpen) <= 1, $"{day:yyyy-MM-dd}: more than one open row");
            Assert.Equal(1, after.Count(r => r.IsOpen));

            // The requested day is now covered by exactly the row the write produced.
            var covering = after.Where(r => r.Covers(day)).ToList();
            Assert.Single(covering);
            Assert.Equal(new TemporalInterval(decision.NewEffectiveFrom, decision.NewEffectiveTo), covering[0]);
            Assert.Equal(covering[0], snapshot.Anchor);
        }

        // The fixture must have exercised every write case except A (the timeline is never empty)
        // and T (it always has an open row).
        Assert.Contains(TemporalWriteCase.UpdateInPlace, seenCases);
        Assert.Contains(TemporalWriteCase.SplitCovering, seenCases);
        Assert.Contains(TemporalWriteCase.InsertBeforeFirst, seenCases);
        Assert.Contains(TemporalWriteCase.InsertInGap, seenCases);
    }

    [Fact]
    public void Sweep_TrailingGapFixture_EveryDayAfterTheLastCloseReopensExactlyOneRow()
    {
        var fixture = new[] { Row(D(1, 10), D(2, 1)), Row(D(2, 1), D(3, 1)) };

        for (var day = D(3, 1); day <= Today; day = day.AddDays(1))
        {
            var decision = TemporalWriteRouter.Decide(fixture, day, Today);
            Assert.Equal(TemporalWriteCase.InsertTrailing, decision.Case);

            var after = ApplyDecision(fixture, decision);
            TimelineSnapshot.Build(after, day);
            Assert.Equal(1, after.Count(r => r.IsOpen));
            Assert.Single(after.Where(r => r.Covers(day)));
        }
    }

    /// <summary>
    /// Simulates what the repositories do with a decision: B' replaces the anchor's interval
    /// (unchanged, or re-extended for a zero-width row); C' shortens the anchor to
    /// <c>[anchor.From, from)</c> and adds the new row; E / G / T / A add the new row.
    /// </summary>
    private static List<TemporalInterval> ApplyDecision(
        IEnumerable<TemporalInterval> before, TemporalWriteDecision decision)
    {
        var after = before.ToList();
        var produced = new TemporalInterval(decision.NewEffectiveFrom, decision.NewEffectiveTo);
        switch (decision.Case)
        {
            case TemporalWriteCase.UpdateInPlace:
                after.Remove(decision.Anchor!.Value);
                after.Add(produced);
                break;
            case TemporalWriteCase.SplitCovering:
                after.Remove(decision.Anchor!.Value);
                after.Add(new TemporalInterval(decision.Anchor.Value.From, decision.NewEffectiveFrom));
                after.Add(produced);
                break;
            case TemporalWriteCase.Create:
            case TemporalWriteCase.InsertBeforeFirst:
            case TemporalWriteCase.InsertInGap:
            case TemporalWriteCase.InsertTrailing:
                after.Add(produced);
                break;
            default:
                throw new InvalidOperationException($"Unexpected case {decision.Case}");
        }
        return after;
    }
}
