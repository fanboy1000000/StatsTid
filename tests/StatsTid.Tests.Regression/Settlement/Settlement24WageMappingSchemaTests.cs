using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S69 / TASK-6907 (ADR-033 slice 1b, Step-0b W4 / TASK-6903) — Docker-gated SCHEMA/seed assertions on
/// the §24 placeholder wage-type mapping. The sentinel lønart <c>SLS_TBD_S24</c> is intentionally
/// reused across many natural keys (the whole agreement/OK matrix), so natural-key uniqueness alone is
/// NOT the collision guarantee. These tests assert the PRECISE semantic-collision contract (the
/// S36/S37 MERARBEJDE→SLS_0210 lesson generalized):
/// <list type="bullet">
///   <item><c>SLS_TBD_S24</c> occurs ONLY for the new §24 <c>time_type</c>
///     (<c>VACATION_SETTLEMENT_PAYOUT</c>) — no unrelated <c>time_type</c> uses the sentinel;</item>
///   <item>that <c>time_type</c> maps to NO non-sentinel wage type this sprint;</item>
///   <item>the §24 mapping covers EXACTLY the same (agreement_code, ok_version) matrix as the existing
///     consumed-VACATION rows (<c>VACATION</c> → <c>SLS_0510</c>), so §24 auto-payout resolves for
///     every agreement/OK a vacation employee can be under.</item>
/// </list>
/// These run against the FULL init.sql schema (the production §24 seed), so they pin the SHIPPED seed,
/// not a test fixture. FAIL-002 protocol per <see cref="SettlementEmitterFixture"/>.
/// </summary>
[Trait("Category", "Docker")]
public sealed class Settlement24WageMappingSchemaTests : IAsyncLifetime
{
    private const string SettlementTimeType = "VACATION_SETTLEMENT_PAYOUT";
    private const string Sentinel = "SLS_TBD_S24";
    private const string ConsumedVacationTimeType = "VACATION";

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

    /// <summary>The sentinel <c>SLS_TBD_S24</c> appears ONLY for the §24 settlement time_type — no
    /// unrelated time_type uses it (the precise semantic-collision assertion, NOT mere natural-key
    /// uniqueness).</summary>
    [Fact]
    public async Task Sentinel_AppearsOnly_OnSettlementTimeType()
    {
        var timeTypes = await QueryStringsAsync(
            "SELECT DISTINCT time_type FROM wage_type_mappings WHERE wage_type = @w",
            ("w", Sentinel));

        Assert.NotEmpty(timeTypes); // the seed landed
        Assert.All(timeTypes, tt => Assert.Equal(SettlementTimeType, tt));
        Assert.Single(timeTypes.Distinct());
    }

    /// <summary>The §24 settlement time_type maps to NO non-sentinel wage type this sprint (every row is
    /// the placeholder; the real SLS code is deferred).</summary>
    [Fact]
    public async Task SettlementTimeType_MapsTo_OnlyTheSentinel()
    {
        var wageTypes = await QueryStringsAsync(
            "SELECT DISTINCT wage_type FROM wage_type_mappings WHERE time_type = @t",
            ("t", SettlementTimeType));

        Assert.NotEmpty(wageTypes);
        Assert.All(wageTypes, wt => Assert.Equal(Sentinel, wt));
    }

    /// <summary>The §24 mapping covers EXACTLY the same (agreement_code, ok_version) matrix as the
    /// existing consumed-VACATION rows (VACATION → SLS_0510). A set-equality assertion: every pair a
    /// vacation employee can be under has a §24 payout mapping, and the §24 set adds no spurious pair.</summary>
    [Fact]
    public async Task SettlementMatrix_Equals_ConsumedVacationMatrix()
    {
        var settlementPairs = await QueryPairsAsync(SettlementTimeType);
        var vacationPairs = await QueryPairsAsync(ConsumedVacationTimeType);

        Assert.NotEmpty(vacationPairs);
        Assert.Equal(vacationPairs, settlementPairs); // HashSet equality — same agreement/OK coverage
    }

    /// <summary>Positive control: the expected 10-pair matrix {AC,HK,PROSA,AC_RESEARCH,AC_TEACHING} ×
    /// {OK24,OK26} is present for the §24 time_type (so the set-equality test above is non-trivially
    /// covering the real matrix, not two coincidentally-empty sets).</summary>
    [Fact]
    public async Task SettlementMatrix_Covers_TheTenAgreementOkPairs()
    {
        var pairs = await QueryPairsAsync(SettlementTimeType);
        string[] agreements = { "AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING" };
        string[] oks = { "OK24", "OK26" };
        foreach (var a in agreements)
            foreach (var ok in oks)
                Assert.Contains((a, ok), pairs);
        Assert.Equal(agreements.Length * oks.Length, pairs.Count);
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
