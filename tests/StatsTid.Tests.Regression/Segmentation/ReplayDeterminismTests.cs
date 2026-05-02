using Npgsql;
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
/// <strong>Scope caveat (Reviewer-flagged honestly):</strong> the assertions here
/// pin manifest-id stability under replay and recomputation, NOT byte-exact replay
/// equality of <c>ExportLines</c> sums under live-DB mutation. The wage-type-mapping
/// lookup in <c>MapSegmentToExportLinesAsync</c> still reads the live DB during the
/// export-line stage; per ADR-016 D5b that source is on the deferred snapshot list
/// (Phase 4 "Versioned History for Non-Dated Boundary Sources"). When that lands,
/// tighten the assertion to compare <c>forward1.ExportLines.Sum(l =&gt; l.Hours)</c>
/// against <c>replay.ExportLines.Sum(l =&gt; l.Hours)</c> after WTM mutation — that
/// is the assertion that would fail if replay started reading live DB.
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
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

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
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

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
}
