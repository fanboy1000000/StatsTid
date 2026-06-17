using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Balance;   // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S80 / TASK-8002 (ADR-033 Slice 2, R4/R5/R6/R8) — Docker-gated integration tests for the
/// SPECIAL_HOLIDAY (særlige feriedage) §15 stk.2/§17 godtgørelse close. Two drive styles:
/// <list type="bullet">
///   <item><description><b>Direct <see cref="VacationSettlementService.SettleAsync"/> drive</b> (the
///   <c>VacationSettlementServiceTests</c> shape) — pins the R4 godtgørelse-only partition (SETTLED +
///   <c>payout_days = remainder</c>, NEVER PENDING_REVIEW/forfeit_days), the
///   <see cref="SaerligeFeriedagePaidOut"/> emission (R8), and replay-determinism.</description></item>
///   <item><description><b>Live-poller drive</b> (the <c>SettlementCloseServiceBoundaryTests</c> shape
///   with a fixed clock) — pins the R5 §15-stk.1 safety gate (DORMANT by default; settles only when
///   <c>Settlement:SpecialHolidaySettlementEnabled</c> is true) and the R6 30-Apr-(Y+2) boundary.</description></item>
/// </list>
///
/// <para>
/// The seeded SPECIAL_HOLIDAY config (DefaultEntitlementConfigs: quota 5, MONTHLY_ACCRUAL, reset_month
/// 1, carryover_max 0) is created by the Program.cs EntitlementConfigSeeder at host boot, so a
/// fully-accrued closed accrual year yields earned=5 — the godtgørelse marquee operand.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SpecialHolidaySettlementTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string SpecialHolidayType = "SPECIAL_HOLIDAY";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";

    // A long-closed SPECIAL_HOLIDAY accrual year — boundary 30 Apr 2023, firmly in the past under a
    // default clock for the direct drive.
    private const int ClosedAccrualYear = 2021;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot the seeders (SPECIAL_HOLIDAY config: quota 5 / reset_month 1).
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private VacationSettlementService Service => _factory.Services.GetRequiredService<VacationSettlementService>();

    // ════════════════════════════════════════════════════════════════════════
    // R4 (HARD) — the godtgørelse-only partition: SETTLED + payout = remainder, NEVER PENDING_REVIEW.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A fully-accrued, unconsumed closed SPECIAL_HOLIDAY year settles SETTLED with
    /// <c>payout_days = 5</c> (the whole §15 stk.2/§17 godtgørelse remainder) and transfer/forfeit 0 —
    /// and emits exactly ONE <see cref="SaerligeFeriedagePaidOut"/>. The R4 discriminator: it does NOT
    /// go through the §34 PENDING_REVIEW path the VACATION partition takes with CarryoverMax=0.
    /// </summary>
    [Fact]
    public async Task Settle_FullUnused_Godtgoerelse_Settled_NeverPendingReview()
    {
        var employeeId = await SeedEmployeeAsync();
        // No balance row, no absences ⇒ used 0; earned at the 31-Dec accrual end = full quota 5.

        var outcome = await SettleAsync(employeeId, ClosedAccrualYear);

        Assert.True(outcome.DidSettle);
        Assert.NotNull(outcome.Partition);
        Assert.Equal(5m, outcome.Partition!.PayoutDays);   // the whole remainder → godtgørelse
        Assert.Equal(0m, outcome.Partition.ForfeitDays);   // R4: NEVER a §34 forfeiture
        Assert.Equal(0m, outcome.Partition.TransferDays);  // no §21

        // The persisted row is SETTLED (NEVER PENDING_REVIEW) with payout 5 / forfeit 0.
        var row = await ReadActiveSettlementAsync(employeeId, ClosedAccrualYear);
        Assert.NotNull(row);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal(0m, row.Value.Transfer);
        Assert.Equal(5m, row.Value.Payout);
        Assert.Equal(0m, row.Value.Forfeit);

        // Exactly ONE godtgørelse event (R8) and NO §34/manual-review event.
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SaerligeFeriedagePaidOut"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
        // An ADR-026 audit row for the godtgørelse, scoped to THIS employee.
        Assert.True(await CountAsync(
            "audit_projection", "target_resource_id = @r", ("r", employeeId)) >= 1);
    }

    /// <summary>
    /// A partly-consumed closed SPECIAL_HOLIDAY year (used 2 within the 1-May-Y+1..30-Apr-Y+2 taking
    /// window) settles SETTLED with payout = remainder (5 − 2 = 3), still forfeit 0. The "used"
    /// operand is the recorded SPECIAL_HOLIDAY consumption within the TAKING window.
    /// </summary>
    [Fact]
    public async Task Settle_PartlyConsumed_PayoutIsRemainder_NeverForfeit()
    {
        var employeeId = await SeedEmployeeAsync();
        // 2 days taken in the taking window (Mar 2023, accrual year 2021 → window 1 May 2022 .. 30 Apr 2023).
        await SeedSpecialHolidayAbsenceAsync(employeeId, new DateOnly(2023, 3, 15), feriedage: 2m);

        var outcome = await SettleAsync(employeeId, ClosedAccrualYear);

        Assert.Equal(3m, outcome.Partition!.PayoutDays);
        Assert.Equal(0m, outcome.Partition.ForfeitDays);
        Assert.Equal("SETTLED", outcome.Row!.SettlementState);
    }

    /// <summary>
    /// Replay-determinism (R11/R12): the recorded godtgørelse payout equals the pure-function remainder
    /// of the captured snapshot's OWN stored operands (earned + carryoverIn − used − planned, clamped
    /// ≥ 0). Re-deriving from the stored snapshot reproduces the recorded bucket byte-identically.
    /// </summary>
    [Fact]
    public async Task Settle_RecordedGodtgoerelse_IsPureFunctionOfStoredSnapshot()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSpecialHolidayAbsenceAsync(employeeId, new DateOnly(2023, 2, 1), feriedage: 1m);

        var outcome = await SettleAsync(employeeId, ClosedAccrualYear);
        var row = await ReadActiveSettlementAsync(employeeId, ClosedAccrualYear);
        Assert.NotNull(row);

        using var snap = JsonDocument.Parse(row!.Value.SnapshotJson);
        var r = snap.RootElement;
        var earned = r.GetProperty("earned").GetDecimal();
        var used = r.GetProperty("used").GetDecimal();
        var planned = r.GetProperty("planned").GetDecimal();
        var carryoverIn = r.GetProperty("carryoverIn").GetDecimal();

        var expectedPayout = Math.Round(Math.Max(0m, earned + carryoverIn - used - planned), 2, MidpointRounding.ToEven);

        Assert.Equal(expectedPayout, row.Value.Payout);
        Assert.Equal(outcome.Partition!.PayoutDays, row.Value.Payout);
        // CarryoverIn is always 0 for SPECIAL_HOLIDAY (no §15 stk.1 modeled).
        Assert.Equal(0m, carryoverIn);
        // earned 5 − used 1 = 4.
        Assert.Equal(4m, row.Value.Payout);
    }

    /// <summary>
    /// Idempotent single-settle: settling the same SPECIAL_HOLIDAY tuple twice sequentially produces
    /// exactly ONE row + ONE godtgørelse event (the in-lock re-check; ADR-033 single-settle).
    /// </summary>
    [Fact]
    public async Task Settle_Twice_Sequential_ExactlyOneRowAndOneEvent()
    {
        var employeeId = await SeedEmployeeAsync();

        var first = await SettleAsync(employeeId, ClosedAccrualYear);
        var second = await SettleAsync(employeeId, ClosedAccrualYear);

        Assert.True(first.DidSettle);
        Assert.False(second.DidSettle);

        Assert.Equal(1L, await CountAsync(
            "vacation_settlements",
            "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            ("e", employeeId), ("t", SpecialHolidayType), ("y", ClosedAccrualYear)));
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SaerligeFeriedagePaidOut"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — the §15 stk.1 wrongful-payout safety gate (operation-level, default OFF).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With the SPECIAL_HOLIDAY settlement flag OFF (default) the live poller settles NOTHING for
    /// SPECIAL_HOLIDAY — even with a go-live date configured, the clock past the 30-Apr boundary, and a
    /// freshly-seeded accrual year. The §15-stk.1 dormant gate writes NO rows (NOT PENDING_REVIEW rows,
    /// which would consume due-detection). The VACATION pass still runs (so a poll demonstrably ran).
    /// </summary>
    [Fact]
    public async Task Poller_SpecialHolidayDisabled_SettlesNoSpecialHoliday()
    {
        var employeeId = await SeedEmployeeAsync();

        // Clock past every 30-Apr boundary; go-live configured (so VACATION settles — a poll witness);
        // SPECIAL_HOLIDAY flag UNSET ⇒ default OFF.
        BootFixedClockHost(new DateOnly(2026, 6, 1), goLiveDate: new DateOnly(2020, 1, 1), specialHolidayEnabled: null);

        // The VACATION pass settles the employee's closed VACATION years (a poll-ran witness).
        await WaitForAnyVacationSettlementAsync(employeeId, TimeSpan.FromSeconds(30));

        // NO SPECIAL_HOLIDAY row ever appears (dormant gate). Give the poll ample time, then assert absent.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0L, await CountSpecialHolidaySettlementsAsync(employeeId));
    }

    /// <summary>
    /// With the SPECIAL_HOLIDAY settlement flag ON (and a go-live date + the clock past the 30-Apr
    /// boundary), the live poller DOES settle the closed SPECIAL_HOLIDAY accrual year — exactly one
    /// SETTLED godtgørelse row appears.
    /// </summary>
    [Fact]
    public async Task Poller_SpecialHolidayEnabled_SettlesClosedAccrualYear()
    {
        var employeeId = await SeedEmployeeAsync();

        BootFixedClockHost(new DateOnly(2026, 6, 1), goLiveDate: new DateOnly(2020, 1, 1), specialHolidayEnabled: true);

        var settled = await WaitForSpecialHolidaySettlementAsync(employeeId, ClosedAccrualYear, TimeSpan.FromSeconds(30));
        Assert.True(settled,
            "With Settlement:SpecialHolidaySettlementEnabled=true the poller must settle the closed " +
            "SPECIAL_HOLIDAY accrual year (boundary 30 Apr 2023 has passed).");

        // SETTLED, never PENDING_REVIEW (R4 through the live poller).
        var row = await ReadSpecialHolidaySettlementAsync(employeeId, ClosedAccrualYear);
        Assert.NotNull(row);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal(5m, row.Value.Payout);
        Assert.Equal(0m, row.Value.Forfeit);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6 — the 30-Apr-(Y+2) boundary: accrual year Y is due only AFTER 30 Apr (Y+2).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The SPECIAL_HOLIDAY 30-Apr-(Y+2) boundary geometry through the live poller: with the flag ON, a
    /// clock on 1 May 2026 (just AFTER the 30-Apr-2026 boundary of accrual year 2024) settles accrual
    /// year 2024, while accrual year 2025 (boundary 30 Apr 2027, NOT yet passed) is NOT settled. This
    /// pins the exact 30-Apr-(Y+2) boundary, distinct from VACATION's 31-Dec geometry.
    /// </summary>
    [Fact]
    public async Task Poller_SpecialHolidayBoundary_30AprYPlus2_SettlesPassedNotFuture()
    {
        var employeeId = await SeedEmployeeAsync();

        // 1 May 2026 — just past the 30-Apr-2026 boundary (accrual year 2024), before 30-Apr-2027 (2025).
        BootFixedClockHost(new DateOnly(2026, 5, 1), goLiveDate: new DateOnly(2020, 1, 1), specialHolidayEnabled: true);

        var settled2024 = await WaitForSpecialHolidaySettlementAsync(employeeId, 2024, TimeSpan.FromSeconds(30));
        Assert.True(settled2024,
            "accrual year 2024 (boundary 30 Apr 2026, passed at 2026-05-01) must settle.");

        // accrual year 2025 — boundary 30 Apr 2027, NOT passed ⇒ never settled.
        Assert.Equal(0L, await CountSpecialHolidaySettlementsAsync(employeeId, 2025));
    }

    // ════════════════════════════════════════════════════════════════════════
    // VACATION close UNCHANGED — a regression pin alongside the SPECIAL_HOLIDAY close.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The same employee's VACATION and SPECIAL_HOLIDAY closes are independent and type-correct: the
    /// VACATION close of a fully-accrued unconsumed year is PENDING_REVIEW (the §34 candidate, the
    /// inherited S68 behavior — UNCHANGED), while the SPECIAL_HOLIDAY close of a fully-accrued
    /// unconsumed year is SETTLED with the godtgørelse payout. The two type passes do not perturb each
    /// other (R4 + the type-scoped enumeration/anti-join).
    /// </summary>
    [Fact]
    public async Task VacationClose_StillPendingReview_While_SpecialHolidayClose_Settled()
    {
        var employeeId = await SeedEmployeeAsync();

        // VACATION ferieår 2021 close (the S68 direct drive) — fully accrued, no consumption.
        await using (var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var vac = await Service.SettleAsync(employeeId, VacationType, 2021, YearEnd, conn, tx);
            await tx.CommitAsync();
            Assert.Equal("PENDING_REVIEW", vac.Row!.SettlementState);   // §34 candidate — UNCHANGED S68 behavior.
            Assert.Equal(20m, vac.Partition!.ForfeitDays);
        }

        // SPECIAL_HOLIDAY accrual year 2021 close — SETTLED godtgørelse.
        var sh = await SettleAsync(employeeId, ClosedAccrualYear);
        Assert.Equal("SETTLED", sh.Row!.SettlementState);
        Assert.Equal(5m, sh.Partition!.PayoutDays);
        Assert.Equal(0m, sh.Partition.ForfeitDays);
    }

    // ─────────────────────────────── drivers ───────────────────────────────

    /// <summary>One SPECIAL_HOLIDAY settlement pass in its OWN ReadCommitted tx, committed.</summary>
    // ════════════════════════════════════════════════════════════════════════
    // S80 Step-5a BLOCKER-1 — a SPECIAL_HOLIDAY tuple whose employee became a leaver
    // (end date passed) under the lock must FAIL CLOSED (NotDue), never settle/pay — the
    // SPECIAL_HOLIDAY×termination godtgørelse interaction is an R12 non-goal. (RED before the fix:
    // the branch was placed BEFORE the leaver guard, so it paid the godtgørelse anyway.)
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Settle_SpecialHolidayLeaver_FailsClosed_NotDue_NoRow_NoEvent()
    {
        var employeeId = await SeedEmployeeAsync();
        // End date in the past ⇒ the in-lock leaver guard fires before any SPECIAL_HOLIDAY settle.
        await SetEmploymentEndDateAsync(employeeId, new DateOnly(ClosedAccrualYear + 1, 6, 30));

        var outcome = await SettleAsync(employeeId, ClosedAccrualYear);

        Assert.False(outcome.DidSettle); // NotDueUnderLock — fail-closed
        Assert.Equal(0L, await CountAsync("vacation_settlements",
            "employee_id = @e AND entitlement_type = @t AND settlement_state <> 'REVERSED'",
            ("e", employeeId), ("t", SpecialHolidayType)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SaerligeFeriedagePaidOut"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
    }

    // S80 Step-5a BLOCKER-2 — SPECIAL_HOLIDAY supersession fails CLOSED at the top of
    // VacationSettlementService.ResettleSupersedingAsync (NEVER routing to SettleActiveYearEndAsync /
    // the shared §34 Partition). NOT directly unit-tested here: `ResettleSupersedingAsync` is internal
    // (the reversal tests drive the public SettlementReversalService), AND the path is UNREACHABLE in
    // 8002 — the SPECIAL_HOLIDAY close is R5-gated DORMANT, so no SPECIAL_HOLIDAY settlement exists to
    // reverse+supersede. The fail-closed guard is the first statement after the user re-read (verified
    // by the re-Step-5a). A dedicated SPECIAL_HOLIDAY-supersession path is a recorded follow-up.

    private async Task<SettlementOutcome> SettleAsync(string employeeId, int accrualYear)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await Service.SettleAsync(employeeId, SpecialHolidayType, accrualYear, YearEnd, conn, tx);
        await tx.CommitAsync();
        return outcome;
    }

    /// <summary>Marks the employee a leaver (end date + is_active=false) for the BLOCKER-1 leaver guard.</summary>
    private async Task SetEmploymentEndDateAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_end_date = @d, is_active = false WHERE user_id = @e", conn);
        cmd.Parameters.AddWithValue("d", endDate);
        cmd.Parameters.AddWithValue("e", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Boots a fixed-clock host with the go-live date + the SPECIAL_HOLIDAY flag.</summary>
    private void BootFixedClockHost(DateOnly fixedDate, DateOnly? goLiveDate, bool? specialHolidayEnabled)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
        {
            var cfg = new Dictionary<string, string?>();
            if (goLiveDate is not null)
                cfg["Settlement:GoLiveDate"] = goLiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (specialHolidayEnabled is not null)
                cfg["Settlement:SpecialHolidaySettlementEnabled"] = specialHolidayEnabled.Value ? "true" : "false";
            if (cfg.Count > 0)
                builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg));
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate)));
        });
        _ = derived.CreateClient(); // triggers host build + hosted-service start (immediate poll).
    }

    // ─────────────────────────────── waits ───────────────────────────────

    private async Task<bool> WaitForSpecialHolidaySettlementAsync(string employeeId, int year, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await CountSpecialHolidaySettlementsAsync(employeeId, year) >= 1) return true;
            await Task.Delay(250);
        }
        return false;
    }

    private async Task WaitForAnyVacationSettlementAsync(string employeeId, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await CountAsync(
                "vacation_settlements",
                "employee_id = @e AND entitlement_type = @t AND settlement_state <> 'REVERSED'",
                ("e", employeeId), ("t", VacationType)) >= 1) return;
            await Task.Delay(250);
        }
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s80_sh_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Seeds a recorded SPECIAL_HOLIDAY consumption (absence_type SPECIAL_HOLIDAY_ALLOWANCE) in
    /// absences_projection — the godtgørelse "used" operand (consumption within the taking window).</summary>
    private async Task SeedSpecialHolidayAbsenceAsync(string employeeId, DateOnly date, decimal feriedage)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, outbox_id)
            VALUES (gen_random_uuid(), @e, @d, 'SPECIAL_HOLIDAY_ALLOWANCE', 7.4, @f,
                    'AC', 'OK24', NOW(),
                    (SELECT COALESCE(MAX(outbox_id), 0) + 1 FROM absences_projection))
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("f", feriedage);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, decimal Transfer, decimal Payout, decimal Forfeit, string SnapshotJson)?>
        ReadActiveSettlementAsync(string employeeId, int year)
        => await ReadSettlementAsync(employeeId, SpecialHolidayType, year);

    private async Task<(string State, decimal Transfer, decimal Payout, decimal Forfeit, string SnapshotJson)?>
        ReadSpecialHolidaySettlementAsync(string employeeId, int year)
        => await ReadSettlementAsync(employeeId, SpecialHolidayType, year);

    private async Task<(string State, decimal Transfer, decimal Payout, decimal Forfeit, string SnapshotJson)?>
        ReadSettlementAsync(string employeeId, string type, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, transfer_days, payout_days, forfeit_days, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", type);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetString(4));
    }

    private async Task<long> CountSpecialHolidaySettlementsAsync(string employeeId, int? year = null)
    {
        var where = "employee_id = @e AND entitlement_type = @t AND settlement_state <> 'REVERSED'"
            + (year is null ? "" : " AND entitlement_year = @y");
        var ps = year is null
            ? new (string, object)[] { ("e", employeeId), ("t", SpecialHolidayType) }
            : new (string, object)[] { ("e", employeeId), ("t", SpecialHolidayType), ("y", year.Value) };
        return await CountAsync("vacation_settlements", where, ps);
    }

    private async Task<long> CountAsync(string table, string whereClause, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table} WHERE {whereClause}", conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountOutboxByTypeAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
