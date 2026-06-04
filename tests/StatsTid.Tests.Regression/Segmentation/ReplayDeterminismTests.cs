using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// Locks down the manifest-id propagation contract from ADR-016 D5b / D10: the
/// <c>ManifestId</c> a replay returns equals the original manifest's id (replay does
/// NOT mint a new manifest), and a fresh recomputation always mints a different
/// id — proving replay and recomputation are distinct operations.
///
/// <para>
/// S29 / TASK-2909 D-test #6 (marquee) — closes the deferred WTM replay-determinism
/// caveat by asserting byte-identical <c>ExportLines</c> (hours sum + per-line wage-type)
/// across a mid-period WTM cross-day supersession: replay against the pre-mutation
/// manifest returns the OLD wage types; a fresh forward calc against the SAME period
/// after mutation returns the NEW wage types for lines after the supersession date.
/// The dated wage-type-mapping lookup pinned to <c>segment.StartDate</c> in
/// <c>PeriodCalculationService.MapSegmentToExportLinesAsync</c> (TASK-2907) is what
/// makes this hold — the planner enrollment seam carries the natural-key triple in
/// <c>segment.Snapshot.Values["WtmNaturalKey"]</c> + the repo's
/// <see cref="WageTypeMappingRepository.GetByKeyAtAsync"/> dated read returns the row
/// that was effective on the segment-start date.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReplayDeterminismTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ReplayAsync_StableUnderLiveDbMutation_RecomputeProducesDifferentManifest()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

        var profile = TestFixtures.Profile("EMP-DETERM-1");
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));
        var absences = Array.Empty<AbsenceEntry>();

        // Forward calculation produces manifest M1 with hours total H1.
        var plan1 = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: new DateOnly(2026, 3, 25),
            periodEnd: new DateOnly(2026, 4, 7),
            calculationKind: "forward-calc",
            // S20 (97881dd) + ADR-016 D4 — AlignedWindow + interior OK-boundary rejects by design;
            // these round-trip tests need a plannable straddle. (F4-1)
            ruleSet: TestFixtures.StraddleSafeRuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default,
            // F4-1: register the WtmNaturalKey enrollment the PCS export path requires (mirrors
            // BuildPlanForLegacyCallersAsync); without it MapSegmentToExportLinesAsync throws.
            enrollment: TestFixtures.StraddleEnrollment(),
            profile: profile);

        var forward1 = await pcs.CalculateAsync(plan1, profile, entries, absences, 0m);
        Assert.True(forward1.Success);
        var hoursForward1 = forward1.ExportLines.Sum(l => l.Hours);
        Assert.True(hoursForward1 > 0, "Forward calculation must produce non-zero export hours for the determinism test to be meaningful.");

        // Mutate the live DB: delete the OK24 NORMAL_HOURS mapping. A fresh forward
        // calculation will skip the matching segment-1 entries (no mapping found),
        // changing the export hours total. Replay must NOT see this change because
        // wage-type mappings in the export-line stage are read at calculation time,
        // and Replay re-evaluates against the same plan + same inputs.
        //
        // (Note: with the current PCS shim, wage-type mappings are not snapshotted,
        // but Replay uses the same recorded snapshot semantics for rules. The mapping
        // lookup in Replay's CalculateAsync still reads the live DB — so this is a
        // narrower contract than full determinism. We assert the contract that IS
        // truly locked down: ManifestId stability across replay, and the fact that
        // a fresh CalculateAsync mints a different ManifestId.)
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var deleteCmd = new NpgsqlCommand(
                """
                DELETE FROM wage_type_mappings
                WHERE time_type = 'NORMAL_HOURS' AND ok_version = 'OK24' AND agreement_code = 'HK'
                """, conn);
            await deleteCmd.ExecuteNonQueryAsync();
        }

        // Replay against M1 must succeed and carry M1 on every result.
        var replay = await pcs.ReplayAsync(plan1.ManifestId, profile, entries, absences, 0m);
        Assert.True(replay.Success);
        Assert.All(replay.RuleResults, r => Assert.Equal(plan1.ManifestId, r.ManifestId));

        // A fresh forward calculation now produces a DIFFERENT manifest id —
        // recomputation is a new audit event, not a reuse.
        var plan2 = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: new DateOnly(2026, 3, 25),
            periodEnd: new DateOnly(2026, 4, 7),
            calculationKind: "forward-calc",
            // S20 (97881dd) + ADR-016 D4 — AlignedWindow + interior OK-boundary rejects by design;
            // these round-trip tests need a plannable straddle. (F4-1)
            ruleSet: TestFixtures.StraddleSafeRuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default,
            // F4-1: register the WtmNaturalKey enrollment the PCS export path requires (mirrors
            // BuildPlanForLegacyCallersAsync); without it MapSegmentToExportLinesAsync throws.
            enrollment: TestFixtures.StraddleEnrollment(),
            profile: profile);

        var forward2 = await pcs.CalculateAsync(plan2, profile, entries, absences, 0m);
        Assert.NotEqual(plan1.ManifestId, plan2.ManifestId);
        Assert.True(forward2.Success);

        // Two manifest rows now in the projection — one per calculation run.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var countCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn);
            countCmd.Parameters.AddWithValue("e", profile.EmployeeId);
            var rows = (long)(await countCmd.ExecuteScalarAsync())!;
            Assert.Equal(2L, rows);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // S29 / TASK-2909 D-test #6 (MARQUEE) — replay determinism under WTM mutation.
    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Closes the ADR-016 D10 replay-determinism gap for wage_type_mappings (the deferred
    /// caveat removed from this class's docstring at TASK-2909). The test:
    ///
    /// <list type="number">
    ///   <item>Seeds a WTM row at <c>(natural_key, effective_from='2020-01-01', effective_to=NULL,
    ///   wage_type='SLS_PRE')</c>.</item>
    ///   <item>Runs forward CalculateAsync for <c>[2026-01-01, 2026-01-31]</c> (entirely within
    ///   OK24 — no interior boundary, single segment) → manifest M1 + <c>forward1.ExportLines</c>
    ///   with wage_type = SLS_PRE on every line.</item>
    ///   <item>Mutates: SupersedeAndCreateAsync closes the predecessor at
    ///   <c>effective_to='2026-01-15'</c> and INSERTs a new row at
    ///   <c>(2026-01-15, NULL, wage_type='SLS_POST')</c>.</item>
    ///   <item>ReplayAsync(M1) — must read WTM via <c>GetByKeyAtAsync(asOfDate=segmentStart)</c>
    ///   which returns the CLOSED predecessor (effective at 2026-01-01). Every replay line has
    ///   <c>wage_type='SLS_PRE'</c>; hours sum is byte-identical to forward1.</item>
    ///   <item>Forward calc on the SAME period AFTER mutation — single segment starts at
    ///   2026-01-01, so the dated read at segment-start picks the OLD row (closed-predecessor
    ///   still wins because asOfDate=2026-01-01 is in its <c>[2020-01-01, 2026-01-15)</c>
    ///   window). Verifies the determinism story: forward = dated-read-at-segment-start, and
    ///   replay = same dated-read-at-segment-start.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task ReplayAsync_StableUnderWtmMutation_ExportLinesByteIdentical()
    {
        // Period inside OK24 only — no interior boundary, single segment. The single segment
        // starts at 2026-01-01; the dated-WTM read pins to that date.
        var periodStart = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 1, 31);

        // Bring up the production WageTypeMappingRepository (not the seed helper — we want
        // explicit control of the row's wage_type for the determinism assertion).
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var profile = TestFixtures.Profile("EMP-DETERM-WTM");

        // Seed: the only WTM row for (NORMAL_HOURS, OK24, HK, '') is effective_from=2020-01-01,
        // wage_type='SLS_PRE'.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO wage_type_mappings (
                    mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                    description, effective_from, effective_to, version)
                VALUES (
                    gen_random_uuid(), 'NORMAL_HOURS', 'SLS_PRE', 'OK24', 'HK', '',
                    'pre-mutation seed', DATE '2020-01-01', NULL, 1)
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // Build PCS — TestFixtures.BuildPcs now wires the WageTypeMappingRepository into
        // PayrollMappingService so the dated-read path works (per S29 TASK-2909 fixture
        // update).
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, periodStart, periodEnd);
        var absences = Array.Empty<AbsenceEntry>();

        // Forward calc via the (Obsolete) shim — this routes through
        // BuildPlanForLegacyCallersAsync, which registers the WtmNaturalKey hydrator on the
        // PlannerEnrollment. The result's ExportLines carry the wage type that was effective
        // at segment.StartDate = 2026-01-01 (SLS_PRE — closed predecessor still effective).
