using StatsTid.Tools.DemoSeed.Generation;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-1; NR-14, B-1) — the seeds PIN-1 pins are the first in 43 to 142 whose demo world holds at
/// least three APPROVED months.
///
/// <para>
/// <b>Why, in plain language.</b> The demo draws each month's outcome at random from four values, so a small world at
/// the default seed holds only about two approved months, too few for the pay pin to be worth anything. Instead of a
/// 3,350-person world, the pin takes a different but equally fixed small world: the first seed whose world holds at
/// least three. This fact proves each pinned seed IS that first one, counting from the generator alone (no database):
/// the manifest's <c>Activity</c> rows with <c>PeriodOutcome == "APPROVED"</c>. A seed whose generation throws cannot
/// have been pinned and counts as not qualifying.
/// </para>
///
/// <para><b>Kind: R</b> (a regression pin on the pinned seeds), not Docker; runs in the local gate. <b>Red conditions:</b>
/// a pinned seed other than the first qualifying one (an earlier seed qualifies, or the pinned seed holds fewer than
/// three), or a change to the generator's draws that moves a world's count.</para>
/// </summary>
public sealed class DemoPinSeedSelectionTests
{
    [Fact]
    public void PinnedSeeds_AreFirstIn43To142_WithThreeApprovedMonths()
    {
        foreach (var world in new[] { PinnedWorld.December2025, PinnedWorld.June2026 })
        {
            int? first = null;
            for (var seed = PinnedWorld.FirstSeed; seed <= PinnedWorld.LastSeed && first is null; seed++)
            {
                if (PinnedWorld.ExcludedSeeds.Contains(seed))
                    continue;
                if (ApprovedMonths(seed, world.ReferenceDate) >= PinnedWorld.MinApprovedMonths)
                    first = seed;
            }

            Assert.True(first is not null, $"no seed in {PinnedWorld.FirstSeed}..{PinnedWorld.LastSeed} qualifies for {world.File}");
            Assert.Equal(first, world.Seed);
            Assert.True(
                ApprovedMonths(world.Seed, world.ReferenceDate) >= PinnedWorld.MinApprovedMonths,
                $"{world.File}: the pinned seed {world.Seed} holds fewer than {PinnedWorld.MinApprovedMonths} APPROVED months");
        }
    }

    /// <summary>The manifest's APPROVED activity months at a seed, or -1 when the generator throws for it.</summary>
    internal static int ApprovedMonths(int seed, DateOnly referenceDate)
    {
        try
        {
            return new DemoGenerator(PinnedWorld.Scale, seed, referenceDate).Generate()
                .Manifest.Activity.Count(a => a.PeriodOutcome == "APPROVED");
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
