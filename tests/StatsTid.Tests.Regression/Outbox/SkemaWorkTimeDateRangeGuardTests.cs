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
/// S56 / Step-7a BLOCKER regression — work-time date-range guard on the Skema POST
/// <c>/api/skema/{employeeId}/save</c> handler (security: period-lock bypass).
///
/// <para>
/// The approval-lock check in the save handler is scoped to the REQUESTED month
/// (<c>GetByEmployeeAndPeriodAsync(employeeId, monthStart, monthEnd)</c> →
/// EMPLOYEE_APPROVED/APPROVED → 409). Before the fix, a save targeting an UNLOCKED
/// month M could smuggle in a <c>workTime[].date</c> belonging to an already-approved /
/// locked month M-1; because the work-time branch upserts <c>work_time_projection</c>
/// by (employee_id, date) regardless of the requested month, that locked month's row
/// would be overwritten — a lock bypass.
/// </para>
///
/// <para>
/// The fix rejects any <c>workTime[].date</c> outside <c>[monthStart, monthEnd]</c>
/// UP FRONT (before the transaction) with HTTP 400
/// <c>{ error: "work_time_date_out_of_range", message }</c>, so the requested-month
/// lock governs every written date and the whole request is rejected before any row
/// is written.
/// </para>
///
/// <para>
/// HTTP-level WAF&lt;Program&gt; harness (mirrors
/// <see cref="StatsTid.Tests.Regression.Config.EmployeeProfileEndpointTests"/>) so the
/// REAL endpoint guard is pinned — not a re-implemented mirror. We POST as the seeded
/// employee <c>emp001</c> (route employeeId == actor, so the self-access check passes)
/// and assert against <c>work_time_projection</c> read back directly from the DB. The
/// Docker-gated fixture (<see cref="TestFixtures.DockerHarness"/> + full init.sql) and
/// trait match <see cref="WorkTimeProjectionAtomicTests"/> / <see cref="AllocationGateTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaWorkTimeDateRangeGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Seeded employee (init.sql L871): emp001, STY01, AC. Self-save → actor == route id.
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

    /// <summary>
    /// POST <c>/save</c> for month M (2026-03) with a <c>workTime</c> entry whose date is
    /// in month M-1 (2026-02-15) → 400 <c>work_time_date_out_of_range</c>, AND no
    /// <c>work_time_projection</c> row written for that out-of-range date (the lock is not
    /// bypassed). An in-range date (2026-03-10) in the SAME request is also NOT written —
    /// the whole request is rejected before the transaction opens.
    /// </summary>
    [Fact]
    public async Task Save_WorkTimeDateInPriorMonth_Returns400_AndWritesNoProjectionRow()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp001, Emp001OrgId));

        var outOfRangeDate = new DateOnly(2026, 2, 15);  // belongs to month M-1 (locked-month proxy)
        var inRangeDate = new DateOnly(2026, 3, 10);      // legitimately inside requested month M

        // Save targets month M = 2026-03 but smuggles in a 2026-02 work-time date.
        var request = new
        {
            year = 2026,
            month = 3,
            workTime = new[]
            {
                new
                {
                    date = inRangeDate.ToString("yyyy-MM-dd"),
                    intervals = new[] { new { start = "08:00", end = "16:00" } },
                    manualHours = 0m,
                },
                new
                {
                    date = outOfRangeDate.ToString("yyyy-MM-dd"),
                    intervals = new[] { new { start = "09:00", end = "17:00" } },
                    manualHours = 0m,
                },
            },
        };

        var rsp = await client.PostAsJsonAsync($"/api/skema/{Emp001}/save", request);

        // ── Up-front 400 with the exact error code ──
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("work_time_date_out_of_range", body.GetProperty("error").GetString());
        // Message names the offending date + the requested period.
        var message = body.GetProperty("message").GetString();
        Assert.Contains("2026-02-15", message);

        // ── Lock NOT bypassed: no projection row for the out-of-range (locked-month) date ──
        Assert.Equal(0, await CountWorkTimeRowsAsync(Emp001, outOfRangeDate));

        // ── Whole request rejected before the tx: the in-range date is NOT partially written ──
        Assert.Equal(0, await CountWorkTimeRowsAsync(Emp001, inRangeDate));
    }

    /// <summary>
    /// Control: an entirely in-range save (all <c>workTime</c> dates inside the requested
    /// month) is accepted and DOES write its <c>work_time_projection</c> row — proving the
    /// guard rejects only out-of-range dates, not all work-time saves.
    /// </summary>
    [Fact]
    public async Task Save_WorkTimeDateInRequestedMonth_Succeeds_AndWritesProjectionRow()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp001, Emp001OrgId));

        var inRangeDate = new DateOnly(2026, 3, 12);
        var request = new
        {
            year = 2026,
            month = 3,
            workTime = new[]
            {
                new
                {
                    date = inRangeDate.ToString("yyyy-MM-dd"),
                    intervals = new[] { new { start = "08:00", end = "16:00" } },
                    manualHours = 0m,
                },
            },
        };

        var rsp = await client.PostAsJsonAsync($"/api/skema/{Emp001}/save", request);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("saved").GetInt32());
        Assert.Equal(1, await CountWorkTimeRowsAsync(Emp001, inRangeDate));
    }

    // ── Helpers ──

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
