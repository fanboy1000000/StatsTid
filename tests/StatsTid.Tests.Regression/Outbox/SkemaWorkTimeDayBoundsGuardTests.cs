using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S58 TASK-5802 regression — per-day work-time bounds guard on the Skema POST
/// <c>/api/skema/{employeeId}/save</c> handler (Arbejdstid only).
///
/// <para>
/// The save handler now rejects, UP FRONT (before the transaction), any
/// <c>workTime[]</c> day that violates a physical input invariant — so no
/// <c>work_time_projection</c> row is written. Two checks remain (manual hours, and with them the
/// negative-manual check, are retired): (1) overlapping intervals → 422
/// <c>work_time_intervals_overlap</c> (pinned here); (2) total worked hours (interval hours) &gt; 24 →
/// 422 <c>work_time_exceeds_day</c>, a backstop that no test reaches today: hours parse 0-23 and overlaps
/// are refused first, so a day tops out at 86 399 s, which rounds to 24,00. Exactly 24,0 t is allowed;
/// touching interval boundaries (next.start == prev.end) are NOT an overlap.
/// </para>
///
/// <para>
/// HTTP-level WAF&lt;Program&gt; harness mirroring
/// <see cref="SkemaWorkTimeDateRangeGuardTests"/> so the REAL endpoint guard is pinned.
/// POST as seeded employee <c>emp001</c> (route id == actor) and assert against
/// <c>work_time_projection</c> read back from the DB.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaWorkTimeDayBoundsGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // S148 S0-5: the negative-manual-hours fact and the two above-24-hours facts are retired with the
    // manual-hours member: the negative check is gone with it, and more than 24 h cannot be reached from
    // same-day clock intervals (TASK-14809 BC-4).

    /// <summary>Overlapping intervals → 422 <c>work_time_intervals_overlap</c>, no row.</summary>
    [Fact]
    public async Task Save_OverlappingIntervals_Returns422_AndWritesNoRow()
    {
        var date = new DateOnly(2026, 3, 10);
        var rsp = await PostWorkDayAsync(date,
            intervals: new[] { ("08:00", "16:00"), ("12:00", "20:00") }); // overlap 12:00–16:00

        await AssertRejectedAsync(rsp, date, "work_time_intervals_overlap");
    }

    /// <summary>
    /// Kind R (S148 S0-5: same assertions as before, the 24 h now expressed as clock time). One interval
    /// 00:00–23:59:59 (86 399 s, 24,00 t at 2 dp) → 200, row written. Proves the cap is strictly
    /// greater-than, not greater-or-equal. Red conditions: none; it holds before and after.
    /// </summary>
    [Fact]
    public async Task Save_Exactly24Hours_Succeeds_AndWritesRow()
    {
        var date = new DateOnly(2026, 3, 13);
        var rsp = await PostWorkDayAsync(date,
            intervals: new[] { ("00:00", "23:59:59") }); // 86 399 s = 24,00 t at 2 dp

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("saved").GetInt32());
        Assert.Equal(1, await CountWorkTimeRowsAsync(Emp001, date));
    }

    /// <summary>
    /// Adjacent / touching intervals (08:00–12:00 + 12:00–13:00, no overlap, 5h total)
    /// → 200, row written. Proves touching boundaries are not treated as overlap.
    /// </summary>
    [Fact]
    public async Task Save_TouchingIntervals_Succeeds_AndWritesRow()
    {
        var date = new DateOnly(2026, 3, 14);
        var rsp = await PostWorkDayAsync(date,
            intervals: new[] { ("08:00", "12:00"), ("12:00", "13:00") });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("saved").GetInt32());
        Assert.Equal(1, await CountWorkTimeRowsAsync(Emp001, date));
    }

    // ── Helpers ──

    private async Task<HttpResponseMessage> PostWorkDayAsync(
        DateOnly date, (string Start, string End)[] intervals)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp001, Emp001OrgId));

        var request = new
        {
            year = date.Year,
            month = date.Month,
            workTime = new[]
            {
                new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    intervals = intervals.Select(i => new { start = i.Start, end = i.End }).ToArray(),
                },
            },
        };

        return await client.PostAsJsonAsync($"/api/skema/{Emp001}/save", request);
    }

    private async Task AssertRejectedAsync(HttpResponseMessage rsp, DateOnly date, string expectedError)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedError, body.GetProperty("error").GetString());
        // Nothing persisted — rejected before the transaction opens.
        Assert.Equal(0, await CountWorkTimeRowsAsync(Emp001, date));
    }

    private async Task<int> CountWorkTimeRowsAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM work_time_projection WHERE employee_id = @e AND date = @d",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }
}
