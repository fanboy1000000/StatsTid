using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using Xunit;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S147 / TASK-14700-T (QUAL-229; AC-229-5; rulings R-2 with R-11 .. R-14) — the Unit matrix of the stale-chain check:
/// <see cref="FlexLedgerOpening.Derive(DateOnly, IReadOnlyList{FlexLedgerRecord}, IReadOnlyList{EmploymentWindow}?)"/> walks the
/// employee's recorded flex chain before it opens a month, and <see cref="FlexLedgerOpening.DeriveLink"/> is the one
/// link (cases 1 .. 4c of S146's R-14) it walks with.
///
/// <para>
/// <b>Plain language.</b> Each paid month records the flex balance it opened and closed at, and the next month opens at that
/// closing. A correction of an early month can change its closing, which leaves every month built on it out of date until it
/// is recalculated, one by one. Before this sprint payroll looked only at the month directly before the one it was about to pay,
/// so a stale month further back went unnoticed. Now payroll checks the whole recorded chain and refuses
/// (<c>payroll-prior-month-stale</c>) by naming the EARLIEST month to recalculate first. These facts build chains month by
/// month and say which month must be named, and why. Every expected value below comes from the refinement (rev 4.1, T1 and
/// AC-229-5), never from what the code returns.
/// </para>
///
/// <para>
/// <b>Kind N for every fact</b> — each names a member this sprint creates (<c>DeriveLink</c>, <c>BuildIndex</c>,
/// <c>LedgerStartOf</c>, the two-argument <c>WindowsReadRange</c>, <c>PriorMonthStale</c>, or the internal counter seam of
/// <c>Derive</c>), so a compile error is not a RED; RED is a named MUTATION (keep the API, break one behaviour), proven at the
/// wave gate in an isolated worktree. The plan's mutations are M-1 .. M-6; this file adds M-7 .. M-12 for the bullets they do not
/// cover. Each fact names its mutation under "Red conditions".
/// </para>
///
/// <para>
/// <b>Calendar and chain.</b> The base chain (all months employed) is January (the ledger start S: 0 to 3), February (3 to 5),
/// March (5 to 8), April (8 to 9) and May (9 to 11); June is M. A month's opening is the previous month's closing, so the
/// consistent chain opens June at 11 CHAINED. The "evaluated" lists count the months whose link was computed by the seam: the
/// walk's compared months in ascending order, then M.
/// </para>
/// </summary>
public class FlexLedgerChainWalkTests
{
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly Feb = new(2026, 2, 1);
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly Jun = new(2026, 6, 1);

    private static FlexLedgerRecord Ledger(DateOnly month, decimal opening, decimal closing, string provenance) =>
        new(month.Year, month.Month, opening, closing, provenance);

    private static FlexLedgerRecord Raw(DateOnly month) => new(month.Year, month.Month, null, null, null);

    private static IReadOnlyList<EmploymentWindow> Continuous =>
        new[] { new EmploymentWindow(new DateOnly(2025, 1, 1), null) };

    private const string C = FlexLedgerOpening.Chained;
    private const string LS = FlexLedgerOpening.LedgerStart;

    private static readonly FlexLedgerRecord JanStart = Ledger(Jan, 0m, 3m, LS);

    /// <summary>The consistent chain, January to May (each opening is its predecessor's closing).</summary>
    private static List<FlexLedgerRecord> ConsistentChain() => new()
    {
        JanStart,
        Ledger(Feb, 3m, 5m, C),
        Ledger(Mar, 5m, 8m, C),
        Ledger(Apr, 8m, 9m, C),
        Ledger(May, 9m, 11m, C),
    };

    private static (FlexLedgerDecision Decision, List<DateOnly> Evaluated) Evaluate(
        DateOnly month, IReadOnlyList<FlexLedgerRecord> records, IReadOnlyList<EmploymentWindow>? windows)
    {
        var evaluated = new List<DateOnly>();
        var decision = FlexLedgerOpening.Derive(month, records, windows, evaluated.Add);
        return (decision, evaluated);
    }