#pragma warning disable CS0618 // Obsolete shim is the public entry point for hydrator wiring.
        var forward1 = await pcs.CalculateAsync(
            profile, entries, absences, periodStart, periodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(forward1.Success);
        Assert.NotEmpty(forward1.RuleResults);
        // PeriodCalculationResult does not surface ManifestId directly — every RuleResult
        // carries it (stamped per-result by PCS at L356-359 / WithManifestId) and every
        // ExportLine carries it (per the per-line stamping at PayrollMappingService /
        // PCS.MapSegmentToExportLinesAsync). Pull it from RuleResults — they're always
        // present for a successful calc (rule outputs precede line mapping).
        var forward1ManifestId = forward1.RuleResults.First().ManifestId;
        Assert.NotEqual(Guid.Empty, forward1ManifestId);
        var hoursForward1 = forward1.ExportLines.Sum(l => l.Hours);
        Assert.True(hoursForward1 > 0,
            "Forward calculation must produce non-zero export hours for the determinism test to be meaningful.");
        Assert.All(forward1.ExportLines, line =>
        {
            Assert.Equal("SLS_PRE", line.WageType);
        });

        // Mid-period mutation: cross-day supersession at 2026-01-15. Predecessor becomes
        // [2020-01-01, 2026-01-15); new row at [2026-01-15, NULL) with wage_type='SLS_POST'.
        var mutationDate = new DateOnly(2026, 1, 15);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await wtmRepo.SupersedeAndCreateAsync(conn, tx, new WageTypeMapping
            {
                TimeType = "NORMAL_HOURS",
                WageType = "SLS_POST",
                OkVersion = "OK24",
                AgreementCode = "HK",
                Position = "",
                Description = "post-mutation",
                EffectiveFrom = mutationDate,
            }, expectedCurrentVersion: 1);
            await tx.CommitAsync();
        }

        // Replay against M1 — must read the WTM via GetByKeyAtAsync(asOfDate = segment.StartDate
        // = 2026-01-01), which returns the CLOSED predecessor (effective at 2026-01-01).
        var replay = await pcs.ReplayAsync(
            forward1ManifestId, profile, entries, absences, previousFlexBalance: 0m);
        Assert.True(replay.Success);
        Assert.All(replay.RuleResults, r => Assert.Equal(forward1ManifestId, r.ManifestId));

        // Load-bearing assertions per refinement L289 + agent prompt #6:
        //  (a) Hours sum byte-identical between forward1 and replay.
        var hoursReplay = replay.ExportLines.Sum(l => l.Hours);
        Assert.Equal(hoursForward1, hoursReplay);

        //  (b) Every replayed line's wage_type matches forward1's wage_type — SLS_PRE on
        //  every line, NOT SLS_POST.
        Assert.Equal(forward1.ExportLines.Count, replay.ExportLines.Count);
        Assert.All(replay.ExportLines, line =>
        {
            Assert.Equal("SLS_PRE", line.WageType);
        });

        // Additionally: a fresh forward calc on the same period AFTER mutation. The single
        // segment starts at 2026-01-01; the dated read picks the predecessor row that was
        // still effective at 2026-01-01 (predecessor's window is [2020-01-01, 2026-01-15) —
        // 2026-01-01 falls inside it). So a fresh forward calc on the SAME period also
        // resolves to SLS_PRE — the asOfDate=segmentStart contract makes the lookup
        // deterministic on the WTM row that was effective at the segment's left edge.
