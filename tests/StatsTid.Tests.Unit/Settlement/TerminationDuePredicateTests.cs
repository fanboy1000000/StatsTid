using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Settlement;

/// <summary>
/// S70 Step-7a BLOCKER B1 (Codex, fix-forward) — unit pins on the PURE in-lock termination-due
/// predicate <see cref="VacationSettlementService.IsTerminationDueUnderLock"/>, re-evaluated by
/// <c>SettleAsync</c>'s TERMINATION fork on the re-read user AFTER the R12 advisory lock is held
/// (the enumeration-time <c>SettlementCloseService.ResolveLeaverTupleTrigger</c> decision is
/// pre-lock and can be staled by a racing end-date correction). Each clause is pinned
/// individually; ANY failure means the caller returns the benign NotDue no-op (no row, no event,
/// no throw). The DB-backed race choreographies live in the regression class
/// <c>Settlement.SettlementCloseLeaverTests</c> (B1_* / R12_* tests).
/// </summary>
public class TerminationDuePredicateTests
{
    private static readonly DateOnly Today = new(2026, 3, 5);     // Copenhagen business date
    private static readonly DateOnly EndDate = new(2026, 2, 28);  // R6 ferieår 2025 (month 2 < 9)
    private static readonly DateOnly Floor = new(2025, 1, 1);     // a go-live before the end date
    private const int EndDateFerieaar = 2025;

    // ────────────────────────── the all-clauses-pass baseline ──────────────────────────

    [Fact]
    public void AllClausesPass_IsDue()
    {
        Assert.True(VacationSettlementService.IsTerminationDueUnderLock(
            isActive: false, employmentEndDate: EndDate, entitlementYear: EndDateFerieaar,
            leaverGoLiveFloor: Floor, copenhagenToday: Today));
    }

    /// <summary>Clause 3 skip: a null floor means the caller supplied no go-live floor (the
    /// direct/test drive shape — the close service ALWAYS supplies it); the clause is waived.</summary>
    [Fact]
    public void NullFloor_ClauseWaived_IsDue()
    {
        Assert.True(VacationSettlementService.IsTerminationDueUnderLock(
            false, EndDate, EndDateFerieaar, leaverGoLiveFloor: null, copenhagenToday: Today));
    }

    // ────────────────────────── clause 1 — is_active FALSE ──────────────────────────

    /// <summary>The B1 exploit clause: a REACTIVATED user (the R1 correct-to-future
    /// re-evaluation) is NOT a leaver — Step B only ever enumerates flipped leavers.</summary>
    [Fact]
    public void ActiveUser_NotDue()
    {
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            isActive: true, employmentEndDate: EndDate, entitlementYear: EndDateFerieaar,
            leaverGoLiveFloor: Floor, copenhagenToday: Today));
    }

    // ────────────────────────── clause 2 — end date non-null AND passed ──────────────────────────

    [Fact]
    public void NullEndDate_NotDue()
    {
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            false, employmentEndDate: null, EndDateFerieaar, Floor, Today));
    }

    /// <summary>The end date is the LAST day employed: endDate == today ⇒ still employed (not
    /// passed); endDate == today − 1 ⇒ passed (the first following Copenhagen business day) —
    /// mirrors the Step-A IsDeactivationDue boundary.</summary>
    [Fact]
    public void EndDateBoundary_TodayNotPassed_YesterdayPassed()
    {
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            false, Today, VacationSettlementService.ResolveTerminationFerieaar(Today), Floor, Today));
        var yesterday = Today.AddDays(-1);
        Assert.True(VacationSettlementService.IsTerminationDueUnderLock(
            false, yesterday, VacationSettlementService.ResolveTerminationFerieaar(yesterday), Floor, Today));
    }

    /// <summary>A FUTURE end date (the correct-to-future race, when the competitor did not
    /// reactivate) is not passed ⇒ NotDue.</summary>
    [Fact]
    public void FutureEndDate_NotDue()
    {
        var future = new DateOnly(2026, 8, 15); // still ferieår 2025 — only the passed clause fails
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            false, future, EndDateFerieaar, Floor, Today));
    }

    // ────────────────────────── clause 3 — the D13 go-live floor ──────────────────────────

    /// <summary>R2/D13: only end dates STRICTLY AFTER the floor settle — endDate == floor is a
    /// pre-launch boundary (manual fallback); endDate == floor + 1 day is due.</summary>
    [Fact]
    public void FloorBoundary_OnFloorNotDue_DayAfterFloorDue()
    {
        var floor = new DateOnly(2026, 1, 15); // inside ferieår 2025
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            false, floor, EndDateFerieaar, floor, Today));
        Assert.True(VacationSettlementService.IsTerminationDueUnderLock(
            false, floor.AddDays(1), EndDateFerieaar, floor, Today));
    }

    /// <summary>The B1 D13 exploit variant: a passed end date at/below the floor (the
    /// correct-to-pre-go-live race) ⇒ NotDue.</summary>
    [Fact]
    public void PassedButPreGoLiveEndDate_NotDue()
    {
        var floor = new DateOnly(2026, 1, 15);
        var preGoLive = new DateOnly(2025, 10, 15); // ferieår 2025, passed at Today, <= floor
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            false, preGoLive, EndDateFerieaar, floor, Today));
    }

    // ────────────────────────── clause 4 — R6 ferieår tuple match ──────────────────────────

    /// <summary>The tuple's entitlementYear must BE the end-date ferieår (R6 9-pivot) — a
    /// different-ferieår correction leaves the stale tuple NotDue.</summary>
    [Theory]
    [InlineData(2024, false)] // stale tuple after a correction out of 2025
    [InlineData(2025, true)]  // the end-date ferieår
    [InlineData(2026, false)] // post-termination year
    public void FerieaarMatchClause(int entitlementYear, bool expectedDue)
    {
        Assert.Equal(expectedDue, VacationSettlementService.IsTerminationDueUnderLock(
            false, EndDate, entitlementYear, Floor, Today));
    }

    /// <summary>R6 boundary pins on the predicate's ferieår clause: 31 Aug / 1 Sep.</summary>
    [Fact]
    public void FerieaarBoundary_Aug31PriorYear_Sep1SameYear()
    {
        var today = new DateOnly(2026, 10, 1);
        Assert.True(VacationSettlementService.IsTerminationDueUnderLock(
            false, new DateOnly(2026, 8, 31), 2025, Floor, today));
        Assert.True(VacationSettlementService.IsTerminationDueUnderLock(
            false, new DateOnly(2026, 9, 1), 2026, Floor, today));
        Assert.False(VacationSettlementService.IsTerminationDueUnderLock(
            false, new DateOnly(2026, 8, 31), 2026, Floor, today));
    }
}
