using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S71 / TASK-7105 — unit pins for the SPRINT-71 R1/R2 export-sequence arithmetic
/// (<see cref="SettlementExportEmitter.ReversalExportSequence"/>).
///
/// <para>
/// R1 (verbatim): settlement generation <c>g</c> uses settlement-ROW sequence <c>2g−1</c>
/// (original = 1, superseding = 3, next = 5 …); the compensating reversal lines for generation
/// <c>g</c> use EXPORT sequence <c>2g</c> (reversal-of-original = 2, reversal-of-successor = 4 …).
/// R2: ORIGINAL lines carry the settlement-row sequence itself (their export sequence EQUALS the
/// odd row sequence — no transformation); ONLY compensating lines take the even <c>2g</c> slot,
/// i.e. <c>settlementRowSequence + 1</c>. No two real lines ever share
/// <c>(identity, sequence, bucket)</c>.
/// </para>
/// </summary>
public sealed class SettlementExportSequenceTests
{
    /// <summary>R1: reversal-of-original = 2 (g=1), reversal-of-successor = 4 (g=2), … — always
    /// the even 2g, always settlementRowSequence + 1.</summary>
    [Theory]
    [InlineData(1, 2)]   // g=1: row 2g−1=1 → compensating export 2g=2
    [InlineData(3, 4)]   // g=2: row 3 → 4
    [InlineData(5, 6)]   // g=3: row 5 → 6
    [InlineData(11, 12)] // g=6
    public void ReversalExportSequence_IsTheEvenTwoG(int settlementRowSequence, int expectedExportSequence)
    {
        Assert.Equal(expectedExportSequence, SettlementExportEmitter.ReversalExportSequence(settlementRowSequence));
        // The even slot — the R1 invariant the line UNIQUE key relies on (an original at 2g−1 and
        // its compensation at 2g can never collide).
        Assert.Equal(0, SettlementExportEmitter.ReversalExportSequence(settlementRowSequence) % 2);
    }

    /// <summary>R1: settlement-row sequences are ODD (2g−1) — an even or non-positive input is a
    /// vocabulary violation (R2: events carry settlement sequence, never export sequence), refused
    /// loudly rather than silently doubled.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]   // an EXPORT sequence passed where a settlement-row sequence belongs (R2 conflation)
    [InlineData(4)]
    [InlineData(-1)]
    public void ReversalExportSequence_RejectsEvenOrNonPositive(int invalidSequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SettlementExportEmitter.ReversalExportSequence(invalidSequence));
    }
}
