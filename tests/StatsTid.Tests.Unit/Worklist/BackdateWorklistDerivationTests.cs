using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Worklist;

/// <summary>
/// S138 / TASK-13803 — DB-free pins for the worklist's DERIVED fields
/// (<see cref="BackdateWorklistDerivation"/>): the flags HR reads next to each stale exported
/// month / settled year are FUNCTIONS of the stored row + the live current state, never of the
/// operator's resolution verb.
///
/// <list type="bullet">
///   <item><b>recalcBlockedBy</b> — the full interior/edge × trigger-kind matrix: a change ON the
///     1st does not split the month (no blocker), a change on the 2nd..last day does — QUAL-149 for
///     PROFILE / EMPLOYMENT_CATEGORY (≥ 2 EMPLOYED segments), QUAL-150 for AGREEMENT_CODE (plan-
///     wide wage-type key). The row-level value is a SET; SETTLED_YEAR rows never block.</item>
///   <item><b>recalculatedSince</b> — per trigger vs row: exact hash comparison against EACH
///     trigger's own baseline; the row is recalculated only when EVERY trigger is.</item>
///   <item><b>reversedSince</b> — both legs: a later sequence (reverse-then-re-settle) OR the
///     baseline row now REVERSED (bare reversal).</item>
///   <item><b>the settled-year DATE rule</b> (S138 / TASK-13810) — a CONJUNCTION: the correction
///     reaches the settlement's valuation boundary (the last day it counted, inclusive) AND overlaps that entitlement year's
///     window. Each half is pinned alone (they fail on different axes), then together, with the
///     conservative legs pinned through the conjunction.</item>
/// </list>
///
/// <para>
/// Not pinnable here, by design: "a REVERSED settlement is not active" is enforced by the
/// repository's SELECT (<c>settlement_state &lt;&gt; 'REVERSED'</c> AND highest sequence), so it
/// stays a Docker fact — <c>HrBackdateWorklistRepositoryTests.WriteForSettledYears_…_ReversedOnly_NotSelected</c>.
/// </para>
/// </summary>
public sealed class BackdateWorklistDerivationTests
{
    // ── recalcBlockedBy: interior / edge matrix × trigger kinds ─────────────────────────────

    [Theory]
    [InlineData(2026, 1, 1, false)]   // the 1st — coincides with the period start: ONE segment
    [InlineData(2026, 1, 2, true)]    // first interior day
    [InlineData(2026, 1, 15, true)]   // mid-month
    [InlineData(2026, 1, 31, true)]   // the LAST day still splits the month ([1..30] + [31])
    [InlineData(2026, 2, 1, false)]   // next month's 1st — outside January
    [InlineData(2025, 12, 31, false)] // the day before — outside
    public void IsStrictlyInsideMonth_January2026_Fenceposts(int y, int m, int d, bool expected)
    {
        Assert.Equal(expected, BackdateWorklistDerivation.IsStrictlyInsideMonth(new DateOnly(y, m, d), 2026, 1));
    }

    [Theory]
    [InlineData(WorklistTriggerKinds.ProfileChange, 15, "QUAL-149")]
    [InlineData(WorklistTriggerKinds.EmploymentCategoryChange, 15, "QUAL-149")]
    [InlineData(WorklistTriggerKinds.AgreementCodeChange, 15, "QUAL-150")]
    [InlineData(WorklistTriggerKinds.ProfileChange, 30, "QUAL-149")]           // last day of April
    [InlineData(WorklistTriggerKinds.AgreementCodeChange, 30, "QUAL-150")]
    [InlineData(WorklistTriggerKinds.ProfileChange, 1, null)]                  // on the 1st: no split
    [InlineData(WorklistTriggerKinds.EmploymentCategoryChange, 1, null)]
    [InlineData(WorklistTriggerKinds.AgreementCodeChange, 1, null)]
    public void RecalcBlockedByForTrigger_April2026_KindByDateMatrix(string kind, int day, string? expected)
    {
        Assert.Equal(expected, BackdateWorklistDerivation.RecalcBlockedByForTrigger(kind, new DateOnly(2026, 4, day), 2026, 4));
    }

