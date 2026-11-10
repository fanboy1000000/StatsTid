using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using Testcontainers.PostgreSql;
using static StatsTid.SharedKernel.Config.PositionOverrideConfigs;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-2) — the configuration rows init.sql seeds equal the rules compiled into the code.
///
/// <para>
/// <b>Why, in plain language.</b> PIN-1 sees only what the narrow demo touches (one month per person, Skema hours only:
/// no overtime tiers, no payout above a cap). When payroll later reads its configuration from the database instead of
/// from the code, any rate the demo never exercises could change silently if the two disagreed. This fact closes that
/// gap now, on the base: for every compiled (agreement, OK version) and every compiled (agreement, OK version,
/// position) the database row must exist and equal the compiled value, member by member.
/// </para>
///
/// <para>
/// <b>How the keys are enumerated.</b> Both compiled tables are private and nothing public lists their keys, so the two
/// private dictionaries (<c>CentralAgreementConfigs.Configs</c>, <c>PositionOverrideConfigs.Overrides</c>) are read by
/// reflection; a missing field fails the fact loudly, and a key added later is seen without editing this file.
/// </para>
///
/// <para>
/// <b>How they are compared.</b> The base config is compared with the base config (every public member of
/// <see cref="AgreementRuleConfig"/>), and the position override record with the override record (its four members),
/// each on its own, so neither side runs through <see cref="PositionOverrideConfigs.ApplyOverride"/> and the fact stays
/// independent of QUAL-190. The database rows are read through the repositories, never through
/// <c>ConfigResolutionService</c>, which falls back to the compiled tables on a missing row and could compare compiled
/// with compiled (BC-P8); each row's presence is asserted before it is compared. The two <c>Read…</c> methods are the
/// only code that knows where the database value comes from: re-pointing the fact to a dated, fail-closed lookup changes
/// those reads, not the comparison.
/// </para>
///
/// <para>
/// <b>Kind: R</b> (a regression pin: the seeds equal the code today and must after the config stage), Docker-gated
/// (<c>[Trait("Category","Docker")]</c>): <b>CI-pending</b>, never locally green. <b>Red conditions</b> (argued, not run
/// locally): a compiled key with no ACTIVE database row, or any member where the row and the compiled value differ.
/// Expected green at CI-W0.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigSeedParityTests
{
    [Fact]
    public async Task EveryCompiledAgreementOkPosition_DatabaseConfigEqualsCompiled()
    {
        var agreementKeys = CompiledKeys(typeof(CentralAgreementConfigs), "Configs", 2);
        var overrideKeys = CompiledKeys(typeof(PositionOverrideConfigs), "Overrides", 3);
        Assert.NotEmpty(agreementKeys);
        Assert.NotEmpty(overrideKeys);

        await using var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await container.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(container.GetConnectionString());

        var db = new DbConnectionFactory(container.GetConnectionString());
        var agreements = new AgreementConfigRepository(db);
        var overrides = new PositionOverrideRepository(db);

        var differences = new List<string>();
        var configMembers = typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var overrideMembers = typeof(PositionConfigOverride).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var key in agreementKeys)
        {
            var label = $"{key[0]}/{key[1]}";
            var row = await ReadConfigAsync(agreements, key[0], key[1]);
            if (row is null)
            {
                differences.Add($"{label}: no ACTIVE agreement_configs row");
                continue;
            }
            Compare(label, CentralAgreementConfigs.GetConfig(key[0], key[1]), row, configMembers, differences);
        }

        foreach (var key in overrideKeys)
        {
            var label = $"{key[0]}/{key[1]}/{key[2]}";
            var row = await ReadOverrideAsync(overrides, key[0], key[1], key[2]);
            if (row is null)
            {
                differences.Add($"{label}: no ACTIVE position_override_configs row");
                continue;
            }
            var compiled = TryGetOverride(key[0], key[1], key[2]);
            if (compiled is null)
            {
                differences.Add($"{label}: the compiled override vanished between enumeration and lookup");
                continue;
            }
            Compare(label, compiled, row, overrideMembers, differences);
        }

        Assert.True(differences.Count == 0,
            $"{differences.Count} difference(s) between the compiled configs ({agreementKeys.Count} agreement/OK keys, " +
            $"{overrideKeys.Count} position keys) and the database:\n" + string.Join("\n", differences));
    }

    // ── The reads: the only place that knows where the database value comes from. ──

    private static async Task<AgreementRuleConfig?> ReadConfigAsync(
        AgreementConfigRepository repository, string agreementCode, string okVersion) =>
        (await repository.GetActiveAsync(agreementCode, okVersion))?.ToRuleConfig();

    private static async Task<PositionConfigOverride?> ReadOverrideAsync(
        PositionOverrideRepository repository, string agreementCode, string okVersion, string position) =>
        (await repository.GetActiveAsync(agreementCode, okVersion, position))?.ToPositionConfigOverride();

    // ── The comparison. ──

    private static void Compare(
        string label, object compiled, object database, PropertyInfo[] members, List<string> differences)
    {
        foreach (var member in members)
        {
            var expected = member.GetValue(compiled);
            var actual = member.GetValue(database);
            if (!Equals(expected, actual))
                differences.Add($"{label}: {member.Name}: compiled {expected ?? "null"}, database {actual ?? "null"}");
        }
    }

    /// <summary>The keys of a compiled private static dictionary, each as its tuple items.</summary>
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
