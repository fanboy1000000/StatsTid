using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Settlement;

/// <summary>
/// S71 / TASK-7104 — unit pins on the two PURE reversal helpers:
///
/// <list type="bullet">
///   <item><description><b>SPRINT-71 R1</b> —
///   <see cref="SettlementReversalService.NextGenerationRowSequence"/>: settlement generation
///   <c>g</c> uses ROW sequence <c>2g−1</c> (original=1, superseding=3, next=5 …); a new
///   settlement ALWAYS allocates <c>g = highest-recorded-generation + 1</c> derived from the
///   tuple's row-sequence history — BOTH schemes pinned: <i>from-1</i> (empty history ⇒ 1, the
///   <c>SettleAsync</c> path) and <i>from-history</i> (never restarts at 1, so a future
///   post-bare-reversal revival's arithmetic is already settled).</description></item>
///   <item><description><b>SPRINT-71 R4</b> —
///   <see cref="VacationSettlementService.IsYearEndSupersedeDueUnderLock"/>: the
///   supersede-as-YEAR_END in-lock eligibility predicate, per clause (ACTIVE-branch
///   leak-proofing ×2, the D13 go-live floor on the 31-Dec boundary, boundary-passed). The
///   TERMINATION twin (<c>IsTerminationDueUnderLock</c>) is already pinned by
///   <see cref="TerminationDuePredicateTests"/>.</description></item>
/// </list>
/// </summary>
public class ReversalSequenceArithmeticTests
{
    // ───────────────────── R1 — NextGenerationRowSequence ─────────────────────

    /// <summary>The from-1 scheme: a virgin tuple's first settlement is generation 1 ⇒ row
    /// sequence 1 (the constant SettleAsync allocates).</summary>
    [Fact]
    public void EmptyHistory_From1_SequenceOne()
    {
        Assert.Equal(1, SettlementReversalService.NextGenerationRowSequence([]));
    }

    /// <summary>The from-history scheme: gen-1 recorded (seq 1, whatever its state — REVERSED
    /// included) ⇒ the next settlement is gen 2 ⇒ row sequence 3. THE pinned post-reversal
    /// allocation (R1: "row seq 3 after a bare-reversed gen-1").</summary>
    [Fact]
    public void HistoryOfOne_NextIsThree()
    {
        Assert.Equal(3, SettlementReversalService.NextGenerationRowSequence([1]));
    }

    [Fact]
    public void HistoryOfTwoGenerations_NextIsFive()
    {
        Assert.Equal(5, SettlementReversalService.NextGenerationRowSequence([1, 3]));
    }

    [Fact]
    public void HistoryOfThreeGenerations_NextIsSeven()
    {
        Assert.Equal(7, SettlementReversalService.NextGenerationRowSequence([1, 3, 5]));
    }

    /// <summary>Order-independence: the derivation keys on the HIGHEST recorded generation,
    /// not on list order or contiguity (defensive — history should always be contiguous odd).</summary>
    [Fact]
    public void UnorderedOrGappyHistory_KeysOnHighestGeneration()
    {
        Assert.Equal(7, SettlementReversalService.NextGenerationRowSequence([5, 1, 3]));
        Assert.Equal(7, SettlementReversalService.NextGenerationRowSequence([5]));
    }

