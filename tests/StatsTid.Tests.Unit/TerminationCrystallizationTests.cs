using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S70 / TASK-7004 (ADR-033 slice 3a; SPRINT-70 R5/R6) — PURE tests for the TERMINATION
/// crystallization (<see cref="VacationSettlementService.CrystallizeTermination"/>) and the R6
/// ferieår resolution (<see cref="VacationSettlementService.ResolveTerminationFerieaar"/>), both
/// reachable via the <c>InternalsVisibleTo StatsTid.Tests.Unit</c> grant on Infrastructure.
///
/// <para>
/// <b>The pinned legal core (SPRINT-70 R5 — implemented verbatim, no invented logic):</b>
/// <c>pre-clamp = round2(Earned + CarryoverIn − Used)</c> (Earned = whole-month EarnedToDate
/// asOf the end date; Used = recorded absences ≤ the end date; carryover_in INCLUDED);
/// pre-clamp ≥ 0 ⇒ <c>SETTLED</c> with <c>CrystallizedDays = pre-clamp</c> (snapshot-only — the
/// row's bucket columns are all zero); pre-clamp NEGATIVE ⇒ <c>PENDING_REVIEW</c> with the
/// <c>|pre-clamp|</c> forfeit-FLAG (the S68 convention), parked until 3b's §7/waiver channel.
/// </para>
///
/// <para>
/// <b>R6 (executable verbatim):</b> <c>entitlementYear = endDate.Month >= 9 ? endDate.Year :
/// endDate.Year - 1</c> (VACATION <c>reset_month</c> = 9, uniform by DB CHECK per S68 B1).
/// Boundary pins: 31 Jan / 31 Aug / 1 Sep.
/// </para>
/// </summary>
public sealed class TerminationCrystallizationTests
{
    private static VacationSettlementSnapshot TerminationSnapshot(
        decimal earned, decimal used, decimal carryoverIn,
        DateOnly? terminationDate = null)
        => new()
        {
            Earned = earned,
            Used = used,
            Planned = 0m,
            CarryoverIn = carryoverIn,
            AnnualQuota = 25m,
            CarryoverMax = 5m,
            ResetMonth = 9,
            OkVersion = "OK24",
            AgreementCode = "AC",
            TerminationDate = terminationDate ?? new DateOnly(2026, 2, 28),
            CrystallizationBasis = "S26_WHOLE_MONTH",
            SettlementBoundaryDate = terminationDate ?? new DateOnly(2026, 2, 28),
            IsFeriehindret = false,
        };

    // ════════════════════════════════════════════════════════════════════════
    // R6 — ferieår-containing-end-date, the three pinned boundary cases.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>31 Jan E ⇒ the PRIOR calendar year's ferieår (month &lt; 9).</summary>
    [Fact]
    public void R6_EndDate31Jan_ResolvesPriorYearFerieaar()
        => Assert.Equal(2025, VacationSettlementService.ResolveTerminationFerieaar(new DateOnly(2026, 1, 31)));

    /// <summary>31 Aug E — the LAST day of ferieår E−1 — ⇒ the PRIOR calendar year's ferieår.</summary>
    [Fact]
    public void R6_EndDate31Aug_ResolvesPriorYearFerieaar()
        => Assert.Equal(2025, VacationSettlementService.ResolveTerminationFerieaar(new DateOnly(2026, 8, 31)));

    /// <summary>1 Sep E — the FIRST day of ferieår E — ⇒ the SAME calendar year's ferieår.</summary>
    [Fact]
    public void R6_EndDate1Sep_ResolvesSameYearFerieaar()
        => Assert.Equal(2026, VacationSettlementService.ResolveTerminationFerieaar(new DateOnly(2026, 9, 1)));

