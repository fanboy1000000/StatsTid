using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S137 / TASK-13703 — OWNER RULING 2026-08-26 (OQ-1a): the SPECIAL_HOLIDAY leaver over-count
/// (VacationSettlementService "site 8"). RED-on-old provenance for the Orchestrator's record.
///
/// <para>
/// <b>The defect (plain language).</b> Særlige feriedage (SPECIAL_HOLIDAY, 5 days/yr) accrue
/// monthly over the calendar year. The §15 stk.2/§17 godtgørelse close valued "earned" at the
/// accrual END (31 Dec) for every employee — so an employee whose last day was 30 Jun was credited
/// the full 5 days instead of the 6/12 they actually earned. The owner ruled this a DELIBERATE
/// settlement-value change on the ADR-033 rails: the employee's end date now caps the accrual
/// (ADR-040 D9) inside <c>AccrualMath.EarnedToDate</c>.
/// </para>
///
/// <para>
/// <b>Why a fixed clock.</b> The S80 BLOCKER-1 guard in <c>SettleAsync</c> fails a SPECIAL_HOLIDAY
/// tuple CLOSED when the end date has already PASSED (<c>end &lt; today</c> — the R12 termination
/// non-goal), so the reachable over-count case is an end date recorded but not yet passed at settle
/// time. The direct <c>SettleAsync</c> drive (the <see cref="SpecialHolidaySettlementTests"/> shape)
/// under a <see cref="FixedTimeProvider"/> pinned ON the end date (<c>end &lt; today</c> is false)
/// reaches the godtgørelse path deterministically. The passed-end-date fail-closed behaviour is
/// re-pinned here too so the two are visibly distinct.
/// </para>
///
/// <para>
/// <b>RED-on-old.</b> <see cref="MidYearLeaver_EarnedCapsAtLeaveDate_Payout2Point5_Not5"/> asserts
/// <c>earned == 2.5</c> (5 × 6/12); against the pre-S137 code the SAME test observes
/// <c>earned == 5.0</c> (12/12 to 31 Dec) and fails — the old (wrong) value is stated inline.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SpecialHolidayLeaverAccrualCapTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string SpecialHolidayType = "SPECIAL_HOLIDAY";
    private const string YearEnd = "YEAR_END";
    private const int AccrualYear = 2021; // calendar accrual 1 Jan .. 31 Dec 2021; seeded quota 5, MONTHLY_ACCRUAL

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot the seeders (SPECIAL_HOLIDAY config: quota 5 / MONTHLY_ACCRUAL / reset_month 1)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // The owner-ruled fix — RED on the pre-S137 code.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mid-year leaver: last employed day 30 Jun 2021, settle accrual year 2021 with the clock ON
    /// the end date (not yet a "passed" leaver). Earned = 5 × 6/12 = <b>2.5</b> (Jan..Jun), used 0,
    /// planned 0 ⇒ godtgørelse payout 2.5, SETTLED, forfeit 0.
    ///
    /// <para><b>RED-on-old:</b> the pre-S137 line
    /// <c>EarnedToDate(quota, 1.0m, accrualStart, employmentStart, period.AccrualEnd)</c> valued
    /// earned to 31 Dec 2021 ⇒ <b>5.0</b> — this assertion would have observed <c>5.0m</c> for
    /// <c>PayoutDays</c> and the snapshot's <c>earned</c>, and failed.</para>
    /// </summary>
    [Fact]
    public async Task MidYearLeaver_EarnedCapsAtLeaveDate_Payout2Point5_Not5()
    {
        var employeeId = await SeedEmployeeAsync();
        var leaveDate = new DateOnly(AccrualYear, 6, 30);
        await SetEmploymentEndDateAsync(employeeId, leaveDate);

        using var host = FixedClockHost(today: leaveDate);
        var outcome = await SettleAsync(host, employeeId, AccrualYear);

        Assert.True(outcome.DidSettle);
        Assert.NotNull(outcome.Partition);
        Assert.Equal(2.5m, outcome.Partition!.PayoutDays);     // old code: 5.0m
        Assert.Equal(0m, outcome.Partition.ForfeitDays);
        Assert.Equal(0m, outcome.Partition.TransferDays);
        Assert.Equal("SETTLED", outcome.Row!.SettlementState);

        var row = await ReadActiveSettlementAsync(employeeId, AccrualYear);
        Assert.NotNull(row);
        Assert.Equal(2.5m, row!.Value.Payout);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        Assert.Equal(2.5m, snap.RootElement.GetProperty("earned").GetDecimal()); // old code: 5.0m
        Assert.Equal(0m, snap.RootElement.GetProperty("used").GetDecimal());
    }

    /// <summary>
    /// The cap composes with mid-year HIRE pro-ration: hired 1 Mar 2021, left 31 Aug 2021 ⇒
    /// Mar..Aug = 6 months ⇒ 2.5 (the old code: Mar..Dec = 10 months ⇒ 4.1667).
    /// </summary>
    [Fact]
    public async Task MidYearHireAndLeaver_EarnedIsHireToLeaveMonths()
    {
        var employeeId = await SeedEmployeeAsync();
        await SetEmploymentDatesAsync(employeeId, start: new DateOnly(AccrualYear, 3, 1), end: new DateOnly(AccrualYear, 8, 31));

        using var host = FixedClockHost(today: new DateOnly(AccrualYear, 8, 31));
        var outcome = await SettleAsync(host, employeeId, AccrualYear);

        Assert.True(outcome.DidSettle);
        Assert.Equal(2.5m, outcome.Partition!.PayoutDays); // old code: round(5 × 10/12, 2) = 4.17
    }

    // ════════════════════════════════════════════════════════════════════════
    // Idempotence at the rail — an end date AFTER the accrual end changes nothing.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// End date 31 Mar 2022 (after the 31 Dec 2021 accrual end), clock on the end date: the cap is
    /// a no-op — earned stays the full 5, payout 5, byte-identical to the S80 baseline.
    /// </summary>
    [Fact]
    public async Task EndDateAfterAccrualEnd_CapIsNoOp_FullQuotaUnchanged()
    {
        var employeeId = await SeedEmployeeAsync();
        var end = new DateOnly(AccrualYear + 1, 3, 31);
        await SetEmploymentEndDateAsync(employeeId, end);

        using var host = FixedClockHost(today: end);
        var outcome = await SettleAsync(host, employeeId, AccrualYear);

        Assert.True(outcome.DidSettle);
        Assert.Equal(5m, outcome.Partition!.PayoutDays);
        var row = await ReadActiveSettlementAsync(employeeId, AccrualYear);
        using var snap = JsonDocument.Parse(row!.Value.SnapshotJson);
        Assert.Equal(5m, snap.RootElement.GetProperty("earned").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // The S80 BLOCKER-1 guard is UNCHANGED — a PASSED end date still fails closed.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Same mid-year leaver, but the clock is a day AFTER the end date: the in-lock leaver guard
    /// fires first (NotDue — no row, no event). The cap only governs the reachable not-yet-passed
    /// case; the termination×godtgørelse interaction stays the R12 non-goal.
    /// </summary>
    [Fact]
    public async Task PassedEndDate_StillFailsClosed_NotDue_NoRow()
    {
        var employeeId = await SeedEmployeeAsync();
        var leaveDate = new DateOnly(AccrualYear, 6, 30);
        await SetEmploymentEndDateAsync(employeeId, leaveDate);

        using var host = FixedClockHost(today: leaveDate.AddDays(1));
        var outcome = await SettleAsync(host, employeeId, AccrualYear);

        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.Null(await ReadActiveSettlementAsync(employeeId, AccrualYear));
    }

    // ─────────────────────────────── drivers ───────────────────────────────

    /// <summary>
    /// A derived host whose <see cref="TimeProvider"/> is pinned to <paramref name="today"/> —
    /// the <c>SettleAsync</c> leaver guard reads Copenhagen "today" from it. Booted via
    /// <c>CreateClient</c> (the established fixed-clock idiom); the poller it starts settles
    /// nothing for these tuples (SPECIAL_HOLIDAY is R5-dormant by default).
    /// </summary>
    private WebApplicationFactory<Program> FixedClockHost(DateOnly today)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(today))));
        _ = derived.CreateClient();
        return derived;
    }

    private static async Task<SettlementOutcome> SettleAsync(WebApplicationFactory<Program> host, string employeeId, int accrualYear)
    {
        var service = host.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = host.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await service.SettleAsync(employeeId, SpecialHolidayType, accrualYear, YearEnd, conn, tx);
        await tx.CommitAsync();
        return outcome;
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s137_shcap_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Records the end date ONLY — is_active stays TRUE (the window is a date fact, ADR-040 D3).</summary>
    private Task SetEmploymentEndDateAsync(string employeeId, DateOnly endDate)
        => SetEmploymentDatesAsync(employeeId, start: null, end: endDate);

    private async Task SetEmploymentDatesAsync(string employeeId, DateOnly? start, DateOnly? end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.Add(new NpgsqlParameter("s", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)start ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("e", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)end ?? DBNull.Value });
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, decimal Payout, decimal Forfeit, string SnapshotJson)?>
        ReadActiveSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, payout_days, forfeit_days, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", SpecialHolidayType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetString(3));
    }
}
