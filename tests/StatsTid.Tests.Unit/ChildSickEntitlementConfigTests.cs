using System.Text.RegularExpressions;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2a. In plain terms: how many sick days a child's illness gives, and whether the
/// "call-home" day exists, depends on the OK version (the agreement round the employee falls under on the day), and
/// two sources must say the same thing: the database seeds (<c>docker/postgres/init.sql</c>) and the compiled defaults
/// (<see cref="DefaultEntitlementConfigs"/>, ADR-014).
///
/// <para>
/// <b>Expected values come from the agreement texts, not from the code:</b> under OK24 a child's illness gives 2 sick
/// days for every agreement (CIR1H 9333 § 18 stk. 6; 029-24 § 14; 012-25 § 14); from 1 April 2026 (OK26) it gives 3
/// plus the call-home day, the remaining hours of the day the child fell ill (021-26 and 020-26 § 14; for AC the
/// settlement item 4, its circular pending, so the OK26 rows of AC, AC_RESEARCH and AC_TEACHING are labelled
/// provisional "(foreløbig)"). The codes offered are day 1 up to min(whole quota, 3), with the call-home code first
/// when the OK version grants it. An OK version the rule does not name grants no call-home day.
/// </para>
///
/// <para>
/// <b>Kind F</b> (Unit). Red conditions: on the base (<c>6ec6a111</c>) AC's compiled OK24 default is 1 and the seeds give
/// AC 1 under both versions, PROSA 3 under OK24 and HK 2 under OK26 (<c>init.sql</c> and
/// <c>DefaultEntitlementConfigs.cs</c> there), so the day-count assertions in steps (1) and (2) fail on the first
/// disagreeing agreement. Steps (3) and (4) name members TASK-14811 created (<c>ChildSickCallHomeDay</c>,
/// <c>ChildSickCodesOffered</c>), so the base cannot compile them; the RED run on the base therefore ran steps (1)
/// and (2) alone (see the pass report). The Unit suite runs on every push, so a seed edited without its default, or
/// the reverse, turns this fact red.
/// </para>
/// </summary>
public class ChildSickEntitlementConfigTests
{
    private static readonly string[] Agreements = { "AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING" };
    private static readonly string[] CompiledAgreements = { "AC", "HK", "PROSA" };
    private static readonly string[] ProvisionalAgreements = { "AC", "AC_RESEARCH", "AC_TEACHING" };

