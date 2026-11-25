using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S148 / TASK-14839-T (AC-C12a, the added rows) — the three config-stage sections of the demo SQL hold exactly the rows
/// the S148 rulings name: one local profile per organisation, agreement and OK version; three example customs per
/// organisation; one HR-facts row per user the file inserts.
///
/// <para><b>Why it matters, in plain words.</b> The demo screens for local profiles, customary days and HR facts are only
/// useful if the example rows are the right ones, and the pay pin (PIN-1) is only meaningful if the profile leaves every
/// pay-relevant column empty.</para>
///
/// <para><b>Oracle.</b> Every expected value is a literal in this file (the regime table, the 37, the dates, the column
/// lists), derived from the rulings and from ADR-024's role rows, never read from the generator's own mapping or constants.
/// Every fact reads the rows from <see cref="SqlEmitter.Emit"/>, the file the demo stack loads.</para>
///
/// <para><b>Kind: F</b> for every fact. <b>Product code between the readings:</b> the generator and the emitter.
/// <b>Red conditions</b> (each on the base of TASK-14839, where no S148 section exists): the section lookup finds no
/// INSERT and the first row assertion fails.</para>
/// </summary>
public sealed class ConfigStageSeedingTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    private const string ProfilesInsert =
        "INSERT INTO local_agreement_profiles (org_id, agreement_code, ok_version, effective_from, weekly_norm_hours, max_flex_balance, max_overtime_hours_per_period, overtime_requires_pre_approval, flex_plus_cap_hours, created_by) VALUES";

    private static DemoDataset Gen(string scale, int seed = 42, DateOnly? reference = null)
        => new DemoGenerator(scale, seed, reference ?? Ref).Generate();

    /// <summary>The rows of the section whose INSERT line starts with <paramref name="insertPrefix"/>: the lines between the
    /// INSERT and the terminator, each without its indent and trailing comma. Fails when the section is absent.</summary>
    private static List<string> RowsOf(string sql, string insertPrefix)
    {
        var lines = GoldenLegacyPinTests.Normalize(sql).Split('\n').ToList();
        var insert = lines.FindIndex(l => l.StartsWith(insertPrefix, StringComparison.Ordinal));
        Assert.True(insert >= 0, $"the emitted SQL carries no '{insertPrefix.Trim()}' section");

        var rows = new List<string>();
        for (var i = insert + 1; i < lines.Count && lines[i] != "ON CONFLICT DO NOTHING;"; i++)
            rows.Add(lines[i].Trim().TrimEnd(','));
        return rows;
    }

    // ── (i) HR facts ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The oracle: ADR-024's role rows and the HK/PROSA overtime rule, written here. A pair outside it fails.</summary>
    private static string? ExpectedRegime(string agreement, string category) => agreement switch
    {
        "HK" or "PROSA" => "OVERTIME",
        "AC" => category switch
        {
            "Fuldmaegtig" => "CONTRACTUAL",
            "Specialkonsulent" => "DISCRETIONARY",
            "Chefkonsulent" or "Kontorchef" => "NONE",
            "Standard" => null,
            _ => throw new InvalidOperationException($"the oracle has no regime for AC + '{category}'"),
        },
        _ => throw new InvalidOperationException($"the oracle has no regime for agreement '{agreement}'"),
    };

    [Theory]
    [InlineData("smoke", 31)]
    [InlineData("full", 3351)]
    public void EveryEmittedUser_HasOneHrFactsRow_FromAgreementAndCategory(string scale, int expectedRows)
    {
        var ds = Gen(scale);
        var rows = RowsOf(SqlEmitter.Emit(ds), "INSERT INTO employee_hr_facts ");

        // demo_admin is not in ds.Users (the emitter writes it separately): AC + Kontorchef, as literals.
        var expected = new List<string> { "('demo_admin', 'NONE', TRUE, DATE '0001-01-01')" };
        foreach (var u in ds.Users)
        {
            var regime = ExpectedRegime(u.AgreementCode, u.EmploymentCategory);
            expected.Add($"('{u.UserId}', {(regime is null ? "NULL" : $"'{regime}'")}, TRUE, DATE '0001-01-01')");
        }

        Assert.Equal(expectedRows, ds.Users.Count + 1);
        Assert.Equal(expectedRows, rows.Count);
        Assert.Equal(expected, rows);
        Assert.Equal(rows.Count, rows.Select(r => r.Split(',')[0]).Distinct(StringComparer.Ordinal).Count());
    }

    // ── (ii) profiles ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("smoke", 12)]
    [InlineData("full", 36)]
    public void EveryOrgAgreementOkVersion_HasOneProfile_PlusCap37_LegacyNull(string scale, int expectedRows)
    {
        var ds = Gen(scale);
        var sql = SqlEmitter.Emit(ds);

        // The INSERT line names exactly the ten columns, the four legacy ones among them (written out as NULL).
        Assert.Contains(ProfilesInsert + "\n", GoldenLegacyPinTests.Normalize(sql));

        var expected = new List<string>();
        foreach (var org in ds.Orgs)
            foreach (var agreement in new[] { "AC", "HK", "PROSA" })
                foreach (var ok in new[] { "OK24", "OK26" })
                    expected.Add($"('{org.OrgId}', '{agreement}', '{ok}', DATE '0001-01-01', NULL, NULL, NULL, NULL, 37, 'DEMO_SEED')");

        var rows = RowsOf(sql, "INSERT INTO local_agreement_profiles ");
        Assert.Equal(expectedRows, expected.Count);
        Assert.Equal(expected, rows);

        // 0001-01-01 is a Monday, and it is on or before the first day of the activity month (1 May 2026).
        Assert.Equal(DayOfWeek.Monday, new DateOnly(1, 1, 1).DayOfWeek);
        Assert.True(new DateOnly(1, 1, 1) <= new DateOnly(2026, 5, 1));
    }

    // ── (iii) customs ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("smoke", 6)]
    [InlineData("full", 18)]
    public void EveryOrg_HasTheThreeExampleCustoms_NoTwoShareADayAndScope(string scale, int expectedRows)
    {
        // Activity month May 2026: Ascension Day is Thursday 14 May 2026, so the closure day is Friday 15 May 2026.
        AssertCustoms(Gen(scale), expectedRows, closureDay: "2026-05-15", validFrom: "2026-01-01");
        Assert.Equal(DayOfWeek.Thursday, new DateOnly(2026, 5, 14).DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, new DateOnly(2026, 5, 15).DayOfWeek);
    }

    [Fact]
    public void EveryOrg_HasTheThreeExampleCustoms_InJanuaryTheYearIsTheActivityMonthsNotTheReferenceDates()
    {
        // Reference Thursday 15 January 2026: the activity month is December 2025, so the year is 2025. Ascension Day 2025 is
        // Thursday 29 May, the closure day Friday 30 May 2025, and valid_from is Wednesday 1 January 2025. A year taken
        // from the reference date (2026) would write 2026-05-15 and 2026-01-01.
        var ds = Gen("smoke", 42, new DateOnly(2026, 1, 15));
        AssertCustoms(ds, 6, closureDay: "2025-05-30", validFrom: "2025-01-01");
        Assert.Equal(DayOfWeek.Friday, new DateOnly(2025, 5, 30).DayOfWeek);
        Assert.Equal(DayOfWeek.Wednesday, new DateOnly(2025, 1, 1).DayOfWeek);
    }

    private static void AssertCustoms(DemoDataset ds, int expectedRows, string closureDay, string validFrom)
    {
        var rows = RowsOf(SqlEmitter.Emit(ds), "INSERT INTO customary_days ");
        Assert.Equal(expectedRows, ds.Orgs.Count * 3);
        Assert.Equal(expectedRows, rows.Count);

        // A row is "(<custom_id expression>, <rest>)". The id's text is not pinned (ruling 8 asks only for a pure
        // function of the organisation and the day), so it is split off; the rest is compared literally.
        var parsed = rows.Select(r =>
        {
            var rest = r.IndexOf(", '", StringComparison.Ordinal);
            Assert.True(rest > 0, $"unparseable customs row: {r}");
            return (IdExpr: r[1..rest], Tail: r[(rest + 2)..^1]);
        }).ToList();

        var remaining = parsed.Select(p => p.Tail).ToList();
        foreach (var org in ds.Orgs)
        {
            var o = org.OrgId;
            foreach (var expected in new[]
            {
                $"'{o}', NULL, 6, 5, NULL, 'PAID_DAY_OFF', 'WHOLE_DAY', 'GIVEN_TO_ALL', DATE '{validFrom}', 'DEMO_SEED'",
                $"'{o}', NULL, 12, 24, NULL, 'PAID_DAY_OFF', 'WHOLE_DAY', 'GIVEN_TO_ALL', DATE '{validFrom}', 'DEMO_SEED'",
                $"'{o}', NULL, NULL, NULL, DATE '{closureDay}', 'CLOSURE_DAY', 'WHOLE_DAY', NULL, DATE '{validFrom}', 'DEMO_SEED'",
            })
            {
                Assert.True(remaining.Remove(expected), $"org {o}: no customs row '{expected}'");
            }
        }
        Assert.Empty(remaining); // exactly three per org, nothing else

        // No two rows share an id (the day and scope are distinct by the rows above; the id is the table's only key).
        Assert.Equal(parsed.Count, parsed.Select(p => p.IdExpr).Distinct(StringComparer.Ordinal).Count());
    }

    // ── (iv) seed independence ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProfilesAndCustoms_AreTheSameForEverySeed()
    {
        // Profiles and customs are drawn from nothing but the organisations and the date. The HR facts are a function of
        // each person's drawn agreement and category, and are covered by the first fact.
        var a = SqlEmitter.Emit(Gen("smoke", 42));
        var b = SqlEmitter.Emit(Gen("smoke", 7));

        foreach (var insert in new[] { "INSERT INTO local_agreement_profiles ", "INSERT INTO customary_days " })
        {
            var rowsA = RowsOf(a, insert);
            var rowsB = RowsOf(b, insert);
            Assert.NotEmpty(rowsA);
            Assert.Equal(rowsA, rowsB); // the custom_id expressions are part of the rows
        }
    }
}
