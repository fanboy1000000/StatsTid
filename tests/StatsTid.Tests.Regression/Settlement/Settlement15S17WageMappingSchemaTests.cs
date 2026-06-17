using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S80 / TASK-8003 (SPRINT-80 R7) — Docker-gated SCHEMA/seed assertions on the §15 stk.2/§17
/// SPECIAL_HOLIDAY godtgørelse placeholder wage-type mapping, mirroring
/// <see cref="Settlement26WageMappingSchemaTests"/> (the S69/S71 precedent the R7 seed reproduces):
/// <list type="bullet">
///   <item><c>SLS_TBD_S15S17</c> occurs ONLY for the new settlement <c>time_type</c>
///     (<c>SPECIAL_HOLIDAY_SETTLEMENT_PAYOUT</c>);</item>
///   <item>that <c>time_type</c> maps to NO non-sentinel wage type;</item>
///   <item>the godtgørelse mapping covers EXACTLY the consumed-VACATION (agreement_code, ok_version)
///     matrix — the same 10 pairs as the §24/§26 seeds (SPECIAL_HOLIDAY accrues under the same
///     agreement/OK dimensions);</item>
///   <item><b>the DISTINCT-from-consumption pin (R7 HARD constraint):</b> the godtgørelse settlement
///     time_type maps to the sentinel, NOT to the CONSUMPTION lønart <c>SLS_0570</c>; and the
///     consumption mapping <c>SPECIAL_HOLIDAY_ALLOWANCE → SLS_0570</c> is untouched and is NOT
///     <c>SLS_TBD_*</c> (it is a real deliverable code).</item>
/// </list>
/// These run against the FULL init.sql schema (the production seed). FAIL-002 protocol per
/// <see cref="SettlementEmitterFixture"/>.
/// </summary>
[Trait("Category", "Docker")]
public sealed class Settlement15S17WageMappingSchemaTests : IAsyncLifetime
{
    private const string GodtgoerelseTimeType = "SPECIAL_HOLIDAY_SETTLEMENT_PAYOUT";
    private const string Sentinel = "SLS_TBD_S15S17";
    private const string ConsumedVacationTimeType = "VACATION";
    private const string ConsumptionTimeType = "SPECIAL_HOLIDAY_ALLOWANCE";
    private const string ConsumptionLonart = "SLS_0570";

    private TestFixtures.DockerHarness _harness = null!;
    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>The sentinel <c>SLS_TBD_S15S17</c> appears ONLY for the §15 stk.2/§17 settlement time_type.</summary>
    [Fact]
    public async Task Sentinel_AppearsOnly_OnGodtgoerelseTimeType()
    {
        var timeTypes = await QueryStringsAsync(
            "SELECT DISTINCT time_type FROM wage_type_mappings WHERE wage_type = @w",
            ("w", Sentinel));

        Assert.NotEmpty(timeTypes); // the R7 seed landed
        Assert.All(timeTypes, tt => Assert.Equal(GodtgoerelseTimeType, tt));
    }

    /// <summary>The godtgørelse settlement time_type maps to NO non-sentinel wage type (deferred SLS code).</summary>
    [Fact]
    public async Task GodtgoerelseTimeType_MapsTo_OnlyTheSentinel()
    {
        var wageTypes = await QueryStringsAsync(
            "SELECT DISTINCT wage_type FROM wage_type_mappings WHERE time_type = @t",
            ("t", GodtgoerelseTimeType));

        Assert.NotEmpty(wageTypes);
        Assert.All(wageTypes, wt => Assert.Equal(Sentinel, wt));
    }

    /// <summary>The godtgørelse mapping covers EXACTLY the consumed-VACATION matrix (set equality) AND
    /// the expected 10 pairs {AC,HK,PROSA,AC_RESEARCH,AC_TEACHING} × {OK24,OK26} (positive control —
    /// non-trivially covering, not two coincidentally-empty sets).</summary>
    [Fact]
    public async Task GodtgoerelseMatrix_Equals_ConsumedVacationMatrix_TenPairs()
    {
        var godtgoerelsePairs = await QueryPairsAsync(GodtgoerelseTimeType);
        var vacationPairs = await QueryPairsAsync(ConsumedVacationTimeType);

        Assert.NotEmpty(vacationPairs);
        Assert.Equal(vacationPairs, godtgoerelsePairs); // HashSet equality — same agreement/OK coverage

        string[] agreements = { "AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING" };
        string[] oks = { "OK24", "OK26" };
        foreach (var a in agreements)
            foreach (var ok in oks)
                Assert.Contains((a, ok), godtgoerelsePairs);
        Assert.Equal(agreements.Length * oks.Length, godtgoerelsePairs.Count);
    }

    /// <summary>R7 HARD constraint — the SETTLEMENT godtgørelse mapping is DISTINCT from the
    /// CONSUMPTION lønart. The settlement time_type never maps to <c>SLS_0570</c>; and the consumption
    /// mapping (<c>SPECIAL_HOLIDAY_ALLOWANCE → SLS_0570</c>) is intact AND is a REAL deliverable code
    /// (NOT an <c>SLS_TBD_*</c> sentinel) — so the two are genuinely different lønarter.</summary>
    [Fact]
    public async Task GodtgoerelseSettlement_IsDistinctFrom_ConsumptionLonart()
    {
        // The settlement time_type maps to the sentinel — and NEVER to the consumption SLS_0570.
        var godtgoerelseWageTypes = await QueryStringsAsync(
            "SELECT DISTINCT wage_type FROM wage_type_mappings WHERE time_type = @t",
            ("t", GodtgoerelseTimeType));
        Assert.DoesNotContain(ConsumptionLonart, godtgoerelseWageTypes);

        // The consumption mapping is intact and is a REAL deliverable code (not a sentinel).
        var consumptionWageTypes = await QueryStringsAsync(
            "SELECT DISTINCT wage_type FROM wage_type_mappings WHERE time_type = @t",
            ("t", ConsumptionTimeType));
        Assert.NotEmpty(consumptionWageTypes);                                  // the consumption mapping exists
        Assert.Contains(ConsumptionLonart, consumptionWageTypes);              // → SLS_0570
        Assert.All(consumptionWageTypes, wt =>
            Assert.False(wt.StartsWith("SLS_TBD_", StringComparison.Ordinal))); // real code, never a sentinel

        // And the sentinel never bleeds onto the consumption time_type.
        var sentinelTimeTypes = await QueryStringsAsync(
            "SELECT DISTINCT time_type FROM wage_type_mappings WHERE wage_type = @w",
            ("w", Sentinel));
        Assert.DoesNotContain(ConsumptionTimeType, sentinelTimeTypes);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<HashSet<(string Agreement, string Ok)>> QueryPairsAsync(string timeType)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT agreement_code, ok_version
            FROM wage_type_mappings
            WHERE time_type = @t AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("t", timeType);
        var set = new HashSet<(string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            set.Add((reader.GetString(0), reader.GetString(1)));
        return set;
    }

    private async Task<List<string>> QueryStringsAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }
}
