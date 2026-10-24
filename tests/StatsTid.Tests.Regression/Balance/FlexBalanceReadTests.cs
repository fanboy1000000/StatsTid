using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S146 / TASK-14607-T (refinement C5, AC-C9; R-7, R-26) — the employee's own flex screens read the
/// latest RECORDED closing with its month.
///
/// <para><b>Plain language.</b> Flex is the running balance of hours above or below the norm. Since S146
/// each month payroll has settled carries a "ledger record" (a <c>payroll_export_records</c> row with
/// opening balance, closing balance and provenance all set). The employee's "Flex saldo"
/// (<c>/api/balance/{id}/summary</c>) and the year overview's flex tile now show the closing of the NEWEST
/// ledger record, labelled with its month (<c>"YYYY-MM"</c>); with no recorded month both are null
/// ("ingen afregnet måned"), never a made-up 0. A "raw" record (a raw export route's row, null balances)
/// is NOT a recorded month and is ignored even when it is the newest row.</para>
///
/// <para><b>Seeds.</b> Ledger records through <see cref="FlexLedgerSeed.SeedAsync"/>; the raw record by
/// SQL with null balances. Employees are the init.sql seed users <c>emp001</c> (STY01) and <c>emp002</c>
/// (STY02); a fresh container per test, so no cleanup is needed.</para>
///
/// <para><b>Every fact is Docker-gated and is CI-pending locally (R-30).</b> Each fact's kind and Red
/// condition are in its docstring (F: fails on today's, pre-TASK-14607, code; R: a regression pin).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexBalanceReadTests : IAsyncLifetime
{
    private const string Ledgered = "emp001";      // STY01, AC
    private const string LedgeredOrg = "STY01";
    private const string RawOnly = "emp002";       // STY02, HK
    private const string RawOnlyOrg = "STY02";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders; seed records AFTER this (S63 lesson)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null) await _harness.DisposeAsync();
    }

    // ── 1. /summary shows the latest LEDGER record's closing and month; a later raw record is ignored ──

    /// <summary>
    /// <b>Kind F.</b> Three records for one employee: a ledger record for 2026-02 (closing 2), a ledger record
    /// for 2026-03 (opening 2, closing 7.25) and a LATER raw record for 2026-05 (null balances). The summary
    /// shows 7.25 with month "2026-03": the newest ledger record, not the newest row. Its
    /// <c>flexDelta</c> is that record's closing − opening = 5.25 (TASK-14607's built meaning, declared
    /// deviation 1). Every number differs from the others, so a first-record, sum or newest-row reading
    /// cannot pass.
    /// <para><b>Red conditions:</b> on today's code <c>flexBalance</c> is read from the never-written
    /// <c>FlexBalanceUpdated</c> event, so it is 0 (not 7.25): the <c>Assert.Equal(7.25m, flexBalance)</c>
    /// fails; <c>flexBalanceMonth</c> does not exist (<c>GetProperty</c> throws).</para>
    /// </summary>
    [Fact]
    public async Task Summary_ShowsLatestLedgerClosingAndMonth_LaterRawRecordIgnored()
    {
        await SeedLedgerAndRawAsync();
        using var client = S120ContractAssert.EmployeeClient(_factory, Ledgered, LedgeredOrg);

        var root = await GetJsonAsync(client, $"/api/balance/{Ledgered}/summary?year=2026&month=6");

        Assert.Equal(7.25m, root.GetProperty("flexBalance").GetDecimal());
        Assert.Equal("2026-03", root.GetProperty("flexBalanceMonth").GetString());
        Assert.Equal(5.25m, root.GetProperty("flexDelta").GetDecimal());
    }

    /// <summary>
    /// <b>Kind F.</b> The same three records through the year overview's flex tile: 7.25 and "2026-03".
    /// <para><b>Red conditions:</b> today <c>tiles.flexBalance</c> is the event-derived 0, so the first
    /// assertion fails; <c>tiles.flexBalanceMonth</c> does not exist.</para>
    /// </summary>
    [Fact]
    public async Task YearOverviewTile_ShowsLatestLedgerClosingAndMonth_LaterRawRecordIgnored()
    {
        await SeedLedgerAndRawAsync();
        using var client = S120ContractAssert.EmployeeClient(_factory, Ledgered, LedgeredOrg);

        var tiles = (await GetJsonAsync(client, $"/api/balance/{Ledgered}/year-overview?year=2026"))
            .GetProperty("tiles");

        Assert.Equal(7.25m, tiles.GetProperty("flexBalance").GetDecimal());
        Assert.Equal("2026-03", tiles.GetProperty("flexBalanceMonth").GetString());
    }

    // ── 2. no recorded month → null balance and null month, never 0 ──

    /// <summary>
    /// <b>Kind F.</b> An employee with no record at all: <c>flexBalance</c> and <c>flexBalanceMonth</c> are
    /// both JSON null on the summary and on the tile (the UI's "ingen afregnet måned"), and
    /// <c>flexDelta</c> is 0.
    /// <para><b>Red conditions:</b> today <c>flexBalance</c> is the number 0 (the default when no event
    /// exists), so <c>Assert.Equal(JsonValueKind.Null, flexBalance.ValueKind)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task NoRecordedMonth_SummaryAndTile_AreNullNotZero()
    {
        using var client = S120ContractAssert.EmployeeClient(_factory, Ledgered, LedgeredOrg);

        var summary = await GetJsonAsync(client, $"/api/balance/{Ledgered}/summary?year=2026&month=6");
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("flexBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("flexBalanceMonth").ValueKind);
        Assert.Equal(0m, summary.GetProperty("flexDelta").GetDecimal());

        var tiles = (await GetJsonAsync(client, $"/api/balance/{Ledgered}/year-overview?year=2026"))
            .GetProperty("tiles");
        Assert.Equal(JsonValueKind.Null, tiles.GetProperty("flexBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, tiles.GetProperty("flexBalanceMonth").ValueKind);
    }

    /// <summary>
    /// <b>Kind F.</b> An employee whose ONLY record is a raw one (null balances) has no recorded month: a raw
    /// record is not a ledger record, so the screens show null and null, not 0 and not the raw month.
    /// <para><b>Red conditions:</b> today <c>flexBalance</c> is 0 (a number), so the null assertion fails.
    /// A reader that took the newest row regardless of balances would hit the NULL balance columns and
    /// throw or show "2026-05", failing the month assertion.</para>
    /// </summary>
    [Fact]
    public async Task OnlyARawRecord_IsNotARecordedMonth_SummaryAndTile_AreNull()
    {
        await SeedRawRecordAsync(RawOnly, 2026, 5);
        using var client = S120ContractAssert.EmployeeClient(_factory, RawOnly, RawOnlyOrg);

        var summary = await GetJsonAsync(client, $"/api/balance/{RawOnly}/summary?year=2026&month=6");
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("flexBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("flexBalanceMonth").ValueKind);

        var tiles = (await GetJsonAsync(client, $"/api/balance/{RawOnly}/year-overview?year=2026"))
            .GetProperty("tiles");
        Assert.Equal(JsonValueKind.Null, tiles.GetProperty("flexBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, tiles.GetProperty("flexBalanceMonth").ValueKind);
    }

    // ── 3. scope is unchanged (R-26) ──

    /// <summary>
    /// <b>Kind R (regression pin — holds today, R-26).</b> A recorded figure must never widen who may read it:
    /// another employee reading the ledgered employee's summary or year overview is 403 (the employee-self
    /// gate), and a leader scoped to a DISJOINT org (STY05, S93 flat role-scope) reading them is 403. The
    /// record is seeded first, so a 200 would carry the 7.25 rather than pass against an empty ledger.
    /// <para><b>Red conditions:</b> none today (boundary pin — holds today); it goes red only if the new
    /// read is moved ahead of, or around, the existing scope checks.</para>
    /// </summary>
    [Fact]
    public async Task ForeignEmployee_And_OutOfScopeLeader_Are403_OnSummaryAndYearOverview()
    {
        await SeedLedgerAndRawAsync();
        var urls = new[]
        {
            $"/api/balance/{Ledgered}/summary?year=2026&month=6",
            $"/api/balance/{Ledgered}/year-overview?year=2026",
        };

        using var foreign = S120ContractAssert.EmployeeClient(_factory, RawOnly, LedgeredOrg);
        using var leaderOutOfScope = S120ContractAssert.LeaderClient(_factory, "mgr_s146flx_oos", "STY05");
        foreach (var url in urls)
        {
            using var asForeign = await foreign.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, asForeign.StatusCode);
            using var asLeader = await leaderOutOfScope.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, asLeader.StatusCode);
        }
    }

    // ── seeds / helpers ──

    /// <summary>Ledger records for 2026-02 (opening 0, closing 2) and 2026-03 (opening 2, closing 7.25), plus
    /// a LATER raw record (null balances) for 2026-05.</summary>
    private async Task SeedLedgerAndRawAsync()
    {
        await FlexLedgerSeed.SeedAsync(_harness.ConnectionString, Ledgered, new DateOnly(2026, 2, 1),
            closing: 2m, opening: 0m);
        await FlexLedgerSeed.SeedAsync(_harness.ConnectionString, Ledgered, new DateOnly(2026, 3, 1),
            closing: 7.25m, opening: 2m, provenance: "CHAINED");
        await SeedRawRecordAsync(Ledgered, 2026, 5);
    }

    /// <summary>A raw record: a raw export route's row, with all three balance columns NULL (the table's
    /// CHECK allows all-null).</summary>
    private async Task SeedRawRecordAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, exported_at,
                 original_lines, current_effective_lines, content_hash, source)
            VALUES
                (gen_random_uuid(), NULL, @emp, @y, @m, NOW(), '[]'::jsonb, '[]'::jsonb, 's146-raw-seed', 'CALCULATE_AND_EXPORT')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using var rsp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }
}