    // One CHILD_SICK seed row per line: ('CHILD_SICK', '<agreement>', '<OK version>', <days>, 'IMMEDIATE', ..., '<description>', '0001-01-01', ...
    private static readonly Regex SeedRow = new(
        @"^\s*\('CHILD_SICK',\s*'(?<agreement>[A-Z_]+)',\s*'(?<ok>OK\d+)',\s*(?<days>\d+),\s*'IMMEDIATE'.*?'(?<description>[^']*)',\s*'0001-01-01'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Seed(int Days, string Description);

    [Fact]
    public void Ok24TwoDays_Ok26ThreeDaysPlusCallHome_AllFiveAgreements()
    {
        var seeds = ReadSeeds();

        // (1) init.sql: all five agreements, both versions, exactly one row each (ten rows).
        Assert.Equal(Agreements.Length * 2, seeds.Count);
        Assert.Multiple(Agreements.SelectMany(agreement => new[]
        {
            (Action)(() => Assert.Equal(2, seeds[(agreement, "OK24")].Days)),
            (Action)(() => Assert.Equal(3, seeds[(agreement, "OK26")].Days)),
            (Action)(() => Assert.Equal(
                ProvisionalAgreements.Contains(agreement), seeds[(agreement, "OK26")].Description.Contains("(foreløbig)"))),
            (Action)(() => Assert.DoesNotContain("(foreløbig)", seeds[(agreement, "OK24")].Description)),
        }).ToArray());

        // (2) the compiled defaults (AC, HK, PROSA) give the same days per illness and the same provisional label.
        Assert.Multiple(CompiledAgreements.SelectMany(agreement => new[] { "OK24", "OK26" }.Select(ok => (Action)(() =>
        {
            var config = DefaultEntitlementConfigs.GetConfigsForAgreement(agreement, ok)
                .Single(c => c.EntitlementType == "CHILD_SICK");
            var seed = seeds[(agreement, ok)];
            Assert.True(config.IsPerEpisode, $"{agreement}/{ok}: the child's-sick row counts per illness");
            Assert.Equal((decimal)seed.Days, config.AnnualQuota);
            Assert.Equal(seed.Description.Contains("(foreløbig)"), config.Description!.Contains("(foreløbig)"));
        }))).ToArray());

        // (3) the SharedKernel rule for the call-home day: OK24 no, OK26 yes, a version it does not name none.
        Assert.Multiple(
            () => Assert.False(DefaultEntitlementConfigs.ChildSickCallHomeDay("OK24")),
            () => Assert.True(DefaultEntitlementConfigs.ChildSickCallHomeDay("OK26")),
            () => Assert.False(DefaultEntitlementConfigs.ChildSickCallHomeDay("OK99")));

        // (4) the codes offered, from the seed's day count under that OK version.
        Assert.Multiple(Agreements.SelectMany(agreement => new[]
        {
            (Action)(() => Assert.Equal(
                new[] { "CHILD_SICK_DAY", "CHILD_SICK_DAY_2" },
                DefaultEntitlementConfigs.ChildSickCodesOffered(Config(agreement, "OK24", seeds[(agreement, "OK24")].Days)))),
            (Action)(() => Assert.Equal(
                new[] { "CHILD_SICK_CALL_HOME", "CHILD_SICK_DAY", "CHILD_SICK_DAY_2", "CHILD_SICK_DAY_3" },
                DefaultEntitlementConfigs.ChildSickCodesOffered(Config(agreement, "OK26", seeds[(agreement, "OK26")].Days)))),
        }).ToArray());
    }

    /// <summary>
    /// QUAL-236 (Fable 5a W-1), kind N (the set is a member TASK-14811 created, so the base cannot compile this and a base run is not RED; the RED is the named mutation below). <see cref="DefaultEntitlementConfigs.ChildSickCodes"/> is exactly the four child's
    /// sick-day codes. The per-code check, the HR opt-in gate and the catalogue filter all read this set, so a code missing
    /// from it would be skipped by all three (call-home hours postable without the opt-in, or on an OK24 date).
    /// Red conditions: set equality. Mutation (local census): remove <c>AbsenceTypes.ChildSickCallHome</c> from the set; the assertion fails with "ChildSickCodes was [CHILD_SICK_DAY, CHILD_SICK_DAY_2, CHILD_SICK_DAY_3]".
    /// </summary>
    [Fact]
    public void ChildSickCodes_AreTheThreeDayCodesAndCallHome()
    {
        Assert.True(
            new HashSet<string>(StringComparer.Ordinal)
                { "CHILD_SICK_DAY", "CHILD_SICK_DAY_2", "CHILD_SICK_DAY_3", "CHILD_SICK_CALL_HOME" }
                .SetEquals(DefaultEntitlementConfigs.ChildSickCodes),
            $"ChildSickCodes was [{string.Join(", ", DefaultEntitlementConfigs.ChildSickCodes.OrderBy(c => c, StringComparer.Ordinal))}]");
    }

    private static EntitlementConfig Config(string agreement, string okVersion, int days) => new()
    {
        ConfigId = Guid.NewGuid(),
        EntitlementType = "CHILD_SICK",
        AgreementCode = agreement,
        OkVersion = okVersion,
        AnnualQuota = days,
        AccrualModel = "IMMEDIATE",
        ResetMonth = 1,
        CarryoverMax = 0m,
        ProRateByPartTime = false,
        IsPerEpisode = true,
    };

    private static Dictionary<(string Agreement, string Ok), Seed> ReadSeeds()
    {
        var initSql = Path.Combine(LocateRepoRoot(), "docker", "postgres", "init.sql");
        var seeds = new Dictionary<(string, string), Seed>();
        foreach (var line in File.ReadLines(initSql))
        {
            var m = SeedRow.Match(line);
            if (!m.Success)
                continue;
            var key = (m.Groups["agreement"].Value, m.Groups["ok"].Value);
            Assert.False(seeds.ContainsKey(key), $"init.sql seeds CHILD_SICK twice for {key}");
            seeds[key] = new Seed(int.Parse(m.Groups["days"].Value), m.Groups["description"].Value);
        }
        return seeds;
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker", "postgres", "init.sql")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from AppContext.BaseDirectory='{AppContext.BaseDirectory}'.");
    }
}
