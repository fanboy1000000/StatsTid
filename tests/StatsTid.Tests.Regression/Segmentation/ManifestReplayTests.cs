using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using Testcontainers.PostgreSql;

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
    public async Task ReplayAsync_ReturnsResult_WithOriginalManifestId_AndMatchingShape()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_factory);
        var pcs = TestFixtures.BuildPcs(_factory, _eventStore);

        var profile = TestFixtures.Profile("EMP-REPLAY-1");
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));
        var absences = Array.Empty<AbsenceEntry>();

        // Forward calculation produces manifest M1.
        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: new DateOnly(2026, 3, 25),
            periodEnd: new DateOnly(2026, 4, 7),
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

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
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE manifest_id = @id", conn);
        countCmd.Parameters.AddWithValue("id", plan.ManifestId);
        var rows = (long)(await countCmd.ExecuteScalarAsync())!;
        Assert.Equal(1L, rows);
    }
}
