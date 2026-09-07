using System.Text.Json;
using Npgsql;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Calendar;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S138 / TASK-13803 — Docker-gated repository pins for <see cref="HrBackdateWorklistRepository"/>
/// (the HR backdate diagnostic worklist, ADR-040 D8 / Increment 3). RED-FIRST: every expected row
/// set below is DERIVED FROM THE REFINEMENT SPEC (rev 4.2, TASK-13803 + Assumptions 3/12), not
/// from observing the code — Docker is unavailable on the authoring machine, so these verify in CI.
///
/// <para>Pinned:</para>
/// <list type="bullet">
///   <item>exported-month selection = every <c>payroll_export_records</c> row whose month intersects
///     <c>[from, toExclusive)</c>; the open-ended case clips at the CURRENT month; approved-but-not-
///     exported months (no export row) yield NOTHING;</item>
///   <item>settled-year selection, S138 / TASK-13810 (owner ruling 2026-09-03) = the ACTIVE
///     settlement (highest sequence, state ≠ REVERSED — a REVERSED-only tuple is NOT selected)
///     that the correction THREATENS, which takes TWO conjoined tests: it starts before the
///     settlement's VALUATION BOUNDARY (the last day it counted; renamed from "crystallization date" at Step-7a) AND overlaps that year's entitlement window, with a
///     conservative flag whenever either half cannot be evaluated. Both halves are pinned by the
///     case that fails without them — window-only raised a row on an ordinary present-day edit
///     (taking windows commonly run past today); boundary-only raised a row for every settlement
///     frozen after an old, narrow correction;</item>
///   <item>the SECOND settled-year entry point (<c>WriteForSkippedSettledYearsAsync</c>): a row for
///     every group the caller's revaluation ACTUALLY skipped, whatever the dates say, collapsing
///     with the date path into ONE row carrying two triggers;</item>
///   <item>append-on-open: a second trigger APPENDS to the open row with its OWN baseline (hash /
///     sequence captured at append time, never inherited), bumps version, emits the Created event
///     with <c>appended = true</c>, and returns the SAME row id;</item>
///   <item>both partial UNIQUEs at the DB level and that resolving frees the key;</item>
///   <item>resolve: version guard (stale → <see cref="OptimisticConcurrencyException"/>), the
///     Resolved event + ADR-026 row in the same tx, already-resolved refused;</item>
///   <item>the derived flags against LIVE current state (hash advanced in place; reverse-then-
///     re-settle AND bare reversal legs);</item>
///   <item>the org-subtree read filters on the employee's <c>users.primary_org_id</c>.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class HrBackdateWorklistRepositoryTests : IAsyncLifetime
{
    private const string Actor = "hr_s138_actor";

    private TestFixtures.DockerHarness _harness = null!;
    private HrBackdateWorklistRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _repo = new HrBackdateWorklistRepository(
            _harness.Factory,
            new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api")),
            new BackdateWorklistRowCreatedAuditMapper(),
            new BackdateWorklistRowResolvedAuditMapper(),
            new AuditProjectionRepository(_harness.Factory),
            TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPORTED_MONTH selection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteForExportedMonths_SelectsMonthsIntersectingInterval_OneRowPerMonth_BaselineHashPerRow()
    {
        const string emp = "wl_emp_exp_select";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        var janId = await SeedExportAsync(emp, 2026, 1, "h-2026-01");
        var febId = await SeedExportAsync(emp, 2026, 2, "h-2026-02");
        var marId = await SeedExportAsync(emp, 2026, 3, "h-2026-03");
        var aprId = await SeedExportAsync(emp, 2026, 4, "h-2026-04");

        // Interval [10 Feb, 1 Apr): Feb (partially covered) + Mar (fully) intersect; Jan is before
        // `from`; Apr's month-start equals toExclusive → the half-open interval does NOT reach it.
        var trigger = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 2, 10));
        var ids = await RunExportedAsync(emp, trigger, new DateOnly(2026, 2, 10), new DateOnly(2026, 4, 1));

        Assert.Equal(2, ids.Count);
        var rows = await _repo.GetOpenAsync(emp);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(WorklistKinds.ExportedMonth, r.Kind));
        Assert.Equal(new[] { (2026, 2), (2026, 3) }, rows.Select(r => (r.Year!.Value, r.Month!.Value)).ToArray());
        Assert.Equal(new[] { febId, marId }, rows.Select(r => r.ExportId!.Value).ToArray());
        Assert.Equal(new[] { "h-2026-02", "h-2026-03" }, rows.Select(r => r.Triggers.Single().BaselineContentHash).ToArray());
        Assert.All(rows, r =>
        {
            Assert.Equal(1L, r.Version);
            Assert.Null(r.ResolvedAt);
            Assert.Equal(Actor, r.CreatedBy);
            Assert.Null(r.EntitlementType);
            Assert.Null(r.EntitlementYear);
            var t = r.Triggers.Single();
            Assert.Equal(WorklistTriggerKinds.ProfileChange, t.Kind);
            Assert.Equal(trigger.EventId, t.EventId);
            Assert.Equal(new DateOnly(2026, 2, 10), t.EffectiveFrom);
            Assert.Equal(Actor, t.ActorId);
            Assert.Null(t.BaselineSettlementSequence);
            // Current state rides with the row: the export's CURRENT hash == the baseline right now.
            Assert.Equal(t.BaselineContentHash, r.Current.CurrentContentHash);
        });
        Assert.DoesNotContain(rows, r => r.ExportId == janId || r.ExportId == aprId);

        // ADR-018 D3: one Created event per row on employee-{id}, none appended; ADR-026 rows.
        var events = await OutboxEventsAsync(emp, "BackdateWorklistRowCreated");
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.False(e.GetProperty("appended").GetBoolean()));
        Assert.Equal(new[] { 2, 3 }, events.Select(e => e.GetProperty("month").GetInt32()).OrderBy(m => m).ToArray());
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowCreated' AND target_resource_id = @p0", emp));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowCreated' AND target_resource_id = @p0 AND target_org_id = 'STY_WL_A' AND visibility_scope = 'TENANT_TARGETED'", emp));
    }

    [Fact]
    public async Task WriteForExportedMonths_OpenEnded_SelectsThroughCurrentMonth_NotBeyond()
    {
        const string emp = "wl_emp_exp_open";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        var today = CopenhagenBusinessDate.Today(TimeProvider.System);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var prevMonth = thisMonth.AddMonths(-1);
        var twoBack = thisMonth.AddMonths(-2);
        await SeedExportAsync(emp, twoBack.Year, twoBack.Month, "h-two-back");
        await SeedExportAsync(emp, prevMonth.Year, prevMonth.Month, "h-prev");
        await SeedExportAsync(emp, thisMonth.Year, thisMonth.Month, "h-current");
        await SeedExportAsync(emp, 2999, 1, "h-far-future"); // cannot legitimately exist; pins the clip

        var ids = await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.AgreementCodeChange, prevMonth.AddDays(4)),
            from: prevMonth.AddDays(4), toExclusive: null);

        Assert.Equal(2, ids.Count);
        var rows = await _repo.GetOpenAsync(emp);
        Assert.Equal(
            new[] { (prevMonth.Year, prevMonth.Month), (thisMonth.Year, thisMonth.Month) },
            rows.Select(r => (r.Year!.Value, r.Month!.Value)).ToArray());
    }

    [Fact]
    public async Task WriteForExportedMonths_ApprovedButNotExported_NoRowsNoEvents()
    {
        const string emp = "wl_emp_exp_none";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        // No payroll_export_records rows at all — an approved-but-not-exported month needs no row
        // (the next export reads dated history; refinement discovery 2).

        var ids = await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 1, 15)),
            new DateOnly(2026, 1, 15), null);

        Assert.Empty(ids);
        Assert.Empty(await _repo.GetOpenAsync(emp));
        Assert.Empty(await OutboxEventsAsync(emp, "BackdateWorklistRowCreated"));

        // An EMPTY / inverted interval also yields nothing, even with an export row present.
        await SeedExportAsync(emp, 2026, 1, "h1");
        Assert.Empty(await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 1, 15)),
            new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15)));
        Assert.Empty(await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 1, 15)),
            new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 10)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Append-on-open + per-trigger baseline capture + derived flags (exported month)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteForExportedMonths_AppendOnOpenRow_CapturesBaselineAtAppendTime_BumpsVersion_EmitsAppended()
    {
        const string emp = "wl_emp_exp_append";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        var exportId = await SeedExportAsync(emp, 2026, 5, "h1");

        var first = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 5, 10));
        var firstIds = await RunExportedAsync(emp, first, new DateOnly(2026, 5, 10), new DateOnly(2026, 6, 1));
        var worklistId = Assert.Single(firstIds);

        // A correction export commits: the Payroll writer advances content_hash IN PLACE
        // (PayrollExportRecordRepository.UpdateCurrentEffectiveLinesAsync — no timestamp moves).
        await ExecAsync(
            """
            UPDATE payroll_export_records
            SET content_hash = 'h2', current_effective_lines = '[{"corrected":true}]'::jsonb
            WHERE export_id = @p0
            """, exportId);

        var second = Trigger(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 5, 20));
        var secondIds = await RunExportedAsync(emp, second, new DateOnly(2026, 5, 20), new DateOnly(2026, 6, 1));

        // SAME row id (append, not a second open row); version bumped; both triggers in order,
        // EACH with the baseline it saw at ITS append time.
        Assert.Equal(worklistId, Assert.Single(secondIds));
        var row = await _repo.GetByIdWithVersionAsync(worklistId);
        Assert.NotNull(row);
        Assert.Equal(2L, row!.Version);
        Assert.Equal(2, row.Triggers.Count);
        Assert.Equal(WorklistTriggerKinds.ProfileChange, row.Triggers[0].Kind);
        Assert.Equal("h1", row.Triggers[0].BaselineContentHash);
        Assert.Equal(WorklistTriggerKinds.AgreementCodeChange, row.Triggers[1].Kind);
        Assert.Equal("h2", row.Triggers[1].BaselineContentHash); // captured NOW, never inherited
        Assert.Equal(second.EventId, row.Triggers[1].EventId);
        Assert.Single(await _repo.GetOpenAsync(emp));

        // Derived flags against the LIVE state: the first trigger's baseline is stale (recalculated
        // since), the second's is current; the ROW is recalculated only when EVERY trigger is.
        Assert.Equal("h2", row.Current.CurrentContentHash);
        Assert.True(BackdateWorklistDerivation.RecalculatedSinceForTrigger(row, row.Triggers[0]));
        Assert.False(BackdateWorklistDerivation.RecalculatedSinceForTrigger(row, row.Triggers[1]));
        Assert.False(BackdateWorklistDerivation.RecalculatedSince(row));
        // Both dates are strictly inside May → both re-plan blockers are visible (owner ruling OQ-3 (a)).
        Assert.Equal(new[] { "QUAL-149", "QUAL-150" }, BackdateWorklistDerivation.RecalcBlockedBy(row));
        Assert.Null(BackdateWorklistDerivation.ReversedSince(row));

        // Events: the first Created (appended=false, rowVersion 1) + the appended one (true, 2).
        var events = await OutboxEventsAsync(emp, "BackdateWorklistRowCreated");
        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { false, true }, events.Select(e => e.GetProperty("appended").GetBoolean()).ToArray());
        Assert.Equal(new[] { 1L, 2L }, events.Select(e => e.GetProperty("rowVersion").GetInt64()).ToArray());
        Assert.Equal("h2", events[1].GetProperty("baselineContentHash").GetString());
        Assert.All(events, e => Assert.Equal(worklistId, e.GetProperty("worklistId").GetGuid()));
    }

    // ════════════════════════════════════════════════════════════════════════
    // SETTLED_YEAR selection
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S138 / TASK-13810 — the DATE rule (both halves conjoined): a settlement is flagged when the
    /// correction reaches back PAST the moment it was frozen AND overlaps that year's entitlement
    /// window. Three settlements, one correction, all three answers pinned — plus the
    /// reverse-then-re-settle baseline (the ACTIVE row is the highest sequence, not the REVERSED
    /// one) and a bare-reversal employee who has no active settlement at all.
    /// </summary>
    [Fact]
    public async Task WriteForSettledYears_SelectsOnlySettlementsFrozenAfterTheCorrection_ReversedOnly_NotActive()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        // The correction is June 2025. Employee A, three settlements:
        //   VACATION 2023  window [1 Sep 2023, 31 Aug 2024] + taking → 31 Dec 2024, frozen
        //                  31 Aug 2024 — BOTH halves fail (June 2025 is outside the window AND
        //                  after the freeze) ⇒ no row.
        //   VACATION 2024  window [1 Sep 2024, 31 Aug 2025] contains June 2025, frozen 31 Aug 2025
        //                  ⇒ both halves hold. Reverse-then-re-settled (seq 1 REVERSED, seq 2
        //                  SETTLED, the ADR-033 D4/D5 shape) ⇒ row, baseline = seq 2.
        //   SPECIAL_HOLIDAY 2024 taking window [1 May 2025, 30 Apr 2026] contains June 2025, frozen
        //                  30 Apr 2026 ⇒ both halves hold ⇒ row.
        const string empA = "wl_emp_set_a";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, empA, "STY_WL_A");
        await SeedSettlementAsync(empA, "VACATION", 2023, 1, "SETTLED", new DateOnly(2024, 8, 31));
        await SeedSettlementAsync(empA, "VACATION", 2024, 1, "REVERSED", new DateOnly(2025, 8, 31));
        await SeedSettlementAsync(empA, "VACATION", 2024, 2, "SETTLED", new DateOnly(2025, 8, 31));
        await SeedSettlementAsync(empA, "SPECIAL_HOLIDAY", 2024, 1, "SETTLED", new DateOnly(2026, 4, 30));

        // Employee B: VACATION 2024 REVERSED only (bare reversal, no re-settle) — NOT active.
        const string empB = "wl_emp_set_b";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, empB, "STY_WL_A");
        await SeedSettlementAsync(empB, "VACATION", 2024, 1, "REVERSED", new DateOnly(2025, 8, 31));

        var from = new DateOnly(2025, 6, 1);
        var to = new DateOnly(2025, 7, 1);
        var trigger = Trigger(WorklistTriggerKinds.EmploymentCategoryChange, from);

        var idsA = await RunSettledAsync(empA, trigger, from, to);
        var idsB = await RunSettledAsync(empB, trigger, from, to);

        Assert.Equal(2, idsA.Count);
        Assert.Empty(idsB);
        Assert.Empty(await _repo.GetOpenAsync(empB));

        var rowsA = await _repo.GetOpenAsync(empA);
        Assert.Equal(2, rowsA.Count);
        Assert.All(rowsA, r =>
        {
            Assert.Equal(WorklistKinds.SettledYear, r.Kind);
            Assert.Null(r.Year);
            Assert.Null(r.Month);
            Assert.Null(r.ExportId);
            Assert.Equal(1L, r.Version);
            Assert.Null(r.Triggers.Single().BaselineContentHash);
        });
        var vacation = Assert.Single(rowsA, r => r.EntitlementType == "VACATION");
        Assert.Equal(2024, vacation.EntitlementYear);
        Assert.Equal(2, vacation.Triggers.Single().BaselineSettlementSequence); // the ACTIVE (highest) row, not the REVERSED seq 1
        Assert.Equal("SETTLED", vacation.Triggers.Single().BaselineSettlementState);
        Assert.Equal(2, vacation.Current.HighestSettlementSequence);
        Assert.Equal(new[] { 1 }, vacation.Current.ReversedSettlementSequences.OrderBy(s => s).ToArray());
        Assert.False(BackdateWorklistDerivation.ReversedSince(vacation)); // baseline 2 is the live one

        var special = Assert.Single(rowsA, r => r.EntitlementType == "SPECIAL_HOLIDAY");
        Assert.Equal(2024, special.EntitlementYear);
        Assert.Equal(1, special.Triggers.Single().BaselineSettlementSequence);

        // Outside its window AND frozen before the correction begins — untouchable either way.
        Assert.DoesNotContain(rowsA, r => r.EntitlementType == "VACATION" && r.EntitlementYear == 2023);

        var events = await OutboxEventsAsync(empA, "BackdateWorklistRowCreated");
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("SETTLED_YEAR", e.GetProperty("kind").GetString()));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowCreated' AND target_resource_id = @p0", empA));
    }

    /// <summary>
    /// S138 / TASK-13810 — THE FALSE POSITIVE THE RULING REMOVES, stated as one falsifiable fact,
    /// and the reason the FREEZE-MOMENT half is load-bearing. A VACATION ferieår 2024 (reset_month
    /// 9) is frozen on 31 Aug 2025; its TAKING window runs on to the §21 deadline of 31 Dec 2025. A
    /// correction dated 1 Oct 2025 therefore sits INSIDE that taking window, so the geometry half
    /// holds and the OLD window-only rule raised a SETTLED_YEAR row — but it starts AFTER the
    /// boundary, so nothing the settlement valued can have moved. No row.
    /// </summary>
    [Fact]
    public async Task WriteForSettledYears_CorrectionAfterTheFreeze_RaisesNothing_EvenInsideTheTakingWindow()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        const string emp = "wl_emp_set_afterfreeze";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));

        var from = new DateOnly(2025, 10, 1);
        var ids = await RunSettledAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, from), from, new DateOnly(2025, 11, 1));

        Assert.Empty(ids);
        Assert.Empty(await _repo.GetOpenAsync(emp));
        Assert.Empty(await OutboxEventsAsync(emp, "BackdateWorklistRowCreated"));
    }

    /// <summary>
    /// S138 / TASK-13810 — the conservative leg of the FREEZE-MOMENT half, exercised through the
    /// conjunction. When the settlement's snapshot carries no usable valuation-boundary date, that half
    /// cannot exonerate the year, so a correction whose interval DOES overlap the year's window is
    /// flagged rather than silently skipped: a dismissable row beats a stale settlement nobody was
    /// told about. Both unusable shapes are pinned — the key ABSENT, and the key present holding the
    /// <c>0001-01-01</c> that an uninitialized snapshot serializes. The geometry half is deliberately
    /// SATISFIED here so the boundary half is the only thing under test; the sibling assertion below
    /// shows an unknown boundary does not rescue a correction that misses the window.
    /// </summary>
    [Fact]
    public async Task WriteForSettledYears_UnreadableBoundary_FlagsConservatively_BothShapes()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        const string emp = "wl_emp_set_noboundary";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "SETTLED");                    // '{}' — key absent
        await SeedSettlementAsync(emp, "SPECIAL_HOLIDAY", 2024, 1, "SETTLED", default(DateOnly)); // "0001-01-01"

        // June 2025 lies inside VACATION 2024's accrual window [1 Sep 2024, 31 Aug 2025] AND inside
        // SPECIAL_HOLIDAY 2024's taking window [1 May 2025, 30 Apr 2026]. Neither boundary can be
        // read ⇒ both flagged.
        var from = new DateOnly(2025, 6, 1);
        var ids = await RunSettledAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, from), from, new DateOnly(2025, 7, 1));

        Assert.Equal(2, ids.Count);
        var rows = await _repo.GetOpenAsync(emp);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(WorklistKinds.SettledYear, r.Kind));

        // A second employee, same unreadable snapshots, a correction that MISSES both windows
        // (Feb 2023 predates every window here): the unknown boundary does not rescue it.
        const string other = "wl_emp_set_noboundary_miss";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, other, "STY_WL_A");
        await SeedSettlementAsync(other, "VACATION", 2024, 1, "SETTLED");
        var missFrom = new DateOnly(2023, 2, 1);
        Assert.Empty(await RunSettledAsync(other, Trigger(WorklistTriggerKinds.ProfileChange, missFrom),
            missFrom, new DateOnly(2023, 3, 1)));
    }

    /// <summary>
    /// S138 / TASK-13810 (conjunction ruled 2026-09-03) — the reason the GEOMETRY half is
    /// load-bearing, as the mirror of the false-positive pin above. One narrow, OLD correction (a
    /// single week of March 2021) against two settled ferieår, BOTH frozen after it:
    ///   • VACATION 2020 — accrual [1 Sep 2020, 31 Aug 2021], frozen 31 Aug 2021: the week is inside
    ///     it ⇒ ROW;
    ///   • VACATION 2024 — accrual [1 Sep 2024, 31 Aug 2025], frozen 31 Aug 2025: the week merely
    ///     PREDATES the freeze and is nowhere near the window ⇒ NO row.
    /// Under the freeze-moment half alone, correcting one week of 2020 would raise a row for 2020
    /// and for every year settled since — dismissals crowding out the one true row.
    /// </summary>
    [Fact]
    public async Task WriteForSettledYears_OldNarrowCorrection_FlagsOnlyTheYearWhoseWindowItOverlaps()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        const string emp = "wl_emp_set_narrow_old";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2020, 1, "SETTLED", new DateOnly(2021, 8, 31));
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));

        var from = new DateOnly(2021, 3, 1);
        var ids = await RunSettledAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, from),
            from, new DateOnly(2021, 3, 8));

        Assert.Single(ids);
        var row = Assert.Single(await _repo.GetOpenAsync(emp));
        Assert.Equal("VACATION", row.EntitlementType);
        Assert.Equal(2020, row.EntitlementYear);
        Assert.Single(await OutboxEventsAsync(emp, "BackdateWorklistRowCreated"));
    }

    [Fact]
    public async Task WriteForSettledYears_AppendOnOpenRow_BaselineSequenceCapturedAtAppendTime_ReversedSinceBothLegs()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        // Leg 1 — reverse-then-re-settle (the correct ADR-033 D4/D5 fix).
        const string empA = "wl_emp_set_append";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, empA, "STY_WL_A");
        await SeedSettlementAsync(empA, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));
        var from = new DateOnly(2025, 3, 1); // before the 31 Aug 2025 freeze ⇒ the date rule selects
        var to = new DateOnly(2025, 4, 1);

        var firstIds = await RunSettledAsync(empA, Trigger(WorklistTriggerKinds.ProfileChange, from), from, to);
        var worklistId = Assert.Single(firstIds);

        await ExecAsync("UPDATE vacation_settlements SET settlement_state = 'REVERSED' WHERE employee_id = @p0 AND entitlement_type = 'VACATION' AND entitlement_year = 2024 AND sequence = 1", empA);
        await SeedSettlementAsync(empA, "VACATION", 2024, 2, "SETTLED", new DateOnly(2025, 8, 31));

        var secondIds = await RunSettledAsync(empA, Trigger(WorklistTriggerKinds.AgreementCodeChange, from), from, to);
        Assert.Equal(worklistId, Assert.Single(secondIds));

        var row = await _repo.GetByIdWithVersionAsync(worklistId);
        Assert.NotNull(row);
        Assert.Equal(2L, row!.Version);
        Assert.Equal(2, row.Triggers.Count);
        Assert.Equal(1, row.Triggers[0].BaselineSettlementSequence);
        Assert.Equal("SETTLED", row.Triggers[0].BaselineSettlementState); // what it saw THEN
        Assert.Equal(2, row.Triggers[1].BaselineSettlementSequence);      // captured NOW
        Assert.Equal(2, row.Current.HighestSettlementSequence);
        Assert.Contains(1, row.Current.ReversedSettlementSequences);
        Assert.True(BackdateWorklistDerivation.ReversedSinceForTrigger(row, row.Triggers[0]));
        Assert.False(BackdateWorklistDerivation.ReversedSinceForTrigger(row, row.Triggers[1]));
        Assert.False(BackdateWorklistDerivation.ReversedSince(row));
        Assert.Null(BackdateWorklistDerivation.RecalculatedSince(row));
        Assert.Empty(BackdateWorklistDerivation.RecalcBlockedBy(row)); // never a re-plan blocker on a settled-year row

        var events = await OutboxEventsAsync(empA, "BackdateWorklistRowCreated");
        Assert.Equal(new[] { false, true }, events.Select(e => e.GetProperty("appended").GetBoolean()).ToArray());
        Assert.Equal(2, events[1].GetProperty("baselineSettlementSequence").GetInt32());

        // Leg 2 — a BARE reversal (no re-settle yet): the baseline row itself is now REVERSED.
        const string empB = "wl_emp_set_bare";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, empB, "STY_WL_A");
        await SeedSettlementAsync(empB, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));
        var bareId = Assert.Single(await RunSettledAsync(empB, Trigger(WorklistTriggerKinds.ProfileChange, from), from, to));
        await ExecAsync("UPDATE vacation_settlements SET settlement_state = 'REVERSED' WHERE employee_id = @p0 AND entitlement_type = 'VACATION' AND entitlement_year = 2024 AND sequence = 1", empB);

        var bare = await _repo.GetByIdWithVersionAsync(bareId);
        Assert.NotNull(bare);
        Assert.Equal(1, bare!.Current.HighestSettlementSequence);
        Assert.Equal(new[] { 1 }, bare.Current.ReversedSettlementSequences.ToArray());
        Assert.True(BackdateWorklistDerivation.ReversedSince(bare));
    }

    // ════════════════════════════════════════════════════════════════════════
    // SETTLED_YEAR — the SKIP path (S138 / TASK-13810)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The half of the ruling that dates cannot express. The revaluation refused to re-record a
    /// (type, year) group because that holiday year is settled; the correction was therefore
    /// WITHHELD, and HR must be told — even when the correction starts AFTER the freeze and the
    /// date rule (correctly) raises nothing. The dates here are exactly the "no row" case pinned
    /// above, so the row this produces can only have come from the skip path.
    /// </summary>
    [Fact]
    public async Task WriteForSkippedSettledYears_RaisesARow_EvenWhenTheDateRuleDoesNot()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        const string emp = "wl_emp_skip_only";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));

        var from = new DateOnly(2025, 10, 1); // after the freeze — the date path stays silent
        var trigger = Trigger(WorklistTriggerKinds.ProfileChange, from);
        Assert.Empty(await RunSettledAsync(emp, trigger, from, new DateOnly(2025, 11, 1)));

        var ids = await RunSkippedAsync(emp, trigger, new[] { ("VACATION", 2024) });

        Assert.Single(ids);
        var row = Assert.Single(await _repo.GetOpenAsync(emp));
        Assert.Equal(WorklistKinds.SettledYear, row.Kind);
        Assert.Equal("VACATION", row.EntitlementType);
        Assert.Equal(2024, row.EntitlementYear);
        Assert.Equal(1L, row.Version);
        var stored = Assert.Single(row.Triggers);
        Assert.Equal(WorklistTriggerKinds.ProfileChange, stored.Kind);
        Assert.Equal(1, stored.BaselineSettlementSequence);   // the baseline is captured, exactly as on the date path
        Assert.Equal("SETTLED", stored.BaselineSettlementState);
        Assert.Null(stored.BaselineContentHash);

        var created = Assert.Single(await OutboxEventsAsync(emp, "BackdateWorklistRowCreated"));
        Assert.Equal("SETTLED_YEAR", created.GetProperty("kind").GetString());
        Assert.False(created.GetProperty("appended").GetBoolean());
    }

    /// <summary>
    /// A year reached by BOTH paths in one transaction — the shape the profile PUT produces for a
    /// genuine backdate into a settled year — collapses to ONE worklist row carrying TWO triggers,
    /// via the same partial UNIQUE and the same append semantics. HR works the year once; the row
    /// records both reasons honestly.
    /// </summary>
    [Fact]
    public async Task BothSettledYearPaths_InOneTransaction_YieldOneRowWithTwoTriggers()
    {
        await EnsureVacationConfigAsync(resetMonth: 9);

        const string emp = "wl_emp_both_paths";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));

        var from = new DateOnly(2025, 3, 1); // BEFORE the freeze — the date path selects too
        var trigger = Trigger(WorklistTriggerKinds.ProfileChange, from);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var dateIds = await _repo.WriteForSettledYearsAsync(
            conn, tx, emp, trigger, from, new DateOnly(2025, 4, 1), CancellationToken.None);
        var skipIds = await _repo.WriteForSkippedSettledYearsAsync(
            conn, tx, emp, trigger, new[] { ("VACATION", 2024) }, CancellationToken.None);
        await tx.CommitAsync();

        Assert.Equal(Assert.Single(dateIds), Assert.Single(skipIds)); // the SAME row

        var row = Assert.Single(await _repo.GetOpenAsync(emp));
        Assert.Equal(2L, row.Version);
        Assert.Equal(2, row.Triggers.Count);
        Assert.All(row.Triggers, t => Assert.Equal(1, t.BaselineSettlementSequence));

        var events = await OutboxEventsAsync(emp, "BackdateWorklistRowCreated");
        Assert.Equal(new[] { false, true }, events.Select(e => e.GetProperty("appended").GetBoolean()).ToArray());
    }

    /// <summary>
    /// Degrade, do not fail (S138 Step-7a — this pin previously demanded the opposite).
    ///
    /// <para>
    /// The caller reports a skip because IT saw an ACTIVE settlement in this same transaction. This
    /// path's own lookup is stricter, so the two can in principle disagree — unreachable through
    /// production writes today (the ADR-033 D5 state machine plus the single-active unique index
    /// keep them aligned), but the schema permits it. The ORIGINAL pin required an
    /// <c>InvalidOperationException</c> here, on the reasoning that dropping the row would hide the
    /// thing the path exists to surface. That reasoning was half right and the remedy was wrong:
    /// this code runs INSIDE the correction's transaction, so throwing rolls back and answers 500,
    /// DESTROYING a valid correction because a diagnostic note could not be decorated. The
    /// correction is the user's work; the row is our comment about it.
    /// </para>
    ///
    /// <para>
    /// So the row IS raised, with no baseline — <c>reversedSince</c> then reads "unknown", which is
    /// the honest answer when the settlement state is inconsistent, and a diagnostic list is exactly
    /// where an inconsistency belongs. Nothing is hidden and nothing is lost.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WriteForSkippedSettledYears_TupleWithNoActiveSettlement_DegradesToAnUnknownBaseline()
    {
        const string emp = "wl_emp_skip_ghost";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "REVERSED", new DateOnly(2025, 8, 31));

        var trigger = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2025, 3, 1));
        var ids = await RunSkippedAsync(emp, trigger, new[] { ("VACATION", 2024) });

        Assert.Single(ids);
        var row = Assert.Single(await _repo.GetOpenAsync(emp));
        var stored = Assert.Single(row.Triggers);
        Assert.Equal(WorklistTriggerKinds.ProfileChange, stored.Kind);
        // The whole point: raised, but with NO baseline, so the read side says "unknown"
        // rather than inventing a sequence it could not observe.
        Assert.Null(stored.BaselineSettlementSequence);
        // …and the HR-visible consequence, not just the stored null: the derivation answers UNKNOWN,
        // never a reassuring false, at both the trigger and the row level (post-close Codex NOTE).
        Assert.Null(BackdateWorklistDerivation.ReversedSinceForTrigger(row, stored));
        Assert.Null(BackdateWorklistDerivation.ReversedSince(row));
    }

    /// <summary>An empty skip list is a no-op — the caller passes it on every ordinary correction.</summary>
    [Fact]
    public async Task WriteForSkippedSettledYears_EmptyList_WritesNothing()
    {
        const string emp = "wl_emp_skip_empty";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedSettlementAsync(emp, "VACATION", 2024, 1, "SETTLED", new DateOnly(2025, 8, 31));

        var ids = await RunSkippedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2025, 3, 1)),
            Array.Empty<(string, int)>());

        Assert.Empty(ids);
        Assert.Empty(await _repo.GetOpenAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Partial UNIQUEs at the DB level + resolve frees the key
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PartialUniques_OneOpenRowPerKey_BothKinds_ResolvedRowFreesKey()
    {
        const string emp = "wl_emp_unique";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        var exportId = await SeedExportAsync(emp, 2026, 7, "h1");
        var monthId = Assert.Single(await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 7, 10)),
            new DateOnly(2026, 7, 10), new DateOnly(2026, 8, 1)));

        const string triggerJson =
            """[{"kind":"PROFILE_CHANGE","eventId":"22222222-2222-2222-2222-222222222222","effectiveFrom":"2026-07-10","appendedAt":"2026-09-03T08:00:00+00:00","actorId":"x","baselineContentHash":"h1"}]""";

        var dupMonth = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, @p1, 'EXPORTED_MONTH', 2026, 7, @p2, @p3::jsonb, 'x')
            """, Guid.NewGuid(), emp, exportId, triggerJson));
        Assert.Equal("23505", dupMonth.SqlState);

        await ExecAsync(
            """
            INSERT INTO hr_backdate_worklist (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
            VALUES (@p0, @p1, 'SETTLED_YEAR', 'VACATION', 2025, @p2::jsonb, 'x')
            """, Guid.NewGuid(), emp, triggerJson);
        var dupYear = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
            VALUES (@p0, @p1, 'SETTLED_YEAR', 'VACATION', 2025, @p2::jsonb, 'x')
            """, Guid.NewGuid(), emp, triggerJson));
        Assert.Equal("23505", dupYear.SqlState);

        // Resolve the month row through the repository → its key is free; the next correction
        // opens a FRESH row (version 1, ONE trigger — its own history, not the resolved row's).
        await RunResolveAsync(monthId, expectedVersion: 1, WorklistResolutions.Dismissed, "S138 pin");
        var freshId = Assert.Single(await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 7, 12)),
            new DateOnly(2026, 7, 12), new DateOnly(2026, 8, 1)));
        Assert.NotEqual(monthId, freshId);
        var fresh = await _repo.GetByIdWithVersionAsync(freshId);
        Assert.Equal(1L, fresh!.Version);
        Assert.Single(fresh.Triggers);
        Assert.Equal(2, (await _repo.GetAllAsync(emp)).Count(r => r.Kind == WorklistKinds.ExportedMonth));
        Assert.Single((await _repo.GetOpenAsync(emp)).Where(r => r.Kind == WorklistKinds.ExportedMonth));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Resolve
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Resolve_VersionGuard_StaleThrows_FreshBumpsVersion_EmitsResolvedEvent_RepeatRefused_UnknownNotFound()
    {
        const string emp = "wl_emp_resolve";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, "STY_WL_A");
        await SeedExportAsync(emp, 2026, 8, "h1");
        var id = Assert.Single(await RunExportedAsync(emp, Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 8, 3)),
            new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 1)));

        // Stale token → OptimisticConcurrencyException carrying expected/actual (the endpoint's 412 body).
        var stale = await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            () => RunResolveAsync(id, expectedVersion: 99, WorklistResolutions.Recalculated, "stale"));
        Assert.Equal(99L, stale.ExpectedVersion);
        Assert.Equal(1L, stale.ActualVersion);
        Assert.Null((await _repo.GetByIdWithVersionAsync(id))!.ResolvedAt); // nothing written
        Assert.Empty(await OutboxEventsAsync(emp, "BackdateWorklistRowResolved"));

        // Fresh token → resolved; version 1 → 2; the operator's verb + reason recorded.
        var result = await RunResolveAsync(id, expectedVersion: 1, WorklistResolutions.Recalculated, "Re-planned via /api/payroll/recalculate");
        Assert.Equal(1L, result.VersionBefore);
        Assert.Equal(2L, result.NewVersion);
        Assert.Equal(emp, result.EmployeeId);
        var row = await _repo.GetByIdWithVersionAsync(id);
        Assert.NotNull(row!.ResolvedAt);
        Assert.Equal(Actor, row.ResolvedBy);
        Assert.Equal(WorklistResolutions.Recalculated, row.Resolution);
        Assert.Equal("Re-planned via /api/payroll/recalculate", row.ResolutionReason);
        Assert.Equal(2L, row.Version);
        Assert.Empty(await _repo.GetOpenAsync(emp));
        Assert.Single(await _repo.GetAllAsync(emp));

        // The Resolved event + its ADR-026 row ARE the audit record (W8).
        var resolved = Assert.Single(await OutboxEventsAsync(emp, "BackdateWorklistRowResolved"));
        Assert.Equal(id, resolved.GetProperty("worklistId").GetGuid());
        Assert.Equal("RECALCULATED", resolved.GetProperty("resolution").GetString());
        Assert.Equal(1L, resolved.GetProperty("versionBefore").GetInt64());
        Assert.Equal(2L, resolved.GetProperty("versionAfter").GetInt64());
        Assert.Equal(1, resolved.GetProperty("triggerCount").GetInt32());
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0 AND target_org_id = 'STY_WL_A'", emp));

        // Already resolved → refused even with the fresh token; unknown id → KeyNotFound.
        await Assert.ThrowsAsync<BackdateWorklistAlreadyResolvedException>(
            () => RunResolveAsync(id, expectedVersion: 2, WorklistResolutions.Dismissed, "again"));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => RunResolveAsync(Guid.NewGuid(), expectedVersion: 1, WorklistResolutions.Dismissed, "ghost"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => RunResolveAsync(id, expectedVersion: 2, "IGNORED", "bad verb"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Org-subtree listing
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOpenForOrgSubtree_FiltersByEmployeePrimaryOrg_NullUnrestricted_EmptyNothing()
    {
        const string emp1 = "wl_emp_org1";
        const string emp2 = "wl_emp_org2";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp1, "STY_WL_ORG1");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp2, "STY_WL_ORG2");
        await SeedExportAsync(emp1, 2026, 2, "h1");
        await SeedExportAsync(emp2, 2026, 2, "h2");
        var t = Trigger(WorklistTriggerKinds.ProfileChange, new DateOnly(2026, 2, 5));
        await RunExportedAsync(emp1, t, new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 1));
        await RunExportedAsync(emp2, t, new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 1));

        var org1Only = await _repo.GetOpenForOrgSubtreeAsync(new[] { "STY_WL_ORG1" });
        Assert.Equal(new[] { emp1 }, org1Only.Select(r => r.EmployeeId).ToArray());

        var both = await _repo.GetOpenForOrgSubtreeAsync(new[] { "STY_WL_ORG1", "STY_WL_ORG2" });
        Assert.Equal(new[] { emp1, emp2 }, both.Select(r => r.EmployeeId).OrderBy(x => x).ToArray());

        var unrestricted = await _repo.GetOpenForOrgSubtreeAsync(null);
        Assert.Contains(unrestricted, r => r.EmployeeId == emp1);
        Assert.Contains(unrestricted, r => r.EmployeeId == emp2);

        Assert.Empty(await _repo.GetOpenForOrgSubtreeAsync(Array.Empty<string>()));
        Assert.Empty(await _repo.GetOpenForOrgSubtreeAsync(new[] { "STY_WL_NOWHERE" }));
    }

    // ─── run helpers (the caller's tx — the writer never commits) ───────────────

    private static WorklistTrigger Trigger(string kind, DateOnly effectiveFrom) =>
        new(kind, Guid.NewGuid(), effectiveFrom, Actor);

    private async Task<IReadOnlyList<Guid>> RunExportedAsync(string employeeId, WorklistTrigger trigger, DateOnly from, DateOnly? toExclusive)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await _repo.WriteForExportedMonthsAsync(conn, tx, employeeId, trigger, from, toExclusive, CancellationToken.None);
        await tx.CommitAsync();
        return ids;
    }

    private async Task<IReadOnlyList<Guid>> RunSettledAsync(string employeeId, WorklistTrigger trigger, DateOnly from, DateOnly? toExclusive)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await _repo.WriteForSettledYearsAsync(conn, tx, employeeId, trigger, from, toExclusive, CancellationToken.None);
        await tx.CommitAsync();
        return ids;
    }

    /// <summary>The SKIP path (S138 / TASK-13810) — the groups the caller's revaluation declined to
    /// re-record. No dates: this path is about what happened, not about when.</summary>
    private async Task<IReadOnlyList<Guid>> RunSkippedAsync(
        string employeeId, WorklistTrigger trigger, IReadOnlyCollection<(string EntitlementType, int EntitlementYear)> skipped)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await _repo.WriteForSkippedSettledYearsAsync(conn, tx, employeeId, trigger, skipped, CancellationToken.None);
        await tx.CommitAsync();
        return ids;
    }

    private async Task<WorklistResolveResult> RunResolveAsync(Guid worklistId, long expectedVersion, string resolution, string reason)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var result = await _repo.ResolveAsync(conn, tx, worklistId, expectedVersion, resolution, reason,
            new WorklistActor(Actor, "LocalHR", Guid.NewGuid()), CancellationToken.None);
        await tx.CommitAsync();
        return result;
    }

    // ─── seed helpers ────────────────────────────────────────────────────────

    private async Task<Guid> SeedExportAsync(string employeeId, int year, int month, string contentHash)
    {
        var exportId = Guid.NewGuid();
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (@p0, NULL, @p1, @p2, @p3, '[]'::jsonb, '[]'::jsonb, @p4)
            """, exportId, employeeId, year, month, contentHash);
        return exportId;
    }

    /// <summary>
    /// A settlement row whose immutable snapshot carries the VALUATION BOUNDARY (the last day it counted) the S138 /
    /// TASK-13810 date rule keys on. <paramref name="boundaryDate"/> <c>null</c> seeds a bare
    /// <c>{}</c> snapshot (the key absent — the conservative "unknown valuation boundary" leg);
    /// <c>default(DateOnly)</c> seeds the <c>0001-01-01</c> that an uninitialized snapshot
    /// serializes, which is the SAME unknown leg through a different door.
    /// </summary>
    private Task SeedSettlementAsync(
        string employeeId, string entitlementType, int entitlementYear, int sequence, string state,
        DateOnly? boundaryDate = null)
    {
        var snapshotJson = boundaryDate is { } boundary
            ? $$"""{"settlementBoundaryDate":"{{boundary:yyyy-MM-dd}}"}"""
            : "{}";
        return ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state, trigger, snapshot)
            VALUES (@p0, @p1, @p2, @p3, @p4, 'YEAR_END', @p5::jsonb)
            """, employeeId, entitlementType, entitlementYear, sequence, state, snapshotJson);
    }

    /// <summary>The live VACATION config for the seeded employees' (AC, OK24) with the given reset
    /// month (upsert-by-hand) — it is what places the entitlement window for half (b) of the
    /// settled-year date rule, so every geometry-sensitive test states it rather than inheriting
    /// the seeded default.</summary>
    private async Task EnsureVacationConfigAsync(int resetMonth)
    {
        var updated = await ExecAsync(
            """
            UPDATE entitlement_configs SET reset_month = @p0
            WHERE entitlement_type = 'VACATION' AND agreement_code = 'AC' AND ok_version = 'OK24' AND effective_to IS NULL
            """, resetMonth);
        if (updated == 0)
        {
            await ExecAsync(
                """
                INSERT INTO entitlement_configs (entitlement_type, agreement_code, ok_version, annual_quota, reset_month)
                VALUES ('VACATION', 'AC', 'OK24', 25, @p0)
                """, resetMonth);
        }
    }

    private async Task<List<JsonElement>> OutboxEventsAsync(string employeeId, string eventType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload::text FROM outbox_events
            WHERE stream_id = @stream AND event_type = @type
            ORDER BY outbox_id
            """, conn);
        cmd.Parameters.AddWithValue("stream", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("type", eventType);
        var result = new List<JsonElement>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(JsonDocument.Parse(reader.GetString(0)).RootElement.Clone());
        return result;
    }

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> ExecAsync(string sql, params object[] args)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteNonQueryAsync();
    }
}
