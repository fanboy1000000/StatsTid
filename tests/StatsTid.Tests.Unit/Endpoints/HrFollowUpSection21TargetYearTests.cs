using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S140 / TASK-14005 (sprint-end review fix) — the non-Docker Unit fact
/// <see cref="HrFollowUpSettlementEndpoints.ResolveSection21TargetYear"/>'s own doc comment promises:
/// "a non-Docker Unit fact can assert 'November 2025, reset 9 ⇒ 2024' without a database." No such
/// fact existed; this is it.
///
/// <para>
/// <b>The rule, from the spec (§21 stk.2 of the Ferieloven, as the method's own doc states it).</b>
/// The target is the ferieår <c>E</c> whose §21 deadline (31 December of the ferieår-END year) falls
/// in the anchor's calendar year — NOT the ferieår containing the anchor. Under the state-sector
/// reset-month-9 geometry a ferieår runs 1 Sep <c>E</c> .. 31 Aug <c>E+1</c>, so its deadline is
/// 31 Dec <c>E+1</c>; the anchor's own ferieår (which the reset-9 geometry would place at
/// <c>E = anchor.Year</c>) has a deadline a YEAR AWAY, not the imminent one. So in November 2025 the
/// answer is <c>E = 2024</c>, not 2025 — exactly the mistake the method's own doc calls "the single
/// easiest thing to get wrong on this surface". Under the OTHER seeded geometry, reset-month-1 (a
/// ferieår coinciding with the calendar year), the deadline IS 31 December of the anchor's OWN year,
/// so <c>E = anchor.Year</c>.
/// </para>
/// </summary>
public class HrFollowUpSection21TargetYearTests
{
    /// <summary>The headline case the method's own doc comment names verbatim. RED if the ±1 year
    /// direction were flipped (a wrong year would list the wrong people in the one window §21
    /// applies).</summary>
    [Fact]
    public void NovemberAnchor_ResetMonth9_ResolvesToPriorYear()
    {
        var targetYear = HrFollowUpSettlementEndpoints.ResolveSection21TargetYear(
            resetMonth: 9, today: new DateOnly(2025, 11, 12));

        Assert.Equal(2024, targetYear);
    }

    /// <summary>The OTHER seeded geometry (reset-month-1, ferieår = calendar year): the deadline is
    /// 31 December of the anchor's OWN year, so the target year equals the anchor's year — the
    /// opposite offset from the reset-9 case above, which is exactly why both geometries need their
    /// own pin rather than one standing in for the other.</summary>
    [Fact]
    public void NovemberAnchor_ResetMonth1_ResolvesToSameYear()
    {
        var targetYear = HrFollowUpSettlementEndpoints.ResolveSection21TargetYear(
            resetMonth: 1, today: new DateOnly(2025, 11, 12));

        Assert.Equal(2025, targetYear);
    }
}
