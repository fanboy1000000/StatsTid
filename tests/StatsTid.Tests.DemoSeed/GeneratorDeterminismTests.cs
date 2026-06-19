using System.Text.Json;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S84 / TASK-8401 — determinism proof: the same (seed, scale, referenceDate) yields
/// byte-identical SQL AND byte-identical manifest JSON. A different seed yields different data.
/// </summary>
public sealed class GeneratorDeterminismTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    private static (string Sql, string ManifestJson) Run(string scale, int seed)
    {
        var dataset = new DemoGenerator(scale, seed, Ref).Generate();
        var sql = SqlEmitter.Emit(dataset);
        var json = JsonSerializer.Serialize(dataset.Manifest, DemoManifestJsonContext.Default.DemoManifest);
        return (sql, json);
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void Generate_Twice_SameSeed_ProducesByteIdenticalSql(string scale)
    {
        var (sql1, _) = Run(scale, 42);
        var (sql2, _) = Run(scale, 42);
        Assert.Equal(sql1, sql2);
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void Generate_Twice_SameSeed_ProducesByteIdenticalManifest(string scale)
    {
        var (_, m1) = Run(scale, 42);
        var (_, m2) = Run(scale, 42);
        Assert.Equal(m1, m2);
    }

    [Fact]
    public void Generate_DifferentSeed_ProducesDifferentData()
    {
        var (sql42, _) = Run("smoke", 42);
        var (sql7, _) = Run("smoke", 7);
        Assert.NotEqual(sql42, sql7);
    }

    [Fact]
    public void Generate_DifferentReferenceDate_ProducesDifferentDates()
    {
        var a = new DemoGenerator("smoke", 42, new DateOnly(2026, 6, 15)).Generate();
        var b = new DemoGenerator("smoke", 42, new DateOnly(2025, 1, 1)).Generate();
        // Reference date threads into employment dates / activity months — output must differ.
        Assert.NotEqual(SqlEmitter.Emit(a), SqlEmitter.Emit(b));
    }
}
