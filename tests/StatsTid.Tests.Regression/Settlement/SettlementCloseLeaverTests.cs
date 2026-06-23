using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S70 / TASK-7005 (ADR-033 slice 3a; SPRINT-70 R2/R3/R4/R6/R8/R12) — Docker-gated tests for the
/// restructured <see cref="SettlementCloseService"/>: <b>Step A</b> (the UNGATED leaver
/// deactivation flip) + <b>Step B</b> (the D13-gated, now leaver-INCLUSIVE settlement
/// enumeration), driven through the LIVE BackgroundService with the PAT-008
/// <c>FixedTimeProvider</c> harness (the <see cref="SettlementCloseServiceBoundaryTests"/>
/// pattern — the host boot runs one poll on ExecuteAsync entry).
///
/// <list type="bullet">
///   <item><description><b>R2:</b> dormant settlement gate + passed end date ⇒ user still
///   deactivated (flip + provenance + version bump + event), NO settlement rows (the pinned
///   dormant-gate-still-deactivates test); pre-go-live end date ⇒ deactivated but NOTHING
///   auto-settled (manual fallback).</description></item>
///   <item><description><b>R3:</b> a manually-inactive user with no end date is never
///   flipped/settled; future-dated and boundary-day end dates are not flipped (the strict
///   <c>end date &lt; today</c> predicate).</description></item>
///   <item><description><b>R4:</b> the leaver year-cap (no post-termination ferieår generated);
///   pin (a) — a flip-failed leaver (still <c>is_active=TRUE</c>, passed end date; simulated via
///   a scoped BEFORE-UPDATE trigger that fails the flip UPDATE) is EXCLUDED from the ACTIVE
///   enumeration branch: no partitioned YEAR_END row, no carryover, no
///   <c>VacationAutoPaidOut</c>.</description></item>
///   <item><description><b>R6 (consumed):</b> the end-date ferieår resolves per the 9-pivot in
///   both the SQL year-cap and the trigger selection (end date 2026-02-28 ⇒ ferieår
///   2025).</description></item>
///   <item><description><b>R8:</b> the second poll is a no-op — the any-trigger anti-join + the
///   single-active index suppress double-settles AND a later YEAR_END for tuples holding
///   TERMINATION rows.</description></item>
///   <item><description><b>R12 races</b> (the advisory-lock parking choreography precedent from
///   <see cref="EmploymentEndDateLifecycleTests"/>/<see cref="TerminationSettlementTests"/>):
///   clear-vs-settle, correct-vs-settle, clear-vs-Step-A-flip — the loser re-evaluates its guard
///   in-lock and the outcome is coherent: per the S70 Step-7a B1 pinned contract a no-longer-due
///   settle yields the benign <c>NotDue</c> no-op (no row, no event, no throw), and a losing flip
///   is a benign 0-rows no-op with no event.</description></item>
///   <item><description><b>S70 Step-7a B1 (Codex BLOCKER):</b> the TERMINATION/leaver due
///   predicate re-evaluated UNDER the R12 lock — correct-to-future-same-ferieår (reactivation),
///   correct-to-pre-go-live-passed-date (D13 floor) and the leaver-deferred fork's floor all
///   yield NotDue: no premature crystallization, no pre-launch boundary settled, no
///   fall-through to the auto-partition.</description></item>
/// </list>
///
/// <para>Race-test modeling note (DECLARED): the foreign advisory-lock-holder transaction stands
/// in for "the competing mutation path holds the R12 lock"; the competing mutation's committed
/// effect is applied via direct SQL while the loser is provably parked, then the lock is
/// released — the same choreography the TASK-7002 R12 pin established (the real endpoint would
/// itself park on the same lock, so its committed outcome is what the direct SQL writes).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementCloseLeaverTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";
    private const string StepAActor = "system:settlement-close:DEACTIVATION";

    /// <summary>Fixed poll clock: UTC 2026-03-05 ⇒ Copenhagen 2026-03-05. End date 2026-02-28
    /// (R6 ferieår 2025) has passed; ferieår 2024's §21/§24 boundary (31 Dec 2025) has passed;
    /// ferieår 2025's boundary (31 Dec 2026) has NOT.</summary>
    private static readonly DateOnly Clock = new(2026, 3, 5);

    /// <summary>The standard leaver end date — ferieår 2025 (month 2 &lt; 9 ⇒ 2026 − 1, R6).</summary>
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int EndDateFerieaar = 2025;

    /// <summary>Go-live between ferieår 2023's boundary (31 Dec 2024) and ferieår 2024's
    /// (31 Dec 2025): exactly ONE prior ferieår (2024) is due for a leaver at <see cref="Clock"/>.</summary>
    private static readonly DateOnly NarrowGoLive = new(2025, 1, 1);

    /// <summary>Go-live before every candidate boundary (the boundary-tests convention).</summary>
    private static readonly DateOnly BroadGoLive = new(2020, 1, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // NOTE: seeding happens BEFORE each test boots its derived fixed-clock host so the
        // immediate first poll observes the employees (the boundary-tests convention).
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R2 — dormant-gate-still-deactivates (the pinned test): no Settlement:GoLiveDate + passed
    // end date ⇒ flip (provenance + version bump + event + audit), NO settlement rows.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R2_DormantGate_PassedEndDate_StillDeactivates_SettlesNothing()
    {
        var employeeId = await SeedEmployeeAsync();
        await SetEndDateAsync(employeeId, EndDate); // active user, end date passed at Clock

        BootFixedClockHost(Clock, goLiveDate: null); // DORMANT — Step B skipped, Step A UNGATED

        Assert.True(await WaitForUserInactiveAsync(employeeId, TimeSpan.FromSeconds(30)),
            "Step A must flip the passed-end-date leaver even with the settlement gate dormant (R2).");

        // The full flip outcome: provenance + version bump (ADR-018 D7).
        var tuple = await ReadUserTupleAsync(employeeId);
        Assert.False(tuple.IsActive);
        Assert.True(tuple.EndDateDeactivated);
        Assert.Equal(EndDate, tuple.EndDate);
        Assert.Equal(2L, tuple.Version);

        // The EmployeeEndDateDeactivationApplied emission — version-before/after, system actor.
        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "EmployeeEndDateDeactivationApplied");
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            var root = doc.RootElement;
            Assert.Equal(employeeId, root.GetProperty("employeeId").GetString());
            Assert.Equal(EndDate.ToString("yyyy-MM-dd"), root.GetProperty("endDate").GetString());
            Assert.True(root.GetProperty("oldIsActive").GetBoolean());
            Assert.False(root.GetProperty("newIsActive").GetBoolean());
            Assert.Equal(1L, root.GetProperty("versionBefore").GetInt64());
            Assert.Equal(2L, root.GetProperty("versionAfter").GetInt64());
            Assert.Equal(StepAActor, root.GetProperty("actorId").GetString());
            Assert.Equal("System", root.GetProperty("actorRole").GetString());
        }

        // ADR-026 audit projection row, same tx (TENANT_TARGETED on the leaver's org).
        Assert.Equal(1L, await CountAsync(
            "audit_projection",
            "event_type = 'EmployeeEndDateDeactivationApplied' AND target_resource_id = @r " +
            "AND visibility_scope = 'TENANT_TARGETED' AND target_org_id = @o",
            ("r", employeeId), ("o", OrgId)));

        // users_audit UPDATED row with the lifecycle tuple transition + the system actor.
        Assert.Equal(1L, await CountAsync(
            "users_audit",
            "user_id = @u AND action = 'UPDATED' AND version_before = 1 AND version_after = 2 " +
            "AND actor_id = @a AND (new_data->>'isActive')::boolean = FALSE " +
            "AND (new_data->>'endDateDeactivated')::boolean = TRUE",
            ("u", employeeId), ("a", StepAActor)));

        // DORMANT: nothing settles — not this leaver, not the seed users (the D13 gate intact).
        Assert.False(await AnySettlementExistsAsync(),
            "A dormant poller (no Settlement:GoLiveDate) must settle NOTHING even though Step A ran.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // R3 + R2 exclusions — manually-inactive (no end date) never flipped/settled; null-end-date
    // active untouched; boundary-day (end date == today) not flipped; first-following-day flipped.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Exclusions_ManualInactive_NullEndDate_BoundaryDay_NotFlipped_DayAfter_Flipped()
    {
        var manualInactive = await SeedEmployeeAsync();   // is_active=FALSE, NO end date (R3)
        await ManuallyDeactivateAsync(manualInactive);
        var noEndDate = await SeedEmployeeAsync();        // active, end date NULL
        var boundaryDay = await SeedEmployeeAsync();      // active, end date == Clock (last employed day)
        await SetEndDateAsync(boundaryDay, Clock);
        var dayAfter = await SeedEmployeeAsync();         // active, end date == Clock − 1 ⇒ DUE
        await SetEndDateAsync(dayAfter, Clock.AddDays(-1));

        BootFixedClockHost(Clock, BroadGoLive);

        // The due flip is the poll witness: once dayAfter is flipped, Step A's candidate loop has
        // processed its whole (single-entry) due set — the others were never candidates.
        Assert.True(await WaitForUserInactiveAsync(dayAfter, TimeSpan.FromSeconds(30)),
            "end date == today − 1 (first following business day) must flip.");
        var flipped = await ReadUserTupleAsync(dayAfter);
        Assert.True(flipped.EndDateDeactivated);
        Assert.Equal(2L, flipped.Version);

        // Manually-inactive with no end date: never flipped (no provenance claim), never settled
        // as a leaver (the R3 leaver predicate keys on the end date, never bare is_active=FALSE).
        var manual = await ReadUserTupleAsync(manualInactive);
        Assert.False(manual.IsActive);
        Assert.False(manual.EndDateDeactivated);
        Assert.Equal(1L, manual.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(manualInactive, "EmployeeEndDateDeactivationApplied"));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", manualInactive)));

        // Null-end-date active user: untouched by Step A.
        var untouched = await ReadUserTupleAsync(noEndDate);
        Assert.True(untouched.IsActive);
        Assert.Equal(1L, untouched.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(noEndDate, "EmployeeEndDateDeactivationApplied"));

        // Boundary day: end date == today ⇒ still the last EMPLOYED day ⇒ NOT flipped.
        var boundary = await ReadUserTupleAsync(boundaryDay);
        Assert.True(boundary.IsActive);
        Assert.False(boundary.EndDateDeactivated);
        Assert.Equal(1L, boundary.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(boundaryDay, "EmployeeEndDateDeactivationApplied"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 + R6 — long-departed year-cap: candidate ferieår capped at the end-date ferieår; NO
    // post-termination tuples are ever generated.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R4_LongDepartedLeaver_YearCappedAtEndDateFerieaar_NoPostTerminationYears()
    {
        var employeeId = await SeedEmployeeAsync();
        var longPastEndDate = new DateOnly(2022, 5, 15); // R6 ferieår 2021 (month 5 < 9)
        await SetEndDateAsync(employeeId, longPastEndDate);

        BootFixedClockHost(Clock, BroadGoLive);

        // Step A flips, then Step B settles the capped band {2020, 2021} (floor 2020, cap 2021).
        Assert.True(await WaitForSettlementAsync(employeeId, 2021, TimeSpan.FromSeconds(30)),
            "the end-date ferieår (2021) must crystallize via TERMINATION.");
        Assert.True(await WaitForSettlementAsync(employeeId, 2020, TimeSpan.FromSeconds(30)),
            "the prior due ferieår (2020) must get the deferred-disposition YEAR_END row.");

        var termination = await ReadActiveSettlementAsync(employeeId, 2021);
        Assert.Equal(Termination, termination!.Value.Trigger);
        var prior = await ReadActiveSettlementAsync(employeeId, 2020);
        Assert.Equal(YearEnd, prior!.Value.Trigger);
        Assert.Equal("PENDING_REVIEW", prior.Value.State); // the R4 fail-closed deferred disposition

        // The R4 cap: NO settlement rows for any ferieår after the end-date ferieår — even though
        // 2022/2023/2024 boundaries have all passed at the fixed clock and go-live is broad.
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e AND entitlement_year > 2021", ("e", employeeId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R2 — pre-go-live end date ⇒ NOT settled (manual fallback), but still deactivated by Step A.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R2_PreGoLiveLeaver_Deactivated_ButNothingAutoSettled()
    {
        var leaver = await SeedEmployeeAsync();
        // End date 2025-03-31 (R6 ferieår 2024) has PASSED at Clock but falls BEFORE go-live
        // 2025-06-01 ⇒ the R2 leaver-level gate (end date STRICTLY after go-live) closes the
        // whole leaver branch: a pre-launch boundary the system never tracked (manual fallback).
        var preGoLiveEndDate = new DateOnly(2025, 3, 31);
        var goLive = new DateOnly(2025, 6, 1);
        await SetEndDateAsync(leaver, preGoLiveEndDate);
        var control = await SeedEmployeeAsync(); // ACTIVE control — the Step-B poll witness

        BootFixedClockHost(Clock, goLive);

        // Step A is UNGATED: the pre-go-live leaver still deactivates.
        Assert.True(await WaitForUserInactiveAsync(leaver, TimeSpan.FromSeconds(30)),
            "the pre-go-live leaver must still be deactivated (R2: Step A is not D13-gated).");
        var tuple = await ReadUserTupleAsync(leaver);
        Assert.True(tuple.EndDateDeactivated);
        Assert.Equal(2L, tuple.Version);

        // Step-B witness: the ACTIVE control settles ferieår 2024 (boundary 31 Dec 2025 > go-live).
        Assert.True(await WaitForSettlementAsync(control, 2024, TimeSpan.FromSeconds(30)),
            "the active control must settle 2024 — proves Step B ran on this poll.");

        // The WHOLE leaver branch is gated on the end date (strictly after go-live): NOTHING is
        // auto-settled for the pre-go-live leaver — no TERMINATION row for ferieår 2024, no
        // prior-year deferred rows. Manual fallback per D13.
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", leaver)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(leaver, "TerminationSettled"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 pin (a) — a flip-FAILED leaver (still is_active=TRUE, passed end date) must NEVER
    // traverse the normal §21/§24 auto-partition. The Step-A failure is simulated with a scoped
    // BEFORE-UPDATE trigger that aborts exactly this employee's deactivation UPDATE (per-leaver
    // isolation logs + continues, so the poll proceeds to Step B with the leaver un-flipped).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R4PinA_FlipFailedLeaver_ExcludedFromActiveBranch_NoPartitionedYearEnd()
    {
        var leaver = await SeedEmployeeAsync();
        // Ferieår 2024 (month 6 < 9) — DUE under the ACTIVE geometry at Clock, so a missing
        // exclusion would auto-partition it (the exact leak this pin forbids).
        await SetEndDateAsync(leaver, new DateOnly(2025, 6, 30));
        await InstallFlipFailureTriggerAsync(leaver);
        var control = await SeedEmployeeAsync(); // ACTIVE witness

        BootFixedClockHost(Clock, BroadGoLive);

        // Witness: Step B ran (the control settles its due years).
        Assert.True(await WaitForSettlementAsync(control, 2024, TimeSpan.FromSeconds(30)),
            "the active control must settle — proves the poll survived the Step-A failure and ran Step B.");

        // The flip genuinely failed: still active, no provenance, no version bump, no event.
        var tuple = await ReadUserTupleAsync(leaver);
        Assert.True(tuple.IsActive);
        Assert.False(tuple.EndDateDeactivated);
        Assert.Equal(1L, tuple.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(leaver, "EmployeeEndDateDeactivationApplied"));

        // R4 pin (a): the ACTIVE enumeration branch skipped the flip-failed leaver — NO settlement
        // row of any kind, NO partitioned YEAR_END, NO carryover write, NO VacationAutoPaidOut.
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", leaver)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(leaver, "VacationAutoPaidOut"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(leaver, "VacationCarryoverExecuted"));
        for (var year = 2021; year <= 2025; year++)
            Assert.Null(await ReadCarryoverInAsync(leaver, year));
    }

    // ════════════════════════════════════════════════════════════════════════
    // End-to-end — Step A flips (event + R1(e) side effects + version bump), Step B writes the
    // TERMINATION row for the end-date ferieår AND the deferred-disposition PENDING_REVIEW
    // YEAR_END row for the prior due ferieår.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EndToEnd_FlipThenSettle_TerminationRow_Plus_DeferredPriorYear()
    {
        var leaver = await SeedEmployeeAsync();
        var report = await SeedEmployeeAsync();
        await SeedReportingLineAsync(employeeId: report, managerId: leaver);
        await SetEndDateAsync(leaver, EndDate); // ferieår 2025; passed at Clock

        BootFixedClockHost(Clock, NarrowGoLive);

        // Step B both tuples (the waits double as the Step-A-then-Step-B ordering proof: the
        // TERMINATION pass requires the in-lock leaver re-read to see the flipped row).
        Assert.True(await WaitForSettlementAsync(leaver, EndDateFerieaar, TimeSpan.FromSeconds(30)),
            "the end-date ferieår must crystallize via TERMINATION.");
        Assert.True(await WaitForSettlementAsync(leaver, 2024, TimeSpan.FromSeconds(30)),
            "the prior due ferieår (2024; boundary 31 Dec 2025 > go-live) must get the deferred row.");

        // Step A outcome: flip + provenance + version bump + event + R1(e) side effect.
        var tuple = await ReadUserTupleAsync(leaver);
        Assert.False(tuple.IsActive);
        Assert.True(tuple.EndDateDeactivated);
        Assert.Equal(2L, tuple.Version);
        Assert.Equal(1L, await CountOutboxByTypeAsync(leaver, "EmployeeEndDateDeactivationApplied"));
        var sideEffect = await ReadLatestOutboxPayloadAsync(
            $"reporting-line-{report}", "ReportingLineManagerDeactivated");
        Assert.NotNull(sideEffect);
        using (var doc = JsonDocument.Parse(sideEffect!))
        {
            Assert.Equal(leaver, doc.RootElement.GetProperty("managerId").GetString());
            Assert.Equal(report, doc.RootElement.GetProperty("employeeId").GetString());
            Assert.Equal(StepAActor, doc.RootElement.GetProperty("actorId").GetString());
        }

        // The TERMINATION row (trigger selection: the end-date ferieår).
        var termination = await ReadActiveSettlementAsync(leaver, EndDateFerieaar);
        Assert.Equal(Termination, termination!.Value.Trigger);
        Assert.Equal("SETTLED", termination.Value.State); // no consumption seeded ⇒ non-negative crystallization
        Assert.Equal(1L, await CountOutboxByTypeAsync(leaver, "TerminationSettled"));

        // The prior-year deferred-disposition row (trigger YEAR_END; the service's in-lock leaver
        // re-read produced the fail-closed PENDING_REVIEW row — no pre-discrimination here).
        var prior = await ReadActiveSettlementAsync(leaver, 2024);
        Assert.Equal(YearEnd, prior!.Value.Trigger);
        Assert.Equal("PENDING_REVIEW", prior.Value.State);
        using (var snap = JsonDocument.Parse(prior.Value.SnapshotJson))
            Assert.True(snap.RootElement.GetProperty("deferredDisposition").GetBoolean());

        // Exactly the two rows — ferieår ≤ 2023 boundaries fell before go-live (gated), and the
        // R4 cap forbids years after 2025.
        Assert.Equal(2L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", leaver)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R8 — double-settle blocked: the second poll is a no-op for the already-settled leaver
    // (any-trigger anti-join + single-active index), Step A does not re-flip, and BOTH the
    // SETTLED TERMINATION row and the PENDING_REVIEW YEAR_END row suppress re-settlement.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R8_SecondPoll_NoOp_NoDoubleSettle_NoSecondFlip()
    {
        var leaver = await SeedEmployeeAsync();
        await SetEndDateAsync(leaver, EndDate);

        BootFixedClockHost(Clock, NarrowGoLive); // poll 1
        Assert.True(await WaitForSettlementAsync(leaver, EndDateFerieaar, TimeSpan.FromSeconds(30)));
        Assert.True(await WaitForSettlementAsync(leaver, 2024, TimeSpan.FromSeconds(30)));

        // A fresh witness seeded BETWEEN the polls: its settlement proves poll 2 ran end-to-end.
        var witness = await SeedEmployeeAsync();
        BootFixedClockHost(Clock, NarrowGoLive); // poll 2 (a second host — same clock/gate)
        Assert.True(await WaitForSettlementAsync(witness, 2024, TimeSpan.FromSeconds(30)),
            "the fresh witness must settle on poll 2 — proves the second poll ran.");

        // No double-settle: still exactly the two rows, version 1 each, no extra events.
        Assert.Equal(2L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", leaver)));
        Assert.Equal(1L, (await ReadActiveSettlementAsync(leaver, EndDateFerieaar))!.Value.Version);
        Assert.Equal(1L, (await ReadActiveSettlementAsync(leaver, 2024))!.Value.Version);
        Assert.Equal(1L, await CountOutboxByTypeAsync(leaver, "TerminationSettled"));
        Assert.Equal(1L, await CountOutboxByTypeAsync(leaver, "SettlementManualReviewFlagged"));
        // Step A idempotence: the flip predicate (is_active = TRUE) failed on poll 2 — no event.
        Assert.Equal(1L, await CountOutboxByTypeAsync(leaver, "EmployeeEndDateDeactivationApplied"));
        Assert.Equal(2L, (await ReadUserTupleAsync(leaver)).Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 race — clear-vs-settle: the settle parks on the employee advisory lock; a clear commits
    // in the window; the settle's in-lock due re-evaluation (S70 Step-7a B1 —
    // IsTerminationDueUnderLock on the re-read user: reactivated + NO end date) fails ⇒ the
    // pinned benign NotDue no-op — NOTHING persisted, NO event, NO throw (the corrected state is
    // owned by later polls under the new facts). [Step-7a fix-forward: the pre-B1 behavior here
    // was a fail-closed throw; the pinned B1 contract replaced it with NotDue — DECLARED.]
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R12_ClearVsSettle_SettleLoses_NotDueUnderLock_PersistsNothing()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);

        var outcome = await RunRacedSettleAsync(
            service, employeeId, EndDateFerieaar, Termination,
            commitWhileParked: () => ClearEndDateAndReactivateAsync(employeeId),
            goLiveFloor: NarrowGoLive);

        // The pinned benign NotDue no-op — the clear's outcome stands.
        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.False(outcome.RefusedConflict);
        Assert.Null(outcome.Row);
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        var tuple = await ReadUserTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 race — correct-vs-settle (DIFFERENT ferieår): a correction moving the end date into
    // ANOTHER ferieår commits in the window; the settle's in-lock due re-evaluation fails (the
    // R6 tuple-match clause — and, until the corrected date passes, the passed clause too) ⇒ the
    // pinned benign NotDue no-op, persisting nothing — the corrected ferieår settles on a later
    // poll under fresh enumeration. [Step-7a fix-forward: pre-B1 this was the R6 fail-loud
    // throw; the pinned B1 contract replaced it with NotDue — DECLARED.]
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R12_CorrectVsSettle_DifferentFerieaar_SettleLoses_NotDueUnderLock_PersistsNothing()
    {
        var service = BootService();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // ferieår 2025
        var correctedEndDate = new DateOnly(2026, 9, 15); // R6 ferieår 2026 — a DIFFERENT ferieår

        var outcome = await RunRacedSettleAsync(
            service, employeeId, EndDateFerieaar, Termination,
            commitWhileParked: () => CorrectEndDateAsync(employeeId, correctedEndDate),
            goLiveFloor: NarrowGoLive);

        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.Null(outcome.Row);
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
        Assert.Equal(correctedEndDate, (await ReadUserTupleAsync(employeeId)).EndDate); // the correction stands
    }

    // ════════════════════════════════════════════════════════════════════════
    // S70 Step-7a BLOCKER B1 (Codex) — the reviewer's exploit choreography: correct-to-FUTURE
    // date in the SAME ferieår racing Step B's TERMINATION. The R1 re-evaluation REACTIVATES the
    // user (correct-to-future on a lifecycle-deactivated row — the TASK-7002 accepted deviation);
    // pre-B1 the in-lock re-check (end-date-non-null + ferieår-match) PASSED and crystallized a
    // TERMINATION row for an ACTIVE employee at a future cutoff — premature crystallization that
    // R7a then 409-blocked from repair. The B1 in-lock predicate (is_active FALSE clause) must
    // yield the benign NotDue no-op instead. Fixed clock for determinism.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task B1_CorrectToFutureSameFerieaar_VsTermination_NotDue_NoRow_UserStaysReactivated()
    {
        var service = BootFixedClockService(Clock); // Copenhagen today = 2026-03-05
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // flipped leaver, end date 2026-02-28, ferieår 2025
        // SAME ferieår (2025 = Sep 2025 .. Aug 2026), FUTURE at the fixed clock.
        var correctedEndDate = new DateOnly(2026, 8, 15);

        var outcome = await RunRacedSettleAsync(
            service, employeeId, EndDateFerieaar, Termination,
            commitWhileParked: () => CorrectEndDateAndReactivateAsync(employeeId, correctedEndDate),
            goLiveFloor: NarrowGoLive);

        // The pinned benign NotDue no-op — NO settlement row of ANY trigger, NO event, NO throw.
        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.False(outcome.RefusedConflict);
        Assert.Null(outcome.Row);
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));

        // The user remains REACTIVATED with the corrected future date — the correction wins.
        var tuple = await ReadUserTupleAsync(employeeId);
        Assert.True(tuple.IsActive);
        Assert.False(tuple.EndDateDeactivated);
        Assert.Equal(correctedEndDate, tuple.EndDate);
    }

    // ════════════════════════════════════════════════════════════════════════
    // S70 Step-7a BLOCKER B1 (Codex) — the D13 variant: correct-to-PASSED PRE-GO-LIVE date in
    // the SAME ferieår racing TERMINATION. The corrected leaver stays deactivated (still-passed
    // date per R1), but its boundary now precedes go-live — a pre-launch boundary the system
    // never tracked (manual fallback per D13). Pre-B1 the in-lock re-check passed (ferieår
    // unchanged) and the system settled a pre-go-live boundary; the B1 floor clause must yield
    // the benign NotDue no-op. Go-live placed INSIDE ferieår 2025 so a same-ferieår pre-go-live
    // date exists.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task B1_CorrectToPreGoLivePassedDate_SameFerieaar_VsTermination_NotDue_NoRow()
    {
        var service = BootFixedClockService(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // end date 2026-02-28 — strictly after the floor below
        var goLiveFloor = new DateOnly(2026, 1, 15);             // inside ferieår 2025
        var correctedEndDate = new DateOnly(2025, 10, 15);        // ferieår 2025, PASSED at Clock, <= floor

        var outcome = await RunRacedSettleAsync(
            service, employeeId, EndDateFerieaar, Termination,
            commitWhileParked: () => CorrectEndDateAsync(employeeId, correctedEndDate),
            goLiveFloor: goLiveFloor);

        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.Null(outcome.Row);
        // NO row written — the pre-go-live boundary is the D13 manual fallback.
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        // The corrected (still-passed) date stands; the user stays deactivated.
        var tuple = await ReadUserTupleAsync(employeeId);
        Assert.False(tuple.IsActive);
        Assert.Equal(correctedEndDate, tuple.EndDate);
    }

    // ════════════════════════════════════════════════════════════════════════
    // S70 Step-7a BLOCKER B1 (Codex) — the leaver-deferred fork's go-live floor: a direct
    // YEAR_END drive for a leaver whose end date is ON/BEFORE the floor must yield the benign
    // NotDue no-op — NEITHER the deferred-disposition PENDING_REVIEW row NOR a fall-through to
    // the ACTIVE §21/§24 auto-partition (the pinned no-fall-through).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task B1_LeaverDeferredFork_PreGoLiveFloor_NotDue_NoDeferredRow_NoAutoPartition()
    {
        var service = BootFixedClockService(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);        // end date 2026-02-28, passed at Clock
        var goLiveFloor = new DateOnly(2026, 6, 1);        // end date <= floor ⇒ pre-go-live leaver

        var outcome = await SettleInOwnTxAsync(service, employeeId, 2024, YearEnd, goLiveFloor);

        // Benign NotDue — no deferred row, no auto-partition row, no event, no carryover.
        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.Null(outcome.Row);
        Assert.Equal(0L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementManualReviewFlagged"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationAutoPaidOut"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted"));
        Assert.Null(await ReadCarryoverInAsync(employeeId, 2025));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 race — clear-vs-Step-A-flip: the live poller's Step A parks on the lock; a clear that
    // wins ⇒ the flip's predicate-guarded UPDATE hits 0 rows ⇒ benign no-op (no event, user stays
    // active), and the per-leaver loop proceeds to the next due leaver.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R12_ClearVsStepAFlip_ClearWins_FlipNoOps_NoEvent_UserStaysActive()
    {
        // Two due leavers with controlled ordering (Step A iterates ORDER BY user_id): the parked
        // 'a' leaver first, then the 'b' witness whose flip proves the loop resumed past the no-op.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var racedLeaver = "emp_s70_lvra_" + suffix;
        var witnessLeaver = "emp_s70_lvrb_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, racedLeaver, OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, witnessLeaver, OrgId, "AC", "OK24");
        await SetEndDateAsync(racedLeaver, EndDate);
        await SetEndDateAsync(witnessLeaver, EndDate);

        // Hold the raced leaver's R12 advisory lock on a foreign tx BEFORE the poller boots.
        await using var lockConn = new NpgsqlConnection(_harness.ConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await lockConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", lockConn, lockTx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", racedLeaver);
            await lockCmd.ExecuteScalarAsync();
        }

        BootFixedClockHost(Clock, goLiveDate: null); // dormant Step B — this race isolates Step A

        // Deterministic parked-proof: some backend is lock-waiting inside pg_advisory_xact_lock.
        Assert.True(await WaitForAdvisoryLockWaiterAsync(TimeSpan.FromSeconds(30)),
            "Step A never parked on the held employee advisory lock.");
        // The witness (later in user_id order) must not have been flipped while the loop is parked.
        Assert.True((await ReadUserTupleAsync(witnessLeaver)).IsActive);

        // The clear WINS while the flip is parked (the committed R1(c) outcome — see the class
        // doc's race-modeling note; the user is still active, so the clear only removes the date).
        await ClearEndDateAndReactivateAsync(racedLeaver);

        // Release the lock — Step A resumes: in-lock re-read + the UPDATE's re-evaluated WHERE
        // both see the cleared end date ⇒ 0 rows ⇒ benign no-op, loop continues to the witness.
        await lockTx.RollbackAsync();

        Assert.True(await WaitForUserInactiveAsync(witnessLeaver, TimeSpan.FromSeconds(30)),
            "the witness leaver must flip after the lock release — proves the loop resumed.");

        // Coherent loser outcome: NO flip, NO event, NO version bump beyond the clear's own.
        var tuple = await ReadUserTupleAsync(racedLeaver);
        Assert.True(tuple.IsActive);
        Assert.False(tuple.EndDateDeactivated);
        Assert.Null(tuple.EndDate);
        Assert.Equal(0L, await CountOutboxByTypeAsync(racedLeaver, "EmployeeEndDateDeactivationApplied"));
        Assert.Equal(0L, await CountAsync(
            "users_audit", "user_id = @u AND actor_id = @a", ("u", racedLeaver), ("a", StepAActor)));
    }

    // ─────────────────────────────── race choreography ───────────────────────────────

    /// <summary>Runs one settlement pass (the Step-B drive shape) against a HELD employee advisory
    /// lock: fire the pass on a background task, prove it parked (pg_stat_activity), commit the
    /// competing mutation, release the lock, and return/propagate the pass outcome.
    /// <paramref name="goLiveFloor"/> mirrors the close service's ALWAYS-supplied Step-B
    /// leaverGoLiveFloor (S70 Step-7a B1); null = the pre-B1 direct-drive shape.</summary>
    private async Task<SettlementOutcome> RunRacedSettleAsync(
        VacationSettlementService service, string employeeId, int year, string trigger,
        Func<Task> commitWhileParked, DateOnly? goLiveFloor = null)
    {
        await using var lockConn = new NpgsqlConnection(_harness.ConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await lockConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", lockConn, lockTx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", employeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        var settleTask = Task.Run(() => SettleInOwnTxAsync(service, employeeId, year, trigger, goLiveFloor));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (settleTask.IsCompleted)
            {
                var early = await settleTask; // propagate a real failure, or fail loud on early success
                Assert.Fail(
                    $"the settle pass completed early (DidSettle={early.DidSettle}) without parking " +
                    "on the held advisory lock — the R12 race window was not exercised.");
            }
            parked = await IsAdvisoryLockWaiterPresentAsync();
            if (!parked)
                await Task.Delay(100);
        }
        Assert.True(parked, "the settle pass never parked on the held employee advisory lock within 30s.");

        await commitWhileParked();
        await lockTx.RollbackAsync(); // release — the pass resumes and re-evaluates in-lock

        return await settleTask;
    }

    /// <summary>One settlement pass in its OWN ReadCommitted tx, committed — the exact
    /// SettlementCloseService Step-B shape (the TerminationSettlementTests driver).
    /// <paramref name="goLiveFloor"/> = the S70 Step-7a B1 leaverGoLiveFloor pass-through.</summary>
    private async Task<SettlementOutcome> SettleInOwnTxAsync(
        VacationSettlementService service, string employeeId, int year, string trigger,
        DateOnly? goLiveFloor = null)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(
                employeeId, VacationType, year, trigger, conn, tx, leaverGoLiveFloor: goLiveFloor);
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

    /// <summary>True when some backend on this database is LOCK-waiting inside a
    /// <c>pg_advisory_xact_lock</c> call — the deterministic is-the-loser-parked probe (the
    /// probing backend itself is never lock-waiting, so the filter excludes it).</summary>
    private async Task<bool> IsAdvisoryLockWaiterPresentAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND wait_event_type = 'Lock'
              AND query ILIKE '%pg_advisory_xact_lock%'
            """, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<bool> WaitForAdvisoryLockWaiterAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsAdvisoryLockWaiterPresentAsync())
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    // ─────────────────────────────── host boot / service ───────────────────────────────

    /// <summary>Boots a derived WAF host with a fixed clock + optional go-live date (the PAT-008
    /// SettlementCloseServiceBoundaryTests harness). The boot starts the live poller, which runs
    /// one poll (Step A, then the gated Step B) immediately against the fixed clock.</summary>
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

    /// <summary>Boots the default host (seeders; dormant close service) and resolves the
    /// DI-registered settlement service — the race-test drive shape.</summary>
    private VacationSettlementService BootService()
    {
        _ = _factory.CreateClient();
        return _factory.Services.GetRequiredService<VacationSettlementService>();
    }

    /// <summary>S70 Step-7a B1 — a FIXED-clock derived host's settlement service (PAT-008), so the
    /// service's in-lock Copenhagen-today comparison is deterministic in the B1 race tests. No
    /// go-live config ⇒ the derived host's own poller stays dormant on Step B (Step A is a no-op
    /// for the already-flipped leavers these tests seed).</summary>
    private VacationSettlementService BootFixedClockService(DateOnly fixedDate)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate))));
        _ = derived.CreateClient();
        return derived.Services.GetRequiredService<VacationSettlementService>();
    }

    // ─────────────────────────────── waits ───────────────────────────────

    private async Task<bool> WaitForUserInactiveAsync(string employeeId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!(await ReadUserTupleAsync(employeeId)).IsActive)
                return true;
            await Task.Delay(250);
        }
        return false;
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

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s70_close_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>A stored end date with NO deactivation — the R1(b)/pre-Step-A state.</summary>
    private async Task SetEndDateAsync(string employeeId, DateOnly endDate)
    {
        await ExecAsync(
            "UPDATE users SET employment_end_date = @endDate, updated_at = NOW() WHERE user_id = @id",
            ("id", employeeId), ("endDate", endDate));
    }

    /// <summary>The post-Step-A leaver state, seeded directly (the TerminationSettlementTests
    /// convention) — for the race tests that drive SettleAsync without the poller.</summary>
    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId), ("endDate", endDate));
    }

    /// <summary>Manual admin deactivation surrogate: is_active=FALSE, NO end date, NO provenance.</summary>
    private async Task ManuallyDeactivateAsync(string employeeId)
    {
        await ExecAsync(
            "UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE user_id = @id",
            ("id", employeeId));
    }

    /// <summary>The committed R1(c) clear outcome (the winning competitor in the race tests):
    /// end date cleared, provenance reset, reactivated, version bumped. Idempotent on an
    /// already-active row (it then only clears the date).</summary>
    private async Task ClearEndDateAndReactivateAsync(string employeeId)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = NULL, end_date_deactivated = FALSE,
                             is_active = TRUE, version = version + 1, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId));
    }

    /// <summary>The committed correction outcome (still-passed date ⇒ stays deactivated, R1).</summary>
    private async Task CorrectEndDateAsync(string employeeId, DateOnly newEndDate)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = @endDate, version = version + 1, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId), ("endDate", newEndDate));
    }

    /// <summary>S70 Step-7a B1 — the committed correct-to-UNPASSED-date outcome on a
    /// lifecycle-deactivated row: the endpoint's R1 deterministic re-evaluation REACTIVATES and
    /// resets provenance (the TASK-7002 accepted deviation; the Step-A poller re-flips when the
    /// new date passes). The reviewer's exploit competitor.</summary>
    private async Task CorrectEndDateAndReactivateAsync(string employeeId, DateOnly newEndDate)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = TRUE,
                             end_date_deactivated = FALSE, version = version + 1, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId), ("endDate", newEndDate));
    }

    private async Task SeedReportingLineAsync(string employeeId, string managerId)
    {
        await ExecAsync(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, organisation_id, relationship, effective_from, created_by)
            VALUES (@employeeId, @managerId, @treeRoot, 'PRIMARY', @from, 'test_s70_close')
            """,
            ("employeeId", employeeId), ("managerId", managerId),
            ("treeRoot", OrgId), ("from", new DateOnly(2024, 1, 1)));
    }

    /// <summary>R4 pin (a) — a scoped BEFORE-UPDATE trigger that aborts exactly THIS employee's
    /// deactivation UPDATE (the simulated Step-A failure; per-leaver isolation logs + continues).</summary>
    private async Task InstallFlipFailureTriggerAsync(string employeeId)
    {
        await ExecAsync(
            $"""
            CREATE OR REPLACE FUNCTION s70_simulate_stepa_failure() RETURNS trigger AS $fn$
            BEGIN
                IF NEW.user_id = '{employeeId}' AND OLD.is_active = TRUE AND NEW.is_active = FALSE THEN
                    RAISE EXCEPTION 'TASK-7005 R4 pin (a): simulated Step-A flip failure for %', NEW.user_id;
                END IF;
                RETURN NEW;
            END;
            $fn$ LANGUAGE plpgsql;

            CREATE TRIGGER s70_simulate_stepa_failure_trg
            BEFORE UPDATE ON users
            FOR EACH ROW EXECUTE FUNCTION s70_simulate_stepa_failure();
            """);
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(DateOnly? EndDate, bool EndDateDeactivated, bool IsActive, long Version)>
        ReadUserTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_end_date, end_date_deactivated, is_active, version
            FROM users WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"user row expected for {employeeId}");
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }

    private async Task<(string State, string Trigger, int Sequence, long Version, string SnapshotJson)?>
        ReadActiveSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, trigger, sequence, version, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetInt64(3), reader.GetString(4));
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

    private async Task<string?> ReadLatestOutboxPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId AND event_type = @eventType
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        return (string?)await cmd.ExecuteScalarAsync();
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

    private async Task<bool> AnySettlementExistsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM vacation_settlements)", conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
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

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
