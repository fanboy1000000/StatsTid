using StatsTid.Infrastructure.Temporal;

namespace StatsTid.Tests.Unit.Infrastructure;

/// <summary>
/// S138 / TASK-13801 — the DB-free matrix for <see cref="TemporalWriteRouter"/>, the pure routing
/// core behind both dated-history writers (<c>EmployeeProfileRepository</c> /
/// <c>UserAgreementCodeRepository</c>). Every case letter of the refinement (A / B' / C' / E / G /
/// T, plus the future refusal) is pinned against live, history, gap and empty timelines, with the
/// fenceposts at every row boundary under end-exclusive <c>[from, to)</c> semantics — a row closed
/// on day <c>d</c> does NOT cover <c>d</c>; the successor does.
///
/// <para>
/// <b>Why this matters (plain language).</b> The router decides what the SQL does: which row is
/// closed, which is edited, and exactly which interval the new row gets. An off-by-one here would
/// let two rows overlap (two answers to "what was her fraction on the 10th?") or leave a day
/// uncovered. The sweep test at the bottom is the safety net: for every calendar day in a mixed
/// fixture (history, a zero-width row, gaps, an open row) the post-write timeline must still have
/// no overlaps, at most one open row, and the requested day covered by the row that was written.
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
    // Future refusal — the owner ruling (ADR-040 D8 amendment).
    // ------------------------------------------------------------------

    [Fact]
    public void FutureDate_IsRejected_OnEmptyAndPopulatedTimelines()
    {
        var tomorrow = Today.AddDays(1);

        var onEmpty = Decide(tomorrow);
        Assert.Equal(TemporalWriteCase.RejectedFutureDated, onEmpty.Case);
        Assert.Null(onEmpty.Anchor);
        Assert.False(onEmpty.ProducesOpenRow);
        Assert.False(onEmpty.InsertsRow);

        var onPopulated = Decide(tomorrow, Row(D(1, 1), null));
        Assert.Equal(TemporalWriteCase.RejectedFutureDated, onPopulated.Case);

        // A rejected decision has no write kind — the repository throws before mapping.
        Assert.Throws<InvalidOperationException>(() => TemporalWriteRouter.KindOf(onPopulated));
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