    [Fact]
    public void RecalcBlockedByForTrigger_DateBeforeTheMonth_CorrectionCoversWholeMonth_NotBlocked()
    {
        // A backdate in February whose interval reaches April covers ALL of April → one segment.
        Assert.Null(BackdateWorklistDerivation.RecalcBlockedByForTrigger(
            WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 2, 10), 2026, 4));
        Assert.Null(BackdateWorklistDerivation.RecalcBlockedByForTrigger(
            WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 2, 10), 2026, 4));
    }

    [Fact]
    public void RecalcBlockedByForTrigger_UnknownKind_Interior_NotBlocked()
    {
        Assert.Null(BackdateWorklistDerivation.RecalcBlockedByForTrigger("SOMETHING_ELSE", new DateOnly(2026, 4, 15), 2026, 4));
    }

    [Fact]
    public void RecalcBlockedBy_Row_IsASet_SortedDistinct()
    {
        var row = ExportedMonthRow(2026, 4, "h1", new[]
        {
            Trigger(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 4, 20), baselineHash: "h1"),
            Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 4, 10), baselineHash: "h1"),
            Trigger(WorklistTriggerKinds.EmploymentCategoryChange, new DateOnly(2026, 4, 12), baselineHash: "h1"), // same id as PROFILE
            Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 4, 1), baselineHash: "h1"),             // on the 1st: contributes nothing
        });

        Assert.Equal(new[] { "QUAL-149", "QUAL-150" }, BackdateWorklistDerivation.RecalcBlockedBy(row));
    }

    [Fact]
    public void RecalcBlockedBy_Row_OnlyEdgeTriggers_Empty()
    {
        var row = ExportedMonthRow(2026, 4, "h1", new[]
        {
            Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 4, 1), baselineHash: "h1"),
            Trigger(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 3, 20), baselineHash: "h1"),
        });

        Assert.Empty(BackdateWorklistDerivation.RecalcBlockedBy(row));
    }

    [Fact]
    public void RecalcBlockedBy_SettledYearRow_AlwaysEmpty_EvenWithInteriorTriggers()
    {
        var row = SettledYearRow("VACATION", 2024, highest: 1, reversed: Array.Empty<int>(), new[]
        {
            Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2025, 3, 15), baselineSequence: 1),
        });

        Assert.Empty(BackdateWorklistDerivation.RecalcBlockedBy(row));
    }

    // ── recalculatedSince: per trigger vs row ────────────────────────────────────────────────

    [Theory]
    [InlineData("h2", "h1", true)]   // hash advanced in place since the trigger → recalculated
    [InlineData("h1", "h1", false)]  // unchanged
    [InlineData(null, "h1", false)]  // export record gone / unknown → cannot claim recalculated
    [InlineData("h2", null, false)]  // no baseline captured → cannot claim
    [InlineData("H1", "h1", true)]   // ordinal comparison, case-sensitive
    public void RecalculatedSinceForTrigger_HashComparison(string? current, string? baseline, bool expected)
    {
        Assert.Equal(expected, BackdateWorklistDerivation.RecalculatedSinceForTrigger(current, baseline));
    }

    [Fact]
    public void RecalculatedSince_PerTriggerVsRow_RowTrueOnlyWhenEveryTriggerIs()
    {
        var t1 = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 5, 10), baselineHash: "h1");
        var t2 = Trigger(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 5, 20), baselineHash: "h2");

        // Current hash h2: t1 stale (recalculated since), t2 current → ROW false.
        var partly = ExportedMonthRow(2026, 5, currentHash: "h2", new[] { t1, t2 });
        Assert.True(BackdateWorklistDerivation.RecalculatedSinceForTrigger(partly, t1));
        Assert.False(BackdateWorklistDerivation.RecalculatedSinceForTrigger(partly, t2));
        Assert.False(BackdateWorklistDerivation.RecalculatedSince(partly));

        // Current hash h3: both baselines differ → ROW true.
        var fully = ExportedMonthRow(2026, 5, currentHash: "h3", new[] { t1, t2 });
        Assert.True(BackdateWorklistDerivation.RecalculatedSinceForTrigger(fully, t1));
        Assert.True(BackdateWorklistDerivation.RecalculatedSinceForTrigger(fully, t2));
        Assert.True(BackdateWorklistDerivation.RecalculatedSince(fully));

        // No triggers (cannot exist in the DB — CHECK ≥ 1 — but the function is total): false.
        Assert.False(BackdateWorklistDerivation.RecalculatedSince(ExportedMonthRow(2026, 5, "h3", Array.Empty<StoredWorklistTrigger>())));
    }

    [Fact]
    public void RecalculatedSince_OnSettledYearRow_IsNull_NotFalse()
    {
        var t = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2025, 3, 1), baselineSequence: 1);
        var row = SettledYearRow("VACATION", 2024, highest: 1, reversed: Array.Empty<int>(), new[] { t });

        Assert.Null(BackdateWorklistDerivation.RecalculatedSince(row));
        Assert.Null(BackdateWorklistDerivation.RecalculatedSinceForTrigger(row, t));
    }

    // ── reversedSince: both legs ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, new[] { 1 }, 1, true)]     // reverse-then-re-settle: a later sequence exists
    [InlineData(1, new[] { 1 }, 1, true)]     // bare reversal: the baseline row itself is REVERSED
    [InlineData(1, new int[0], 1, false)]     // untouched
    [InlineData(3, new[] { 1, 2 }, 3, false)] // the trigger saw the LIVE seq 3 — nothing since
    [InlineData(null, new int[0], 1, false)]  // tuple vanished / unknown → cannot claim
    [InlineData(2, new[] { 1 }, null, null)]  // no baseline captured → UNKNOWN, not "not reversed" (post-close Codex NOTE; the degraded write path is the only producer)
    public void ReversedSinceForTrigger_BothLegs(int? highest, int[] reversed, int? baseline, bool? expected)
    {
        Assert.Equal(expected, BackdateWorklistDerivation.ReversedSinceForTrigger(highest, reversed, baseline));
    }

    [Fact]
    public void ReversedSince_PerTriggerVsRow_RowTrueOnlyWhenEveryTriggerIs()
    {
        var saw1 = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2025, 3, 1), baselineSequence: 1);
        var saw2 = Trigger(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2025, 3, 5), baselineSequence: 2);

        // Live seq 2 (seq 1 reversed): the first trigger is superseded, the second is current.
        var partly = SettledYearRow("VACATION", 2024, highest: 2, reversed: new[] { 1 }, new[] { saw1, saw2 });
        Assert.True(BackdateWorklistDerivation.ReversedSinceForTrigger(partly, saw1));
        Assert.False(BackdateWorklistDerivation.ReversedSinceForTrigger(partly, saw2));
        Assert.False(BackdateWorklistDerivation.ReversedSince(partly));

        // Live seq 3 after another reverse-then-re-settle: every baseline superseded → ROW true.
        var fully = SettledYearRow("VACATION", 2024, highest: 3, reversed: new[] { 1, 2 }, new[] { saw1, saw2 });
        Assert.True(BackdateWorklistDerivation.ReversedSince(fully));

        // Bare reversal of the live row after both triggers saw seq 2 → both reversed → ROW true.
        var bare = SettledYearRow("VACATION", 2024, highest: 2, reversed: new[] { 1, 2 }, new[] { saw2, saw2 });
        Assert.True(BackdateWorklistDerivation.ReversedSince(bare));
    }

    [Fact]
    public void ReversedSince_OnExportedMonthRow_IsNull()
    {
        var t = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 5, 10), baselineHash: "h1");
        var row = ExportedMonthRow(2026, 5, "h1", new[] { t });

        Assert.Null(BackdateWorklistDerivation.ReversedSince(row));
        Assert.Null(BackdateWorklistDerivation.ReversedSinceForTrigger(row, t));
    }

    // ── settled-year DATE selection ──────────────────────────────────────────────────────────
    //
    // S138 / TASK-13810 (owner ruling 2026-09-03, amending OQ-2 (i)). The rule is a CONJUNCTION of
    // two halves, each pinned on its own below and then pinned together:
    //   (a) the correction reaches back PAST the moment the settlement was frozen; AND
    //   (b) the corrected interval OVERLAPS that entitlement year's accrual or taking window.
    // Each half alone over-flags on a different axis — (b) alone fires on an ordinary today-dated
    // edit (settled years' taking windows commonly run past today), (a) alone fires for every
    // settlement frozen after an old correction. A diagnostic list that is usually wrong is one HR
    // learns to ignore, at which point it stops surfacing the genuine rows too.

    // Half (b) — the geometry.

    [Fact]
    public void IntervalIntersectsClosedWindow_HalfOpenFenceposts()
    {
        var wStart = new DateOnly(2024, 9, 1);
        var wEnd = new DateOnly(2025, 8, 31);

        Assert.True(BackdateWorklistDerivation.IntervalIntersectsClosedWindow(new DateOnly(2025, 8, 31), new DateOnly(2025, 9, 1), wStart, wEnd)); // from == window end
        Assert.False(BackdateWorklistDerivation.IntervalIntersectsClosedWindow(new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 1), wStart, wEnd)); // from after window
        Assert.False(BackdateWorklistDerivation.IntervalIntersectsClosedWindow(new DateOnly(2024, 6, 1), new DateOnly(2024, 9, 1), wStart, wEnd)); // toExclusive == window start
        Assert.True(BackdateWorklistDerivation.IntervalIntersectsClosedWindow(new DateOnly(2024, 6, 1), new DateOnly(2024, 9, 2), wStart, wEnd));  // reaches the first day
        Assert.True(BackdateWorklistDerivation.IntervalIntersectsClosedWindow(new DateOnly(2024, 6, 1), null, wStart, wEnd));                       // open-ended
        Assert.False(BackdateWorklistDerivation.IntervalIntersectsClosedWindow(new DateOnly(2025, 9, 1), null, wStart, wEnd));                      // open-ended but after
    }

    [Theory]
    // VACATION with reset_month 9: entitlement year 2024 = accrual [1 Sep 2024, 31 Aug 2025], taking → 31 Dec 2025.
    [InlineData("VACATION", 9, 2024, "2025-06-01", "2025-07-01", true)]
    [InlineData("VACATION", 9, 2024, "2025-12-31", null, true)]      // the §21 boundary day itself
    [InlineData("VACATION", 9, 2024, "2026-01-01", null, false)]     // after the boundary
    [InlineData("VACATION", 9, 2025, "2025-06-01", "2025-07-01", false)] // starts 1 Sep 2025
    [InlineData("VACATION", 9, 2025, "2025-06-01", "2025-09-02", true)]  // reaches 1 Sep 2025
    // Calendar-year type (reset_month 1): 2025 = [1 Jan 2025, 31 Dec 2025].
    [InlineData("CARE_DAY", 1, 2025, "2025-12-31", null, true)]
    [InlineData("CARE_DAY", 1, 2025, "2026-01-01", null, false)]
    // SPECIAL_HOLIDAY 2024 (S80/8001): accrual [1 Jan 2024, 31 Dec 2024]; taking [1 May 2025, 30 Apr 2026].
    [InlineData("SPECIAL_HOLIDAY", 9, 2024, "2026-02-01", "2026-03-01", true)]   // Feb 2026 inside the taking window (resetMonth ignored)
    [InlineData("SPECIAL_HOLIDAY", 1, 2024, "2025-02-01", "2025-03-01", false)]  // Feb 2025: the GAP between accrual and taking
    [InlineData("SPECIAL_HOLIDAY", 1, 2024, "2024-06-01", "2024-07-01", true)]   // accrual window
    [InlineData("SPECIAL_HOLIDAY", 1, 2025, "2026-02-01", "2026-03-01", false)]  // 2025's gap (taking opens 1 May 2026)
    [InlineData("SPECIAL_HOLIDAY", 1, 2024, "2026-05-01", null, false)]          // after the 30 Apr 2026 close
    public void SettledYearIntersects_PerTypeGeometry(string type, int resetMonth, int year, string from, string? toExclusive, bool expected)
    {
        Assert.Equal(expected, BackdateWorklistDerivation.SettledYearIntersects(
            type, resetMonth, year, DateOnly.Parse(from), toExclusive is null ? null : DateOnly.Parse(toExclusive)));
    }

    // Half (a) — the settlement's valuation boundary: the LAST DAY it counted, INCLUSIVE.
    //
    // S138 Step-7a (Reviewer WARNING, absorbed): this pin previously expected `false` on the boundary
    // day itself, with the rationale "strict `<`, nothing before it changes". That rationale was one
    // day wrong. `SettlementBoundaryDate` is a valuation as-of date, and the settlement's own
    // consumption read is `date >= start AND date <= end` — so a correction effective ON the boundary
    // changes a day the settlement VALUED. The comparison is `<=`, and the day flips to `true`.

    [Theory]
    // A VACATION ferieår valued through 31 Aug 2025 (the settle-time snapshot's settlementBoundaryDate).
    [InlineData("2025-03-01", true)]   // a genuine backdate INTO the settled stretch
    [InlineData("2025-08-30", true)]   // the day before the boundary
    [InlineData("2025-08-31", true)]   // ON the boundary — INSIDE the settled window (was: false)
    [InlineData("2025-09-01", false)]  // the day after — the first day the settlement never counted
    [InlineData("2026-05-20", false)]  // an ordinary PRESENT-DAY edit — the false positive this ruling removes
    public void CorrectionReachesSettlementBoundary_Fenceposts(string correctedFrom, bool expected)
    {
        Assert.Equal(expected, BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(
            DateOnly.Parse(correctedFrom), new DateOnly(2025, 8, 31)));
    }

    [Fact]
    public void CorrectionReachesSettlementBoundary_UnknownBoundary_FlagsConservatively()
    {
        // The snapshot carries no usable settlementBoundaryDate (absent, or the 0001-01-01 that an
        // uninitialized snapshot serializes — the repository maps both to null). We cannot place the
        // valuation boundary, so we FLAG: a dismissable row beats a silently stale settlement.
        Assert.True(BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(new DateOnly(2026, 5, 20), null));
        Assert.True(BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(new DateOnly(2019, 1, 1), null));
    }

    [Fact]
    public void CorrectionReachesSettlementBoundary_TodayForwardEdit_AgainstAnOldSettlement_RaisesNothing()
    {
        // The shape the ruling was written for, stated as one fact: HR edits a fraction effective
        // TODAY; the employee has a settlement frozen years ago whose taking window has long since
        // closed AND one frozen more recently whose taking window is still open. Neither can have
        // been changed by a today-forward edit, and neither is flagged by the date rule.
        var today = new DateOnly(2026, 5, 20);
        Assert.False(BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(today, new DateOnly(2023, 8, 31)));
        Assert.False(BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(today, new DateOnly(2025, 8, 31)));
    }

    // The CONJUNCTION — the actual rule. Both halves must hold.

    /// <summary>
    /// S138 / TASK-13810 (conjunction ruled 2026-09-03) — the pin that makes each half necessary.
    /// One narrow correction (a single week of March 2021) is tested against TWO settled VACATION
    /// years, both frozen after it:
    /// <list type="bullet">
    ///   <item><b>ferieår 2020</b> — accrual [1 Sep 2020, 31 Aug 2021], frozen 31 Aug 2021. The week
    ///     falls inside that window and predates the freeze ⇒ genuinely threatened, row raised.</item>
    ///   <item><b>ferieår 2024</b> — accrual [1 Sep 2024, 31 Aug 2025], frozen 31 Aug 2025. The week
    ///     predates the freeze too, so half (a) alone would flag it — but the corrected week is
    ///     nowhere near that year's windows, so nothing in it can have moved. NO row.</item>
    /// </list>
    /// Without the geometry half, correcting one week of 2020 would raise a row for 2020 AND every
    /// year settled since: dismissals crowding out the one true row.
    /// </summary>
    [Fact]
    public void SettledYearThreatened_PredatesTheFreezeButMissesTheWindow_RaisesNothing()
    {
        var from = new DateOnly(2021, 3, 1);
        var to = new DateOnly(2021, 3, 8);

        Assert.True(BackdateWorklistDerivation.SettledYearThreatened(
            "VACATION", 9, 2020, from, to, new DateOnly(2021, 8, 31)));

        Assert.False(BackdateWorklistDerivation.SettledYearThreatened(
            "VACATION", 9, 2024, from, to, new DateOnly(2025, 8, 31)));

        // …and the halves disagree exactly there, which is why both are load-bearing.
        Assert.True(BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(from, new DateOnly(2025, 8, 31)));
        Assert.False(BackdateWorklistDerivation.SettledYearIntersects("VACATION", 9, 2024, from, to));
    }

    /// <summary>
    /// The mirror image: the correction overlaps the year's window but starts AFTER the freeze — an
    /// ordinary present-day edit landing inside a taking window that is still open. Half (b) holds,
    /// half (a) does not, so nothing is raised. (The one settled year this DOES threaten is the one
    /// still open at that date, shown as the positive control.)
    /// </summary>
    [Fact]
    public void SettledYearThreatened_OverlapsTheWindowButPostdatesTheFreeze_RaisesNothing()
    {
        var from = new DateOnly(2025, 10, 1);
        var to = new DateOnly(2025, 11, 1);

        // VACATION 2024's taking window runs to the §21 deadline of 31 Dec 2025, so the geometry
        // half is satisfied — but the year was frozen on 31 Aug 2025.
        Assert.True(BackdateWorklistDerivation.SettledYearIntersects("VACATION", 9, 2024, from, to));
        Assert.False(BackdateWorklistDerivation.SettledYearThreatened(
            "VACATION", 9, 2024, from, to, new DateOnly(2025, 8, 31)));

        // Positive control: the SAME correction against a year still open at that date (2025,
        // frozen 31 Aug 2026) satisfies both halves.
        Assert.True(BackdateWorklistDerivation.SettledYearThreatened(
            "VACATION", 9, 2025, from, to, new DateOnly(2026, 8, 31)));
    }

    /// <summary>
    /// The conservative leg survives the conjunction: an unreadable valuation boundary must not
    /// exonerate a settlement whose window the correction really does overlap.
    /// </summary>
    [Fact]
    public void SettledYearThreatened_UnknownBoundary_StillRaisesWhenTheWindowOverlaps()
    {
        var from = new DateOnly(2025, 6, 1);
        var to = new DateOnly(2025, 7, 1);

        Assert.True(BackdateWorklistDerivation.SettledYearThreatened("VACATION", 9, 2024, from, to, null));
        // …but an unknown boundary does NOT rescue a correction that misses the window entirely.
        Assert.False(BackdateWorklistDerivation.SettledYearThreatened("VACATION", 9, 2030, from, to, null));
    }

    // ── builders ─────────────────────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset AppendedAt = new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    private static StoredWorklistTrigger Trigger(string kind, DateOnly effectiveFrom, string? baselineHash = null, int? baselineSequence = null) =>
        new(kind, Guid.NewGuid(), effectiveFrom, AppendedAt, "hr01", baselineHash, baselineSequence, baselineSequence is null ? null : "SETTLED");

    private static HrBackdateWorklistRow ExportedMonthRow(int year, int month, string? currentHash, IReadOnlyList<StoredWorklistTrigger> triggers) =>
        new(Guid.NewGuid(), "emp1", WorklistKinds.ExportedMonth, year, month, Guid.NewGuid(), null, null,
            triggers, AppendedAt, "hr01", null, null, null, null, 1,
            new WorklistCurrentState(currentHash, null, Array.Empty<int>()));

    private static HrBackdateWorklistRow SettledYearRow(string type, int year, int? highest, int[] reversed, IReadOnlyList<StoredWorklistTrigger> triggers) =>
        new(Guid.NewGuid(), "emp1", WorklistKinds.SettledYear, null, null, null, type, year,
            triggers, AppendedAt, "hr01", null, null, null, null, 1,
            new WorklistCurrentState(null, highest, reversed));
}
