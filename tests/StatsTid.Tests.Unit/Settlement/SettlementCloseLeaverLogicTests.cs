using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Settlement;

/// <summary>
/// S70 / TASK-7005 (ADR-033 slice 3a, SPRINT-70 R2 + R4 + R6) — unit pins on the PURE leaver
/// decision helpers behind the restructured <see cref="SettlementCloseService"/> poll:
/// <see cref="SettlementCloseService.IsDeactivationDue"/> (the Step-A flip due-predicate),
/// <see cref="SettlementCloseService.ResolveLeaverFerieaar"/> (R6 ferieår resolution) and
/// <see cref="SettlementCloseService.ResolveLeaverTupleTrigger"/> (the R2 leaver-level go-live
/// gate + R4 year-cap + TERMINATION/YEAR_END trigger selection). The DB-backed / live-poller
/// behavior is pinned in the regression class <c>Settlement.SettlementCloseLeaverTests</c>.
/// </summary>
public class SettlementCloseLeaverLogicTests
{
    private const int ResetMonth = 9; // VACATION reset_month — uniform by the S68 B1 DB CHECK.

    private static readonly DateOnly Today = new(2026, 3, 5);

    // ────────────────────────── IsDeactivationDue (Step A, R2/R1) ──────────────────────────

    [Fact]
    public void IsDeactivationDue_PassedEndDate_ActiveUser_IsDue()
    {
        Assert.True(SettlementCloseService.IsDeactivationDue(
            employmentEndDate: new DateOnly(2026, 2, 28), isActive: true, copenhagenToday: Today));
    }

    /// <summary>The R1 boundary: the end date is the LAST day employed — due requires today
    /// STRICTLY after it. endDate == today ⇒ still employed (no flip); endDate == today − 1 ⇒
    /// flip due (the first following Copenhagen business day).</summary>
    [Fact]
    public void IsDeactivationDue_BoundaryDay_NotDue_FirstFollowingDay_Due()
    {
        Assert.False(SettlementCloseService.IsDeactivationDue(Today, isActive: true, copenhagenToday: Today));
        Assert.True(SettlementCloseService.IsDeactivationDue(Today.AddDays(-1), isActive: true, copenhagenToday: Today));
    }

    [Fact]
    public void IsDeactivationDue_FutureEndDate_NotDue()
    {
        Assert.False(SettlementCloseService.IsDeactivationDue(
            Today.AddYears(2), isActive: true, copenhagenToday: Today));
    }

    /// <summary>A manually-deactivated user with no end date is NEVER lifecycle-flipped (R3
    /// adjacency: the flip keys on the end date, never on bare is_active).</summary>
    [Fact]
    public void IsDeactivationDue_NullEndDate_NeverDue()
    {
        Assert.False(SettlementCloseService.IsDeactivationDue(null, isActive: true, copenhagenToday: Today));
        Assert.False(SettlementCloseService.IsDeactivationDue(null, isActive: false, copenhagenToday: Today));
    }

    /// <summary>An already-inactive row is never due again (the flip predicate re-evaluates
    /// is_active = TRUE — Step A is idempotent across polls).</summary>
    [Fact]
    public void IsDeactivationDue_AlreadyInactive_NotDue()
    {
        Assert.False(SettlementCloseService.IsDeactivationDue(
            new DateOnly(2026, 2, 28), isActive: false, copenhagenToday: Today));
    }

    // ────────────────────────── ResolveLeaverFerieaar (R6) ──────────────────────────

    /// <summary>R6 verbatim: entitlementYear = endDate.Month >= 9 ? endDate.Year : endDate.Year − 1
    /// (VACATION reset_month = 9, uniform by DB CHECK) — pinned on the 31 Aug / 1 Sep boundary.</summary>
    [Theory]
    [InlineData(2026, 2, 28, 2025)]  // mid-ferieår (Sep 2025 .. Aug 2026)
    [InlineData(2026, 8, 31, 2025)]  // last day of ferieår 2025
    [InlineData(2026, 9, 1, 2026)]   // first day of ferieår 2026
    [InlineData(2025, 12, 31, 2025)] // calendar year-end is NOT a ferieår boundary
    [InlineData(2026, 1, 1, 2025)]
    public void ResolveLeaverFerieaar_PinnedBoundaries(int y, int m, int d, int expectedFerieaar)
    {
        Assert.Equal(expectedFerieaar, SettlementCloseService.ResolveLeaverFerieaar(new DateOnly(y, m, d)));
    }

    // ────────────────────────── ResolveLeaverTupleTrigger (R2 gate + R4 cap + trigger) ──────────────────────────

    private static readonly DateOnly EndDate = new(2026, 2, 28); // ferieår 2025
    private static readonly DateOnly GoLiveBefore = new(2025, 1, 1); // strictly before the end date

