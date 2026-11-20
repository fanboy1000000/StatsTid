using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using Testcontainers.PostgreSql;
using static StatsTid.SharedKernel.Config.PositionOverrideConfigs;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-2), re-pointed by TASK-14825-T (N-4; B14) — the configuration rows init.sql seeds equal the
/// rules compiled into the code, read the way payroll will read them.
///
/// <para>
/// <b>Why, in plain language.</b> PIN-1 sees only what the narrow demo touches (one month per person, Skema hours only:
/// no overtime tiers, no payout above a cap). When payroll later reads its configuration from the database instead of
/// from the code (the engine switch, T14b), any rate the demo never exercises could change silently if the two
/// disagreed. This fact closes that gap: for every compiled (agreement, OK version) and every compiled (agreement,
/// OK version, position), the configuration the dated, fail-closed lookup (<see cref="ConfigResolutionService"/>)
/// resolves from the reseeded database must equal the compiled value, member by member.
/// </para>
///
/// <para>
/// <b>How the keys are enumerated.</b> Both compiled tables are private and nothing public lists their keys, so the two
/// private dictionaries (<c>CentralAgreementConfigs.Configs</c>, <c>PositionOverrideConfigs.Overrides</c>) are read by
/// reflection; a missing field fails the fact loudly, and a key added later is seen without editing this file.
/// </para>
///
/// <para>
/// <b>What it reads, and on which days.</b> The lookup is the real <c>ConfigResolutionService</c> over the container's
/// database, asked as of a real day: <b>Thursday 1 January 2026 for an OK24 key, Wednesday 1 July 2026 for an OK26 key</b>
/// (the days AC-C2n uses). A key the lookup refuses ("no configuration in force", or a failed read) is a listed
/// difference naming the key and the day, never an unhandled exception. The fresh database holds no
/// <c>local_agreement_profiles</c> row (asserted first), so the lookup's local layer is empty by construction.
/// </para>
///
/// <para>
/// <b>How they are compared.</b> <b>The agreement arm:</b> the full chain with no position, compared with
/// <c>CentralAgreementConfigs.GetConfig</c>, every public member of <see cref="AgreementRuleConfig"/>. <b>The position
/// arm, both arms (B14):</b> (i) <i>the merged arm</i>: the full chain WITH the position, compared with
/// <c>ApplyOverride(CentralAgreementConfigs.GetConfig(…), TryGetOverride(…))</c>, every member of
/// <see cref="AgreementRuleConfig"/>: it pins what payroll will receive; <c>ApplyOverride</c> runs on both sides here,
/// so QUAL-190's field loss would hide on both. (ii) <i>the record arm</i>: the override record read through
/// <c>PositionOverrideRepository.GetInForceAsync</c> (the dated read the lookup composes) compared with the compiled
/// override record, its four members, so neither side of this arm runs <c>ApplyOverride</c> and the pin stays
/// independent of QUAL-190. One failure message lists every difference.
/// </para>
///
/// <para>
/// <b>Kind: R</b> (a regression pin: the seeds equal the code today and must stay so through the engine switch),
/// Docker-gated (<c>[Trait("Category","Docker")]</c>): <b>CI-pending</b>, never locally green. <b>Red conditions</b>
/// (argued, not run locally): a compiled key the lookup refuses on its day (no published, dated version in force), or
/// any member, in either arm, where the resolved value and the compiled one differ.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigSeedParityTests
{
    /// <summary>An organisation with no local agreement profile on the fresh database.</summary>
    private const string OrgWithoutProfile = "STY01";

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
        var overrides = new PositionOverrideRepository(db);
        var lookup = new ConfigResolutionService(
            new AgreementConfigRepository(db),
            new LocalConfigurationRepository(db),
            overrides,
            new LocalAgreementProfileRepository(db),
            NullLogger<ConfigResolutionService>.Instance);

        // The local layer is empty by construction: no profile row exists on the fresh database.
        await using (var conn = new NpgsqlConnection(container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM local_agreement_profiles", conn);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }

        var differences = new List<string>();
        var configMembers = typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var overrideMembers = typeof(PositionConfigOverride).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var key in agreementKeys)
        {
            var day = DayFor(key[1]);
            var label = $"{key[0]}/{key[1]} on {day:yyyy-MM-dd}";
            var (resolved, refusal) = await ResolveAsync(lookup, key[0], key[1], null, day);
            if (resolved is null)
            {
                differences.Add($"{label}: {refusal}");
                continue;
            }
            Compare(label, CentralAgreementConfigs.GetConfig(key[0], key[1]), resolved, configMembers, differences);
        }

        foreach (var key in overrideKeys)
        {
            var day = DayFor(key[1]);
            var label = $"{key[0]}/{key[1]}/{key[2]} on {day:yyyy-MM-dd}";
            var compiledOverride = TryGetOverride(key[0], key[1], key[2]);
            if (compiledOverride is null)
            {
                differences.Add($"{label}: the compiled override vanished between enumeration and lookup");
                continue;
            }

            // S148 N-4 (TASK-14825; B14): the merged arm compares what the lookup hands on; the record arm keeps
            // the comparison independent of QUAL-190.
            // (i) the merged arm: the chain with the position against the compiled merge.
            var (merged, refusal) = await ResolveAsync(lookup, key[0], key[1], key[2], day);
            if (merged is null)
                differences.Add($"{label} (merged): {refusal}");
            else
                Compare($"{label} (merged)",
                    ApplyOverride(CentralAgreementConfigs.GetConfig(key[0], key[1]), compiledOverride),
                    merged, configMembers, differences);

            // (ii) the record arm: the dated override read field by field, no ApplyOverride on either side.
            var record = await ReadOverrideRecordAsync(overrides, key[0], key[1], key[2], day);
            if (record is null)
                differences.Add($"{label} (record): no ACTIVE position override in force");
            else
                Compare($"{label} (record)", compiledOverride, record, overrideMembers, differences);
        }

        Assert.True(differences.Count == 0,
            $"{differences.Count} difference(s) between the compiled configs ({agreementKeys.Count} agreement/OK keys, " +
            $"{overrideKeys.Count} position keys) and the dated lookup over the database:\n" + string.Join("\n", differences));
    }

    // ── The reads: the only place that knows where the database value comes from. ──

    /// <summary>Thursday 1 January 2026 for an OK24 key, Wednesday 1 July 2026 for an OK26 key (AC-C2n's days).</summary>
    private static DateOnly DayFor(string okVersion) =>
        okVersion == "OK24" ? new DateOnly(2026, 1, 1) : new DateOnly(2026, 7, 1);

    /// <summary>The full chain as of <paramref name="day"/>; a refusal is returned as text, never thrown.</summary>
    private static async Task<(AgreementRuleConfig? Config, string? Refusal)> ResolveAsync(
        IAgreementConfigLookup lookup, string agreementCode, string okVersion, string? position, DateOnly day)
    {
        try
        {
            return (await lookup.ResolveAsync(OrgWithoutProfile, agreementCode, okVersion, position, day), null);
        }
        catch (NoConfigInForceException)
        {
            return (null, "no configuration in force");
        }
        catch (ConfigLookupUnavailableException exception)
        {
            return (null, $"the configuration read failed: {exception.Message}");
        }
    }

    private static async Task<PositionConfigOverride?> ReadOverrideRecordAsync(
        PositionOverrideRepository repository, string agreementCode, string okVersion, string position, DateOnly day) =>
        (await repository.GetInForceAsync(agreementCode, okVersion, position, day))?.ToPositionConfigOverride();

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
