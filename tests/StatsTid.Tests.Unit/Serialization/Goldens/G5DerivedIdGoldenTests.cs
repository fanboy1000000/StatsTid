using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// G-5 (TASK-14900, ADR-045) — the two ids that are derived by hashing text.
///
/// <para>
/// <b>Plain language.</b> Two ids are not random: an entitlement configuration's <c>ConfigId</c> (seeded into the
/// database, so it must be the same on every machine and every year) and the id that links the two halves of a
/// night shift split at midnight when the source row carries none. Each is the SHA-256 of a formatted string, so a
/// change in how a number or date is formatted would give a different id. <c>G5-ids.golden</c> holds the
/// vacation <c>ConfigId</c> for HK under OK24, and the stint id both halves of a 22:00 to 06:00 entry
/// (8.0 hours, no source stint id) carry. Reached through the public paths only.
/// </para>
///
/// <para>
/// <b>Kind: R.</b> Red conditions: a different input text to either hash fails its literal. Proved by the
/// mutations M-11 (the config id's input loses its <c>entitlement:</c> prefix) and M-10 (the stint key formats the
/// hours as <c>F2</c>) at the wave gate.
/// </para>
/// </summary>
public sealed class G5DerivedIdGoldenTests
{
    private const string File = "G5-ids.golden";

    [Fact]
    public void The_vacation_config_id_and_both_stint_ids_equal_their_literals()
    {
        var vacation = DefaultEntitlementConfigs.GetConfigsForAgreement("HK", "OK24")
            .Single(c => c.EntitlementType == "VACATION");

        var crossing = new TimeEntry
        {
            EmployeeId = "EMP-0001",
            Date = new DateOnly(2026, 3, 2),
            Hours = 8.0m,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0),
            AgreementCode = "HK",
            OkVersion = "OK24",
            SourceStintId = null,
        };
        var (pre, post) = MidnightCrossingNormalizer.Split(crossing);

        GoldenFile.AssertMatches(File, new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["1.configId.HK.OK24.VACATION"] = vacation.ConfigId.ToString(),
            ["2.stintId.pre"] = pre.SourceStintId!.Value.ToString(),
            ["3.stintId.post"] = post.SourceStintId!.Value.ToString(),
        });
    }

    /// <summary>Capture sanity: reads only the data file, so it is not a pin.</summary>
    [Fact]
    public void Capture_sanity_both_halves_of_the_crossing_carry_the_same_stint_id_literal()
    {
        var golden = GoldenFile.Load(File);
        Assert.Equal(golden["2.stintId.pre"], golden["3.stintId.post"]);
    }
}
