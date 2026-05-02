using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Segmentation;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// TASK-1903 absorbed (mixed-version export contract). Verifies that an OK24/OK26
/// straddling period produces correctly per-line OK-version-stamped
/// <see cref="PayrollExportLine"/>s, that every line carries the same non-empty
/// <see cref="PayrollExportLine.ManifestId"/>, and that the SLS file format
/// emitted by <see cref="SlsExportFormatter.Format"/> contains the per-line
/// manifest-id columns and per-segment <c>OK24</c>/<c>OK26</c> data lines.
///
/// <para>
/// This is the explicit replacement for the deleted
/// <c>OkVersionBoundary.ResolveProfile</c> collapse behaviour (TASK-2010 wave 2 retired
/// it). The test locks down the per-line stamping contract end-to-end through the file
/// format, so any future regression that re-introduces single-OK-version export rows
/// for straddling periods fails here.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class MixedVersionExportTests : IAsyncLifetime
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
    public async Task StraddlingPeriod_ProducesPerLineOkVersionStamping_AndSlsFileColumns()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_factory);
        var pcs = TestFixtures.BuildPcs(_factory, _eventStore);

        var profile = TestFixtures.Profile("EMP-MIXED-1");
        var periodStart = new DateOnly(2026, 3, 25);
        var periodEnd = new DateOnly(2026, 4, 30);
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, periodStart, periodEnd);

        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: periodStart,
            periodEnd: periodEnd,
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);

        var result = await pcs.CalculateAsync(plan, profile, entries, Array.Empty<AbsenceEntry>(), 0m);
        Assert.True(result.Success);
        Assert.NotEmpty(result.ExportLines);

        // ---- Per-line OK-version stamping ----
        // Lines whose date is on or before 2026-03-31 carry OK24; on or after 2026-04-01 carry OK26.
        // PCS stamps PeriodStart on each line at the segment's start, but the OK version is
        // resolved per-segment (which == per-line in this scenario).
        var ok24Lines = result.ExportLines.Where(l => l.PeriodEnd <= new DateOnly(2026, 3, 31)).ToList();
        var ok26Lines = result.ExportLines.Where(l => l.PeriodStart >= new DateOnly(2026, 4, 1)).ToList();

        Assert.NotEmpty(ok24Lines);
        Assert.NotEmpty(ok26Lines);

        Assert.All(ok24Lines, line => Assert.Equal("OK24", line.OkVersion));
        Assert.All(ok26Lines, line => Assert.Equal("OK26", line.OkVersion));

        // ---- Manifest-id linkage ----
        Assert.All(result.ExportLines, line =>
        {
            Assert.NotEqual(Guid.Empty, line.ManifestId);
            Assert.Equal(plan.ManifestId, line.ManifestId);
        });

        // ---- SLS file format ----
        var sls = SlsExportFormatter.Format(
            result.ExportLines,
            exportId: "EXP-MIXED-1",
            exportTimestamp: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            manifestId: plan.ManifestId);

        Assert.False(string.IsNullOrEmpty(sls));

        var lines = sls.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        // Header line: H|EXP-MIXED-1|...|<count>|<manifestId>
        var header = lines[0];
        Assert.StartsWith("H|EXP-MIXED-1|", header);
        Assert.EndsWith($"|{plan.ManifestId}", header);

        // Data lines start with D| — every data line ends with the line's manifest id.
        var dataLines = lines.Where(l => l.StartsWith("D|", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(dataLines);

        // Per-line OK24 vs OK26 columns. Pipe-separated layout (9 fields, 0-indexed):
        // [0]="D" [1]=EmpId [2]=WageType [3]=Hours [4]=Amount [5]=PeriodStart
        // [6]=PeriodEnd [7]=OkVersion [8]=ManifestId. OkVersion is at index 7.
        var ok24DataLines = dataLines.Where(l => l.Split('|')[7] == "OK24").ToList();
        var ok26DataLines = dataLines.Where(l => l.Split('|')[7] == "OK26").ToList();
        Assert.NotEmpty(ok24DataLines);
        Assert.NotEmpty(ok26DataLines);

        // Each data line carries the manifest id in its trailing column.
        Assert.All(dataLines, l =>
        {
            var fields = l.Split('|');
            Assert.Equal(plan.ManifestId.ToString(), fields[^1]);
        });

        // Trailer line is present and starts with T|.
        Assert.Contains(lines, l => l.StartsWith("T|", StringComparison.Ordinal));
    }
}
