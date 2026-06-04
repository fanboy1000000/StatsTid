using Npgsql;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// Replay-determinism contract for <see cref="StatsTid.Integrations.Payroll.Services.PeriodCalculationService.ReplayAsync(System.Guid, EmploymentProfile, IReadOnlyList{TimeEntry}, IReadOnlyList{AbsenceEntry}, decimal, string?, System.Guid?, System.Threading.CancellationToken)"/>
/// (ADR-016 D10): replay loads the persisted manifest, reconstructs the plan, and
/// re-evaluates rules against the supplied inputs WITHOUT minting a new manifest.
/// The replay-emitted result MUST carry the original <c>ManifestId</c> end-to-end.
///
/// <para>
/// Step structure:
/// </para>
/// <list type="number">
///   <item>Forward calculation produces manifest M1.</item>
///   <item>Replay against M1 returns a result whose rule results all carry M1.</item>
///   <item>Replay rule-result count and total export-line hours match the forward
///         calculation (deterministic re-evaluation against the same inputs).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManifestReplayTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ReplayAsync_ReturnsResult_WithOriginalManifestId_AndMatchingShape()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

        var profile = TestFixtures.Profile("EMP-REPLAY-1");
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));
        var absences = Array.Empty<AbsenceEntry>();

        // Forward calculation produces manifest M1.
        var plan = PeriodPlanner.Plan(
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

        var forward = await pcs.CalculateAsync(
            plan, profile, entries, absences,
            previousFlexBalance: 0m);
        Assert.True(forward.Success);

        // Replay against the same manifest id with the same inputs.
        var replay = await pcs.ReplayAsync(
            plan.ManifestId, profile, entries, absences,
            previousFlexBalance: 0m);

        // Replay result is success, carries the same manifest id on every rule result,
        // and produces the same number of rule results / total hours as the forward calc.
        Assert.True(replay.Success);

        Assert.All(replay.RuleResults, r =>
        {
            Assert.Equal(plan.ManifestId, r.ManifestId);
        });
        Assert.All(replay.ExportLines, line =>
        {
            Assert.Equal(plan.ManifestId, line.ManifestId);
        });

        Assert.Equal(forward.RuleResults.Count, replay.RuleResults.Count);
        Assert.Equal(forward.ExportLines.Sum(l => l.Hours), replay.ExportLines.Sum(l => l.Hours));

        // Replay must NOT mint a new manifest row — only one row in the projection.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE manifest_id = @id", conn);
        countCmd.Parameters.AddWithValue("id", plan.ManifestId);
        var rows = (long)(await countCmd.ExecuteScalarAsync())!;
        Assert.Equal(1L, rows);
    }
}
