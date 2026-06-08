using System.Data;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S68 / TASK-6808 (ADR-033 D3/D5/D6) — Docker-gated integration tests for the atomic
/// vacation-settlement pass <see cref="VacationSettlementService.SettleAsync"/>, driven the EXACT
/// way the TASK-6805 <c>SettlementCloseService</c> drives it: resolve the DI-registered service
/// from the booted Backend.Api host, open a connection, begin ONE ReadCommitted tx, call
/// <c>SettleAsync</c> (which neither commits nor rolls back), then the test owns commit/rollback.
///
/// <para>Covers the prompt's scenarios 1 (partition-via-SettleAsync against the seeded
/// VACATION config — quota 25, carryover_max 5, reset_month 9), 2 (marquee replay-determinism —
/// re-reading the recorded row yields byte-identical quantities), 3 (atomic/forced-rollback — a
/// failure mid-pass persists NOTHING), 4 (idempotent single-settle — settling the same tuple twice
/// sequentially yields exactly ONE row + ONE event family), 5 (missing-balance-row — an employee
/// with no <c>entitlement_balances</c> row still settles zero-state), and 6 (first-non-zero
/// carryover_in — a §21 settlement writes next-year carryover_in, and the booking guard admits
/// <c>used</c> up to the raised ceiling).</para>
///
/// <para>Mirrors the <see cref="EmployeeProfile.Adr032RevaluationTests"/> / <c>YearOverviewTests</c>
/// harness: <see cref="TestFixtures.DockerHarness"/> + <see cref="StatsTidWebApplicationFactory"/>
/// + <see cref="RegressionSeed"/>. The seeded VACATION entitlement config (DefaultEntitlementConfigs:
/// quota 25, MONTHLY_ACCRUAL, reset_month 9, carryover_max 5) is created by the Program.cs
/// EntitlementConfigSeeder at host boot, so a fully-accrued closed ferieår yields earned=25 — the
/// prompt's marquee operands.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class VacationSettlementServiceTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";

    // A long-closed ferieår so a default-clock SettleAsync always treats it as settleable and the
    // boundary is firmly in the past. Ferieår 2021 = Sep 2021 .. Aug 2022 (reset_month 9).
    private const int ClosedYear = 2021;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot the host (runs the entitlement-config + employee-profile seeders) so the VACATION
        // config (quota 25 / carryover_max 5 / reset_month 9) is present for SettleAsync's snapshot.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private VacationSettlementService Service => _factory.Services.GetRequiredService<VacationSettlementService>();

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 1 — partition through the real pass: earned 25 / used 0 / cap 5 ⇒
    //   under_cap 5 (§24 payout default), over_cap 20 (§34 forfeit) ⇒ PENDING_REVIEW.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A fully-accrued closed VACATION year with NO consumption and NO §21 agreement settles to
    /// transfer=0, payout=5 (§24 default for the under-cap tranche), forfeit=20 (§34-candidate),
    /// state PENDING_REVIEW (the §34 candidate must NOT auto-forfeit, ADR-033 D10). The persisted
    /// row carries exactly those buckets.
    /// </summary>
    [Fact]
    public async Task Settle_FullEntitlement_NoAgreement_PayoutsUnderCap_FlagsForfeit_PendingReview()
    {
        var employeeId = await SeedEmployeeAsync();
        // No balance row, no absences ⇒ used 0; earned at the closed boundary = full quota 25.

        var outcome = await SettleAsync(employeeId, ClosedYear);

        Assert.True(outcome.DidSettle);
        Assert.NotNull(outcome.Partition);
        Assert.Equal(0m, outcome.Partition!.TransferDays);
        Assert.Equal(5m, outcome.Partition.PayoutDays);     // §24 default
        Assert.Equal(20m, outcome.Partition.ForfeitDays);   // §34-candidate (== D9 expiring)
        Assert.Equal("PENDING_REVIEW", outcome.Row.SettlementState);

        // The persisted row reflects the same buckets.
        var row = await ReadActiveSettlementAsync(employeeId, ClosedYear);
        Assert.NotNull(row);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(0m, row.Value.Transfer);
        Assert.Equal(5m, row.Value.Payout);
        Assert.Equal(20m, row.Value.Forfeit);
        Assert.Equal(1, row.Value.Sequence);
        Assert.Equal(1L, row.Value.Version);
    }

    /// <summary>
    /// With a §21 written-transfer agreement of 5 for the closed year, the under-cap tranche
    /// transfers instead of paying out: transfer=5, payout=0, forfeit=20 (still PENDING_REVIEW for
    /// the §34 candidate). The next-year (<c>ClosedYear+1</c>) carryover_in is written = 5 (D6).
    /// </summary>
    [Fact]
    public async Task Settle_With21Agreement_Transfers_NoPayout_WritesNextYearCarryover()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(employeeId, ClosedYear, transferDays: 5m);

        var outcome = await SettleAsync(employeeId, ClosedYear);

        Assert.Equal(5m, outcome.Partition!.TransferDays);
        Assert.Equal(0m, outcome.Partition.PayoutDays);
        Assert.Equal(20m, outcome.Partition.ForfeitDays);

        // §21 carryover written to next year's balance (ADR-033 D6).
        var nextYear = await ReadBalanceAsync(employeeId, VacationType, ClosedYear + 1);
        Assert.NotNull(nextYear);
        Assert.Equal(5m, nextYear!.Value.CarryoverIn);
    }

    /// <summary>
    /// A fully-consumed closed year (used=25) settles SETTLED (no §34 candidate) with every bucket
    /// zero — and NO next-year carryover row is written (the WARNING-1 skip: transfer 0 must not
    /// CLOBBER next-year carryover_in).
    /// </summary>
    [Fact]
    public async Task Settle_FullyConsumed_AllZero_Settled_NoCarryoverWrite()
    {
        var employeeId = await SeedEmployeeAsync();
        // Seed the closed-year balance with used = 25 (the whole quota consumed).
        await SeedBalanceAsync(employeeId, VacationType, ClosedYear, used: 25m, planned: 0m, carryoverIn: 0m);

        var outcome = await SettleAsync(employeeId, ClosedYear);

        Assert.Equal(0m, outcome.Partition!.TransferDays);
        Assert.Equal(0m, outcome.Partition.PayoutDays);
        Assert.Equal(0m, outcome.Partition.ForfeitDays);
        Assert.Equal("SETTLED", outcome.Row.SettlementState);

        // No next-year carryover row materialized (transfer 0 ⇒ skipped, not a 0-clobber).
        var nextYear = await ReadBalanceAsync(employeeId, VacationType, ClosedYear + 1);
        Assert.Null(nextYear);
    }

    /// <summary>
    /// A half-timer settles to the SAME 25/5 flat day-count as a full-timer (ADR-031:
    /// part-time-fraction-INDEPENDENT VACATION day-count). Seeding a 0.5 part_time_fraction
    /// employee with no consumption still yields earned=25 at the boundary ⇒ under_cap 5 / over_cap
    /// 20, identical to the full-timer's settlement.
    /// </summary>
    [Fact]
    public async Task Settle_HalfTimer_SameFlatDayCount_AsFullTimer()
    {
        var employeeId = await SeedEmployeeAsync(partTimeFraction: 0.5m);

        var outcome = await SettleAsync(employeeId, ClosedYear);

        // ADR-031 flat: the half-timer's earned-at-boundary is still 25 (fraction 1.0 in the day basis).
        Assert.Equal(5m, outcome.Partition!.PayoutDays);
        Assert.Equal(20m, outcome.Partition.ForfeitDays);
        Assert.Equal("PENDING_REVIEW", outcome.Row.SettlementState);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 5 — missing-balance-row: still enumerated + settled (zero-state).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An employee with NO <c>entitlement_balances</c> row for the closed year settles zero-state:
    /// used=0 (no recorded consumption) ⇒ the full earned 25 disposable ⇒ payout 5 / forfeit 20.
    /// The pass does not require a pre-existing balance row (Codex W4: enumeration is config-driven,
    /// not balance-driven; the snapshot's used defaults to 0 on a missing row).
    /// </summary>
    [Fact]
    public async Task Settle_NoBalanceRow_StillSettlesZeroState()
    {
        var employeeId = await SeedEmployeeAsync();

        // Confirm there is genuinely no closed-year balance row before settling.
        Assert.Null(await ReadBalanceAsync(employeeId, VacationType, ClosedYear));

        var outcome = await SettleAsync(employeeId, ClosedYear);

        Assert.True(outcome.DidSettle);
        Assert.Equal(5m, outcome.Partition!.PayoutDays);
        Assert.Equal(20m, outcome.Partition.ForfeitDays);
        Assert.NotNull(await ReadActiveSettlementAsync(employeeId, ClosedYear));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 2 — marquee replay-determinism: the recorded quantities are a pure function of the
    //   captured snapshot; re-reading the row reproduces them byte-identically.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The recorded disposition is a pure function of the captured snapshot: re-reading the
    /// persisted settlement row's recorded buckets equal the PURE-function partition of the captured
    /// snapshot's OWN recorded operands (the §21/§24/§34 formula re-applied to the stored snapshot
    /// numbers reproduces the recorded buckets byte-identically). Settlement does not re-derive after
    /// capture (ADR-033 D3); the recorded disposition is a function of the snapshot, so re-deriving
    /// it from the snapshot the row stored is the replay-determinism proof. (The internal
    /// <c>Partition</c> is exercised directly in the Unit-project partition tests; here we re-derive
    /// via the documented formula since the Regression project has no internals access.)
    /// </summary>
    [Fact]
    public async Task Settle_RecordedQuantities_ArePureFunctionOfStoredSnapshot()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedBalanceAsync(employeeId, VacationType, ClosedYear, used: 4m, planned: 0m, carryoverIn: 2m);

        var outcome = await SettleAsync(employeeId, ClosedYear);
        var row = await ReadActiveSettlementAsync(employeeId, ClosedYear);
        Assert.NotNull(row);

        // Deserialize the STORED snapshot and re-derive the partition from its OWN recorded operands
        // (the documented ADR-033 D5/D10 formula; ToEven rounding to 2dp == the D9 reader).
        using var snap = JsonDocument.Parse(row!.Value.SnapshotJson);
        var sRoot = snap.RootElement;
        var earned = sRoot.GetProperty("earned").GetDecimal();
        var used = sRoot.GetProperty("used").GetDecimal();
        var planned = sRoot.GetProperty("planned").GetDecimal();
        var carryoverIn = sRoot.GetProperty("carryoverIn").GetDecimal();
        var carryoverMax = sRoot.GetProperty("carryoverMax").GetDecimal();
        var transferAgreementDays = sRoot.GetProperty("transferAgreementDays").GetDecimal();

        var disposable = Math.Max(0m, earned + carryoverIn - used - planned);
        var underCap = Math.Min(disposable, carryoverMax);
        var overCap = Math.Max(0m, disposable - carryoverMax);
        var expectedTransfer = Round2(Math.Max(0m, Math.Min(transferAgreementDays, underCap)));
        var expectedPayout = Round2(underCap - Math.Max(0m, Math.Min(transferAgreementDays, underCap)));
        var expectedForfeit = Round2(overCap);

        // The recorded buckets equal the pure-function partition of the captured snapshot.
        Assert.Equal(expectedTransfer, row.Value.Transfer);
        Assert.Equal(expectedPayout, row.Value.Payout);
        Assert.Equal(expectedForfeit, row.Value.Forfeit);
        // And the recorded row equals the SettleAsync outcome partition (no drift live-vs-stored).
        Assert.Equal(outcome.Partition!.TransferDays, row.Value.Transfer);
        Assert.Equal(outcome.Partition.PayoutDays, row.Value.Payout);
        Assert.Equal(outcome.Partition.ForfeitDays, row.Value.Forfeit);

        // earned + carryoverIn − used − planned − cap = 25 + 2 − 4 − 0 − 5 = 18 forfeit; under-cap 5.
        Assert.Equal(18m, row.Value.Forfeit);
        Assert.Equal(5m, row.Value.Payout);
    }

    /// <summary>2dp rounding matching the D9 reader (ToEven) — the partition's bucket rounding.</summary>
    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 3 — atomic / forced-rollback: a failure mid-pass persists NOTHING.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The settlement tx is all-or-nothing (ADR-018 D3). When the test ROLLS BACK after a successful
    /// <c>SettleAsync</c> (simulating a failure between the pass and the commit — <c>SettleAsync</c>
    /// itself never commits), NOTHING persists: no <c>vacation_settlements</c> row, no
    /// <c>vacation_settlement_audit</c> row, no next-year carryover, no outbox event, no
    /// <c>audit_projection</c> row. A fresh connection (post-rollback MVCC) sees none of it.
    /// </summary>
    [Fact]
    public async Task Settle_ForcedRollback_PersistsNothing()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(employeeId, ClosedYear, transferDays: 5m); // exercises the carryover + event paths

        await using (var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var outcome = await Service.SettleAsync(employeeId, VacationType, ClosedYear, YearEnd, conn, tx);
            Assert.True(outcome.DidSettle); // the pass succeeded in-tx (writes are visible in-tx only)
            // Simulate a failure AFTER the pass but BEFORE commit: roll the whole tx back.
            await tx.RollbackAsync();
        }

        // Fresh-connection assertions — NOTHING leaked outside the rolled-back tx.
        Assert.Null(await ReadActiveSettlementAsync(employeeId, ClosedYear));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlement_audit", "employee_id = @e", ("e", employeeId)));
        Assert.Null(await ReadBalanceAsync(employeeId, VacationType, ClosedYear + 1)); // no carryover row
        Assert.Equal(0L, await CountAsync(
            "outbox_events", "stream_id = @s", ("s", $"employee-{employeeId}")));
        // Scope to THIS employee (target_resource_id = employee_id; the settlement audit mappers) —
        // the live SettlementCloseService poller legitimately settles the seeded init.sql users with
        // the SAME system actor_id, so an actor_id-only filter would (correctly) see those.
        Assert.Equal(0L, await CountAsync(
            "audit_projection", "target_resource_id = @r", ("r", employeeId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 4 — idempotent single-settle: same tuple twice (sequential) ⇒ exactly ONE row + ONE
    //   event family (the in-lock re-check; ADR-033 single-settle).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Settling the SAME due tuple twice sequentially (two committed passes, mirroring two poller
    /// runs) produces exactly ONE active settlement row and ONE event family: the second call is an
    /// idempotent no-op (the in-lock re-check finds the active row first). DidSettle is true on the
    /// first call, false on the second.
    /// <para>(A concurrent-poller test is not run here — the harness drives passes sequentially; the
    /// in-lock re-check + the partial-unique 23505 backstop, exercised in the
    /// <c>SettlementCloseService</c> boundary suite via the live poller, are the concurrent
    /// correctness path. DECLARED.)</para>
    /// </summary>
    [Fact]
    public async Task Settle_Twice_Sequential_ExactlyOneRowAndOneEventFamily()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(employeeId, ClosedYear, transferDays: 5m); // emits a carryover event

        var first = await SettleAsync(employeeId, ClosedYear);
        var second = await SettleAsync(employeeId, ClosedYear);

        Assert.True(first.DidSettle);
        Assert.False(second.DidSettle); // idempotent no-op (active settlement already exists)

        // Exactly ONE settlement row (any sequence) for the tuple.
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements",
            "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            ("e", employeeId), ("t", VacationType), ("y", ClosedYear)));

        // Exactly ONE carryover event family on the employee stream (no double-emit on the re-settle).
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted"));
        // And exactly ONE manual-review-flagged event (the §34 candidate flagged once).
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 6 — first-non-zero carryover_in: a §21 settlement raises next-year's bookable
    //   ceiling, and the booking guard admits used up to that raised ceiling.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After a §21 settlement writes carryover_in=5 to next year, the atomic booking guard
    /// (<see cref="EntitlementBalanceRepository.CheckAndAdjustAsync"/>) admits <c>used</c> up to the
    /// RAISED ceiling (<c>guardCap + carryover_in</c>): booking the full quota 25 PLUS the 5 carried
    /// days succeeds (30 ≤ 25 + 5), while a 31st day is refused. This pins the carryover_in being
    /// live on next year's balance, not merely a display number.
    /// </summary>
    [Fact]
    public async Task Settle_NextYearCarryover_RaisesBookingCeiling()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(employeeId, ClosedYear, transferDays: 5m);

        await SettleAsync(employeeId, ClosedYear);
        var nextYear = ClosedYear + 1;
        var balanceRepo = _factory.Services.GetRequiredService<EntitlementBalanceRepository>();

        // guardCap = the annual bookable limit EXCLUDING carryover (25); the guard adds carryover_in
        // (5) once ⇒ ceiling 30. Booking 30 succeeds; the 31st day is refused.
        var (ok30, used30) = await balanceRepo.CheckAndAdjustAsync(
            employeeId, VacationType, nextYear, deltaDays: 30m, guardCap: 25m, seedQuota: 25m);
        Assert.True(ok30);
        Assert.Equal(30m, used30);

        var (ok1More, _) = await balanceRepo.CheckAndAdjustAsync(
            employeeId, VacationType, nextYear, deltaDays: 1m, guardCap: 25m, seedQuota: 25m);
        Assert.False(ok1More); // 31 > 25 + 5 — the raised ceiling is respected, not exceeded
    }

    // ════════════════════════════════════════════════════════════════════════
    // The atomic write set lands (positive control for the rollback test).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Positive control for the all-or-nothing contract: a COMMITTED §21 settlement lands the FULL
    /// write set in the one tx — the settlement row + its CREATED audit row + the next-year
    /// carryover + the outbox event family + the audit_projection rows. (The rollback test asserts
    /// the complement: none of these survive a rolled-back pass.)
    /// </summary>
    [Fact]
    public async Task Settle_Committed_WritesFullAtomicSet()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(employeeId, ClosedYear, transferDays: 5m);

        await SettleAsync(employeeId, ClosedYear);

        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        // CREATED audit row for the settlement.
        Assert.True(await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND action = 'CREATED'", ("e", employeeId)) >= 1);
        // Next-year carryover present.
        var nextYear = await ReadBalanceAsync(employeeId, VacationType, ClosedYear + 1);
        Assert.Equal(5m, nextYear!.Value.CarryoverIn);
        // The outbox carries the carryover + flagged events; audit_projection has matching rows.
        Assert.True(await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted") >= 1);
        // Scope to THIS employee (target_resource_id = employee_id) so the assertion is isolated
        // from the live poller's settlements of the seeded init.sql users.
        Assert.True(await CountAsync(
            "audit_projection", "target_resource_id = @r", ("r", employeeId)) >= 1);
    }

    // ─────────────────────────────── drivers ───────────────────────────────

    /// <summary>Drives one settlement pass in its OWN ReadCommitted tx and COMMITS — the exact
    /// SettlementCloseService shape (open conn, begin tx, SettleAsync, commit).</summary>
    private async Task<SettlementOutcome> SettleAsync(string employeeId, int year)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await Service.SettleAsync(employeeId, VacationType, year, YearEnd, conn, tx);
        await tx.CommitAsync();
        return outcome;
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(decimal partTimeFraction = 1.000m)
    {
        var employeeId = "emp_s68_settle_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "AC", "OK24", partTimeFraction);
        return employeeId;
    }

    private async Task SeedBalanceAsync(
        string employeeId, string entitlementType, int year, decimal used, decimal planned, decimal carryoverIn)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year,
                 total_quota, used, planned, carryover_in, updated_at)
            VALUES
                (gen_random_uuid(), @e, @t, @y, 25, @used, @planned, @carryover, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
                DO UPDATE SET used = EXCLUDED.used, planned = EXCLUDED.planned,
                              carryover_in = EXCLUDED.carryover_in, updated_at = NOW()
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("used", used);
        cmd.Parameters.AddWithValue("planned", planned);
        cmd.Parameters.AddWithValue("carryover", carryoverIn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTransferAgreementAsync(string employeeId, int year, decimal transferDays)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_transfer_agreements
                (employee_id, entitlement_year, entitlement_type, transfer_days, agreement_date, recorded_by, version)
            VALUES (@e, @y, @t, @days, @date, 'test-seed-hr', 1)
            ON CONFLICT (employee_id, entitlement_year, entitlement_type)
                DO UPDATE SET transfer_days = EXCLUDED.transfer_days
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("days", transferDays);
        // Within the §21 deadline of the closed year (31 Dec of the ferieår-end year, E+1).
        cmd.Parameters.AddWithValue("date", new DateOnly(year + 1, 6, 30));
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, decimal Transfer, decimal Payout, decimal Forfeit, int Sequence, long Version, string SnapshotJson)?>
        ReadActiveSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, transfer_days, payout_days, forfeit_days, sequence, version, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3),
                reader.GetInt32(4), reader.GetInt64(5), reader.GetString(6));
    }

    private async Task<(decimal CarryoverIn, decimal Used)?> ReadBalanceAsync(
        string employeeId, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT carryover_in, used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetDecimal(0), reader.GetDecimal(1));
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
