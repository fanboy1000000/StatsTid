using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// Regression test for the manifest emission contract introduced in Sprint 20
/// (ADR-016 D10): every successful <see cref="PeriodCalculationService.CalculateAsync"/>
/// produces exactly one <c>segment_manifests</c> row, that row's <c>manifest_id</c>
/// matches the per-line <see cref="PayrollExportLine.ManifestId"/>, the
/// <c>boundary_cause_summary</c> array contains <c>"OkTransition"</c>, and
/// <c>segments_jsonb</c> deserialises to the expected segment list.
///
/// <para>
/// Uses a Postgres testcontainer with the canonical
/// <c>segment_manifests</c> + event-store DDL copied verbatim from
/// <c>docker/postgres/init.sql</c>. The Rule Engine is mocked via a stub
/// <see cref="HttpMessageHandler"/> so the test stays self-contained — no second
/// container or live HTTP service required.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManifestCreationTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// 2-segment OK-straddling forward calculation (2026-03-25 .. 2026-04-07).
    /// Asserts the manifest projection row, manifest-id linkage, boundary-cause
    /// summary, calculation kind, and segment shape.
    /// </summary>
    [Fact]
    public async Task CalculateAsync_StraddlingPeriod_PersistsManifestRow_AndLinksExportLines()
    {
        var profile = TestFixtures.Profile("EMP-MFCREATE-1");
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));
        var absences = Array.Empty<AbsenceEntry>();

        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

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

        Assert.Equal(2, plan.Segments.Count);

        var result = await pcs.CalculateAsync(
            plan, profile, entries, absences,
            previousFlexBalance: 0m);

        Assert.True(result.Success);
        Assert.NotEmpty(result.ExportLines);

        // ---- Manifest projection row ----
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE manifest_id = @id", conn);
        countCmd.Parameters.AddWithValue("id", plan.ManifestId);
        var count = (long)(await countCmd.ExecuteScalarAsync())!;
        Assert.Equal(1L, count);

        await using var rowCmd = new NpgsqlCommand(
            """
            SELECT period_start, period_end, employee_id, calculation_kind,
                   boundary_cause_summary, segments_jsonb
            FROM segment_manifests
            WHERE manifest_id = @id
            """, conn);
        rowCmd.Parameters.AddWithValue("id", plan.ManifestId);

        await using var reader = await rowCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(new DateOnly(2026, 3, 25), DateOnly.FromDateTime(reader.GetDateTime(0)));
        Assert.Equal(new DateOnly(2026, 4, 7), DateOnly.FromDateTime(reader.GetDateTime(1)));
        Assert.Equal(profile.EmployeeId, reader.GetString(2));
        Assert.Equal("forward-calc", reader.GetString(3));

        var causes = (string[])reader.GetValue(4);
        Assert.Contains("OkTransition", causes);

        var segmentsJson = reader.GetString(5);
        Assert.False(string.IsNullOrWhiteSpace(segmentsJson));
        using var doc = JsonDocument.Parse(segmentsJson);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());

        // ---- Per-line ManifestId linkage ----
        Assert.All(result.ExportLines, line =>
        {
            Assert.NotEqual(Guid.Empty, line.ManifestId);
            Assert.Equal(plan.ManifestId, line.ManifestId);
        });
    }
}
