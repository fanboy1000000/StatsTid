using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Settlement;

/// <summary>
/// S80 / TASK-8002 (ADR-033 Slice 2, R4) — PURE tests for the SPECIAL_HOLIDAY (særlige feriedage)
/// §15 stk.2/§17 godtgørelse partition (<see cref="VacationSettlementService.PartitionSpecialHoliday"/>,
/// reachable via the <c>InternalsVisibleTo StatsTid.Tests.Unit</c> grant on Infrastructure).
///
/// <para>
/// <b>The R4 HARD discriminator (the legal core).</b> The VACATION
/// <see cref="VacationSettlementService.Partition"/> computes
/// <c>over_cap = max(0, disposable − CarryoverMax)</c>; for SPECIAL_HOLIDAY (CarryoverMax = 0) that
/// would flag the WHOLE balance as a §34 forfeiture-candidate → PENDING_REVIEW — the exact
/// compliance bug. The godtgørelse partition NEVER does that: the unused remainder
/// (<c>max(0, earned + carryoverIn − used − planned)</c>) goes ENTIRELY to
/// <see cref="SettlementPartition.PayoutDays"/>; <see cref="SettlementPartition.ForfeitDays"/> and
/// <see cref="SettlementPartition.TransferDays"/> are ALWAYS 0. These tests pin that divergence
/// directly — they are RED against the VACATION partition on the same operands. Money-free
/// (day-count only; SLS owns the 2½%, §17 ≠ §10's 2,02%).
/// </para>
/// </summary>
public sealed class SpecialHolidayPartitionTests
{
    private const string SpecialHolidayType = "SPECIAL_HOLIDAY";

    /// <summary>A SPECIAL_HOLIDAY snapshot (CarryoverMax = 0 by law — no carryover modeled).</summary>
    private static VacationSettlementSnapshot Snapshot(
        decimal earned, decimal used = 0m, decimal planned = 0m)
        => new()
        {
            Earned = earned,
            Used = used,
            Planned = planned,
            CarryoverIn = 0m,
            AnnualQuota = 5m,
            CarryoverMax = 0m,
            ResetMonth = 1,
            OkVersion = "OK24",
            TransferAgreementDays = 0m,
            IsFeriehindret = false,
        };

    // ════════════════════════════════════════════════════════════════════════
    // R4 — the godtgørelse-only partition: the whole unused remainder → payout, NEVER forfeit.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A fully-accrued, unconsumed SPECIAL_HOLIDAY year (earned 5, used 0): the WHOLE 5-day remainder
    /// is the §15 stk.2/§17 godtgørelse — payout 5, transfer 0, forfeit 0. The R4 discriminator: the
    /// VACATION partition would forfeit all 5 (over_cap = 5 − 0) → PENDING_REVIEW; the godtgørelse
    /// partition NEVER forfeits.
    /// </summary>
    [Fact]
    public void Godtgoerelse_FullUnused_AllPayout_NeverForfeit()
    {
        var p = VacationSettlementService.PartitionSpecialHoliday(Snapshot(earned: 5m));

        Assert.Equal(5m, p.PayoutDays);   // the whole remainder → godtgørelse
        Assert.Equal(0m, p.ForfeitDays);  // R4: NEVER a §34 forfeiture
        Assert.Equal(0m, p.TransferDays); // no §21
    }

    /// <summary>
    /// The R4 discriminator made explicit: on IDENTICAL operands (earned 5, CarryoverMax 0) the
    /// VACATION <see cref="VacationSettlementService.Partition"/> forfeits the whole balance
    /// (forfeit 5, the §34 candidate that drives PENDING_REVIEW) while the godtgørelse partition pays
    /// it out (payout 5, forfeit 0). This pins that the dedicated method is NOT the VACATION one.
    /// </summary>
    [Fact]
    public void Godtgoerelse_DivergesFromVacationPartition_OnIdenticalOperands()
    {
        var snap = Snapshot(earned: 5m);

        var vacation = VacationSettlementService.Partition(snap);
        var godtgoerelse = VacationSettlementService.PartitionSpecialHoliday(snap);

        // The VACATION partition would §34-forfeit the entire balance (CarryoverMax 0) — PENDING_REVIEW.
        Assert.Equal(5m, vacation.ForfeitDays);
        Assert.Equal(0m, vacation.PayoutDays);

        // The godtgørelse partition pays it out and NEVER forfeits.
        Assert.Equal(0m, godtgoerelse.ForfeitDays);
        Assert.Equal(5m, godtgoerelse.PayoutDays);
    }

    /// <summary>
    /// A partly-consumed year: earned 5, used 2 ⇒ remainder 3 → godtgørelse 3 (still 0 forfeit).
    /// </summary>
    [Fact]
    public void Godtgoerelse_PartlyConsumed_RemainderIsPayout()
    {
        var p = VacationSettlementService.PartitionSpecialHoliday(Snapshot(earned: 5m, used: 2m));

        Assert.Equal(3m, p.PayoutDays);
        Assert.Equal(0m, p.ForfeitDays);
        Assert.Equal(0m, p.TransferDays);
    }

    /// <summary>
    /// A fully-consumed year (earned 5, used 5) settles to zero godtgørelse — and STILL no forfeit.
    /// </summary>
    [Fact]
    public void Godtgoerelse_FullyConsumed_ZeroPayout_ZeroForfeit()
    {
        var p = VacationSettlementService.PartitionSpecialHoliday(Snapshot(earned: 5m, used: 5m));

        Assert.Equal(0m, p.PayoutDays);
        Assert.Equal(0m, p.ForfeitDays);
        Assert.Equal(0m, p.TransferDays);
    }

    /// <summary>
    /// Over-consumption (used &gt; earned, e.g. a planned booking exceeding accrual) clamps the
    /// remainder at ≥ 0 — payout 0, forfeit 0 (NEVER a negative godtgørelse, NEVER a forfeit).
    /// </summary>
    [Fact]
    public void Godtgoerelse_OverConsumed_ClampsAtZero()
    {
        var p = VacationSettlementService.PartitionSpecialHoliday(Snapshot(earned: 3m, used: 4m, planned: 1m));

        Assert.Equal(0m, p.PayoutDays);
        Assert.Equal(0m, p.ForfeitDays);
    }

    /// <summary>
    /// Planned-but-untaken days subtract from the godtgørelse remainder (earned 5, planned 1 ⇒
    /// remainder 4): the unused, untransferred, unplanned balance is what gets the godtgørelse.
    /// </summary>
    [Fact]
    public void Godtgoerelse_PlannedSubtractsFromRemainder()
    {
        var p = VacationSettlementService.PartitionSpecialHoliday(Snapshot(earned: 5m, used: 0m, planned: 1m));

        Assert.Equal(4m, p.PayoutDays);
        Assert.Equal(0m, p.ForfeitDays);
    }
}