    [Fact]
    public void LeaverTrigger_EndDateFerieaar_Termination_WhenEndDatePassed()
    {
        Assert.Equal("TERMINATION", SettlementCloseService.ResolveLeaverTupleTrigger(
            entitlementYear: 2025, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: GoLiveBefore));
    }

    /// <summary>TERMINATION crystallizes AT the end date, NOT at the 31-Dec boundary: on the end
    /// date itself (last employed day) the tuple is not yet due; the first following day it is.</summary>
    [Fact]
    public void LeaverTrigger_EndDateFerieaar_NotDueOnEndDateItself_DueDayAfter()
    {
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            2025, EndDate, ResetMonth, copenhagenToday: EndDate, goLiveDate: GoLiveBefore));
        Assert.Equal("TERMINATION", SettlementCloseService.ResolveLeaverTupleTrigger(
            2025, EndDate, ResetMonth, copenhagenToday: EndDate.AddDays(1), goLiveDate: GoLiveBefore));
    }

    /// <summary>A leaver's OTHER (earlier) due ferieår settles with YEAR_END — the EXISTING
    /// boundary geometry unchanged (ferieår 2024 boundary 31 Dec 2025 passed at 2026-03-05 and
    /// strictly after go-live).</summary>
    [Fact]
    public void LeaverTrigger_PriorFerieaar_YearEnd_WhenBoundaryPassedAndPostGoLive()
    {
        Assert.Equal("YEAR_END", SettlementCloseService.ResolveLeaverTupleTrigger(
            entitlementYear: 2024, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: GoLiveBefore));
    }

    /// <summary>A prior ferieår whose 31-Dec deadline has NOT yet passed is not due (unchanged
    /// IsBoundaryPassed geometry: ferieår 2025's boundary is 31 Dec 2026 — after 2026-03-05; here
    /// probed via a later end date so 2025 is a PRIOR year).</summary>
    [Fact]
    public void LeaverTrigger_PriorFerieaar_BoundaryNotYetPassed_NotDue()
    {
        var endDateInFerieaar2026 = new DateOnly(2026, 10, 31); // ferieår 2026
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            entitlementYear: 2025, endDateInFerieaar2026, ResetMonth,
            copenhagenToday: new DateOnly(2026, 11, 15), goLiveDate: GoLiveBefore));
    }

    /// <summary>A prior ferieår whose boundary fell BEFORE go-live stays the manual fallback even
    /// for a post-go-live leaver (the per-boundary D13 gate inside the YEAR_END leg).</summary>
    [Fact]
    public void LeaverTrigger_PriorFerieaar_BoundaryBeforeGoLive_NotDue()
    {
        var goLive = new DateOnly(2025, 1, 1); // ferieår 2023's boundary (31 Dec 2024) < go-live
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            entitlementYear: 2023, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: goLive));
    }

    // R2 — the leaver-level go-live gate keys on the END DATE, strictly-after, whole branch.

    /// <summary>A pre-go-live leaver gets NOTHING auto-settled — neither the TERMINATION tuple nor
    /// any prior-year YEAR_END tuple (R2: pre-launch boundaries the system never tracked remain the
    /// manual fallback per D13).</summary>
    [Fact]
    public void LeaverTrigger_PreGoLiveEndDate_NothingDue_WholeBranchGated()
    {
        var goLiveAfterEndDate = new DateOnly(2026, 6, 1); // end date 2026-02-28 NOT strictly after
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            2025, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: goLiveAfterEndDate));
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            2024, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: goLiveAfterEndDate));
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            2020, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: goLiveAfterEndDate));
    }

    /// <summary>STRICTLY after: an end date exactly ON the go-live date is still pre-go-live.</summary>
    [Fact]
    public void LeaverTrigger_EndDateEqualsGoLive_StillGated()
    {
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            2025, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: EndDate));
        Assert.Equal("TERMINATION", SettlementCloseService.ResolveLeaverTupleTrigger(
            2025, EndDate, ResetMonth, copenhagenToday: Today, goLiveDate: EndDate.AddDays(-1)));
    }

    // R4 — the year-cap: no post-termination ferieår is ever due.

    [Theory]
    [InlineData(2026)] // the ferieår after the end-date ferieår (2025)
    [InlineData(2027)]
    [InlineData(2030)]
    public void LeaverTrigger_PostTerminationFerieaar_NeverDue(int entitlementYear)
    {
        Assert.Null(SettlementCloseService.ResolveLeaverTupleTrigger(
            entitlementYear, EndDate, ResetMonth, copenhagenToday: new DateOnly(2035, 1, 1),
            goLiveDate: GoLiveBefore));
    }
}
