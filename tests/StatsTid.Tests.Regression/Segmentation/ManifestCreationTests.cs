using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using Testcontainers.PostgreSql;

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
    private const string ImageTag = "postgres:16-alpine";

    /// <summary>
    /// Compatible-with-<c>init.sql</c> shape covering <c>segment_manifests</c> +
    /// <c>events</c> + <c>event_streams</c> + <c>wage_type_mappings</c>. The
    /// <c>events</c> shape is hand-synthesised: production <c>init.sql</c> creates the
    /// base table and adds <c>actor_id</c> / <c>actor_role</c> / <c>correlation_id</c>
    /// via subsequent <c>ALTER TABLE</c> statements (S3 / S4); we collapse them inline
    /// here. Schema drift in those columns will NOT auto-surface — when adding columns
    /// to <c>events</c> in production, mirror them here. <c>segment_manifests</c> and
    /// <c>wage_type_mappings</c> are single-block <c>CREATE TABLE</c> in production;
    /// those rows match init.sql byte-for-byte.
    /// </summary>
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

        await TestFixtures.SeedWageTypeMappingsAsync(_factory);
        var pcs = TestFixtures.BuildPcs(_factory, _eventStore);

        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: new DateOnly(2026, 3, 25),
            periodEnd: new DateOnly(2026, 4, 7),
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);

        var result = await pcs.CalculateAsync(
            plan, profile, entries, absences,
            previousFlexBalance: 0m);

        Assert.True(result.Success);
        Assert.NotEmpty(result.ExportLines);

        // ---- Manifest projection row ----
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
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
