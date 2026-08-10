using System.Text.Json;
using System.Text.RegularExpressions;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S127 / TASK-12701a — the evidence behind the deliberate golden regeneration.
///
/// <para><see cref="GoldenLegacyPinTests"/> compares the generator against goldens that S127
/// REGENERATED, so it can no longer testify that S127 left the pre-existing world alone: it now
/// compares the changed code to bytes produced by the changed code. This class restores that
/// testimony. <c>Golden/pre-s127-legacy-smoke.*</c> are the UNTOUCHED S114 artifacts (sha256
/// 202edbb5… SQL / 8705ee87… manifest) and these tests assert that the CURRENT generator's output,
/// with EXACTLY the two S127 additions removed, still reproduces them byte for byte.</para>
///
/// <para><b>What that actually proves — and its one limit.</b> The generator consumes ONE seeded
/// <see cref="Random"/> in a fixed order, so an extra or reordered draw taken at or before the last
/// consumer cascades through every later draw: names, birth dates, agreements, leavers, the manager
/// set, the activity subset, absence types and days, period outcomes, vikar reasons, messy cases.
/// All of those are in the compared bytes, so these pins go red on any such shift. Verified by
/// probe, not assumed: a draw injected before user generation reddens BOTH pins; one injected
/// between the profile and activity passes reddens the manifest pin.</para>
///
/// <para><b>The limit, stated:</b> a draw taken AFTER the last existing consumer perturbs nothing
/// and these pins stay green — verified by probe too. That is not a hole in the argument, because
/// the S127 fill sits exactly there (step 3c): the pins therefore certify that everything drawn
/// BEFORE it is untouched, which is the whole population. What guards the position itself is the
/// call site's placement as a post-pass, and the fact that
/// <see cref="AllocatedMonthBuilder"/> holds no <see cref="Random"/> reference at all.</para>
///
/// <para>These pins must NOT be regenerated. If a future sprint changes the generated world on
/// purpose, delete them and capture a fresh pair against that sprint's own baseline — a
/// regenerated "unchanged" pin is a contradiction.</para>
/// </summary>
public sealed class LegacyUnchangedByS127Tests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    [Fact]
    public void Sql_MinusTheS127ProjectsBlock_ReproducesThePreS127Golden()
    {
        var dataset = new DemoGenerator(GoldenLegacyPinTests.LegacySmokeClone(), 42, Ref).Generate();
        var current = GoldenLegacyPinTests.Normalize(SqlEmitter.Emit(dataset));
        var stripped = StripProjectsSection(current);

        var preS127 = GoldenLegacyPinTests.Normalize(
            File.ReadAllText(GoldenLegacyPinTests.GoldenPath("pre-s127-legacy-smoke.sql")));

        Assert.Equal(preS127, stripped);
    }

    [Fact]
    public void Manifest_MinusTheS127AllocatedMonths_ReproducesThePreS127Golden()
    {
        var dataset = new DemoGenerator(GoldenLegacyPinTests.LegacySmokeClone(), 42, Ref).Generate();

        // Sanity: the fill actually ran. Without this the stripping below would be vacuous and the
        // comparison would pass for the wrong reason.
        Assert.Contains(dataset.Manifest.Activity, a => a.Allocations is { Count: > 0 });
        Assert.Contains(dataset.Manifest.Activity, a => a.WorkTime is { Count: > 0 });

        foreach (var activity in dataset.Manifest.Activity)
        {
            activity.Allocations = null; // null ⇒ the key is omitted (WhenWritingNull)
            activity.WorkTime = null;
        }

        var stripped = GoldenLegacyPinTests.Normalize(
            JsonSerializer.Serialize(dataset.Manifest, DemoManifestJsonContext.Default.DemoManifest));
        var preS127 = GoldenLegacyPinTests.Normalize(
            File.ReadAllText(GoldenLegacyPinTests.GoldenPath("pre-s127-legacy-smoke.manifest.json")));

        Assert.Equal(preS127, stripped);
    }

    /// <summary>
    /// Removes the two S127 additions to the emitted SQL — the header's <c>projects=N</c> counter and
    /// the whole projects section (its leading comment block, the INSERT, its terminator and the
    /// blank line that follows). Located structurally (from the INSERT outwards) rather than by
    /// matching comment prose, so re-wording a comment does not silently turn this into a no-op.
    /// </summary>
    private static string StripProjectsSection(string sql)
    {
        var lines = sql.Split('\n').ToList();

        var insertIndex = lines.FindIndex(l => l.StartsWith("INSERT INTO projects ", StringComparison.Ordinal));
        Assert.True(insertIndex >= 0, "the emitted SQL carries no projects INSERT — the S127 emission is missing");

        var start = insertIndex;
        while (start > 0 && lines[start - 1].StartsWith("--", StringComparison.Ordinal))
            start--;

        var end = insertIndex;
        while (end < lines.Count && lines[end] != "ON CONFLICT DO NOTHING;")
            end++;
        Assert.True(end < lines.Count, "the projects INSERT has no ON CONFLICT terminator");

        // Swallow the single blank separator line the emitter writes after each section.
        if (end + 1 < lines.Count && lines[end + 1].Length == 0)
            end++;

        lines.RemoveRange(start, end - start + 1);

        var rebuilt = string.Join("\n", lines);
        return Regex.Replace(rebuilt, @"  projects=\d+", "");
    }
}