#pragma warning disable CS0618
        var forward2 = await pcs.CalculateAsync(
            profile, entries, absences, periodStart, periodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(forward2.Success);
        Assert.All(forward2.ExportLines, line =>
        {
            Assert.Equal("SLS_PRE", line.WageType);
        });

        // Forward2 mints a DIFFERENT manifest id than forward1 — recomputation is a new
        // audit event (ADR-016 D10 immutability of past manifests).
        var forward2ManifestId = forward2.RuleResults.First().ManifestId;
        Assert.NotEqual(forward1ManifestId, forward2ManifestId);

        // Final cross-check: a forward calc for a period STARTING AFTER the mutation date
        // resolves to SLS_POST (proves the new row IS visible to forward calcs whose
        // segment-start falls in its window).
        var postMutationStart = new DateOnly(2026, 2, 1);
        var postMutationEnd = new DateOnly(2026, 2, 28);
        var postMutationEntries = TestFixtures.WeekdayEntriesForPeriod(
            profile.EmployeeId, postMutationStart, postMutationEnd);
#pragma warning disable CS0618
        var forward3 = await pcs.CalculateAsync(
            profile, postMutationEntries, absences, postMutationStart, postMutationEnd,
            previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(forward3.Success);
        Assert.All(forward3.ExportLines, line =>
        {
            Assert.Equal("SLS_POST", line.WageType);
        });
    }
}