    /// <summary>Row sequences are always ODD (even sequences are EXPORT-side per R1/R2): the
    /// result is odd for every plausible history.</summary>
    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 1, 3 })]
    [InlineData(new[] { 1, 3, 5, 7 })]
    public void ResultIsAlwaysAnOddRowSequence(int[] history)
    {
        Assert.Equal(1, SettlementReversalService.NextGenerationRowSequence(history) % 2);
    }

    // ───────────────── R4 — IsYearEndSupersedeDueUnderLock clauses ─────────────────

    private static readonly DateOnly Today = new(2026, 3, 5);   // Copenhagen business date
    private const int Year = 2024;                              // ferieår Sep 2024 .. Aug 2025
    private const int ResetMonth = 9;                           // boundary = 31 Dec 2025 (passed at Today)
    private static readonly DateOnly Floor = new(2025, 1, 1);   // go-live before the boundary

    [Fact]
    public void ActiveUser_NoEndDate_BoundaryPassed_PostGoLive_IsDue()
    {
        Assert.True(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            isActive: true, employmentEndDate: null, Year, ResetMonth, Floor, Today));
    }

    /// <summary>Clause 1 — the ACTIVE branch only: a (manually or lifecycle) inactive user is
    /// never auto-partitioned by a supersession.</summary>
    [Fact]
    public void InactiveUser_NotDue()
    {
        Assert.False(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            isActive: false, employmentEndDate: null, Year, ResetMonth, Floor, Today));
    }

    /// <summary>Clause 2 — the S70 R4 leak-proofing pin: a PASSED-end-date leaver must never
    /// traverse the §21/§24 auto-partition (not even through a supersession).</summary>
    [Fact]
    public void PassedEndDateLeaver_NotDue()
    {
        Assert.False(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            isActive: true, employmentEndDate: new DateOnly(2026, 2, 28), Year, ResetMonth, Floor, Today));
    }

    /// <summary>A FUTURE-dated end date is fine — mirrors the enumeration's ACTIVE branch
    /// (`NOT (end_date IS NOT NULL AND end_date &lt; today)`).</summary>
    [Fact]
    public void FutureEndDate_StillDue()
    {
        Assert.True(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, new DateOnly(2026, 12, 31), Year, ResetMonth, Floor, Today));
    }

    /// <summary>End date == today is the LAST employed day (not passed) — still due.</summary>
    [Fact]
    public void EndDateToday_StillDue()
    {
        Assert.True(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, Today, Year, ResetMonth, Floor, Today));
    }

    /// <summary>Clause 4 — the boundary (31 Dec of the ferieår-end year) has not passed:
    /// ferieår 2025 (Sep 2025 .. Aug 2026) ⇒ boundary 31 Dec 2026 &gt; Today.</summary>
    [Fact]
    public void BoundaryNotPassed_NotDue()
    {
        Assert.False(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, null, entitlementYear: 2025, ResetMonth, Floor, Today));
    }

    /// <summary>Clause 3 — the D13 floor: boundary == go-live is NOT strictly after ⇒ manual
    /// fallback (the IsBoundaryPassed `boundary &gt; goLiveDate` geometry verbatim).</summary>
    [Fact]
    public void BoundaryOnGoLiveFloor_NotDue()
    {
        Assert.False(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, null, Year, ResetMonth, supersedeGoLiveFloor: new DateOnly(2025, 12, 31), Today));
    }

    [Fact]
    public void BoundaryStrictlyAfterGoLiveFloor_IsDue()
    {
        Assert.True(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, null, Year, ResetMonth, supersedeGoLiveFloor: new DateOnly(2025, 12, 30), Today));
    }

    /// <summary>Null floor = the caller supplied no go-live (direct/test drives) — clause waived.</summary>
    [Fact]
    public void NullFloor_ClauseWaived_IsDue()
    {
        Assert.True(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, null, Year, ResetMonth, supersedeGoLiveFloor: null, Today));
    }

    /// <summary>reset_month 1 geometry: ferieår 2025 = calendar 2025 ⇒ boundary 31 Dec 2025
    /// (passed at Today) — the generic `(E, resetMonth, 1).AddYears(1).AddDays(−1)` formula
    /// collapses to Dec 31 E for reset-1.</summary>
    [Fact]
    public void ResetMonthOne_CalendarGeometry()
    {
        Assert.True(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, null, entitlementYear: 2025, resetMonth: 1, Floor, Today));
        Assert.False(VacationSettlementService.IsYearEndSupersedeDueUnderLock(
            true, null, entitlementYear: 2026, resetMonth: 1, Floor, Today));
    }
}
