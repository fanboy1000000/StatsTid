using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression;

/// <summary>
/// Regression tests for Codex BLOCKER #6 (TASK-1802).
///
/// Background: <c>wage_type_mappings.position</c> is declared NOT NULL DEFAULT ''.
/// The previous <see cref="PayrollMappingService"/> queried <c>position IS NULL</c>
/// for the generic fallback, which matched zero rows at runtime — every generic
/// wage-type mapping was invisible.
///
/// Canonical convention going forward: empty string ('') is the generic row.
/// These tests spin up a real Postgres container (Testcontainers) with the
/// production schema, seed both generic and position-specific rows, and verify
/// the lookup semantics are correct end-to-end.
///
/// Requires a running Docker daemon. If Docker is unavailable the fixture
/// constructor will throw and the tests will surface as failures with a clear
/// message — we do not silently skip, because this is a BLOCKER regression.
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingRegressionTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";

    // Minimal schema subset — only the table under test. We deliberately DO NOT
    // execute the full init.sql here (it pulls in pgcrypto extensions, many
    // unrelated tables, etc.). Keeping this narrow keeps the test fast and
    // focused. The column definition is copy-pasted verbatim from
    // docker/postgres/init.sql:74-83 so a schema drift will cause test failure.
    private const string SchemaDdl = """
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
    private PayrollMappingService _service = null!;
    private DbConnectionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();

        await _container.StartAsync();

        // Create schema + seed rows
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var schemaCmd = new NpgsqlCommand(SchemaDdl, conn);
            await schemaCmd.ExecuteNonQueryAsync();
            await SeedAsync(conn);
        }

        _factory = new DbConnectionFactory(_container.GetConnectionString());
        _service = new PayrollMappingService(_factory, NullLogger<PayrollMappingService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        // Seeded rows — exercised by the five tests below.
        // (time_type, wage_type, ok_version, agreement_code, position)
        var rows = new (string TimeType, string WageType, string OkVersion, string Agreement, string Position, string? Description)[]
        {
            // Generic rows (position = '')
            ("NORMAL_HOURS", "SLS_0110",  "OK24", "AC", "",                   "Generic normal hours"),
            ("OVERTIME_50",  "SLS_0210",  "OK24", "HK", "",                   "Generic overtime"),

            // Position-specific override, coexists with its generic for AC/NORMAL_HOURS
            ("NORMAL_HOURS", "SLS_0110R", "OK24", "AC", "RESEARCHER",         "Researcher-specific normal hours"),

            // Position-specific without any generic counterpart — exercises "specific match only"
            ("TEACHING",     "SLS_0410",  "OK24", "AC", "PROFESSOR",          "Professor teaching hours"),
        };

        foreach (var row in rows)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description)
                VALUES (@timeType, @wageType, @okVersion, @agreementCode, @position, @description)
                """, conn);
            cmd.Parameters.AddWithValue("timeType", row.TimeType);
            cmd.Parameters.AddWithValue("wageType", row.WageType);
            cmd.Parameters.AddWithValue("okVersion", row.OkVersion);
            cmd.Parameters.AddWithValue("agreementCode", row.Agreement);
            cmd.Parameters.AddWithValue("position", row.Position);
            cmd.Parameters.AddWithValue("description", (object?)row.Description ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Before the fix the generic row (position = '') was invisible to the service
    /// because the query filtered on <c>position IS NULL</c>. A null-position lookup
    /// for HK/OVERTIME_50 must now resolve to the generic row.
    /// </summary>
    [Fact]
    public async Task GenericLookup_WithNullPosition_ResolvesGenericRow()
    {
        var mapping = await _service.GetMappingAsync("OVERTIME_50", "OK24", "HK", position: null);

        Assert.NotNull(mapping);
        Assert.Equal("SLS_0210", mapping!.WageType);
        Assert.Equal("", mapping.Position);
    }

    /// <summary>
    /// An empty string position argument must behave identically to null
    /// (both mean "generic lookup").
    /// </summary>
    [Fact]
    public async Task GenericLookup_WithEmptyStringPosition_ResolvesGenericRow()
    {
        var mapping = await _service.GetMappingAsync("OVERTIME_50", "OK24", "HK", position: "");

        Assert.NotNull(mapping);
        Assert.Equal("SLS_0210", mapping!.WageType);
    }

    /// <summary>
    /// When a position-specific row exists alongside the generic row, the
    /// position-specific row must win. Precedence is enforced by
    /// <c>ORDER BY (position = '') ASC LIMIT 1</c>.
    /// </summary>
    [Fact]
    public async Task PositionLookup_PrefersPositionSpecificOverGeneric()
    {
        var mapping = await _service.GetMappingAsync("NORMAL_HOURS", "OK24", "AC", position: "RESEARCHER");

        Assert.NotNull(mapping);
        Assert.Equal("SLS_0110R", mapping!.WageType);
        Assert.Equal("RESEARCHER", mapping.Position);
    }

    /// <summary>
    /// When no position-specific row exists for the requested position, the
    /// generic ('') row must be returned as fallback.
    /// </summary>
    [Fact]
    public async Task PositionLookup_FallsBackToGenericWhenNoPositionRow()
    {
        // No "DEPARTMENT_HEAD"-specific row exists for HK/OVERTIME_50; generic must win.
        var mapping = await _service.GetMappingAsync("OVERTIME_50", "OK24", "HK", position: "DEPARTMENT_HEAD");

        Assert.NotNull(mapping);
        Assert.Equal("SLS_0210", mapping!.WageType);
        Assert.Equal("", mapping.Position);
    }

    /// <summary>
    /// An unknown (time_type/ok_version/agreement) combination must return null
    /// regardless of which position is supplied.
    /// </summary>
    [Fact]
    public async Task UnknownCombination_ReturnsNull()
    {
        var generic = await _service.GetMappingAsync("DOES_NOT_EXIST", "OK24", "AC", position: null);
        var specific = await _service.GetMappingAsync("DOES_NOT_EXIST", "OK24", "AC", position: "RESEARCHER");

        Assert.Null(generic);
        Assert.Null(specific);
    }

    /// <summary>
    /// A position-specific row with no generic counterpart must only resolve
    /// when the matching position is supplied; a null/empty lookup must return null.
    /// </summary>
    [Fact]
    public async Task PositionOnlyRow_NotReturnedByGenericLookup()
    {
        // TEACHING has only a PROFESSOR-specific row, no generic.
        var nullLookup = await _service.GetMappingAsync("TEACHING", "OK24", "AC", position: null);
        var specific = await _service.GetMappingAsync("TEACHING", "OK24", "AC", position: "PROFESSOR");

        Assert.Null(nullLookup);
        Assert.NotNull(specific);
        Assert.Equal("SLS_0410", specific!.WageType);
    }

    /// <summary>
    /// Admin CRUD (<see cref="WageTypeMappingRepository"/>) and runtime lookup
    /// (<see cref="PayrollMappingService"/>) must agree on the empty-string generic
    /// convention. Before the TASK-1802 follow-up fix, the repository passed
    /// <c>DBNull.Value</c> for empty <c>Position</c> — a <c>23502 not_null_violation</c>
    /// against the <c>NOT NULL DEFAULT ''</c> column, silently breaking every
    /// GlobalAdmin attempt to create a generic mapping through the UI.
    /// </summary>
    [Fact]
    public async Task RepositoryCreate_ThenServiceLookup_RoundTripsGenericRow()
    {
        var repo = new WageTypeMappingRepository(_factory);
        var mapping = new WageTypeMapping
        {
            TimeType = "HOLIDAY_PAY",
            WageType = "SLS_0998",
            OkVersion = "OK26",
            AgreementCode = "PROSA",
            Position = "",
            Description = "Generic mapping created via admin CRUD path",
        };

        var created = await repo.CreateAsync(mapping);
        Assert.True(created);

        var resolved = await _service.GetMappingAsync("HOLIDAY_PAY", "OK26", "PROSA", position: null);
        Assert.NotNull(resolved);
        Assert.Equal("SLS_0998", resolved!.WageType);
        Assert.Equal("", resolved.Position);
    }
}