    private static void AssertStale(FlexLedgerDecision decision, DateOnly named)
    {
        Assert.Null(decision.Opening);
        var refusal = decision.Refusal!;
        Assert.Equal("payroll-prior-month-stale", refusal.Kind);
        Assert.Equal(FlexLedgerRefusalException.PriorMonthStale, refusal.Kind);
        Assert.Equal(named, refusal.Month);
        Assert.Equal(FlexLedgerOpening.PriorMonthStaleSentence, refusal.ErrorSentence);
    }

    private static void AssertOpens(FlexLedgerDecision decision, decimal balance, string provenance, DateOnly ledgerStart)
    {
        Assert.Null(decision.Refusal);
        Assert.Equal(balance, decision.Opening!.Balance);
        Assert.Equal(provenance, decision.Opening.Provenance);
        Assert.Equal(ledgerStart, decision.Opening.LedgerStart);
    }

    // ---- the link reproduces S146's cases 1 .. 4c (R-11) --------------------------------------------------------------

    /// <summary>
    /// <c>DeriveLink</c> case 1 — M is a LEDGER_START record: its opening (7.5, not the 0 case 2 would give) is kept, and the
    /// walking <c>Derive</c> says the same. Kind N. Red conditions: Balance 7.5, LEDGER_START, LedgerStart = M for both
    /// functions; mutation M-7 (case 1 deleted from the link: a start is re-derived by case 2 as 0 or by case 4) trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case1_LedgerStartRecordOfM_KeepsItsOpening()
    {
        var records = new[] { Ledger(Jun, 7.5m, 9m, LS) };

        var link = FlexLedgerOpening.DeriveLink(Jun, FlexLedgerOpening.BuildIndex(records), null);

        AssertOpens(link, 7.5m, LS, Jun);
        Assert.Equal(FlexLedgerOpening.Derive(Jun, records, null), link);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 2 — an empty ledger (raw history only): LEDGER_START 0, whatever the month, including a raw
    /// record of M. Kind N. Red conditions: (0, LEDGER_START, LedgerStart = M) for a raw-only index and for an empty one;
    /// mutation M-8 (raw records counted as ledger records in <c>BuildIndex</c>'s S) refuses or goes UNTRACKED.
    /// </summary>
    [Fact]
    public void DeriveLink_Case2_EmptyLedger_RawHistoryOnly_IsLedgerStartZero()
    {
        var raw = new[] { Raw(Mar), Raw(Apr), Raw(Jun) };

        AssertOpens(FlexLedgerOpening.DeriveLink(Jun, FlexLedgerOpening.BuildIndex(raw), null), 0m, LS, Jun);
        AssertOpens(FlexLedgerOpening.DeriveLink(May, FlexLedgerOpening.BuildIndex(raw), null), 0m, LS, May);
        AssertOpens(FlexLedgerOpening.DeriveLink(May, FlexLedgerOpening.BuildIndex(Array.Empty<FlexLedgerRecord>()), null), 0m, LS, May);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 3a — M precedes S and has no record: 409 <c>payroll-export-out-of-order</c> naming S (June). Kind
    /// N. Red conditions: Opening null, Kind out-of-order, Month = S, and the same refusal from <c>Derive</c>; mutation M-9
    /// (3a names M instead of S, or opens it) trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case3a_UnrecordedMonthBeforeLedgerStart_RefusesNamingS()
    {
        var records = new[] { Ledger(Jun, 0m, 1m, LS) };

        var link = FlexLedgerOpening.DeriveLink(Apr, FlexLedgerOpening.BuildIndex(records), Continuous);

        Assert.Null(link.Opening);
        Assert.Equal("payroll-export-out-of-order", link.Refusal!.Kind);
        Assert.Equal(Jun, link.Refusal.Month);
        Assert.Equal(FlexLedgerOpening.Derive(Apr, records, Continuous), link);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 3b — M precedes S but has a raw record: 0, UNTRACKED, LedgerStart = S. Kind N. Red conditions:
    /// (0, UNTRACKED, June) and <c>IsUntracked</c>; mutation M-9 (3b refuses like 3a) trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case3b_RawMonthBeforeLedgerStart_IsUntrackedZero()
    {
        var records = new[] { Raw(Apr), Ledger(Jun, 0m, 1m, LS) };

        var link = FlexLedgerOpening.DeriveLink(Apr, FlexLedgerOpening.BuildIndex(records), Continuous);

        AssertOpens(link, 0m, "UNTRACKED", Jun);
        Assert.True(link.Opening!.IsUntracked);
        Assert.Equal(FlexLedgerOpening.Derive(Apr, records, Continuous), link);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 4a — P is employed and a ledger record: CHAINED from P's CLOSING (9.5, not P's opening 4); a
    /// later record (July, closing 77) is not consulted; M may be any day of its month (17 June). Kind N. Red conditions:
    /// (9.5, CHAINED, March); mutation M-10 (chain from P's opening, or from the latest record's closing) trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case4a_PreviousMonthEmployedAndRecorded_ChainsFromItsClosing()
    {
        var records = new[]
        {
            Ledger(Mar, 0m, 3m, LS),
            Ledger(Apr, 3m, 4m, C),
            Ledger(May, 4m, 9.5m, C),
            Ledger(new DateOnly(2026, 7, 1), 9.5m, 77m, C),
        };

        var link = FlexLedgerOpening.DeriveLink(new DateOnly(2026, 6, 17), FlexLedgerOpening.BuildIndex(records), Continuous);

        AssertOpens(link, 9.5m, C, Mar);
        Assert.Equal(FlexLedgerOpening.Derive(Jun, records, Continuous), link);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 4b, P unrecorded — 409 <c>payroll-prior-month-not-recorded</c> naming P (May), "Export that month
    /// first". Kind N. Red conditions: Opening null, the kind, Month = May, the export-first sentence and not the
    /// correct-first one; mutation M-11 (4b opens at 0 as REHIRE) trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case4b_PreviousMonthUnrecorded_RefusesNamingP_ExportFirst()
    {
        var records = new[] { Ledger(Mar, 0m, 3m, LS) };

        var link = FlexLedgerOpening.DeriveLink(Jun, FlexLedgerOpening.BuildIndex(records), Continuous);

        Assert.Null(link.Opening);
        Assert.Equal("payroll-prior-month-not-recorded", link.Refusal!.Kind);
        Assert.Equal(May, link.Refusal.Month);
        Assert.Contains("Export that month first", link.Refusal.ErrorSentence);
        Assert.DoesNotContain("Correct that month first", link.Refusal.ErrorSentence);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 4b, P raw — the same refusal naming P with "Correct that month first". Kind N. Red conditions:
    /// the correct-first sentence and not the export-first one; mutation M-11 trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case4b_PreviousMonthRaw_RefusesNamingP_CorrectFirst()
    {
        var records = new[] { Ledger(Mar, 0m, 3m, LS), Raw(May) };

        var link = FlexLedgerOpening.DeriveLink(Jun, FlexLedgerOpening.BuildIndex(records), Continuous);

        Assert.Null(link.Opening);
        Assert.Equal("payroll-prior-month-not-recorded", link.Refusal!.Kind);
        Assert.Equal(May, link.Refusal.Month);
        Assert.Contains("Correct that month first", link.Refusal.ErrorSentence);
        Assert.DoesNotContain("Export that month first", link.Refusal.ErrorSentence);
    }

    /// <summary>
    /// <c>DeriveLink</c> case 4c — P holds no employed day: REHIRE 0, even though an older closing exists. Kind N. Red
    /// conditions: (0, REHIRE, March); mutation M-11 (4c chains from the older record, or refuses) trips it.
    /// </summary>
    [Fact]
    public void DeriveLink_Case4c_PreviousMonthNotEmployed_IsRehireZero()
    {
        var records = new[] { Ledger(Mar, 0m, 3m, LS) };
        var windows = new[]
        {
            new EmploymentWindow(new DateOnly(2025, 1, 1), new DateOnly(2026, 4, 30)),
            new EmploymentWindow(new DateOnly(2026, 6, 1), null),
        };

        var link = FlexLedgerOpening.DeriveLink(Jun, FlexLedgerOpening.BuildIndex(records), windows);

        AssertOpens(link, 0m, FlexLedgerOpening.Rehire, Mar);
        Assert.Equal(FlexLedgerOpening.Derive(Jun, records, windows), link);
    }

    /// <summary>
    /// <c>DeriveLink</c> reads the link ONLY — it never walks. A chain whose February is stale gives the link of June the
    /// same answer as if it were consistent (May's closing, 11), while <c>Derive</c> refuses it. Kind N. Red conditions: the
    /// link opens (11, CHAINED, January) and the walking <c>Derive</c> names February; mutation M-12 (the link itself walks
    /// and refuses stale chains) makes the link refuse, tripping the first assertion.
    /// </summary>
    [Fact]
    public void DeriveLink_NeverWalks_StaleEarlierMonthDoesNotChangeTheLink()
    {
        var records = ConsistentChain();
        records[1] = Ledger(Feb, 99m, 5m, C); // February is stale (January closes at 3)

        var link = FlexLedgerOpening.DeriveLink(Jun, FlexLedgerOpening.BuildIndex(records), Continuous);

        AssertOpens(link, 11m, C, Jan);
        AssertStale(FlexLedgerOpening.Derive(Jun, records, Continuous), Feb);
    }

    /// <summary>
    /// <c>BuildIndex</c> / <c>LedgerStartOf</c> (R-11, R-13) — S is the earliest LEDGER month (raw records do not count),
    /// <c>null</c> for an empty or raw-only ledger; <c>TryGetRecord</c> finds a month by any of its days; two records for one
    /// month throw. Kind N. Red conditions: S = February although a raw January exists; null for raw-only; the record found
    /// for 17 March; the throw; mutation M-8 (raw counted) gives January.
    /// </summary>
    [Fact]
    public void BuildIndex_LedgerStartIsEarliestLedgerMonth_RawIgnored_DuplicateThrows()
    {
        var records = new[] { Raw(Jan), Ledger(Feb, 0m, 1m, LS), Ledger(Mar, 1m, 2m, C) };

        var index = FlexLedgerOpening.BuildIndex(records);

        Assert.Equal(Feb, index.LedgerStart);
        Assert.Equal(Feb, FlexLedgerOpening.LedgerStartOf(records));
        Assert.Equal(3, index.Count);
        Assert.True(index.TryGetRecord(new DateOnly(2026, 3, 17), out var march));
        Assert.Equal(Mar, march!.MonthStart);
        Assert.False(index.TryGetRecord(Apr, out _));
        Assert.Null(FlexLedgerOpening.LedgerStartOf(new[] { Raw(Jan), Raw(Feb) }));
        Assert.Null(FlexLedgerOpening.LedgerStartOf(Array.Empty<FlexLedgerRecord>()));
        Assert.Throws<InvalidOperationException>(() => FlexLedgerOpening.BuildIndex(new[] { Raw(May), Raw(May) }));
    }

    // ---- each link is evaluated once ----------------------------------------------------------------------------------

    /// <summary>
    /// Each link is evaluated once (R-11, "O(n)"). On the consistent January-to-May chain with M = June the seam sees exactly
    /// February, March, April, May (the compared months, S excluded) and then June (M itself): five evaluations, in that order,
    /// no month twice. Count = compared months + 1. Kind N. Red conditions: the exact list; mutation M-5 (call the link of M a
    /// second time) gives six entries ending June, June.
    /// </summary>
    [Fact]
    public void Walk_EachLinkIsEvaluatedOnce_ComparedMonthsPlusM()
    {
        var (decision, evaluated) = Evaluate(Jun, ConsistentChain(), Continuous);

        AssertOpens(decision, 11m, C, Jan);
        Assert.Equal(new[] { Feb, Mar, Apr, May, Jun }, evaluated);
    }

    /// <summary>
    /// A month with nothing to walk evaluates only M: M = S (the start is never re-derived, so the link of S is the only
    /// evaluation), and M before S, and an empty ledger. Kind N. Red conditions: each list is exactly [M]; mutation M-5
    /// (call the link of M a second time) lists M twice and trips every list.
    /// </summary>
    [Fact]
    public void Walk_NothingToWalk_EvaluatesOnlyM()
    {
        Assert.Equal(new[] { Jan }, Evaluate(Jan, ConsistentChain(), Continuous).Evaluated);
        Assert.Equal(new[] { Jan }, Evaluate(Jan, new[] { Raw(Jan) }, Continuous).Evaluated);
        var beforeStart = Evaluate(Jan, new[] { Raw(Jan), Ledger(Mar, 0m, 1m, LS) }, Continuous);
        Assert.Equal(new[] { Jan }, beforeStart.Evaluated);
        Assert.Equal("UNTRACKED", beforeStart.Decision.Opening!.Provenance);
    }

    // ---- consistent chains pass ---------------------------------------------------------------------------------------

    /// <summary>
    /// A consistent chain passes: June opens at May's closing, 11, CHAINED, with S = January. Also with a non-zero ledger-start
    /// opening (S is never re-derived: a start of 7.5 is not stale) and with a later record beyond M. Kind N. Red conditions:
    /// (11, CHAINED, January) and (13.5, CHAINED, January); mutation M-13 (the walk starts at S inclusive and re-derives S as case 2,
    /// 0 LEDGER_START, instead of case 1) names January in the second case.
    /// </summary>
    [Fact]
    public void Walk_ConsistentChain_Passes_AndTheStartIsNeverReDerived()
    {
        AssertOpens(Derive(Jun, ConsistentChain()), 11m, C, Jan);

        var nonZeroStart = new List<FlexLedgerRecord>
        {
            Ledger(Jan, 7.5m, 9m, LS),
            Ledger(Feb, 9m, 10m, C),
            Ledger(Mar, 10m, 12m, C),
            Ledger(Apr, 12m, 12.5m, C),
            Ledger(May, 12.5m, 13.5m, C),
            Ledger(new DateOnly(2026, 7, 1), 13.5m, 99m, C),
        };
        AssertOpens(Derive(Jun, nonZeroStart), 13.5m, C, Jan);
    }

    private static FlexLedgerDecision Derive(DateOnly month, IReadOnlyList<FlexLedgerRecord> records, IReadOnlyList<EmploymentWindow>? windows = null) =>
        FlexLedgerOpening.Derive(month, records, windows ?? Continuous);

    // ---- stale months are named ---------------------------------------------------------------------------------------

    /// <summary>
    /// A stale P is named: May's recorded opening is 7 although April closed at 9, so June cannot be opened and the month to
    /// recalculate is May (the sentence is the fixed, date-free one, and the exception carries yyyy-MM). Kind N. Red
    /// conditions: Kind payroll-prior-month-stale, Month May, the fixed sentence, MonthText "2026-05", no digit in the sentence;
    /// mutation M-14 (the walk's upper bound excludes P, <c>x &lt; p</c>) lets June through (Opening non-null).
    /// </summary>
    [Fact]
    public void Walk_StalePreviousMonth_IsNamed()
    {
        var records = ConsistentChain();
        records[4] = Ledger(May, 7m, 11m, C);

        var decision = Derive(Jun, records);

        AssertStale(decision, May);
        Assert.DoesNotContain(decision.Refusal!.ErrorSentence, char.IsDigit);
        Assert.Equal("2026-05", decision.Refusal.ToException("emp-1").MonthText);
        Assert.Equal("payroll-prior-month-stale", decision.Refusal.ToException("emp-1").Kind);
    }

    /// <summary>
    /// A deep stale month: February is stale (opening 99, January closes at 3) while March, April and May each follow their own
    /// predecessor, and June is M. The earliest stale month, February, is named (the walk does not stop at P). Kind N. Red
    /// conditions: Month February; mutation M-1 (compare only X = P) finds May consistent and opens June.
    /// </summary>
    [Fact]
    public void Walk_DeepStaleMonth_NamesTheEarliest_NotP()
    {
        var records = ConsistentChain();
        records[1] = Ledger(Feb, 99m, 5m, C);

        AssertStale(Derive(Jun, records), Feb);
    }

    /// <summary>
    /// Several stale months: February, April and May all disagree with their predecessors; only the EARLIEST (February) is
    /// named. As each is repaired the next attempt names the next (the repair sequence): with February repaired, April; with
    /// April repaired too, May; with all repaired June opens. Kind N. Red conditions: Feb, Apr, May, then an opening; mutation
    /// M-2 (return the latest differing month) names May first.
    /// </summary>
    [Fact]
    public void Walk_SeveralStaleMonths_OnlyTheEarliestIsNamed_ThenTheRepairSequence()
    {
        var records = ConsistentChain();
        records[1] = Ledger(Feb, 99m, 5m, C);
        records[3] = Ledger(Apr, 50m, 9m, C);
        records[4] = Ledger(May, 60m, 11m, C);
        AssertStale(Derive(Jun, records), Feb);

        records[1] = Ledger(Feb, 3m, 5m, C); // February recalculated
        AssertStale(Derive(Jun, records), Apr);

        records[3] = Ledger(Apr, 8m, 9m, C); // April recalculated
        AssertStale(Derive(Jun, records), May);

        records[4] = Ledger(May, 9m, 11m, C); // May recalculated
        AssertOpens(Derive(Jun, records), 11m, C, Jan);
    }

    /// <summary>
    /// A recalculation of the named month never refuses itself: with February stale, M = February (the correction of the
    /// named month) has nothing before it to walk and opens CHAINED from January's closing, 3; M = January keeps its start.
    /// And a LATER month's recalculation (March) is refused naming February. Kind N. Red conditions: (3, CHAINED, January) for
    /// February, (0, LEDGER_START) for January, stale February for March; mutation M-15 (the walk includes M itself) names
    /// February when M = February.
    /// </summary>
    [Fact]
    public void Walk_RecalculatingTheNamedMonth_NeverRefusesItself_ALaterMonthDoes()
    {
        var records = ConsistentChain();
        records[1] = Ledger(Feb, 99m, 5m, C);

        AssertOpens(Derive(Feb, records), 3m, C, Jan);
        AssertOpens(Derive(Jan, records), 0m, LS, Jan);
        AssertStale(Derive(Mar, records), Feb);
    }

    // ---- R-48: months with no employed day ----------------------------------------------------------------------------

    /// <summary>
    /// An R-48 month whose own opening is stale is skipped, and its successor is named (R-14). Employment starts in April, so
    /// February and March hold no employed day: February's recorded opening is stale (99; January closes at 3) and can never
    /// be repaired (its correction derives nothing); April, recorded CHAINED 8, now follows a month with no employed day and
    /// re-derives REHIRE 0, so APRIL is named, not February. M = May. The seam shows February and March were not evaluated: the
    /// list is [April, May]. Kind N. Red conditions: Month April and the list; mutation M-3 (do not skip a month with no
    /// employed day) names February.
    /// </summary>
    [Fact]
    public void Walk_MonthWithNoEmployedDay_IsSkipped_ItsSuccessorIsNamed()
    {
        var windows = new[] { new EmploymentWindow(Apr, null) };
        var records = new List<FlexLedgerRecord>
        {
            JanStart,
            Ledger(Feb, 99m, 5m, C),
            Ledger(Mar, 5m, 8m, C),
            Ledger(Apr, 8m, 9m, C),
        };

        var (decision, evaluated) = Evaluate(May, records, windows);

        AssertStale(decision, Apr);
        Assert.Equal(new[] { Apr }, evaluated);
    }

    /// <summary>
    /// After the successor is repaired (April recorded REHIRE 0) the unrepairable stale February no longer blocks: May opens
    /// CHAINED from April's closing, 9. The seam lists [April, May]. Kind N. Red conditions: (9, CHAINED, January) and the list;
    /// mutation M-3 (no skip) names February although it can never be repaired.
    /// </summary>
    [Fact]
    public void Walk_MonthWithNoEmployedDay_StaleOwnOpening_SuccessorAlreadyRehire_Passes()
    {
        var windows = new[] { new EmploymentWindow(Apr, null) };
        var records = new List<FlexLedgerRecord>
        {
            JanStart,
            Ledger(Feb, 99m, 5m, C),
            Ledger(Mar, 5m, 8m, C),
            Ledger(Apr, 0m, 9m, FlexLedgerOpening.Rehire),
        };

        var (decision, evaluated) = Evaluate(May, records, windows);

        AssertOpens(decision, 9m, C, Jan);
        Assert.Equal(new[] { Apr, May }, evaluated);
    }

    // ---- refusing links -----------------------------------------------------------------------------------------------

    /// <summary>
    /// A refusing link is named, with its inner kind in the diagnostic and never in the body. February has no record although it
    /// is employed; March (recorded CHAINED) therefore re-derives a case-4b refusal naming February; March is the earliest month
    /// whose link refuses, so the walk refuses with <c>payroll-prior-month-stale</c> naming MARCH. The diagnostic names the inner
    /// kind and February; the fixed sentence names neither. Kind N. Red conditions: stale, March, the diagnostic contains
    /// "payroll-prior-month-not-recorded" and "2026-02", the sentence does not; mutation M-6 (swallow an inner refusal as
    /// consistent) lets June open.
    /// </summary>
    [Fact]
    public void Walk_RefusingLink_IsNamedWithItsInnerKind_InTheDiagnosticOnly()
    {
        var records = ConsistentChain();
        records.RemoveAt(1); // no February record

        var decision = Derive(Jun, records);

        AssertStale(decision, Mar);
        Assert.Contains("payroll-prior-month-not-recorded", decision.Refusal!.Diagnostic);
        Assert.Contains("2026-02", decision.Refusal.Diagnostic);
        Assert.DoesNotContain("payroll-prior-month-not-recorded", decision.Refusal.ErrorSentence);
    }

    /// <summary>
    /// The same with a RAW month in the chain (February exported without a balance): March's link refuses case 4b ("correct it
    /// first" arm); March is named stale. Kind N. Red conditions: stale, March, diagnostic mentions 2026-02; mutation M-6.
    /// </summary>
    [Fact]
    public void Walk_RefusingLink_RawMonthInTheChain_NamesTheNextLedgerMonth()
    {
        var records = ConsistentChain();
        records[1] = Raw(Feb);

        var decision = Derive(Jun, records);

        AssertStale(decision, Mar);
        Assert.Contains("2026-02", decision.Refusal!.Diagnostic);
    }

    /// <summary>
    /// A recalculation of a month that was named because its own link refuses answers the case-4b refusal naming its
    /// predecessor to export or correct first (the implementer's documented behaviour, contract 2; brief note 5): M = March with
    /// February unrecorded refuses <c>payroll-prior-month-not-recorded</c> naming February (boundary pin: that is
    /// S146's answer for it today, and the walk has nothing before March to refuse on). Kind B — holds today. Red conditions:
    /// kind not-recorded and Month February; a walk that named March, or opened it at 0, trips it.
    /// </summary>
    [Fact]
    public void Walk_RecalculatingAMonthNamedForItsRefusingLink_AnswersCase4b_NamingItsPredecessor()
    {
        var records = ConsistentChain();
        records.RemoveAt(1);

        var decision = Derive(Mar, records);

        Assert.Null(decision.Opening);
        Assert.Equal("payroll-prior-month-not-recorded", decision.Refusal!.Kind);
        Assert.Equal(Feb, decision.Refusal.Month);
    }

    /// <summary>
    /// Months with no ledger record are not compared (the Orchestrator's R-12 reading, "every RECORDED month"): P = May unrecorded
    /// with an otherwise consistent chain is NOT a stale refusal — it is June's own case-4b refusal naming May, exactly as before
    /// the walk; the same when May is raw ("correct it first"). Kind B — holds today (boundary pin: nobody "fixes" this into a
    /// stale refusal). Red conditions: kind not-recorded, Month May, in both shapes; a walk that compared unrecorded months and
    /// refused them as stale trips it.
    /// </summary>
    [Fact]
    public void Walk_UnrecordedOrRawP_IsStillTheCase4bRefusal_NotStale()
    {
        var unrecorded = ConsistentChain();
        unrecorded.RemoveAt(4);
        var raw = ConsistentChain();
        raw[4] = Raw(May);

        foreach (var records in new[] { unrecorded, raw })
        {
            var decision = Derive(Jun, records);
            Assert.Null(decision.Opening);
            Assert.Equal("payroll-prior-month-not-recorded", decision.Refusal!.Kind);
            Assert.Equal(May, decision.Refusal.Month);
        }
    }

    // ---- boundaries: REHIRE, S ----------------------------------------------------------------------------------------

    /// <summary>
    /// A stale month before a REHIRE boundary is named (R-12: the walk always starts at S). Employment is January to March and
    /// from May (April holds no employed day); February is stale (opening 99); March follows February; April keeps the triple
    /// it was paid at; May is recorded REHIRE 0 and is consistent; M = June would chain from May. Still February is named,
    /// although June's own stretch begins at May. Kind N. Red conditions: Month February; mutation M-4 (start the walk at the
    /// latest REHIRE, May, instead of S) finds May consistent and opens June.
    /// </summary>
    [Fact]
    public void Walk_StaleMonthBeforeARehireBoundary_IsNamed()
    {
        var windows = new[]
        {
            new EmploymentWindow(new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 31)),
            new EmploymentWindow(May, null),
        };
        var records = new List<FlexLedgerRecord>
        {
            JanStart,
            Ledger(Feb, 99m, 5m, C),
            Ledger(Mar, 5m, 8m, C),
            Ledger(Apr, 8m, 9m, C), // no employed day: keeps the triple it was paid at, not compared
            Ledger(May, 0m, 11m, FlexLedgerOpening.Rehire),
        };

        AssertStale(Derive(Jun, records, windows), Feb);
    }

    /// <summary>
    /// A consistent chain with a REHIRE inside passes: the same employment, February repaired (3); April has no employed day;
    /// May is recorded REHIRE 0; June opens CHAINED from May's closing, 11. Kind N. Red conditions: (11, CHAINED, January);
    /// mutation M-16 (case 4c deleted from the link, so a month after an unemployed month is derived CHAINED) names May.
    /// </summary>
    [Fact]
    public void Walk_ConsistentChainWithRehireInside_Passes()
    {
        var windows = new[]
        {
            new EmploymentWindow(new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 31)),
            new EmploymentWindow(May, null),
        };
        var records = new List<FlexLedgerRecord>
        {
            JanStart,
            Ledger(Feb, 3m, 5m, C),
            Ledger(Mar, 5m, 8m, C),
            Ledger(Apr, 8m, 9m, C),
            Ledger(May, 0m, 11m, FlexLedgerOpening.Rehire),
        };

        AssertOpens(Derive(Jun, records, windows), 11m, C, Jan);
    }

    /// <summary>
    /// The provenance is part of the comparison: February is recorded REHIRE 0 but January is employed and closes at 0, so the
    /// link now gives CHAINED 0 — the same balance, a different provenance, and February is named. Kind N. Red conditions:
    /// Month February; mutation M-17 (compare the balance only) lets June open.
    /// </summary>
    [Fact]
    public void Walk_SameBalanceDifferentProvenance_IsStale()
    {
        var records = new List<FlexLedgerRecord>
        {
            Ledger(Jan, 0m, 0m, LS),
            Ledger(Feb, 0m, 2m, FlexLedgerOpening.Rehire),
            Ledger(Mar, 2m, 3m, C),
        };

        AssertStale(Derive(Apr, records), Feb);
    }

    // ---- the windows read range (R-13) --------------------------------------------------------------------------------

    /// <summary>
    /// R-13 — with a ledger start the windows are read over <c>[min(S, P).start, M.end]</c>: S well before P gives S's first
    /// day; S = P or S = M or S after M gives today's range (P's first day); no S (null) gives today's range. The one-argument
    /// calls in <c>FlexLedgerOpeningTests</c> keep today's range. Kind N. Red conditions: the five exact tuples; mutation M-18
    /// (the helper ignores <c>ledgerStart</c>) gives (2026-05-01, ...) for the first.
    /// </summary>
    [Fact]
    public void WindowsReadRange_WithLedgerStart_StartsAtMinOfSAndP()
    {
        var m = new DateOnly(2026, 6, 17);
        var end = new DateOnly(2026, 6, 30);

        Assert.Equal((Jan, end), FlexLedgerOpening.WindowsReadRange(m, new DateOnly(2026, 1, 20)));
        Assert.Equal((May, end), FlexLedgerOpening.WindowsReadRange(m, May));
        Assert.Equal((May, end), FlexLedgerOpening.WindowsReadRange(m, Jun));
        Assert.Equal((May, end), FlexLedgerOpening.WindowsReadRange(m, new DateOnly(2026, 8, 1)));
        Assert.Equal((May, end), FlexLedgerOpening.WindowsReadRange(m, null));
        Assert.Equal((new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31)), FlexLedgerOpening.WindowsReadRange(Jan, null));
    }
}
