using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using Testcontainers.PostgreSql;

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
    private const string ImageTag = "postgres:16-alpine";

    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS event_streams (
            stream_id   TEXT        PRIMARY KEY,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE TABLE IF NOT EXISTS events (
            global_position BIGSERIAL   PRIMARY KEY,
            event_id        UUID        NOT NULL UNIQUE,
            stream_id       TEXT        NOT NULL REFERENCES event_streams(stream_id),
            stream_version  INT         NOT NULL,
            event_type      TEXT        NOT NULL,
            data            JSONB       NOT NULL,
            occurred_at     TIMESTAMPTZ NOT NULL,
            stored_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            actor_id        TEXT,
            actor_role      TEXT,
            correlation_id  UUID,
            UNIQUE (stream_id, stream_version)
        );
        CREATE TABLE IF NOT EXISTS segment_manifests (
            manifest_id             UUID        PRIMARY KEY,
            period_start            DATE        NOT NULL,
            period_end              DATE        NOT NULL,
            employee_id             TEXT        NOT NULL,
            calculation_kind        TEXT        NOT NULL,
            boundary_cause_summary  TEXT[]      NOT NULL,
            created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
            segments_jsonb          JSONB       NOT NULL
        );
        CREATE TABLE IF NOT EXISTS wage_type_mappings (
            time_type       TEXT        NOT NULL,
            wage_type       TEXT        NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            position        TEXT        NOT NULL DEFAULT '',
            description     TEXT,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (time_type, ok_version, agreement_code, position)
        );
        """;

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _factory = null!;
    private PostgresEventStore _eventStore = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(SchemaDdl, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        _factory = new DbConnectionFactory(_container.GetConnectionString());
        _eventStore = new PostgresEventStore(_factory);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    [Fact]
    public async Task RebuildAsync_AfterTruncate_RestoresProjectionFromEvents()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_factory);
        var pcs = TestFixtures.BuildPcs(_factory, _eventStore);

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
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var truncCmd = new NpgsqlCommand("TRUNCATE TABLE segment_manifests", conn);
            await truncCmd.ExecuteNonQueryAsync();
        }

        // Confirm truncate took effect.
        Assert.Null(await ReadSingleRowAsync(plan.ManifestId));

        // Rebuild via the canonical entry point.
        var rebuiltCount = await SegmentManifestProjectionRebuilder.RebuildAsync(
            _factory, NullLogger.Instance);
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
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
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
