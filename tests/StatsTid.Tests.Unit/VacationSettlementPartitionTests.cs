using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S68 / TASK-6808 (ADR-033 D5/D10) — PURE tests for the §21/§24/§34 settlement partition
/// (<see cref="VacationSettlementService.Partition"/>, reachable via the
/// <c>InternalsVisibleTo StatsTid.Tests.Unit</c> grant on Infrastructure) plus the snapshot
/// replay-determinism contract (the recorded disposition is a pure function of the captured
/// snapshot; re-running <see cref="VacationSettlementService.Partition"/> over the SAME snapshot
/// and round-tripping it through the canonical <see cref="EventSerializer"/> reproduces the
/// buckets byte-identically — ADR-033 D3 quantity-determinism, priority 2).
///
/// <para>
/// These are NOT Docker-gated — <see cref="VacationSettlementService.Partition"/> is a static pure
/// function and <see cref="VacationSettlementSnapshot"/> / the settlement events live in
/// SharedKernel. They pin the legal core (the prompt's marquee scenarios) without a database.
/// </para>
///
/// <para>
/// <b>The legal core (prompt scenario 1).</b> A closed VACATION year with
/// <c>earned=25, used=0, carryover_in=0, carryover_max=5</c> partitions into
/// <c>under_cap=5</c> (§21+§24 tranche) and <c>over_cap=20</c> (§34-candidate ≡ the S66 D9
/// <c>expiring</c> figure). With no §21 agreement the under-cap tranche is the §24 auto-payout
/// (<c>payout=5</c>, <c>transfer=0</c>); with a §21 agreement of 5 it becomes a §21 transfer
/// (<c>transfer=5</c>, <c>payout=0</c>). With <c>used=25</c> everything is zero. The day-count is
/// part-time-fraction-INDEPENDENT (ADR-031 flat) because the snapshot's <c>Earned</c> is the
/// already-flat day basis — a half-timer's snapshot carries the SAME 25/5 numbers.
/// </para>
/// </summary>
public sealed class VacationSettlementPartitionTests
{
    private const string VacationType = "VACATION";

