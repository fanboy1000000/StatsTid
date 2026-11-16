using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using StatsTid.SharedKernel.Config;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — AC-C2n: on a fresh database every compiled (agreement, OK version) has a published
/// version in force from <c>0001-01-01</c>. Plain language: the seeded configurations must be dated and
/// published, otherwise "which version applied on day X?" would answer "none" for an agreement the system
/// ships with. PIN-2 re-asserts this field by field after TASK-14825; this fact reads only the dated read.
/// Docker-gated: CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SeededConfigsInForceTests : DatedConfigDockerFixture
{
    /// <summary>
    /// AC-C2n. For every compiled (agreement, OK version) key (reflection on
    /// <c>CentralAgreementConfigs.Configs</c>, as PIN-2 enumerates it; at least 10 keys, non-vacuity asserted):
    /// the in-force read on Thursday 1 January 2026 (OK24 keys) or Wednesday 1 July 2026 (OK26 keys) returns a
    /// version dated <c>0001-01-01</c> with <c>published_at</c> set. The failure message lists every failing key.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-10)</b>: it names <see cref="StatsTid.Infrastructure.AgreementConfigRepository.GetInForceAsync"/>.
    /// <b>Red conditions</b> (M-10, the seed path writes no <c>published_at</c>): the dated read requires
    /// <c>published_at IS NOT NULL</c>, so every key returns no version and the listed-failures assertion trips.
    /// This is the only fact of the wave that reads a seeded configuration through the in-force read (the
    /// others publish their own versions under test-unique codes), so M-10's frozen list for this task is this
    /// fact alone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EveryCompiledAgreementOk_HasPublishedVersionInForceFrom00010101()
    {
        var keys = CompiledKeys(typeof(CentralAgreementConfigs), "Configs", 2);
        Assert.True(keys.Count >= 10, $"expected at least 10 compiled (agreement, OK version) keys, found {keys.Count}");

        var failures = new List<string>();
        foreach (var key in keys)
        {
            var day = key[1] == "OK24" ? new DateOnly(2026, 1, 1) : new DateOnly(2026, 7, 1);
            var row = await ConfigRepo.GetInForceAsync(key[0], key[1], day);
            if (row is null)
                failures.Add($"{key[0]}/{key[1]}: no published version in force on {day:yyyy-MM-dd}");
            else if (row.AppliesFrom != new DateOnly(1, 1, 1))
                failures.Add($"{key[0]}/{key[1]}: in force from {row.AppliesFrom:yyyy-MM-dd}, expected 0001-01-01");
            else if (row.PublishedAt is null)
                failures.Add($"{key[0]}/{key[1]}: published_at is not set");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {keys.Count} compiled keys lack a published version in force from 0001-01-01:\n"
            + string.Join("\n", failures));
    }

    /// <summary>The keys of a compiled private static dictionary, each as its tuple items (PIN-2's enumeration).</summary>
    private static List<string[]> CompiledKeys(Type owner, string fieldName, int arity)
    {
        var field = owner.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null, $"{owner.Name} no longer has a private static field '{fieldName}'");
        var table = field!.GetValue(null) as IDictionary;
        Assert.True(table is not null, $"{owner.Name}.{fieldName} is not a dictionary");

        var keys = new List<string[]>();
        foreach (var key in table!.Keys)
        {
            var tuple = (ITuple)key;
            Assert.Equal(arity, tuple.Length);
            keys.Add(Enumerable.Range(0, arity).Select(i => (string)tuple[i]!).ToArray());
        }
        return keys;
    }
}
