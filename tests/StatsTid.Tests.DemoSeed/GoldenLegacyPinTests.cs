using System.Text.Json;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S114 / TASK-11400 — the GOLDEN-SUBSET pin. The artifacts under <c>Golden/</c> were captured
/// from the PRE-S114 generator (git-clean tree, BEFORE any S114 generator edit) at the frozen
/// no-override config: TODAY's-then smoke scale (1 tree, 30 users, TargetSpan 4), seed 42,
/// referenceDate 2026-06-15 — sha256 202edbb5… (SQL) / 8705ee87… (manifest).
///
/// These tests rebuild that exact config (override knobs ABSENT — the shipped smoke scale now
/// carries them, hence the internal config clone) and assert the CURRENT generator reproduces the
/// golden bytes: people, edges, activity, roles and profiles are untouched by the S114 unit work,
/// and override-absence is the byte-exact legacy path. A pin captured after the change, or one
/// comparing the changed code to itself, would verify nothing — this one does.
/// </summary>
public sealed class GoldenLegacyPinTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    /// <summary>The PRE-S114 shipped smoke scale, verbatim — crucially WITHOUT the S114 override
    /// knobs (UnitSpanOverride / ManagerCountOverride both absent).</summary>
    private static ScaleConfig LegacySmokeClone() => new()
    {
        Name = "smoke",
        MinistryId = "MINX",
        MinistryName = "Demoministeriet",
        TargetSpan = 4,
        ActivityFraction = 0.30,
        PartTimeFraction = 0.15,
        MessyCaseCount = 4,
        Trees = new[]
        {
            new TreeProfile
            {
                OrganisationId = "STYX1",
                OrgName = "Demostyrelsen (smoke)",
                TargetUsers = 30,
                AgreementMix = (55, 35, 10),
                RootAgreement = "AC",
            },
        },
    };

    private static string GoldenPath(string file)
        => Path.Combine(AppContext.BaseDirectory, "Golden", file);

    /// <summary>Line-ending normalization mirroring Program's deterministic write (LF, no BOM) —
    /// robust against git autocrlf on the checked-out golden files.</summary>
    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void NoOverrideConfig_Sql_IsByteIdenticalToPreChangeGolden()
    {
        var dataset = new DemoGenerator(LegacySmokeClone(), 42, Ref).Generate();
        var sql = Normalize(SqlEmitter.Emit(dataset));
        var golden = Normalize(File.ReadAllText(GoldenPath("golden-legacy-smoke.sql")));
        Assert.Equal(golden, sql);
    }

    [Fact]
    public void NoOverrideConfig_Manifest_IsByteIdenticalToPreChangeGolden()
    {
        var dataset = new DemoGenerator(LegacySmokeClone(), 42, Ref).Generate();

        // The legacy path must not even EMIT a unitPlans section (null → key absent), so the
        // whole-manifest byte comparison against the pre-S114 golden is exact.
        Assert.Null(dataset.Manifest.UnitPlans);

        var json = Normalize(JsonSerializer.Serialize(dataset.Manifest, DemoManifestJsonContext.Default.DemoManifest));
        var golden = Normalize(File.ReadAllText(GoldenPath("golden-legacy-smoke.manifest.json")));
        Assert.Equal(golden, json);
    }
}
