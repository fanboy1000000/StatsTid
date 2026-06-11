using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S70 / TASK-7004 (ADR-033 slice 3a; SPRINT-70 R4/R5/R6/R7b/R8/R9d/R10/R12) — Docker-gated
/// integration tests for the crystallized TERMINATION settlement record in
/// <see cref="VacationSettlementService.SettleAsync"/> plus the two endpoint 422 guards:
///
/// <list type="bullet">
///   <item><description><b>R9d/R5:</b> an <c>is_active=FALSE</c> leaver settles atomically
///   (terminated-inclusive in-tx read) — SETTLED with ALL bucket columns zero, the crystallized
///   §26 day-count snapshot-only, <c>TerminationSettled</c> emitted (R10) + ADR-026 audit, NO
///   payroll line / §24 staging (ADR-033 D1/D13);</description></item>
///   <item><description><b>R5:</b> negative pre-clamp ⇒ PENDING_REVIEW with the |pre-clamp|
///   forfeit-FLAG (S68 convention), parked; non-zero carryover INCLUDED; a post-end-date booking
///   does NOT consume (the declared balance.Used divergence); replay parity (snapshot-only
///   re-derivation byte-identical after live-record mutations);</description></item>
///   <item><description><b>R4:</b> a leaver's OTHER due ferieår gets the fail-closed
///   deferred-disposition PENDING_REVIEW row (full-disposable flag + <c>DeferredDisposition</c>
///   marker + NO carryover + NO VacationAutoPaidOut/VacationCarryoverExecuted), while a
///   FUTURE-dated end date still auto-partitions normally; and the no-carryover-writes invariant
///   (FORFEIT and DEFER on a deferred-disposition row leave carryover untouched — replacing the
///   former DEFER-guard);</description></item>
///   <item><description><b>R7b:</b> a TERMINATION colliding with an active YEAR_END row is
///   REFUSED — <c>SettlementManualReviewFlagged</c> + audit emitted, NO row written, no throw
///   (flagged-ONCE rides the R3 any-trigger anti-join; the in-tx guard is the race
///   backstop);</description></item>
///   <item><description><b>R8:</b> a non-REVERSED TERMINATION row (SETTLED and PENDING_REVIEW)
///   suppresses the YEAR_END due-check for its tuple against the LIVE
///   <c>SettlementCloseService</c> anti-join;</description></item>
///   <item><description><b>R5 guards:</b> manual-resolve AND reconcile-payout 422-reject
///   <c>trigger=TERMINATION</c> rows in 3a.</description></item>
/// </list>
///
/// <para>Harness/JWT/seeding conventions mirror <see cref="VacationSettlementServiceTests"/> +
/// <see cref="EmploymentEndDateLifecycleTests"/> (Docker harness, direct seeding, PAT-008
/// FixedTimeProvider for the close-service poll in the R8 pin). Seeded VACATION config: quota 25,
/// MONTHLY_ACCRUAL, reset_month 9, carryover_max 5 ⇒ end date 2026-02-28 lies in ferieår 2025
/// (Sep 2025 .. Aug 2026), whole months Sep..Feb = 6 ⇒ earned = 25 × 6/12 = 12.5.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TerminationSettlementTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";

    /// <summary>End date 2026-02-28 ⇒ R6 ferieår 2025 (month 2 &lt; 9 ⇒ 2026 − 1); 6 whole months
    /// (Sep..Feb) ⇒ earned 12.5 under the seeded MONTHLY_ACCRUAL quota 25.</summary>
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int EndDateFerieaar = 2025;

    /// <summary>A long-closed prior ferieår for the R4 leaver other-ferieår scenarios (the
    /// VacationSettlementServiceTests convention).</summary>
    private const int PriorClosedYear = 2021;

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

    /// <summary>Boots the default host (seeders) and resolves the DI-registered service — the
    /// SettlementCloseService drive shape.</summary>
    private VacationSettlementService BootService()
    {
        _ = _factory.CreateClient();
        return _factory.Services.GetRequiredService<VacationSettlementService>();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9d + R5 + R10 — the marquee: an is_active=FALSE leaver settles atomically; SETTLED row
    // shape (all buckets zero, CrystallizedDays snapshot-only); TerminationSettled + audit in-tx.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_InactiveLeaver_Settles_ZeroBuckets_SnapshotCrystallized()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // is_active=FALSE — the R9d proof

        var outcome = await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        Assert.True(outcome.DidSettle);
        Assert.False(outcome.RefusedConflict);
        Assert.Null(outcome.Partition); // no §21/§24/§34 partition on a TERMINATION

        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.NotNull(row);
        Assert.Equal(Termination, row!.Value.Trigger);
        Assert.Equal("SETTLED", row.Value.State);
        // R5 pinned row shape: ALL bucket columns zero on a SETTLED TERMINATION.
        Assert.Equal(0m, row.Value.Transfer);
        Assert.Equal(0m, row.Value.Payout);
        Assert.Equal(0m, row.Value.Forfeit);
        Assert.Equal(1, row.Value.Sequence);
        Assert.Equal(1L, row.Value.Version);

        // The crystallized §26 quantity lives in the snapshot ONLY: 25 × 6/12 = 12.5.
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        var root = snap.RootElement;
        Assert.Equal(12.5m, root.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal("S26_WHOLE_MONTH", root.GetProperty("crystallizationBasis").GetString());
        Assert.Equal(EndDate.ToString("yyyy-MM-dd"), root.GetProperty("terminationDate").GetString());
        Assert.Equal(12.5m, root.GetProperty("earned").GetDecimal());
        Assert.Equal(0m, root.GetProperty("used").GetDecimal());
        // The valuation boundary is the END DATE (not the ferieår end).
        Assert.Equal(EndDate.ToString("yyyy-MM-dd"), root.GetProperty("settlementBoundaryDate").GetString());
        // NOT a deferred-disposition row (the marker is omitted-when-false).
        Assert.False(root.TryGetProperty("deferredDisposition", out var dd) && dd.GetBoolean());

        // R10 — TerminationSettled emitted (emitted-no-consumer) + the ADR-026 audit row, in-tx.
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
        Assert.Equal(1L, await CountAsync(
            "audit_projection", "event_type = 'TerminationSettled' AND target_resource_id = @r", ("r", employeeId)));
        Assert.True(await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND action = 'CREATED'", ("e", employeeId)) >= 1);

        // Money/staging leak guard (ADR-033 D1; SPRINT-70 "no line emission"): no §24 events, no
        // §21 events, no export lines, no next-year carryover.
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationAutoPaidOut"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted"));
        Assert.Equal(0L, await CountAsync("settlement_export_lines", "employee_id = @e", ("e", employeeId)));
        Assert.Null(await ReadCarryoverInAsync(employeeId, EndDateFerieaar + 1));
        // SETTLED pre-clamp ≥ 0 ⇒ no manual-review flag.
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — negative pre-clamp ⇒ PENDING_REVIEW forfeit-FLAG |pre-clamp|; the row PARKS.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>End date 2025-09-30 ⇒ 1 whole month ⇒ earned 25/12 ≈ 2.0833; 5 recorded days
    /// taken ≤ the end date ⇒ pre-clamp round2(2.0833 − 5) = −2.92 ⇒ PENDING_REVIEW with
    /// forfeit_days = 2.92 (the S68 flag convention), transfer = payout = 0, CrystallizedDays 0.
    /// Both TerminationSettled AND SettlementManualReviewFlagged are emitted.</summary>
    [Fact]
    public async Task Termination_NegativePreClamp_PendingReview_ForfeitFlagIsAbsoluteValue()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        var earlyEndDate = new DateOnly(2025, 9, 30); // ferieår 2025, 1 whole month
        for (var i = 0; i < 5; i++)
            await SeedAbsenceAsync(employeeId, new DateOnly(2025, 9, 8 + i), feriedage: 1.0m);
        await MarkLeaverAsync(employeeId, earlyEndDate);

        var outcome = await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        Assert.True(outcome.DidSettle);
        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.NotNull(row);
        Assert.Equal(Termination, row!.Value.Trigger);
        Assert.Equal("PENDING_REVIEW", row.Value.State);
        Assert.Equal(0m, row.Value.Transfer);
        Assert.Equal(0m, row.Value.Payout);
        Assert.Equal(2.92m, row.Value.Forfeit); // |round2(2.0833... − 5)| = 2.92 (ToEven)

        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        Assert.Equal(0m, snap.RootElement.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal(5m, snap.RootElement.GetProperty("used").GetDecimal()); // the recorded sum

        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        // Parked: NO carryover, NO §24 staging, NO forfeit event (FORFEIT is 422-blocked, below).
        Assert.Null(await ReadCarryoverInAsync(employeeId, EndDateFerieaar + 1));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — carryover_in is INCLUDED in the crystallization.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_NonZeroCarryover_IncludedInCrystallization()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await SeedBalanceAsync(employeeId, EndDateFerieaar, used: 0m, planned: 0m, carryoverIn: 2m);
        await MarkLeaverAsync(employeeId, EndDate);

        await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        Assert.Equal(2m, snap.RootElement.GetProperty("carryoverIn").GetDecimal());
        // crystallized = max(0, 12.5 + 2 − 0) = 14.5 — the previously transferred balance did not vanish.
        Assert.Equal(14.5m, snap.RootElement.GetProperty("crystallizedDays").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — a booking AFTER the end date cannot be taken and must NOT consume (the declared
    // divergence from the slice-1a balance.Used scalar).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_PostEndDateBooking_DoesNotConsume()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        // 2 recorded days BEFORE the end date; 3 recorded days AFTER it (still inside ferieår 2025).
        await SeedAbsenceAsync(employeeId, new DateOnly(2025, 10, 6), feriedage: 1.0m);
        await SeedAbsenceAsync(employeeId, new DateOnly(2025, 10, 7), feriedage: 1.0m);
        for (var i = 0; i < 3; i++)
            await SeedAbsenceAsync(employeeId, new DateOnly(2026, 3, 9 + i), feriedage: 1.0m);
        // The balance scalar counts ALL 5 (the booking guard wrote them) — TERMINATION must not read it.
        await SeedBalanceAsync(employeeId, EndDateFerieaar, used: 5m, planned: 0m, carryoverIn: 0m);
        await MarkLeaverAsync(employeeId, EndDate);

        await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        // used = the recorded sum ≤ end date (2), NOT balance.Used (5).
        Assert.Equal(2m, snap.RootElement.GetProperty("used").GetDecimal());
        // crystallized = max(0, 12.5 + 0 − 2) = 10.5 (with balance.Used it would wrongly be 7.5).
        Assert.Equal(10.5m, snap.RootElement.GetProperty("crystallizedDays").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — the consumption WINDOW boundaries (Step-5a W3): the recorded-absence window is
    // [ferieår start, end date] BOTH-INCLUSIVE (the `date >= @start AND date <= @end` SQL in
    // VacationSettlementService.ReadRecordedFeriedageAsync) — a day before the ferieår start
    // and a day after the end date are EXCLUDED; the end date itself is INCLUDED.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_ConsumptionWindow_OnEndDateIncluded_BeforeStartAndDayAfterExcluded()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        // (a) the day BEFORE the ferieår start (31 Aug 2025 — the prior ferieår's last day): EXCLUDED.
        await SeedAbsenceAsync(employeeId, new DateOnly(2025, 8, 31), feriedage: 1.0m);
        // (b) exactly ON the end date: INCLUDED (date ≤ endDate — the inclusive upper bound).
        await SeedAbsenceAsync(employeeId, EndDate, feriedage: 1.0m);
        // (c) ONE DAY after the end date: EXCLUDED.
        await SeedAbsenceAsync(employeeId, EndDate.AddDays(1), feriedage: 1.0m);
        await MarkLeaverAsync(employeeId, EndDate);

        await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        var root = snap.RootElement;
        // Only (b) consumes: used = 1, and the auditable component list carries exactly one entry.
        Assert.Equal(1m, root.GetProperty("used").GetDecimal());
        Assert.Equal(1, root.GetProperty("recordedAbsences").GetArrayLength());
        // crystallized = max(0, 12.5 + 0 − 1) = 11.5.
        Assert.Equal(11.5m, root.GetProperty("crystallizedDays").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 / owner D-B — BLOCKER B1 pin (Step-5a W1i): an IMMEDIATE-accrual config grants the full
    // quota up-front for CONSUMPTION, but a mid-year TERMINATION crystallizes the whole-month
    // EarnedToDate(asOf=endDate) UNCONDITIONALLY. Would fail pre-B1-fix (earned/crystallized 25).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_ImmediateAccrualConfig_CrystallizesWholeMonth_NotFullQuota()
    {
        var service = BootService();
        const string agreement = "S70IMM";
        await SeedVacationConfigAsync(agreement, annualQuota: 25m, accrualModel: "IMMEDIATE",
            carryoverMax: 5m, effectiveFrom: new DateOnly(1, 1, 1));
        var employeeId = await SeedEmployeeAsync(agreement);
        await MarkLeaverAsync(employeeId, EndDate); // 2026-02-28 ⇒ 6 whole months of ferieår 2025

        var outcome = await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        Assert.True(outcome.DidSettle);
        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal(Termination, row!.Value.Trigger);
        Assert.Equal("SETTLED", row.Value.State);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        // The whole-month §26 basis (25 × 6/12 = 12.5) — NOT the IMMEDIATE full quota 25.
        Assert.Equal(12.5m, snap.RootElement.GetProperty("earned").GetDecimal());
        Assert.Equal(12.5m, snap.RootElement.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal("S26_WHOLE_MONTH", snap.RootElement.GetProperty("crystallizationBasis").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5/R4 + D10 — BLOCKER B2 pins (Step-5a W1ii): a dated entitlement-config MISS fails CLOSED
    // (throws, persists NOTHING) for BOTH the TERMINATION capture and the R4 leaver-deferred
    // capture — the current live quota must never determine a leaver quantity. The ACTIVE-employee
    // YEAR_END capture keeps the D9 live-fallback chain unchanged (the S68 behavior). The config
    // rows below open MID-ferieår (after every probed ferieår-start anchor) so the dated read
    // misses while the live-open row still resolves — the exact pre-fix exploit shape.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Would settle on the LIVE quota pre-B2-fix; now throws and persists nothing.</summary>
    [Fact]
    public async Task Termination_MissingDatedConfig_FailsClosed_NoLiveFallback_NoRow()
    {
        var service = BootService();
        const string agreement = "S70MST";
        await SeedVacationConfigAsync(agreement, annualQuota: 30m, accrualModel: "MONTHLY_ACCRUAL",
            carryoverMax: 5m, effectiveFrom: new DateOnly(2025, 12, 1)); // misses 2025-09-01
        var employeeId = await SeedEmployeeAsync(agreement);
        await MarkLeaverAsync(employeeId, EndDate);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SettleAsync(service, employeeId, EndDateFerieaar, Termination));

        Assert.Contains("entitlement_configs", ex.Message);
        Assert.Contains("fails closed", ex.Message);
        Assert.Equal(0L, await CountAsync("vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
    }

    /// <summary>The R4 leaver-deferred capture is R5-anchored too: same fail-closed throw.</summary>
    [Fact]
    public async Task LeaverDeferred_MissingDatedConfig_FailsClosed_NoLiveFallback_NoRow()
    {
        var service = BootService();
        const string agreement = "S70MSL";
        await SeedVacationConfigAsync(agreement, annualQuota: 30m, accrualModel: "MONTHLY_ACCRUAL",
            carryoverMax: 5m, effectiveFrom: new DateOnly(2025, 12, 1)); // misses 2021-09-01
        var employeeId = await SeedEmployeeAsync(agreement);
        await MarkLeaverAsync(employeeId, EndDate);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SettleAsync(service, employeeId, PriorClosedYear, YearEnd));

        Assert.Contains("fails closed", ex.Message);
        Assert.Equal(0L, await CountAsync("vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
    }

    /// <summary>The ACTIVE-employee YEAR_END control: the same dated MISS still settles via the
    /// D9 live-fallback chain (datedConfig = the live-open row, quota 30) — no behavior change.</summary>
    [Fact]
    public async Task ActiveYearEnd_MissingDatedConfig_LiveFallbackChain_StillSettles_Unchanged()
    {
        var service = BootService();
        const string agreement = "S70MSA";
        await SeedVacationConfigAsync(agreement, annualQuota: 30m, accrualModel: "MONTHLY_ACCRUAL",
            carryoverMax: 5m, effectiveFrom: new DateOnly(2025, 12, 1)); // misses 2021-09-01
        var employeeId = await SeedEmployeeAsync(agreement); // ACTIVE — no end date

        var outcome = await SettleAsync(service, employeeId, PriorClosedYear, YearEnd);

        Assert.True(outcome.DidSettle);
        Assert.NotNull(outcome.Partition); // the normal S68 auto-partition ran
        Assert.Equal(5m, outcome.Partition!.PayoutDays);   // underCap = min(30, 5)
        Assert.Equal(25m, outcome.Partition.ForfeitDays);  // overCap = 30 − 5
        var row = await ReadActiveSettlementAsync(employeeId, PriorClosedYear);
        using var snap = JsonDocument.Parse(row!.Value.SnapshotJson);
        // The LIVE-open quota (30) valued the year — the unchanged D9 fallback terminal.
        Assert.Equal(30m, snap.RootElement.GetProperty("annualQuota").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 / ADR-033 D3 — replay parity: snapshot-only re-derivation byte-identical AFTER
    // subsequent live-record mutations.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_ReplayParity_SnapshotOnlyRederivation_SurvivesLiveMutations()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await SeedBalanceAsync(employeeId, EndDateFerieaar, used: 3m, planned: 0m, carryoverIn: 2m);
        for (var i = 0; i < 3; i++)
            await SeedAbsenceAsync(employeeId, new DateOnly(2025, 10, 6 + i), feriedage: 1.0m);
        await MarkLeaverAsync(employeeId, EndDate);

        await SettleAsync(service, employeeId, EndDateFerieaar, Termination);
        var settled = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.NotNull(settled);

        // MUTATE the live records the capture read — replay must not see any of this.
        await SeedAbsenceAsync(employeeId, new DateOnly(2025, 11, 3), feriedage: 1.0m); // ≤ end date!
        await SeedBalanceAsync(employeeId, EndDateFerieaar, used: 9m, planned: 1m, carryoverIn: 4m);

        // Snapshot-only re-derivation (the documented R5 basis applied to the STORED operands).
        var after = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        using var snap = JsonDocument.Parse(after!.Value.SnapshotJson);
        var root = snap.RootElement;
        var earned = root.GetProperty("earned").GetDecimal();
        var carryoverIn = root.GetProperty("carryoverIn").GetDecimal();
        var used = root.GetProperty("used").GetDecimal();
        var preClamp = Math.Round(earned + carryoverIn - used, 2, MidpointRounding.ToEven);
        var expectedCrystallized = Math.Max(0m, preClamp);

        // The stored snapshot still carries the SETTLE-TIME operands (carryover 2, used 3)…
        Assert.Equal(2m, carryoverIn);
        Assert.Equal(3m, used);
        // …and re-derivation reproduces the recorded outcome byte-identically.
        Assert.Equal(expectedCrystallized, root.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal(11.5m, expectedCrystallized); // 12.5 + 2 − 3
        Assert.Equal(preClamp >= 0m ? "SETTLED" : "PENDING_REVIEW", after.Value.State);
        Assert.Equal(settled.Value.SnapshotJson, after.Value.SnapshotJson); // immutable snapshot
        Assert.Equal(settled.Value.State, after.Value.State);
        Assert.Equal(settled.Value.Forfeit, after.Value.Forfeit);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6 + S70 Step-7a B1 — the ferieår-containing-end-date contract and the missing-end-date
    // case are clauses of the IN-LOCK due predicate (IsTerminationDueUnderLock): ANY failure is
    // the pinned benign NotDue no-op — no row, no event, NO throw (the next poll re-enumerates
    // against fresh state, so a stale tuple never refires). [S70 Step-7a fix-forward: this
    // REPLACES the pre-B1 fail-loud throws this test previously pinned — DECLARED.]
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_WrongFerieaarOrMissingEndDate_NotDueBenignNoOp_PersistsNothing()
    {
        var service = BootService();

        // (a) entitlementYear ≠ the R6 ferieår of the end date (2026-02-28 ⇒ 2025, not 2024).
        var leaver = await SeedEmployeeAsync();
        await MarkLeaverAsync(leaver, EndDate);
        var wrongYear = await SettleAsync(service, leaver, 2024, Termination);
        Assert.False(wrongYear.DidSettle);
        Assert.True(wrongYear.NotDue);
        Assert.False(wrongYear.RefusedConflict);
        Assert.Null(wrongYear.Row);

        // (b) no employment_end_date at all (an ACTIVE user) — not a leaver, benign NotDue.
        var noEndDate = await SeedEmployeeAsync();
        var noLeaverFact = await SettleAsync(service, noEndDate, EndDateFerieaar, Termination);
        Assert.False(noLeaverFact.DidSettle);
        Assert.True(noLeaverFact.NotDue);
        Assert.Null(noLeaverFact.Row);

        // Nothing persisted, nothing emitted — for either case.
        Assert.Equal(0L, await CountAsync("vacation_settlements", "employee_id = @e", ("e", leaver)));
        Assert.Equal(0L, await CountAsync("vacation_settlements", "employee_id = @e", ("e", noEndDate)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(leaver, "TerminationSettled"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(noEndDate, "TerminationSettled"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(leaver, "SettlementManualReviewFlagged"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(noEndDate, "SettlementManualReviewFlagged"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 — a leaver's OTHER due ferieår: fail-closed deferred-disposition PENDING_REVIEW row,
    // NO auto-partition, NO carryover, NO §21/§24 events — even when a §21 agreement exists.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LeaverOtherFerieaar_YearEnd_DeferredDisposition_FullDisposableFlag_NoPartition()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        // A §21 agreement that the ACTIVE auto-partition WOULD have transferred — leavers must not.
        await SeedTransferAgreementAsync(employeeId, PriorClosedYear, transferDays: 5m);
        await MarkLeaverAsync(employeeId, EndDate);

        var outcome = await SettleAsync(service, employeeId, PriorClosedYear, YearEnd);

        Assert.True(outcome.DidSettle);
        Assert.Null(outcome.Partition); // no partition computed for a leaver (R4)

        var row = await ReadActiveSettlementAsync(employeeId, PriorClosedYear);
        Assert.NotNull(row);
        Assert.Equal(YearEnd, row!.Value.Trigger);
        Assert.Equal("PENDING_REVIEW", row.Value.State);   // fail-closed (D10)
        Assert.Equal(0m, row.Value.Transfer);
        Assert.Equal(0m, row.Value.Payout);
        Assert.Equal(25m, row.Value.Forfeit);              // the FULL disposable as a FLAG (S68 convention)

        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        var root = snap.RootElement;
        Assert.True(root.GetProperty("deferredDisposition").GetBoolean()); // the R4 marker
        Assert.Equal(EndDate.ToString("yyyy-MM-dd"), root.GetProperty("terminationDate").GetString());
        Assert.False(root.TryGetProperty("crystallizationBasis", out var cb) && cb.ValueKind != JsonValueKind.Null);

        // NO carryover write, NO §21/§24 events (nothing leaks into the S69 §24 staging), ONE flag.
        Assert.Null(await ReadCarryoverInAsync(employeeId, PriorClosedYear + 1));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationAutoPaidOut"));
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        Assert.Equal(0L, await CountAsync("settlement_export_lines", "employee_id = @e", ("e", employeeId)));
    }

    /// <summary>The leaver predicate is STRICT (end date &lt; Copenhagen today): a FUTURE-dated
    /// end date is not yet a leaver — the ACTIVE-employee YEAR_END auto-partition runs unchanged
    /// (byte-identical S68 behavior; the R4 fork must not over-trigger).</summary>
    [Fact]
    public async Task FutureEndDate_YearEnd_StillAutoPartitions()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        var futureEndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);
        await SetEndDateAsync(employeeId, futureEndDate); // stored, is_active stays TRUE (R1b)

        var outcome = await SettleAsync(service, employeeId, PriorClosedYear, YearEnd);

        // The normal S68 partition: earned 25, cap 5 ⇒ payout 5 / forfeit 20, PENDING_REVIEW.
        Assert.NotNull(outcome.Partition);
        Assert.Equal(5m, outcome.Partition!.PayoutDays);
        Assert.Equal(20m, outcome.Partition.ForfeitDays);

        var row = await ReadActiveSettlementAsync(employeeId, PriorClosedYear);
        Assert.Equal(5m, row!.Value.Payout);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        Assert.False(snap.RootElement.TryGetProperty("deferredDisposition", out var dd) && dd.GetBoolean());

        // N1 (Step-5a polish) — the EXACT pre-S70 shape pin: the ACTIVE-employee YEAR_END snapshot
        // carries precisely the 14 pre-S70 keys — NONE of the four S70 termination extensions
        // (terminationDate / crystallizationBasis / crystallizedDays / deferredDisposition) may
        // appear (omitted-when-unset), and no pre-S70 key may be dropped or renamed. Key ORDER is
        // deliberately not asserted: the jsonb column normalizes it on storage, so set-equality IS
        // the storage-level byte-stability equivalent (DECLARED in the fix-forward report).
        var actualKeys = snap.RootElement.EnumerateObject()
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "agreementCode", "annualQuota", "carryoverIn", "carryoverMax", "earned",
            "isFeriehindret", "okVersion", "planned", "position", "recordedAbsences",
            "resetMonth", "settlementBoundaryDate", "transferAgreementDays", "used",
        }, actualKeys);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 invariant — NO resolve disposition writes carryover_in in 3a (replaces the former
    // DEFER-guard): FORFEIT and DEFER on a deferred-disposition YEAR_END row leave carryover
    // untouched. (TERMINATION rows are 422-blocked — next region.)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Resolve_OnDeferredDispositionRow_ForfeitAndDefer_WriteNoCarryover()
    {
        var service = BootService();
        var hr = HrClient();

        // FORFEIT — resolves the FULL flagged disposable; carryover stays untouched.
        var forfeitEmp = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(forfeitEmp, PriorClosedYear, transferDays: 5m);
        await MarkLeaverAsync(forfeitEmp, EndDate);
        await SettleAsync(service, forfeitEmp, PriorClosedYear, YearEnd);

        var forfeitRsp = await ResolveAsync(hr, forfeitEmp, PriorClosedYear, "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, forfeitRsp.StatusCode);
        var forfeitBody = await forfeitRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(forfeitBody.GetProperty("resolved").GetBoolean());
        Assert.Equal(25m, forfeitBody.GetProperty("forfeitDays").GetDecimal());
        Assert.Null(await ReadCarryoverInAsync(forfeitEmp, PriorClosedYear + 1)); // the invariant
        // The documented audit-semantics overload: VacationForfeitedToFeriefond for an
        // UNPARTITIONED full disposable (the snapshot's DeferredDisposition marker discriminates).
        Assert.Equal(1L, await CountOutboxByTypeAsync(forfeitEmp, "VacationForfeitedToFeriefond"));

        // DEFER — pinned marker-only in 3a: stays PENDING_REVIEW, writes NO carryover.
        var deferEmp = await SeedEmployeeAsync();
        await MarkLeaverAsync(deferEmp, EndDate);
        await SettleAsync(service, deferEmp, PriorClosedYear, YearEnd);

        var deferRsp = await ResolveAsync(hr, deferEmp, PriorClosedYear, "DEFER", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, deferRsp.StatusCode);
        var deferBody = await deferRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(deferBody.GetProperty("resolved").GetBoolean());
        Assert.Equal("PENDING_REVIEW", deferBody.GetProperty("settlementState").GetString());
        Assert.Null(await ReadCarryoverInAsync(deferEmp, PriorClosedYear + 1)); // the invariant
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — the two 422 guards: manual-resolve AND reconcile-payout reject trigger=TERMINATION.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Resolve_TerminationRow_Returns422_ForBothDispositions_RowUntouched()
    {
        _ = BootService();
        var hr = HrClient();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        // A parked negative-pre-clamp TERMINATION row (the cycle-4 scenario the guard exists for).
        await SeedSettlementRowAsync(employeeId, EndDateFerieaar, Termination, "PENDING_REVIEW", forfeitDays: 2.92m);

        var forfeit = await ResolveAsync(hr, employeeId, EndDateFerieaar, "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, forfeit.StatusCode);

        var defer = await ResolveAsync(hr, employeeId, EndDateFerieaar, "DEFER", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, defer.StatusCode);

        // The row parks untouched — no state flip, no version bump, no materially false §34 event.
        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
    }

    [Fact]
    public async Task ReconcilePayout_TerminationRow_Returns422()
    {
        _ = BootService();
        var hr = HrClient();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        // A SETTLED zero-bucket TERMINATION row (the payout_days<=0 409 would also exclude it —
        // the explicit 422 makes the 3a contract explicit, pinned).
        await SeedSettlementRowAsync(employeeId, EndDateFerieaar, Termination, "SETTLED", forfeitDays: 0m);

        var rsp = await ReconcilePayoutAsync(hr, employeeId, EndDateFerieaar, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal(1L, row!.Value.Version); // untouched
    }

    // ════════════════════════════════════════════════════════════════════════
    // R7b — a TERMINATION colliding with an active YEAR_END row is REFUSED: loud signal + audit,
    // NO row written, NO throw. (Flagged-ONCE idempotency rides the R3 any-trigger anti-join —
    // pinned in the R8 test below; this in-tx guard is the race backstop.)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Termination_ConflictingActiveYearEndRow_Refused_SignalAndAudit_NoRow()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        // First the YEAR_END close wins the end-date ferieår (active employee; used=20 ⇒ a clean
        // SETTLED payout-5 row with NO forfeit flag, so the ONLY flag event below is the refusal's).
        await SeedBalanceAsync(employeeId, EndDateFerieaar, used: 20m, planned: 0m, carryoverIn: 0m);
        var yearEnd = await SettleAsync(service, employeeId, EndDateFerieaar, YearEnd);
        Assert.Equal("SETTLED", yearEnd.Row!.SettlementState);

        // THEN the employee terminates inside that ferieår — the TERMINATION pass must refuse.
        await MarkLeaverAsync(employeeId, EndDate);
        var outcome = await SettleAsync(service, employeeId, EndDateFerieaar, Termination);

        Assert.False(outcome.DidSettle);
        Assert.True(outcome.RefusedConflict);
        Assert.Equal(YearEnd, outcome.Row!.Trigger); // the untouched conflicting row

        // Exactly ONE row for the tuple — the YEAR_END row, byte-untouched (state/version).
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements",
            "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            ("e", employeeId), ("t", VacationType), ("y", EndDateFerieaar)));
        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal(YearEnd, row!.Value.Trigger);
        Assert.Equal("SETTLED", row.Value.State);
        Assert.Equal(1L, row.Value.Version);

        // The durable loud signal + its ADR-026 audit row; NO TerminationSettled was emitted.
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        Assert.Equal(1L, await CountAsync(
            "audit_projection", "event_type = 'SettlementManualReviewFlagged' AND target_resource_id = @r",
            ("r", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));

        // A REPEATED race-window call emits the backstop signal again (the in-tx guard is a race
        // backstop, not the dedup — flagged-ONCE in steady state is the R3 anti-join, which removes
        // the tuple from the due set entirely; pinned in the R8 test below).
        var second = await SettleAsync(service, employeeId, EndDateFerieaar, Termination);
        Assert.True(second.RefusedConflict);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R7b — the 23505 RECOVERY branch (Step-5a W2): the competing-insert race the in-lock
    // pre-check CANNOT see. Choreography: a competing row is inserted UNCOMMITTED on a second
    // connection (invisible to the pre-check under READ COMMITTED), the TERMINATION pass blocks
    // INSIDE VacationSettlementRepository.InsertAsync on the partial-unique active-index entry,
    // and the competitor commits only once the pass is PROVABLY blocked (pg_stat_activity) —
    // deterministic ordering, no sleeps-as-synchronization.
    //
    // MECHANICS (the B3 finding, declared): InsertAsync savepoint-wraps the INSERT and rolls back
    // TO the savepoint on 23505, so the caller tx survives into the catch — the refusal emissions
    // ride the SAME tx. The competing row uses sequence 2 (the 3b reversal-history shape): a
    // SAME-sequence competitor collides on the composite PK FIRST, which InsertAsync deliberately
    // RETHROWS raw (the discriminating catch — a duplicate sequence is a different defect class),
    // so the active-index 23505 branch is reachable in-process only via a different-sequence
    // active winner.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A concurrent YEAR_END winner in the 23505 window is REFUSED loudly — the durable
    /// signal + audit row exist, the YEAR_END row is untouched, and NO second row was written
    /// (pre-B3-fix this returned a silent benign no-op, suppressed forever by the R3 anti-join).</summary>
    [Fact]
    public async Task Termination_CompetingYearEndInsert_23505Recovery_RefusedLoudly_YearEndUntouched()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);

        var outcome = await SettleTerminationAgainstUncommittedCompetitorAsync(
            service, employeeId, EndDateFerieaar, competingTrigger: YearEnd);

        Assert.False(outcome.DidSettle);
        Assert.True(outcome.RefusedConflict);
        Assert.Equal(YearEnd, outcome.Row!.Trigger); // the committed competitor, reported untouched

        // Exactly ONE row for the tuple — the competitor's YEAR_END row, byte-untouched.
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements",
            "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            ("e", employeeId), ("t", VacationType), ("y", EndDateFerieaar)));
        var row = await ReadActiveSettlementAsync(employeeId, EndDateFerieaar);
        Assert.Equal(YearEnd, row!.Value.Trigger);
        Assert.Equal("SETTLED", row.Value.State);
        Assert.Equal(2, row.Value.Sequence);
        Assert.Equal(1L, row.Value.Version);

        // The durable loud signal + its ADR-026 audit row; NO TerminationSettled (the emission
        // sits after the insert and was never reached).
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        Assert.Equal(1L, await CountAsync(
            "audit_projection", "event_type = 'SettlementManualReviewFlagged' AND target_resource_id = @r",
            ("r", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
    }

    /// <summary>A concurrent TERMINATION winner in the 23505 window stays the BENIGN single-settle
    /// no-op (trigger discrimination inside the recovery branch): no refusal signal, no second row,
    /// and the WINNER row (not the unpersisted candidate) is reported.</summary>
    [Fact]
    public async Task Termination_CompetingTerminationInsert_23505Recovery_BenignAlreadySettled()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);

        var outcome = await SettleTerminationAgainstUncommittedCompetitorAsync(
            service, employeeId, EndDateFerieaar, competingTrigger: Termination);

        Assert.False(outcome.DidSettle);
        Assert.False(outcome.RefusedConflict);
        Assert.Equal(Termination, outcome.Row!.Trigger);
        Assert.Equal(2, outcome.Row!.Sequence); // the PERSISTED winner — never the unpersisted candidate

        Assert.Equal(1L, await CountAsync(
            "vacation_settlements",
            "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y",
            ("e", employeeId), ("t", VacationType), ("y", EndDateFerieaar)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R8 — a non-REVERSED TERMINATION row (SETTLED and PENDING_REVIEW) CONSUMES the YEAR_END
    // due-check for its tuple, against the LIVE SettlementCloseService anti-join
    // (SettlementCloseService.cs:304-310). Other due years still settle (the poll witness).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TerminationRow_SettledAndPendingReview_ConsumeYearEndDueCheck()
    {
        // Two ACTIVE employees so the current due-enumeration reaches the anti-join for them:
        // ferieår 2024 (boundary 31 Dec 2025) IS due at the fixed clock 2026-01-01 — only the
        // seeded TERMINATION row keeps it out of the due set.
        var settledEmp = await SeedEmployeeAsync();
        var pendingEmp = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(settledEmp, 2024, Termination, "SETTLED", forfeitDays: 0m);
        await SeedSettlementRowAsync(pendingEmp, 2024, Termination, "PENDING_REVIEW", forfeitDays: 2.92m);

        BootFixedClockHost(new DateOnly(2026, 1, 1), goLiveDate: new DateOnly(2020, 1, 1));

        // Poll witness: the OTHER due years (e.g. 2023, boundary 31 Dec 2024) settle for both.
        Assert.True(await WaitForSettlementAsync(settledEmp, 2023, TimeSpan.FromSeconds(30)),
            "ferieår 2023 should auto-settle for the SETTLED-row employee (the poll ran).");
        Assert.True(await WaitForSettlementAsync(pendingEmp, 2023, TimeSpan.FromSeconds(30)),
            "ferieår 2023 should auto-settle for the PENDING_REVIEW-row employee (the poll ran).");

        // The consumed tuple: STILL exactly the one TERMINATION row each — no YEAR_END row was
        // generated for 2024 despite its boundary having passed.
        foreach (var (emp, expectedState) in new[] { (settledEmp, "SETTLED"), (pendingEmp, "PENDING_REVIEW") })
        {
            Assert.Equal(1L, await CountAsync(
                "vacation_settlements",
                "employee_id = @e AND entitlement_type = @t AND entitlement_year = 2024",
                ("e", emp), ("t", VacationType)));
            var row = await ReadActiveSettlementAsync(emp, 2024);
            Assert.Equal(Termination, row!.Value.Trigger);
            Assert.Equal(expectedState, row.Value.State);
            Assert.Equal(1L, row.Value.Version);
        }
    }

    // ─────────────────────────────── drivers ───────────────────────────────

    /// <summary>One settlement pass in its OWN ReadCommitted tx, committed — the exact
    /// SettlementCloseService shape (SPRINT-70 R12: SettleAsync takes the employee advisory lock
    /// FIRST on this tx).</summary>
    private async Task<SettlementOutcome> SettleAsync(
        VacationSettlementService service, string employeeId, int year, string trigger)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(employeeId, VacationType, year, trigger, conn, tx);
            await tx.CommitAsync();
            return outcome;
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>The W2 23505-recovery choreography (see the region comment above the two race
    /// tests): competitor row inserted UNCOMMITTED (sequence 2) → the TERMINATION pass started →
    /// wait until it is provably lock-blocked inside its INSERT → commit the competitor → the pass
    /// gets the 23505, recovers on the savepoint-restored tx, and resolves the winner.</summary>
    private async Task<SettlementOutcome> SettleTerminationAgainstUncommittedCompetitorAsync(
        VacationSettlementService service, string employeeId, int year, string competingTrigger)
    {
        await using var competitorConn = new NpgsqlConnection(_harness.ConnectionString);
        await competitorConn.OpenAsync();
        await using var competitorTx = await competitorConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await InsertCompetingRowAsync(competitorConn, competitorTx, employeeId, year, competingTrigger);

        // The pass: its in-lock pre-check CANNOT see the uncommitted competitor (READ COMMITTED),
        // so it proceeds to InsertAsync and blocks on the active-index entry the competitor holds.
        var settleTask = Task.Run(() => SettleAsync(service, employeeId, year, Termination));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var blocked = false;
        while (!blocked && DateTime.UtcNow < deadline)
        {
            if (settleTask.IsCompleted)
            {
                // Propagate the real failure, or fail loud on an unexpected early completion —
                // either way the recovery branch was NOT exercised.
                var early = await settleTask;
                Assert.Fail(
                    $"the TERMINATION pass completed early (DidSettle={early.DidSettle}) without blocking " +
                    "on the competitor — the 23505 recovery branch was not exercised.");
            }
            blocked = await IsSettlementInsertLockBlockedAsync();
            if (!blocked)
                await Task.Delay(100);
        }
        Assert.True(blocked, "the TERMINATION pass never lock-blocked inside its settlement INSERT within 30s.");

        await competitorTx.CommitAsync(); // releases the index-entry wait → the pass gets its 23505
        return await settleTask;
    }

    /// <summary>True when some backend on this database is LOCK-waiting inside a
    /// <c>vacation_settlements</c> INSERT (the unique-index xid wait) — the deterministic
    /// is-the-pass-past-its-pre-check probe. The probing backend itself is never lock-waiting,
    /// so the <c>wait_event_type = 'Lock'</c> filter excludes it.</summary>
    private async Task<bool> IsSettlementInsertLockBlockedAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND wait_event_type = 'Lock'
              AND query ILIKE '%INSERT INTO vacation_settlements%'
            """, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>The W2 competitor row, inserted on the caller's HELD transaction. Sequence 2 —
    /// the 3b reversal-history shape — so the collision lands on the partial-unique ACTIVE index
    /// (a same-sequence row would collide on the composite PK first, which the repository
    /// deliberately rethrows raw as a different defect class; see the W2 region comment).</summary>
    private static async Task InsertCompetingRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, int year, string trigger)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 12.5m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            agreementCode = "AC",
            transferAgreementDays = 0m,
            isFeriehindret = false,
        });
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, @y, 2, 'SETTLED', @trigger, @snapshot::jsonb, 0, 0, 0, NULL, 1)
            """, conn, tx);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Boots a derived WAF host with a fixed clock + go-live date — the
    /// SettlementCloseServiceBoundaryTests harness (PAT-008), for the R8 live-poller pin.</summary>
    private void BootFixedClockHost(DateOnly fixedDate, DateOnly? goLiveDate)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
        {
            if (goLiveDate is not null)
            {
                builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Settlement:GoLiveDate"] = goLiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }));
            }
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate));
            });
        });
        _ = derived.CreateClient(); // host build + hosted-service start (immediate poll)
    }

    private async Task<bool> WaitForSettlementAsync(string employeeId, int year, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountAsync(
                    "vacation_settlements",
                    "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND settlement_state <> 'REVERSED'",
                    ("e", employeeId), ("t", VacationType), ("y", year)) >= 1)
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    // ─────────────────────────────── clients / endpoints ───────────────────────────────

    private HttpClient HrClient()
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = svc.GenerateToken(
            employeeId: "hr_s70_term", name: "hr_s70_term", role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, OrgId, "ORG_AND_DESCENDANTS") });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> ResolveAsync(
        HttpClient client, string employeeId, int year, string disposition, string ifMatch)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/resolve")
        {
            Content = JsonContent.Create(new { disposition }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> ReconcilePayoutAsync(
        HttpClient client, string employeeId, int year, string ifMatch)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/reconcile-payout");
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync(string agreementCode = "AC")
    {
        var employeeId = "emp_s70_term_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, agreementCode, "OK24");
        return employeeId;
    }

    /// <summary>S70 fix-forward (Step-5a W1) — a dedicated VACATION entitlement config under a
    /// test-owned agreement code (never the seeded 'AC' row — other tests share it). reset_month
    /// is 9 by the S68 B1 DB CHECK; <paramref name="effectiveFrom"/> placed AFTER a ferieår start
    /// engineers a dated-read MISS while the live-open row still resolves (the D9 fallback
    /// operand).</summary>
    private async Task SeedVacationConfigAsync(
        string agreementCode, decimal annualQuota, string accrualModel,
        decimal carryoverMax, DateOnly effectiveFrom)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs
                (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model,
                 reset_month, carryover_max, pro_rate_by_part_time, effective_from, effective_to)
            VALUES ('VACATION', @a, 'OK24', @quota, @model, 9, @cap, false, @from, NULL)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("quota", annualQuota);
        cmd.Parameters.AddWithValue("model", accrualModel);
        cmd.Parameters.AddWithValue("cap", carryoverMax);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The post-Step-A leaver state (TASK-7005 owns the live flip; this test seeds its
    /// outcome directly): end date + lifecycle deactivation with provenance.</summary>
    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("endDate", endDate);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A stored (e.g. future-dated, R1b) end date with NO deactivation.</summary>
    private async Task SetEndDateAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_end_date = @endDate, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("endDate", endDate);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedBalanceAsync(
        string employeeId, int year, decimal used, decimal planned, decimal carryoverIn)
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
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("used", used);
        cmd.Parameters.AddWithValue("planned", planned);
        cmd.Parameters.AddWithValue("carryover", carryoverIn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A recorded per-absence feriedage row (ADR-032 D2) — the authoritative consumption
    /// record the R5 <c>consumedToEndDate</c> operand counts.</summary>
    private async Task SeedAbsenceAsync(string employeeId, DateOnly date, decimal feriedage)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @e, @date, 'VACATION', 7.4, @feriedage,
                 'AC', 'OK24', NOW(), 0)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("feriedage", feriedage);
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
        cmd.Parameters.AddWithValue("date", new DateOnly(year + 1, 6, 30));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Direct settlement-row seed (the EmploymentEndDateLifecycleTests convention) with a
    /// minimal valid TERMINATION-shaped snapshot — for the guard/R8 tests that pin behavior
    /// AGAINST a pre-existing row rather than the write path.</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string trigger, string state, decimal forfeitDays)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 12.5m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            agreementCode = "AC",
            transferAgreementDays = 0m,
            isFeriehindret = false,
            terminationDate = "2026-02-28",
            crystallizationBasis = "S26_WHOLE_MONTH",
            crystallizedDays = state == "SETTLED" ? 12.5m : 0m,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, @y, 1, @state, @trigger, @snapshot::jsonb, 0, 0, @forfeit, NULL, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("forfeit", forfeitDays);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, string Trigger, decimal Transfer, decimal Payout, decimal Forfeit,
            int Sequence, long Version, string SnapshotJson)?>
        ReadActiveSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, trigger, transfer_days, payout_days, forfeit_days,
                   sequence, version, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetInt32(5), reader.GetInt64(6), reader.GetString(7));
    }

    private async Task<decimal?> ReadCarryoverInAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT carryover_in FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (decimal)result;
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
