using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7105 (SPRINT-71 R11) — Docker-gated SCHEMA/seed assertions on the §26 placeholder
/// wage-type mapping, mirroring <see cref="Settlement24WageMappingSchemaTests"/> (the S69
/// precedent the R11 seed must reproduce exactly):
/// <list type="bullet">
///   <item><c>SLS_TBD_S26</c> occurs ONLY for the new §26 <c>time_type</c>
///     (<c>VACATION_TERMINATION_PAYOUT</c>);</item>
///   <item>that <c>time_type</c> maps to NO non-sentinel wage type;</item>
///   <item>the §26 mapping covers EXACTLY the consumed-VACATION (agreement_code, ok_version)
///     matrix — the same 10 pairs as the §24 seed;</item>
///   <item><b>the parked-§7 pin (gate (i) waiver-only branch):</b> NO <c>SLS_TBD_S7</c> wage type
///     and NO <c>VACATION_TERMINATION_DEDUCTION</c> time_type exist anywhere — the §7 seeds are
///     PARKED behind the SLS-dialogue task and must not leak in.</item>
/// </list>
/// These run against the FULL init.sql schema (the production seed). FAIL-002 protocol per
/// <see cref="SettlementEmitterFixture"/>.
/// </summary>
[Trait("Category", "Docker")]
public sealed class Settlement26WageMappingSchemaTests : IAsyncLifetime
{
    private const string TerminationTimeType = "VACATION_TERMINATION_PAYOUT";
    private const string Sentinel = "SLS_TBD_S26";
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

    /// <summary>The sentinel <c>SLS_TBD_S26</c> appears ONLY for the §26 termination time_type.</summary>
    [Fact]
    public async Task Sentinel_AppearsOnly_OnTerminationTimeType()
    {
        var timeTypes = await QueryStringsAsync(
            "SELECT DISTINCT time_type FROM wage_type_mappings WHERE wage_type = @w",
            ("w", Sentinel));

        Assert.NotEmpty(timeTypes); // the R11 seed landed
        Assert.All(timeTypes, tt => Assert.Equal(TerminationTimeType, tt));
    }

    /// <summary>The §26 time_type maps to NO non-sentinel wage type (the real SLS code is deferred).</summary>
    [Fact]
    public async Task TerminationTimeType_MapsTo_OnlyTheSentinel()
    {
        var wageTypes = await QueryStringsAsync(
            "SELECT DISTINCT wage_type FROM wage_type_mappings WHERE time_type = @t",
            ("t", TerminationTimeType));

        Assert.NotEmpty(wageTypes);
        Assert.All(wageTypes, wt => Assert.Equal(Sentinel, wt));
    }

    /// <summary>The §26 mapping covers EXACTLY the consumed-VACATION matrix (set equality) AND the
    /// expected 10 pairs {AC,HK,PROSA,AC_RESEARCH,AC_TEACHING} × {OK24,OK26} (positive control —
    /// non-trivially covering, not two coincidentally-empty sets).</summary>
    [Fact]
    public async Task TerminationMatrix_Equals_ConsumedVacationMatrix_TenPairs()
    {
        var terminationPairs = await QueryPairsAsync(TerminationTimeType);
        var vacationPairs = await QueryPairsAsync(ConsumedVacationTimeType);

        Assert.NotEmpty(vacationPairs);
        Assert.Equal(vacationPairs, terminationPairs); // HashSet equality — same agreement/OK coverage

        string[] agreements = { "AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING" };
        string[] oks = { "OK24", "OK26" };
        foreach (var a in agreements)
            foreach (var ok in oks)
                Assert.Contains((a, ok), terminationPairs);
        Assert.Equal(agreements.Length * oks.Length, terminationPairs.Count);
    }

    /// <summary>Gate-(i) parked-§7 pin: NO <c>SLS_TBD_S7</c> rows and NO
    /// <c>VACATION_TERMINATION_DEDUCTION</c> time_type exist — the §7 modregning seeds are PARKED
    /// behind the SLS-dialogue task (the waiver-only branch is ACTIVE).</summary>
    [Fact]
    public async Task ParkedS7_HasNoSeedRows()
    {
        var s7WageRows = await QueryStringsAsync(
            "SELECT wage_type FROM wage_type_mappings WHERE wage_type LIKE 'SLS_TBD_S7%'");
        Assert.Empty(s7WageRows);

        var s7TimeTypeRows = await QueryStringsAsync(
            "SELECT time_type FROM wage_type_mappings WHERE time_type = 'VACATION_TERMINATION_DEDUCTION'");
        Assert.Empty(s7TimeTypeRows);
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
