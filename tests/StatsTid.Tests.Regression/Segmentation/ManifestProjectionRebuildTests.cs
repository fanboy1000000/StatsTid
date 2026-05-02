using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// Verifies the <see cref="SegmentManifestProjectionRebuilder.RebuildAsync"/> ops entry
/// point (TASK-2011): truncate + replay rebuilds the projection from
/// <c>SegmentManifestCreated</c> events identically to its pre-truncation state.
///
/// <para>
/// Steps:
/// </para>
/// <list type="number">
///   <item>Forward calculation populates <c>segment_manifests</c> with one row.</item>
///   <item><c>TRUNCATE segment_manifests</c> directly via SQL.</item>
///   <item>Invoke <see cref="SegmentManifestProjectionRebuilder.RebuildAsync"/>.</item>
///   <item>Assert: same row reappears with identical key fields
///         (<c>manifest_id</c>, period bounds, employee id, calculation kind,
///         boundary cause summary, segments_jsonb).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManifestProjectionRebuildTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task RebuildAsync_AfterTruncate_RestoresProjectionFromEvents()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

        var profile = TestFixtures.Profile("EMP-REBUILD-1");
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));

        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: new DateOnly(2026, 3, 25),
            periodEnd: new DateOnly(2026, 4, 7),
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

        await pcs.CalculateAsync(plan, profile, entries, Array.Empty<AbsenceEntry>(), 0m);

        // Snapshot the row pre-truncate so we can compare exactly after rebuild.
        var pre = await ReadSingleRowAsync(plan.ManifestId);
        Assert.NotNull(pre);

        // Truncate.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var truncCmd = new NpgsqlCommand("TRUNCATE TABLE segment_manifests", conn);
            await truncCmd.ExecuteNonQueryAsync();
        }

        // Confirm truncate took effect.
        Assert.Null(await ReadSingleRowAsync(plan.ManifestId));

        // Rebuild via the canonical entry point.
        var rebuiltCount = await SegmentManifestProjectionRebuilder.RebuildAsync(
            _harness.Factory, NullLogger.Instance);
        Assert.True(rebuiltCount >= 1, $"Rebuild reported {rebuiltCount} rows; expected >= 1.");

        // Post-rebuild row matches pre-truncate row on the audit-of-record fields.
        var post = await ReadSingleRowAsync(plan.ManifestId);
        Assert.NotNull(post);

        Assert.Equal(pre!.ManifestId, post!.ManifestId);
        Assert.Equal(pre.PeriodStart, post.PeriodStart);
        Assert.Equal(pre.PeriodEnd, post.PeriodEnd);
        Assert.Equal(pre.EmployeeId, post.EmployeeId);
        Assert.Equal(pre.CalculationKind, post.CalculationKind);
        Assert.Equal(pre.BoundaryCauseSummary, post.BoundaryCauseSummary);
        Assert.Equal(pre.SegmentsJson, post.SegmentsJson);
    }

    private async Task<ManifestRow?> ReadSingleRowAsync(Guid manifestId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT manifest_id, period_start, period_end, employee_id, calculation_kind,
                   boundary_cause_summary, segments_jsonb::text
            FROM segment_manifests
            WHERE manifest_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", manifestId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new ManifestRow(
            reader.GetGuid(0),
            DateOnly.FromDateTime(reader.GetDateTime(1)),
            DateOnly.FromDateTime(reader.GetDateTime(2)),
            reader.GetString(3),
            reader.GetString(4),
            ((string[])reader.GetValue(5)).ToList(),
            reader.GetString(6));
    }

    private sealed record ManifestRow(
        Guid ManifestId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string EmployeeId,
        string CalculationKind,
        IReadOnlyList<string> BoundaryCauseSummary,
        string SegmentsJson);
}
