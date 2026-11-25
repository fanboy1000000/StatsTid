using StatsTid.Tools.DemoSeed;
using StatsTid.Tools.DemoSeed.Generation;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S148 / TASK-14839-T (AC-C12a, the committed file) — the demo file in the repository equals what the generator makes.
///
/// <para>The demo stack loads <c>docker/postgres/99-demo-seed.sql</c> as committed. Nothing checked that it still matched
/// the generator, so a change to the emitter without a regeneration went unnoticed until someone read the demo. This fact
/// generates the tool's defaults (full scale, seed 42, <see cref="ReferenceDateResolver.PinnedDefault"/>) and requires the
/// committed file to equal them, both with line endings normalised.</para>
///
/// <para><b>Kind: R</b> (a regression pin: the committed file kept current). <b>Red conditions:</b> an emitter or
/// generator change without regenerating the file (<c>dotnet run --project tools/StatsTid.DemoSeed -- generate</c>), or a
/// hand edit of the file. <b>Given up:</b> every later emitter change must regenerate the file in the same change, which
/// is what the tool's README promises. <b>Product code between the readings:</b> the generator and the emitter.</para>
/// </summary>
public sealed class CommittedDemoSeedTests
{
    [Fact]
    public void CommittedSql_EqualsTheGeneratorAtThePinnedDefaults()
    {
        var dataset = new DemoGenerator("full", 42, ReferenceDateResolver.PinnedDefault).Generate();
        var generated = GoldenLegacyPinTests.Normalize(SqlEmitter.Emit(dataset));

        var path = Path.Combine(AppContext.BaseDirectory, "Committed", "99-demo-seed.sql");
        var committed = GoldenLegacyPinTests.Normalize(File.ReadAllText(path));

        Assert.Equal(committed, generated);
    }
}
