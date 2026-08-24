using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression;

/// <summary>
/// Regression tests for Codex BLOCKER #6 (TASK-1802) — the generic-vs-position-specific
/// wage-type-mapping lookup semantics of the SHIPPED <see cref="PayrollMappingService"/>.
///
/// <para>
/// <b>Why this test exists (PM view):</b> the payroll export must translate a "time type"
/// (e.g. NORMAL_HOURS) into a payroll "wage type" (e.g. SLS_0110). The lookup has a
/// fallback rule — a role/position-specific mapping wins over the generic one, and the
/// generic row is stored with an EMPTY-STRING position (not NULL). A past bug queried
/// <c>position IS NULL</c>, which matched zero rows, so every generic mapping was invisible.
/// These tests seed real rows and call the real service to prove the fallback resolves.
/// </para>
///
/// <para>
/// <b>S133 / TASK-13302 (QUAL-110) — drift-guard rewire:</b> the schema this test runs
/// against is now the SHIPPED <c>docker/postgres/init.sql</c>, applied verbatim via
/// <see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/> — NOT a hand-pasted
/// copy of the table DDL. The previous copy had drifted from production three ways, so it
/// "accepted writes production rejects":
/// <list type="bullet">
///   <item>it invented an <c>effective_from … DEFAULT '2020-01-01'</c> the real column
///   LACKS (production is <c>effective_from DATE NOT NULL</c>, no default) — so the old
///   seed could omit <c>effective_from</c> and still succeed;</item>
///   <item>it OMITTED the <c>idx_wtm_natural_key_history</c> unique index, so a duplicate
///   (natural-key, effective_from) history row — which production REJECTS with 23505 — was
///   silently accepted;</item>
///   <item>its "copied verbatim from init.sql:74-83" comment pointed at the wrong lines
///   entirely.</item>
/// </list>
/// Binding to the real init.sql makes drift impossible: any change to the shipped
/// <c>wage_type_mappings</c> DDL is reflected here automatically. The two guard tests at the
/// bottom pin exactly the two properties the old copy got wrong (NOT-NULL effective_from and
/// the history unique index), each with a documented mutation that turns them RED.
/// </para>
///
/// Requires a running Docker daemon. If Docker is unavailable the fixture constructor will
/// throw and the tests will surface as failures with a clear message — we do not silently
/// skip, because this is a BLOCKER regression.
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingRegressionTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";

    // The generic-row seed effective_from. Production's wage_type_mappings.effective_from is
    // NOT NULL with NO default (see the drift note above), so — like the real init.sql seeds
    // and the real repo/endpoint write paths — we MUST supply it explicitly. Omitting it (as
    // the old hand-copied schema's bogus DEFAULT '2020-01-01' allowed) is now a NOT-NULL
    // violation, which is precisely the production behaviour this guard restores.
    private static readonly DateOnly SeedEffectiveFrom = new(2020, 1, 1);

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

        // QUAL-110: apply the SHIPPED docker/postgres/init.sql (the exact production schema +
        // migrations) instead of a hand-pasted copy. On a fresh/empty database init.sql's real
        // `CREATE TABLE wage_type_mappings` runs, so the table has the true production shape
        // (surrogate mapping_id PK, effective_from NOT NULL with no default, both unique
        // indexes, and the version column added by the s25-d2-2-version migration).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_container.GetConnectionString());

        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();

            // init.sql seeds its own wage_type_mappings rows (e.g. NORMAL_HOURS/OK24/AC generic),
            // which would collide with this test's seed on idx_wtm_natural_key_open. TRUNCATE
            // clears the DATA while keeping the production SCHEMA (columns, indexes, constraints)
            // fully intact — so the drift guard still reflects production exactly.
            await using (var truncate = new NpgsqlCommand("TRUNCATE TABLE wage_type_mappings", conn))
                await truncate.ExecuteNonQueryAsync();

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
        // Seeded rows — exercised by the lookup tests below.
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
            // effective_from is supplied EXPLICITLY — production's column is NOT NULL with no
            // default (QUAL-110). This mirrors how init.sql seeds and how the repo/endpoint
            // write paths always set effective_from.
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
                VALUES (@timeType, @wageType, @okVersion, @agreementCode, @position, @description, @effectiveFrom)
                """, conn);
            cmd.Parameters.AddWithValue("timeType", row.TimeType);
            cmd.Parameters.AddWithValue("wageType", row.WageType);
            cmd.Parameters.AddWithValue("okVersion", row.OkVersion);
            cmd.Parameters.AddWithValue("agreementCode", row.Agreement);
            cmd.Parameters.AddWithValue("position", row.Position);
            cmd.Parameters.AddWithValue("description", (object?)row.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("effectiveFrom", SeedEffectiveFrom);
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
            // The repo's ExecuteCreateAsync writes mapping.EffectiveFrom; supply a real date so
            // the write satisfies the production NOT-NULL effective_from column (QUAL-110).
            EffectiveFrom = SeedEffectiveFrom,
        };

        var created = await repo.CreateAsync(mapping);
        Assert.True(created);

        var resolved = await _service.GetMappingAsync("HOLIDAY_PAY", "OK26", "PROSA", position: null);
        Assert.NotNull(resolved);
        Assert.Equal("SLS_0998", resolved!.WageType);
        Assert.Equal("", resolved.Position);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // QUAL-013 fold-in — real-resolver, seeded-mapping-regression proof.
    //
    // The deleted unit-project theater (PayrollMappingTests.cs) "asserted values it just
    // assigned and re-implemented the lookup in the test body" — it would pass even if the
    // whole PayrollMappingService were deleted. The real resolver is DB-backed and cannot
    // run in the Docker-less unit project, so its genuine coverage lives HERE: seed a row,
    // call the SHIPPED resolver, assert its EXACT output.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// QUAL-013: the SHIPPED <see cref="PayrollMappingService.GetMappingAsync"/> returns
    /// EXACTLY the wage type seeded for the natural key — the real translation the payroll
    /// export depends on. This is falsifiable in the way the unit theater was not:
    /// <b>mutation → RED</b> — change the seed's wage type (e.g. SLS_0110 → SLS_9999) OR
    /// break the resolver's SELECT/precedence, and this assertion fails. Deleting the
    /// production resolver fails compilation.
    /// </summary>
    [Fact]
    public async Task RealResolver_ResolvesExactSeededWageType_ForGenericKey()
    {
        // AC/NORMAL_HOURS generic was seeded as SLS_0110. A generic (null-position) lookup
        // must resolve to precisely that wage type.
        var mapping = await _service.GetMappingAsync("NORMAL_HOURS", "OK24", "AC", position: null);

        Assert.NotNull(mapping);
        Assert.Equal("SLS_0110", mapping!.WageType);
        Assert.Equal("NORMAL_HOURS", mapping.TimeType);
        Assert.Equal("OK24", mapping.OkVersion);
        Assert.Equal("AC", mapping.AgreementCode);
        Assert.Equal("", mapping.Position);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // QUAL-110 drift guards — pin the two properties the hand-copied schema got wrong.
    // These are RED under the OLD hand-pasted DDL and GREEN only against the real init.sql.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// QUAL-110 guard #1 — the SHIPPED <c>idx_wtm_natural_key_history</c> unique index
    /// (which the old hand-copied schema OMITTED) forbids two history rows sharing the same
    /// (natural-key, effective_from). We insert a CLOSED row and then an OPEN row at the
    /// SAME effective_from: the partial <c>idx_wtm_natural_key_open</c> index permits this
    /// (only one open row), so the ONLY thing that can reject it is the history index.
    ///
    /// <para><b>Mutation → RED:</b> remove <c>idx_wtm_natural_key_history</c> from
    /// <c>docker/postgres/init.sql</c> (the exact drift the old copy embodied) and the
    /// second insert succeeds, so <c>Assert.Throws</c> fails. That is the drift the guard
    /// now catches automatically.</para>
    /// </summary>
    [Fact]
    public async Task DuplicateHistoryRow_RejectedBy_RealHistoryUniqueIndex()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        // A natural key with no seeded rows, to isolate this guard.
        const string tt = "WTM_QUAL110_HISTORY";
        var ef = new DateOnly(2020, 1, 1);

        // Row 1: CLOSED at [2020-01-01, 2021-01-01).
        await InsertRowAsync(conn, tt, ef, effectiveTo: new DateOnly(2021, 1, 1));

        // Row 2: OPEN at the SAME effective_from. Passes idx_wtm_natural_key_open (row 1 is
        // closed, so this is the only open row) — so a rejection here can ONLY come from
        // idx_wtm_natural_key_history.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertRowAsync(conn, tt, ef, effectiveTo: null));

        Assert.Equal("23505", ex.SqlState); // unique_violation
        Assert.Equal("idx_wtm_natural_key_history", ex.ConstraintName);
    }

    /// <summary>
    /// QUAL-110 guard #2 — the SHIPPED <c>wage_type_mappings.effective_from</c> is
    /// <c>NOT NULL</c> with NO default (the old hand-copied schema invented a
    /// <c>DEFAULT '2020-01-01'</c> production lacks). An INSERT that omits
    /// <c>effective_from</c> must therefore be REJECTED with a not-null violation.
    ///
    /// <para><b>Mutation → RED:</b> add a <c>DEFAULT</c> to the real column (or make it
    /// nullable) and the omitting insert succeeds, so <c>Assert.Throws</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task InsertOmittingEffectiveFrom_RejectedBy_RealNotNullColumn()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description)
                VALUES ('WTM_QUAL110_NODEFAULT', 'SLS_0000', 'OK24', 'AC', '', 'no effective_from supplied')
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.Equal("23502", ex.SqlState); // not_null_violation
        Assert.Equal("effective_from", ex.ColumnName);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Raw insert of one wage_type_mappings row for the natural key
    /// (<paramref name="timeType"/>/OK24/AC/'') at the given effective range. Supplies
    /// effective_from explicitly (production NOT-NULL column).
    /// </summary>
    private static async Task InsertRowAsync(
        NpgsqlConnection conn, string timeType, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (
                time_type, wage_type, ok_version, agreement_code, position, description,
                effective_from, effective_to)
            VALUES (@tt, 'SLS_0110', 'OK24', 'AC', '', 'history-guard', @ef, @et)
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ef", effectiveFrom);
        cmd.Parameters.AddWithValue("et", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