    // ════════════════════════════════════════════════════════════════════════
    // R5 — the pinned state rule: SETTLED unless the pre-clamp is negative.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A positive pre-clamp ⇒ SETTLED; CrystallizedDays = the pre-clamp; the forfeit
    /// FLAG is zero (the row's buckets are all zero — CrystallizedDays is snapshot-only).</summary>
    [Fact]
    public void Crystallize_PositivePreClamp_Settled_NoForfeitFlag()
    {
        var c = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 12.5m, used: 3m, carryoverIn: 0m));

        Assert.Equal(9.5m, c.PreClamp);
        Assert.Equal(9.5m, c.CrystallizedDays);
        Assert.Equal("SETTLED", c.SettlementState);
        Assert.Equal(0m, c.ForfeitFlagDays);
    }

    /// <summary>The pre-clamp ≥ 0 rule is INCLUSIVE of zero: an exactly-consumed leaver settles
    /// SETTLED with CrystallizedDays = 0 (not PENDING_REVIEW).</summary>
    [Fact]
    public void Crystallize_ZeroPreClamp_Settled()
    {
        var c = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 10m, used: 10m, carryoverIn: 0m));

        Assert.Equal(0m, c.PreClamp);
        Assert.Equal(0m, c.CrystallizedDays);
        Assert.Equal("SETTLED", c.SettlementState);
        Assert.Equal(0m, c.ForfeitFlagDays);
    }

    /// <summary>A NEGATIVE pre-clamp (over-taken days) ⇒ PENDING_REVIEW with
    /// <c>forfeit_days = |pre-clamp|</c> (the S68 flag convention) and CrystallizedDays = 0 —
    /// the §7 modregning question is deferred to 3b; the row parks.</summary>
    [Fact]
    public void Crystallize_NegativePreClamp_PendingReview_AbsoluteValueFlag()
    {
        var c = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 4.17m, used: 7m, carryoverIn: 0m));

        Assert.Equal(-2.83m, c.PreClamp);
        Assert.Equal(0m, c.CrystallizedDays);
        Assert.Equal("PENDING_REVIEW", c.SettlementState);
        Assert.Equal(2.83m, c.ForfeitFlagDays);
    }

    /// <summary>carryover_in is INCLUDED in the crystallization (SPRINT-70 R5 — a previously
    /// transferred balance must not vanish): earned 12.5 + carryover 2 − used 0 = 14.5.</summary>
    [Fact]
    public void Crystallize_CarryoverIn_IsIncluded()
    {
        var c = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 12.5m, used: 0m, carryoverIn: 2m));

        Assert.Equal(14.5m, c.PreClamp);
        Assert.Equal(14.5m, c.CrystallizedDays);
        Assert.Equal("SETTLED", c.SettlementState);
    }

    /// <summary>carryover_in can also RESCUE an otherwise-negative remainder: earned 2.08 +
    /// carryover 3 − used 4 = 1.08 ≥ 0 ⇒ SETTLED (no false over-taken flag).</summary>
    [Fact]
    public void Crystallize_CarryoverIn_RescuesNegativeEarnedMinusUsed()
    {
        var c = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 2.08m, used: 4m, carryoverIn: 3m));

        Assert.Equal(1.08m, c.PreClamp);
        Assert.Equal("SETTLED", c.SettlementState);
        Assert.Equal(0m, c.ForfeitFlagDays);
    }

    /// <summary>Rounding follows the existing AccrualMath/D9 convention — 2dp, ToEven midpoint —
    /// applied to the pre-clamp BEFORE the sign decision, so the recorded state/buckets are
    /// derivable from stored 2dp quantities. 10.005 → 10.00 (ToEven, not 10.01).</summary>
    [Fact]
    public void Crystallize_Rounds2dpToEven_BeforeSignDecision()
    {
        // 8.3350 + 1.6700 − 0 = 10.0050 → ToEven 2dp = 10.00 (AwayFromZero would give 10.01).
        var c = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 8.3350m, used: 0m, carryoverIn: 1.6700m));
        Assert.Equal(10.00m, c.PreClamp);
        Assert.Equal(10.00m, c.CrystallizedDays);

        // A sub-cent over-take that rounds to 0.00 is NOT negative post-rounding ⇒ SETTLED with a
        // zero crystallization (no unresolvable 0.00-flag PENDING_REVIEW row).
        var tiny = VacationSettlementService.CrystallizeTermination(
            TerminationSnapshot(earned: 5.0000m, used: 5.0010m, carryoverIn: 0m));
        Assert.Equal(0.00m, tiny.PreClamp);
        Assert.Equal("SETTLED", tiny.SettlementState);
        Assert.Equal(0m, tiny.ForfeitFlagDays);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Replay determinism (ADR-033 D3) — pure function + serializer round-trip.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The crystallization is a pure function of the snapshot AND survives the canonical
    /// <see cref="EventSerializer"/> round-trip on the <see cref="TerminationSettled"/> payload:
    /// the S70 snapshot extensions (TerminationDate / CrystallizationBasis / CrystallizedDays)
    /// round-trip byte-identically, and re-crystallizing the deserialized snapshot reproduces the
    /// recorded result exactly (replay reads the snapshot verbatim — never re-derives from live
    /// data).
    /// </summary>
    [Fact]
    public void Crystallize_SurvivesEventSerializerRoundTrip_ByteIdentical()
    {
        var snapshot = TerminationSnapshot(earned: 12.5m, used: 3m, carryoverIn: 2m,
            terminationDate: new DateOnly(2026, 2, 28));
        var original = VacationSettlementService.CrystallizeTermination(snapshot);
        snapshot = snapshot with { CrystallizedDays = original.CrystallizedDays };

        var evt = new TerminationSettled
        {
            EmployeeId = "emp-x",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            Sequence = 1,
            Snapshot = snapshot,
            PayoutDays = 0m,        // 3a: bucket day-counts stay 0 — CrystallizedDays is snapshot-only
            ModregningDays = 0m,
            UnearnedAdvanceDays = 0m,
        };

        var json1 = EventSerializer.Serialize(evt);
        var roundTripped = (TerminationSettled)EventSerializer.Deserialize(evt.EventType, json1);
        var json2 = EventSerializer.Serialize(roundTripped);
        Assert.Equal(json1, json2);

        var replayed = roundTripped.Snapshot!;
        Assert.Equal(new DateOnly(2026, 2, 28), replayed.TerminationDate);
        Assert.Equal("S26_WHOLE_MONTH", replayed.CrystallizationBasis);
        Assert.Equal(original.CrystallizedDays, replayed.CrystallizedDays);
        Assert.False(replayed.DeferredDisposition);

        var rederived = VacationSettlementService.CrystallizeTermination(replayed);
        Assert.Equal(original, rederived);
    }

    /// <summary>The R4 <c>DeferredDisposition</c> marker survives the serializer round-trip on a
    /// leaver other-ferieår snapshot (distinguishing no-partition from a computed partition for
    /// the deferred owner ruling).</summary>
    [Fact]
    public void DeferredDispositionMarker_SurvivesRoundTrip()
    {
        var snapshot = TerminationSnapshot(earned: 25m, used: 0m, carryoverIn: 0m) with
        {
            CrystallizationBasis = null, // a YEAR_END deferred-disposition row crystallizes nothing
            DeferredDisposition = true,
        };
        var evt = new SettlementManualReviewFlagged
        {
            EmployeeId = "emp-x",
            EntitlementType = "VACATION",
            EntitlementYear = 2024,
            Sequence = 1,
            Snapshot = snapshot,
            FlaggedDays = 25m,
        };

        var roundTripped = (SettlementManualReviewFlagged)EventSerializer.Deserialize(
            evt.EventType, EventSerializer.Serialize(evt));
        Assert.True(roundTripped.Snapshot!.DeferredDisposition);
        Assert.Null(roundTripped.Snapshot.CrystallizationBasis);
    }
}
