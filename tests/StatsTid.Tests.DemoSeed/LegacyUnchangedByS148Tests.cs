using StatsTid.Tools.DemoSeed.Generation;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S148 / TASK-14839-T (AC-C12a) — the evidence that the three S148 sections are the ONLY change to the demo SQL.
///
/// <para><see cref="GoldenLegacyPinTests"/> compares the generator against a golden that S148 REGENERATED (the three
/// added sections are in it), so it can no longer testify that S148 left the existing world alone.
/// <c>Golden/pre-s148-legacy-smoke.sql</c> is the golden exactly as it stood before S148 (the bytes of
/// <c>golden-legacy-smoke.sql</c> at the master TASK-14839 started from, unchanged). This fact removes the three
/// S148 sections from the current output and requires the remainder to equal those bytes: so no existing line moved,
/// nothing was removed, and the header is untouched. The manifest is pinned by
/// <see cref="GoldenLegacyPinTests.NoOverrideConfig_Manifest_IsByteIdenticalToPreChangeGolden"/>, whose golden did not move.</para>
///
/// <para><b>What it proves, and its limit.</b> The generator draws from one seeded <see cref="Random"/> in a fixed order,
/// so a draw added at or before the last existing consumer shifts every later value and the SQL and the manifest both go
/// red. The limit is the one <see cref="LegacyUnchangedByS127Tests"/> states: a draw taken AFTER the last consumer would
/// change nothing, so what keeps the S148 derivation draw-free is its placement as a post-pass and TASK-14839's grep gate.
/// These pins must NOT be regenerated.</para>
///
/// <para><b>Product code between the readings:</b> the generator and the emitter, nothing else.</para>
///
/// <para><b>Kind: F</b> (AC-C12a, "the golden SQL changes only by the added rows"). <b>Red conditions:</b> on the base
/// of TASK-14839 no <c>INSERT INTO local_agreement_profiles</c> exists, so the presence assertion fails with "the S148
/// profiles emission is missing"; afterwards any existing line that moves makes the final <c>Assert.Equal</c> fail.</para>
/// </summary>
public sealed class LegacyUnchangedByS148Tests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    /// <summary>The three S148 sections, in emission order: the INSERT each starts with, and a label for the failure.</summary>
    private static readonly (string InsertPrefix, string Kind)[] Sections =
    {
        ("INSERT INTO local_agreement_profiles ", "profiles"),
        ("INSERT INTO customary_days ", "customs"),
        ("INSERT INTO employee_hr_facts ", "HR facts"),
    };

    [Fact]
    public void Sql_MinusTheS148Sections_ReproducesThePreS148Golden()
    {
        var dataset = new DemoGenerator(GoldenLegacyPinTests.LegacySmokeClone(), 42, Ref).Generate();
        var current = GoldenLegacyPinTests.Normalize(SqlEmitter.Emit(dataset));
        var stripped = StripS148Sections(current, requireEachSection: true);

        var preS148 = GoldenLegacyPinTests.Normalize(
            File.ReadAllText(GoldenLegacyPinTests.GoldenPath("pre-s148-legacy-smoke.sql")));

        Assert.Equal(preS148, stripped);
    }

    /// <summary>
    /// Removes the three S148 sections: for each, from its INSERT outward, the leading comment lines, the rows up to the
    /// <c>ON CONFLICT DO NOTHING;</c> terminator and the one blank line after it. Found structurally, not by comment
    /// prose. Shared with <see cref="LegacyUnchangedByS127Tests"/>: <paramref name="requireEachSection"/> makes a missing
    /// section fail (this class), or lets it pass (the S127 pin, which only needs the sections gone when present).
    /// </summary>
    internal static string StripS148Sections(string sql, bool requireEachSection)
    {
        var lines = sql.Split('\n').ToList();

        foreach (var (insertPrefix, kind) in Sections)
        {
            var insertIndex = lines.FindIndex(l => l.StartsWith(insertPrefix, StringComparison.Ordinal));
            if (insertIndex < 0)
            {
                Assert.False(requireEachSection, $"the S148 {kind} emission is missing");
                continue;
            }

            var start = insertIndex;
            while (start > 0 && lines[start - 1].StartsWith("--", StringComparison.Ordinal))
                start--;

            var end = insertIndex;
            while (end < lines.Count && lines[end] != "ON CONFLICT DO NOTHING;")
                end++;
            Assert.True(end < lines.Count, $"the S148 {kind} INSERT has no ON CONFLICT terminator");

            if (end + 1 < lines.Count && lines[end + 1].Length == 0)
                end++;

            lines.RemoveRange(start, end - start + 1);
        }

        return string.Join("\n", lines);
    }
}