    /// <summary>Builds a closed-year snapshot with the given balance + config operands. The
    /// recorded-absence component list is irrelevant to the partition (the authoritative "used"
    /// scalar is <see cref="VacationSettlementSnapshot.Used"/>), so it is left empty here.</summary>
    private static VacationSettlementSnapshot Snapshot(
        decimal earned, decimal used, decimal carryoverIn, decimal carryoverMax,
        decimal transferAgreementDays = 0m, decimal planned = 0m, decimal annualQuota = 25m)
        => new()
        {
            Earned = earned,
            Used = used,
            Planned = planned,
            CarryoverIn = carryoverIn,
            AnnualQuota = annualQuota,
            CarryoverMax = carryoverMax,
            ResetMonth = 9,
            OkVersion = "OK24",
            TransferAgreementDays = transferAgreementDays,
            IsFeriehindret = false,
        };

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 1 — the §24 default (no §21 agreement): under_cap → payout, over_cap → forfeit.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// earned=25, used=0, carryover_in=0, carryover_max=5, NO §21 agreement →
    /// under_cap=5 (§21+§24), over_cap=20 (§34-candidate); the law's default is §24 auto-payout,
    /// so transfer=0, payout=5, forfeit=20. over_cap == the D9 expiring figure (25 − 0 − 5 = 20).
    /// </summary>
    [Fact]
    public void Partition_FullEntitlement_NoAgreement_PayoutsUnderCap_ForfeitsOverCap()
    {
        var partition = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 0m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: 0m));

        Assert.Equal(25m, partition.Disposable);
        Assert.Equal(5m, partition.UnderCap);   // §21+§24 tranche (≤ carryover_max)
        Assert.Equal(20m, partition.OverCap);   // §34-candidate (== D9 expiring)
        Assert.Equal(0m, partition.TransferDays);  // no §21 agreement
        Assert.Equal(5m, partition.PayoutDays);    // §24 default = the whole under-cap tranche
        Assert.Equal(20m, partition.ForfeitDays);  // §34-candidate
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 1b — a §21 agreement of 5: the under-cap tranche transfers, payout 0.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Same operands WITH a §21 written-transfer agreement of 5 → transfer=5, payout=0 (the §21
    /// agreement claims the whole under-cap tranche), forfeit=20 unchanged. transfer_days is the
    /// next-year carryover_in provenance (ADR-033 D6) — pinned at exactly the agreed 5.
    /// </summary>
    [Fact]
    public void Partition_FullEntitlement_With21Agreement_TransfersUnderCap_NoPayout()
    {
        var partition = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 0m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: 5m));

        Assert.Equal(5m, partition.UnderCap);
        Assert.Equal(5m, partition.TransferDays);  // §21 claims the whole under-cap tranche
        Assert.Equal(0m, partition.PayoutDays);    // nothing left for §24
        Assert.Equal(20m, partition.ForfeitDays);  // §34-candidate unchanged
    }

    /// <summary>
    /// A §21 agreement BELOW the under-cap tranche (3 of the available 5) splits: transfer=3 (§21),
    /// payout=2 (§24 default for the un-transferred remainder of the under-cap tranche), forfeit=20.
    /// </summary>
    [Fact]
    public void Partition_PartialAgreement_SplitsTransferAndPayout()
    {
        var partition = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 0m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: 3m));

        Assert.Equal(5m, partition.UnderCap);
        Assert.Equal(3m, partition.TransferDays);  // §21 agreed
        Assert.Equal(2m, partition.PayoutDays);    // §24 default for the rest of the under-cap tranche
        Assert.Equal(20m, partition.ForfeitDays);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 1c — fully consumed: everything zero.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// earned=25, used=25 → disposable=0 → every bucket zero (nothing to transfer / pay out /
    /// forfeit). This is the SETTLED-not-PENDING_REVIEW case (no §34 candidate).
    /// </summary>
    [Fact]
    public void Partition_FullyConsumed_AllBucketsZero()
    {
        var partition = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 25m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: 5m));

        Assert.Equal(0m, partition.Disposable);
        Assert.Equal(0m, partition.UnderCap);
        Assert.Equal(0m, partition.OverCap);
        Assert.Equal(0m, partition.TransferDays);
        Assert.Equal(0m, partition.PayoutDays);
        Assert.Equal(0m, partition.ForfeitDays);  // no §34 candidate ⇒ the service settles SETTLED
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 1d — half-timer (fraction-independent flat day-count, ADR-031).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-031: VACATION day-count is part-time-fraction-INDEPENDENT. The snapshot's <c>Earned</c>
    /// is the already-flat day basis (the service computes it with the 1.0 fraction), so a half-
    /// timer's closed-year snapshot carries the SAME 25/5 numbers as a full-timer's — and therefore
    /// the SAME partition (under_cap=5, over_cap=20). This pins that the partition does not re-apply
    /// any fraction (there is no fraction in the snapshot to re-apply).
    /// </summary>
    [Fact]
    public void Partition_HalfTimer_SameFlatDayCount_AsFullTimer()
    {
        // A half-timer's snapshot — identical day-count operands to the full-timer (ADR-031 flat).
        var halfTimer = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 0m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: 0m));
        var fullTimer = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 0m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: 0m));

        Assert.Equal(fullTimer, halfTimer); // record value-equality — every bucket identical
        Assert.Equal(5m, halfTimer.UnderCap);
        Assert.Equal(20m, halfTimer.OverCap);
    }

    // ════════════════════════════════════════════════════════════════════════
    // over_cap == the D9 expiring figure (the §34-candidate bucket).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The D9 <c>expiring</c> figure (BalanceEndpoints) is <c>round(max(0, earned + carryoverIn −
    /// used − planned − carryover_max), 2)</c> with <see cref="MidpointRounding.ToEven"/>. The
    /// partition's <c>ForfeitDays</c> (== OverCap) must equal it BYTE-FOR-BYTE for arbitrary
    /// operands. Verified across a table, including a carryover_in contribution and a .xx5 midpoint
    /// (which discriminates ToEven from AwayFromZero).
    /// </summary>
    [Theory]
    [InlineData(25, 0, 0, 0, 5, 20)]     // the marquee: 25 − 5 = 20
    [InlineData(25, 22, 0, 0, 5, 0)]     // below cap (raw 3 ≤ 5) ⇒ 0
    [InlineData(25, 0, 3, 0, 5, 23)]     // carryover_in 3 enters the disposable: 28 − 5 = 23
    [InlineData(25, 0, 0, 2, 5, 18)]     // planned 2 is subtracted: 23 − 5 = 18
    [InlineData(10, 0, 0, 0, 5, 5)]      // 10 − 5 = 5
    public void Partition_OverCap_EqualsD9ExpiringFormula(
        double earned, double used, double carryoverIn, double planned, double carryoverMax, double expectedExpiring)
    {
        var s = Snapshot(
            earned: (decimal)earned, used: (decimal)used, carryoverIn: (decimal)carryoverIn,
            carryoverMax: (decimal)carryoverMax, planned: (decimal)planned);

        // The D9 reader's exact formula (BalanceEndpoints: round(max(0, raw − cap), 2), ToEven).
        var d9Expiring = Math.Round(
            Math.Max(0m, (s.Earned + s.CarryoverIn - s.Used - s.Planned) - s.CarryoverMax),
            2, MidpointRounding.ToEven);

        var partition = VacationSettlementService.Partition(s);

        Assert.Equal((decimal)expectedExpiring, partition.ForfeitDays);
        Assert.Equal(d9Expiring, partition.ForfeitDays);  // byte-for-byte with the D9 figure
        Assert.Equal(partition.OverCap, partition.ForfeitDays); // forfeit IS the over-cap bucket
    }

    /// <summary>
    /// A .xx5 midpoint discriminates the rounding mode: raw − cap = 0.125 with a quota chosen so the
    /// 2dp midpoint is exactly .xx5. ToEven rounds 0.125 → 0.12 (the even neighbour); AwayFromZero
    /// would give 0.13. Pins that the partition uses ToEven (so it never diverges from D9).
    /// </summary>
    [Fact]
    public void Partition_MidpointRounding_IsToEven_MatchesD9()
    {
        // earned 5.125, cap 0 ⇒ raw − cap = 5.125 ⇒ rounds to 5.12 under ToEven (5.13 under AwayFromZero).
        var partition = VacationSettlementService.Partition(
            Snapshot(earned: 5.125m, used: 0m, carryoverIn: 0m, carryoverMax: 0m));
        Assert.Equal(5.12m, partition.ForfeitDays);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Defensive clamp — a negative agreement can never inflate payout / produce a negative bucket.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WARNING-2 defensive clamp (the DB CHECK blocks storing a negative agreement, but the partition
    /// stays robust): a stray negative <c>TransferAgreementDays</c> → transfer clamps to 0 and payout
    /// stays the whole under-cap tranche (never EXCEEDS under_cap). No bucket goes negative.
    /// </summary>
    [Fact]
    public void Partition_NegativeAgreement_ClampsTransferToZero_NoBucketNegative()
    {
        var partition = VacationSettlementService.Partition(
            Snapshot(earned: 25m, used: 0m, carryoverIn: 0m, carryoverMax: 5m, transferAgreementDays: -3m));

        Assert.Equal(0m, partition.TransferDays);
        Assert.Equal(5m, partition.PayoutDays);    // the whole under-cap tranche — NOT inflated past 5
        Assert.True(partition.PayoutDays <= partition.UnderCap);
        Assert.True(partition.TransferDays >= 0m && partition.PayoutDays >= 0m && partition.ForfeitDays >= 0m);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 2 — replay-determinism: pure function of the captured snapshot.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The partition is a PURE function of the snapshot: invoking it twice on the SAME snapshot
    /// yields the identical buckets (record value-equality). This is the in-memory half of the
    /// ADR-033 D3 replay-determinism contract — no captured state changes between calls.
    /// </summary>
    [Fact]
    public void Partition_IsPureFunctionOfSnapshot_ReplayYieldsIdenticalBuckets()
    {
        var s = Snapshot(earned: 25m, used: 4m, carryoverIn: 2m, carryoverMax: 5m, transferAgreementDays: 3m);

        var first = VacationSettlementService.Partition(s);
        var second = VacationSettlementService.Partition(s);

        Assert.Equal(first, second); // every bucket identical across the two evaluations
    }

    /// <summary>
    /// A settlement event carrying the snapshot round-trips through the CANONICAL
    /// <see cref="EventSerializer"/> (the same serializer the outbox + replay use) byte-identically,
    /// AND the partition re-derived from the DESERIALIZED snapshot equals the partition from the
    /// original — proving the recorded disposition survives the persistence boundary unchanged
    /// (ADR-033 D3 quantity-determinism; the S66 e0d1dc3 round-trippability lesson).
    /// </summary>
    [Fact]
    public void Snapshot_RoundTripsThroughCanonicalSerializer_PartitionReproducesByteIdentical()
    {
        var snapshot = Snapshot(
            earned: 25m, used: 4m, carryoverIn: 2m, carryoverMax: 5m, transferAgreementDays: 3m);
        var original = VacationSettlementService.Partition(snapshot);

        var evt = new VacationCarryoverExecuted
        {
            EmployeeId = "emp_replay",
            EntitlementType = VacationType,
            EntitlementYear = 2024,
            Sequence = 1,
            Snapshot = snapshot,
            TransferDays = original.TransferDays,
        };

        // Serialize → deserialize through the canonical EventSerializer (camelCase + enum-as-string).
        var json1 = EventSerializer.Serialize(evt);
        var roundTripped = (VacationCarryoverExecuted)EventSerializer.Deserialize(evt.EventType, json1);
        var json2 = EventSerializer.Serialize(roundTripped);

        // The serialized payloads are byte-identical across the round-trip.
        Assert.Equal(json1, json2);

        // The snapshot survived: re-deriving the partition from the deserialized snapshot reproduces
        // the buckets EXACTLY (the recorded disposition is a pure function of the captured snapshot).
        Assert.NotNull(roundTripped.Snapshot);
        var replayed = VacationSettlementService.Partition(roundTripped.Snapshot!);
        Assert.Equal(original, replayed);

        // And the snapshot operands themselves are preserved value-for-value.
        Assert.Equal(snapshot.Earned, roundTripped.Snapshot!.Earned);
        Assert.Equal(snapshot.Used, roundTripped.Snapshot.Used);
        Assert.Equal(snapshot.CarryoverIn, roundTripped.Snapshot.CarryoverIn);
        Assert.Equal(snapshot.CarryoverMax, roundTripped.Snapshot.CarryoverMax);
        Assert.Equal(snapshot.TransferAgreementDays, roundTripped.Snapshot.TransferAgreementDays);
    }
}
