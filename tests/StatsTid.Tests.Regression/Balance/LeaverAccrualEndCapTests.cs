using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S137 / TASK-13703 — Docker-gated HTTP pins for two Balance-side correctness fixes:
///
/// <list type="bullet">
///   <item><description><b>ADR-040 D9 — the leaver END-cap.</b> A leaver's RUNNING vacation
///   balance stops accruing at the last employed day across <c>/summary</c>, <c>/series</c> and
///   the <c>/year-overview</c> matrix + tile. Fixture: a leaver whose last day is 15 Mar 2026 in the
///   Sep-2025 ferieår (VACATION 25 d, reset month 9). Sep..Mar = 7 accrual months ⇒
///   <c>25 × 7/12 = 14.58</c>; every later month repeats that value instead of climbing to 25.
///   The next ferieår (from 1 Sep 2026) starts AFTER the leave date ⇒ 0.</description></item>
///   <item><description><b>ADR-040 D4 / QUAL-147 — the OK version follows the MONTH READ.</b>
///   <c>/summary</c>'s norm-hours lookup used the live <c>users.ok_version</c>; for a now-OK26
///   employee an OK24 month must be valued under the OK24 agreement config. Observable through
///   <c>normHoursExpected</c> once the AC/OK24 row's <c>weekly_norm_hours</c> is made to differ
///   from OK26's (the seeded values are identical — 37.0 — so the fixture mutates OK24 to 36.0 for
///   the duration of the test and restores it).</description></item>
/// </list>
///
/// <para>
/// Today seam: the year-overview derives "today" from <see cref="TimeProvider"/>; the derived host
/// pins it to 2026-06-15 (the <see cref="YearOverviewTests"/> anchor) — inside the 2025 ferieår,
/// after the leave date. Employees stay <c>is_active = TRUE</c>: the window is a DATE fact (ADR-040
/// D3), and the summary/series read the active-only <c>GetByIdAsync</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LeaverAccrualEndCapTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private static readonly DateOnly FixedToday = new(2026, 6, 15);
    private static readonly DateOnly LeaveDate = new(2026, 3, 15);

    /// <summary>25 × 7/12, rounded to 2 dp the way every Balance read rounds for display.</summary>
    private static readonly decimal EarnedAtLeave = Math.Round(25m * 7 / 12m, 2); // 14.58

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _fixedTodayHost = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (entitlement + agreement configs)

        _fixedTodayHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday))));
        _ = _fixedTodayHost.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _fixedTodayHost?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // D9 — /summary
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The running <c>earned</c> for VACATION climbs until the leave month and then holds: Feb =
    /// 6 months (12.50), Mar = 7 (14.58), Apr / Jun / Aug = still 14.58 (pre-S137: 16.67 / 20.83 / 25).
    /// </summary>
    [Theory]
    [InlineData(2026, 2, "12.50")]
    [InlineData(2026, 3, "14.58")]
    [InlineData(2026, 4, "14.58")]
    [InlineData(2026, 6, "14.58")]
    [InlineData(2026, 8, "14.58")]
    public async Task Summary_LeaverVacationEarned_StopsAtLeaveMonth(int year, int month, string expected)
    {
        var employeeId = await SeedLeaverAsync(LeaveDate);

        var earned = await GetSummaryEarnedAsync(Client(employeeId), employeeId, year, month, "VACATION");

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), earned);
    }

    /// <summary>A windowless employee's summary is byte-identical to before (full 12/12 by August).</summary>
    [Fact]
    public async Task Summary_WindowlessEmployee_Unchanged_FullQuotaByFerieaarEnd()
    {
        var employeeId = await SeedLeaverAsync(end: null);

        Assert.Equal(25m, await GetSummaryEarnedAsync(Client(employeeId), employeeId, 2026, 8, "VACATION"));
        Assert.Equal(Math.Round(25m * 7 / 12m, 2), await GetSummaryEarnedAsync(Client(employeeId), employeeId, 2026, 3, "VACATION"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // D9 — /series
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The 12-point VACATION curve (Sep 2025 .. Aug 2026) rises by 25/12 per month through March
    /// (point index 6) and then PLATEAUS: points 7..11 (Apr..Aug) all equal the March value. Still
    /// monotonic non-decreasing; the selected point reconciles with /summary.
    /// </summary>
    [Fact]
    public async Task Series_LeaverCurve_PlateausFromLeaveMonth()
    {
        var employeeId = await SeedLeaverAsync(LeaveDate);
        var client = Client(employeeId);

        var body = await GetJsonAsync(client, $"/api/balance/{employeeId}/series?year=2026&month=4");
        var points = body.GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("type").GetString() == "VACATION")
            .GetProperty("points").EnumerateArray()
            .Select(p => p.GetProperty("earned").GetDecimal())
            .ToList();

        Assert.Equal(12, points.Count);
        for (var i = 0; i <= 6; i++)
            Assert.Equal(Math.Round(25m * (i + 1) / 12m, 2), points[i]);   // Sep..Mar rising
        for (var i = 7; i < 12; i++)
            Assert.Equal(EarnedAtLeave, points[i]);                          // Apr..Aug plateau
        Assert.Equal(points[6], points[11]);

        // Reconciliation with /summary for the selected month (April) still holds.
        Assert.Equal(points[7], await GetSummaryEarnedAsync(client, employeeId, 2026, 4, "VACATION"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // D9 — /year-overview (matrix saldo, disposition projection, today tile)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calendar-2026 VACATION matrix: Jan 10.42 → Feb 12.50 → Mar 14.58, then Apr..Aug hold 14.58;
    /// Sep..Dec belong to the 2026 ferieår, which begins AFTER the leave date ⇒ 0.00. The
    /// closed-ferieår disposition projection (<c>expiring</c>) is <c>max(0, 14.58 − cap 5) = 9.58</c>
    /// (pre-S137: 20.00), and the today tile (<c>ferieRemaining</c>, today 2026-06-15) reads 14.58.
    /// </summary>
    [Fact]
    public async Task YearOverview_LeaverSaldo_PlateausThenZeroInNextFerieaar_ExpiringAndTileCapped()
    {
        var employeeId = await SeedLeaverAsync(LeaveDate);

        var body = await GetJsonAsync(Client(employeeId), $"/api/balance/{employeeId}/year-overview?year=2026");
        var vacation = body.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == "VACATION");
        var saldo = vacation.GetProperty("saldo").EnumerateArray()
            .Select(s => s.ValueKind == JsonValueKind.Null ? (decimal?)null : s.GetDecimal())
            .ToList();

        Assert.Equal(12, saldo.Count);
        Assert.Equal(Math.Round(25m * 5 / 12m, 2), saldo[0]);   // Jan  — 5 months
        Assert.Equal(Math.Round(25m * 6 / 12m, 2), saldo[1]);   // Feb  — 6
        Assert.Equal(EarnedAtLeave, saldo[2]);                   // Mar  — 7 (the leave month)
        for (var m = 3; m <= 7; m++)
            Assert.Equal(EarnedAtLeave, saldo[m]);               // Apr..Aug — plateau
        for (var m = 8; m <= 11; m++)
            Assert.Equal(0m, saldo[m]);                          // Sep..Dec — ferieår 2026 starts after the leave

        Assert.Equal(Math.Round(25m * 7 / 12m - 5m, 2), vacation.GetProperty("expiring").GetDecimal()); // 9.58

        var tile = body.GetProperty("tiles").GetProperty("ferieRemaining");
        Assert.NotEqual(JsonValueKind.Null, tile.ValueKind);
        Assert.Equal(EarnedAtLeave, tile.GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // D4 / QUAL-147 — /summary OK version follows the MONTH read (BalanceEndpoints L152-157)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A live-OK26 employee reading March 2026 (an OK24 month) gets <c>normHoursExpected</c> from
    /// the AC/OK24 agreement config (mutated to 36.0 h/week for this test), NOT from the live-OK26
    /// config (37.0). April 2026 (OK26) reads 37.0. Pre-S137 both months used the live OK26 row —
    /// March would have read <c>weekdays/5 × 37.0</c>.
    /// </summary>
    [Fact]
    public async Task Summary_NormHours_UseTheOkVersionOfTheMonthRead_NotTheLiveUsersColumn()
    {
        var employeeId = await SeedLeaverAsync(end: null, okVersion: "OK26");
        Assert.Equal("OK26", await ReadUsersOkVersionAsync(employeeId));

        await SetActiveWeeklyNormAsync("AC", "OK24", 36.0m);
        try
        {
            var client = Client(employeeId);

            var march = await GetJsonAsync(client, $"/api/balance/{employeeId}/summary?year=2026&month=3");
            var marchExpected = (CountWeekdays(2026, 3) / 5.0m) * 36.0m;
            Assert.Equal(marchExpected, march.GetProperty("normHoursExpected").GetDecimal());
            Assert.NotEqual((CountWeekdays(2026, 3) / 5.0m) * 37.0m, march.GetProperty("normHoursExpected").GetDecimal());

            var april = await GetJsonAsync(client, $"/api/balance/{employeeId}/summary?year=2026&month=4");
            Assert.Equal((CountWeekdays(2026, 4) / 5.0m) * 37.0m, april.GetProperty("normHoursExpected").GetDecimal());
        }
        finally
        {
            await SetActiveWeeklyNormAsync("AC", "OK24", 37.0m);
        }
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<string> SeedLeaverAsync(DateOnly? end, string okVersion = "OK24")
    {
        var employeeId = "emp_s137_cap_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", okVersion);
        if (end is { } e)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET employment_end_date = @e WHERE user_id = @id", conn);
            cmd.Parameters.AddWithValue("e", e);
            cmd.Parameters.AddWithValue("id", employeeId);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        return employeeId;
    }

    private async Task SetActiveWeeklyNormAsync(string agreementCode, string okVersion, decimal weeklyNorm)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs SET weekly_norm_hours = @w
            WHERE agreement_code = @a AND ok_version = @ok AND status = 'ACTIVE'
            """, conn);
        cmd.Parameters.AddWithValue("w", weeklyNorm);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync()); // the seeded ACTIVE row must exist
    }

    // Literal SQL (CA2100: the CI ratchet counts non-constant command text in test projects too).
    private async Task<string> ReadUsersOkVersionAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT ok_version FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static decimal CountWeekdays(int year, int month)
    {
        var count = 0;
        for (var d = new DateOnly(year, month, 1); d.Month == month; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        return count;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var rsp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<decimal> GetSummaryEarnedAsync(HttpClient client, string employeeId, int year, int month, string type)
    {
        var body = await GetJsonAsync(client, $"/api/balance/{employeeId}/summary?year={year}&month={month}");
        return body.GetProperty("entitlements").EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == type)
            .GetProperty("earned").GetDecimal();
    }

    private HttpClient Client(string employeeId)
    {
        var client = _fixedTodayHost.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
        return client;
    }

    private static string MintEmployeeToken(string actorId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }
}
