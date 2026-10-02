using System.Reflection;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Outbox;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S145 / TASK-14501 (R51) — the harness-drift guard: the test harness's private copies of the three
/// tables that sprint 145 changes must equal what <c>docker/postgres/init.sql</c> builds.
///
/// <para><b>Plain language.</b> Three test helpers keep their own hand-written copies of production
/// tables so a test can start with a small database instead of the whole schema:
/// <c>ProjectionSchemaTestFixture</c> (the two hours tables), <c>TxContractTests</c> (the same two
/// tables, again) and <c>TestFixtures.DockerHarness.SchemaDdl</c> (<c>segment_manifests</c>). A copy
/// that lags behind production breaks every test that writes a column the copy lacks, and it breaks
/// them in setup, with an error that points at the test and not at the stale copy. S144's close went
/// red on exactly that shape (<c>SPRINT-144.md</c> § C-5). This guard compares each copy with the real
/// schema, table by table, so drift fails here with the name of the column, index or constraint.</para>
///
/// <para><b>How it compares (and why not the obvious way).</b> Each theory row creates its copy in a
/// FRESH database, then builds a SECOND fresh database from the full <c>init.sql</c> alone
/// (<see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/>), and snapshots both:
/// <c>information_schema.columns</c> (name, type, precision, nullability, default),
/// <c>pg_indexes</c> (name and full definition) and <c>pg_constraint</c> (name and definition: the
/// source CHECK and the keys) for the row's tables. The two snapshots must be equal.
/// Applying <c>init.sql</c> on top of the copy would be weaker: every <c>CREATE TABLE</c> in it is
/// <c>IF NOT EXISTS</c> (<c>init.sql:1880, :1903, :2056</c>), and <c>ADD COLUMN IF NOT EXISTS</c> is a
/// no-op on a column that is present with the wrong type, nullability or default. So applying on top
/// would catch a missing column but never type drift. Real drift of that kind existed when this guard
/// was written: <c>hours</c> was <c>NUMERIC(6,2)</c> in both hours copies against production's
/// <c>NUMERIC(8,4)</c>, which the same task corrected in the copies.</para>
///
/// <para><b>Scope, stated.</b> Only the tables named on each row are compared. The other tables the
/// same helpers create (<c>events</c>, <c>wage_type_mappings</c>, <c>work_time_projection</c>,
/// <c>entitlement_balances</c>, <c>outbox_events</c>, every <c>TxContractTests</c> table) are not
/// checked by this guard. Column order is not compared (a copy may list columns in its own order);
/// comments are not compared.</para>
///
/// <para><b>Red conditions (guard, no MD: it passes once the copies and <c>init.sql</c> agree).</b>
/// Dropping <c>plan_inputs_jsonb</c> from the <c>segment_manifests</c> copy, a <c>source</c> default
/// that is not <c>'TIME_POST'</c>, a nullable <c>source</c>, a missing partial unique index (or one
/// without the time-entry index's <c>NULLS NOT DISTINCT</c> clause, which <c>pg_indexes.indexdef</c> prints), a
/// <c>hours</c> that is not <c>NUMERIC(8,4)</c>, or a renamed CHECK each put one named line on one
/// side of the diff and fail the row. Until TASK-14500's <c>init.sql</c> change is on the same branch
/// the copies are AHEAD of <c>init.sql</c> and the rows fail on the five new columns and two indexes:
/// that ordering is expected, and the Orchestrator reconciles the names at the merge.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class HarnessDdlMatchesInitSqlTests : IAsyncLifetime
{
    // Fresh per row: a database name that already exists (a retried row, a shared server) must not
    // fail the guard. The names are test-generated hex, never user input.
    private readonly string _copyDatabase = "hdl_copy_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string _initSqlDatabase = "hdl_init_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private const string ColumnsSql =
        """
        SELECT table_name::text, column_name::text, data_type::text, udt_name::text, is_nullable::text,
               COALESCE(column_default, '<none>'),
               COALESCE(character_maximum_length::text, ''),
               COALESCE(numeric_precision::text, ''),
               COALESCE(numeric_scale::text, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name::text = ANY(@tables)
        ORDER BY table_name::text, column_name::text
        """;

    private const string IndexesSql =
        """
        SELECT tablename::text, indexname::text, indexdef
        FROM pg_indexes
        WHERE schemaname = 'public' AND tablename::text = ANY(@tables)
        ORDER BY tablename::text, indexname::text
        """;

    private const string ConstraintsSql =
        """
        SELECT c.relname::text, con.conname::text, con.contype::text, pg_get_constraintdef(con.oid)
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND c.relname::text = ANY(@tables)
          AND con.contype IN ('c', 'p', 'u', 'f', 'x')
        ORDER BY c.relname::text, con.conname::text
        """;

    private static readonly string[] HoursTables = { "time_entries_projection", "absences_projection" };
    private static readonly string[] ManifestTables = { "segment_manifests" };

    /// <summary>A Postgres container that hosts the two fresh databases of the row under test.</summary>
    private TestFixtures.DockerHarness _host = null!;

    public async Task InitializeAsync() => _host = await TestFixtures.DockerHarness.StartAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    /// <summary>
    /// One row per harness copy. Each row: copy the copy's DDL into a fresh database, build a second
    /// fresh database from <c>init.sql</c> alone, and require the snapshots of the row's tables equal.
    /// </summary>
    [Theory]
    [InlineData("ProjectionSchemaTestFixture")]
    [InlineData("TxContractTests")]
    [InlineData("TestFixtures.DockerHarness.SchemaDdl")]
    public async Task HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema(string copy)
    {
        var (ddl, tables) = ResolveCopy(copy);

        var copyConnection = await CreateFreshDatabaseAsync(_copyDatabase);
        await using (var conn = new NpgsqlConnection(copyConnection))
        {
            await conn.OpenAsync();
            // CA2100-justified (QUAL-073 ratchet): the DDL is a compile-time constant of a test helper.
#pragma warning disable CA2100
            await using var cmd = new NpgsqlCommand(ddl, conn);
#pragma warning restore CA2100
            await cmd.ExecuteNonQueryAsync();
        }

        var initSqlConnection = await CreateFreshDatabaseAsync(_initSqlDatabase);
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(initSqlConnection);

        var problems = new List<string>();
        foreach (var (section, sql) in new[]
                 {
                     ("column", ColumnsSql),
                     ("index", IndexesSql),
                     ("constraint", ConstraintsSql),
                 })
        {
            var fromCopy = await SnapshotAsync(copyConnection, sql, tables);
            var fromInitSql = await SnapshotAsync(initSqlConnection, sql, tables);

            // A section that is empty on BOTH sides would pass vacuously; every table here has columns,
            // a primary key and at least one index, so an empty snapshot means the query found nothing.
            Assert.True(fromInitSql.Count > 0, $"snapshot of {section}s from init.sql is empty for {string.Join(", ", tables)}: the guard compared nothing");

            problems.AddRange(fromCopy.Except(fromInitSql, StringComparer.Ordinal)
                .Select(l => $"{section} only in the harness copy: {l}"));
            problems.AddRange(fromInitSql.Except(fromCopy, StringComparer.Ordinal)
                .Select(l => $"{section} only in init.sql: {l}"));
        }

        Assert.True(problems.Count == 0,
            $"Harness copy '{copy}' has drifted from docker/postgres/init.sql for {string.Join(", ", tables)}. " +
            "Mirror the change in the copy (or, if init.sql is behind, in init.sql).\n  " +
            string.Join("\n  ", problems));
    }

    // Two literal statements (no interpolation, so CA2100 is not touched): the same cell, a NULL task_id,
    // both rows CURRENT Skema rows. Every other NOT NULL column is filled; event_id and outbox_id differ.
    private const string InsertNullTaskSkemaRowFirst =
        """
        INSERT INTO time_entries_projection
            (event_id, employee_id, date, hours, task_id, agreement_code, ok_version, occurred_at, outbox_id,
             source, superseded_by_outbox_id)
        VALUES
            (gen_random_uuid(), 'emp_hdl_null_task', DATE '2026-05-04', 7.4, NULL, 'HK', 'OK24', NOW(), 1,
             'SKEMA', NULL)
        """;

    private const string InsertNullTaskSkemaRowSecond =
        """
        INSERT INTO time_entries_projection
            (event_id, employee_id, date, hours, task_id, agreement_code, ok_version, occurred_at, outbox_id,
             source, superseded_by_outbox_id)
        VALUES
            (gen_random_uuid(), 'emp_hdl_null_task', DATE '2026-05-04', 8.0, NULL, 'HK', 'OK24', NOW(), 2,
             'SKEMA', NULL)
        """;

    /// <summary>
    /// <b>(guard)</b> The production index that makes "one current Skema row per cell" true must also
    /// hold for a cell with NO task id. <b>Plain language:</b> a database treats two NULLs as different
    /// values, so a plain unique index would let two current Skema rows for the same employee and day
    /// coexist whenever the task is empty, and a re-save would then be counted twice. The time-entry
    /// index is created <c>NULLS NOT DISTINCT</c> to close that; this fact proves it on a database built
    /// from <c>init.sql</c> alone: the first row goes in, the second current Skema row for the same
    /// employee, day and (NULL) task is refused with a unique violation on
    /// <c>ux_time_entries_proj_current_skema_cell</c>. (The Skema endpoint always sends a task id
    /// today, <c>SkemaEndpoints.cs:2367</c>; this pins the schema, not the endpoint.)
    ///
    /// <para><b>Guard, no MD: passes once TASK-14500's schema is on the branch.</b> Before it, the
    /// <c>source</c> and <c>superseded_by_outbox_id</c> columns do not exist and the insert fails on the
    /// missing column (42703) at the first statement. <b>Red condition:</b> drop <c>NULLS NOT
    /// DISTINCT</c> from the index (in <c>init.sql</c>): the two NULLs are then distinct, the second
    /// insert is accepted, no exception is thrown and <c>ThrowsAsync</c> fails with "no exception".</para>
    /// </summary>
    [Fact]
    public async Task CurrentSkemaCellIndex_NullTaskId_TwoCurrentRows_Collide23505()
    {
        var connectionString = await CreateFreshDatabaseAsync("hdl_null_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var first = new NpgsqlCommand(InsertNullTaskSkemaRowFirst, conn))
            Assert.Equal(1, await first.ExecuteNonQueryAsync());

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var second = new NpgsqlCommand(InsertNullTaskSkemaRowSecond, conn);
            await second.ExecuteNonQueryAsync();
        });
        Assert.Equal("23505", ex.SqlState);
        Assert.Equal("ux_time_entries_proj_current_skema_cell", ex.ConstraintName);
    }

    /// <summary>The DDL to apply and the tables to compare, for one harness copy.</summary>
    private static (string Ddl, string[] Tables) ResolveCopy(string copy) => copy switch
    {
        "ProjectionSchemaTestFixture" => (ProjectionSchemaTestFixture.Ddl, HoursTables),
        "TxContractTests" => (ReadTxContractSchemaDdl(), HoursTables),
        "TestFixtures.DockerHarness.SchemaDdl" => (TestFixtures.DockerHarness.SchemaDdl, ManifestTables),
        _ => throw new ArgumentOutOfRangeException(nameof(copy), copy, "Unknown harness copy."),
    };

    /// <summary>
    /// <c>TxContractTests.SchemaDdl</c> is a <c>private const</c> in a file whose authorized edit range
    /// for this task is the DDL block only, so it is read by reflection rather than made
    /// <c>internal</c>. A rename or move fails loudly here. (Making it <c>internal</c> in
    /// <c>TxContractTests.cs</c> would remove the reflection; that is a one-token change outside the
    /// task's file range, reported for the Orchestrator.)
    /// </summary>
    private static string ReadTxContractSchemaDdl()
    {
        var field = typeof(StatsTid.Tests.Regression.Infrastructure.TxContractTests)
            .GetField("SchemaDdl", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "TxContractTests.SchemaDdl was renamed or moved; update HarnessDdlMatchesInitSqlTests.");
        return (string)field.GetRawConstantValue()!;
    }

    /// <summary>Creates an empty database in the container and returns its connection string.</summary>
    private async Task<string> CreateFreshDatabaseAsync(string databaseName)
    {
        await using (var admin = new NpgsqlConnection(_host.ConnectionString))
        {
            await admin.OpenAsync();
            // CA2100-justified (QUAL-073 ratchet): `databaseName` is one of the two test-generated hex
            // names above; DDL cannot take a parameter for an identifier.
#pragma warning disable CA2100
            await using var cmd = new NpgsqlCommand("CREATE DATABASE " + databaseName, admin);
#pragma warning restore CA2100
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_host.ConnectionString) { Database = databaseName }.ConnectionString;
    }

    /// <summary>Runs one snapshot query and returns each row as a single pipe-joined line.</summary>
    private static async Task<List<string>> SnapshotAsync(string connectionString, string sql, string[] tables)
    {
        var lines = new List<string>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): `sql` is one of the three constant snapshot queries above;
        // the only variable input (the table names) travels as a parameter.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("tables", tables);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var parts = new string[reader.FieldCount];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = reader.IsDBNull(i) ? "<null>" : reader.GetString(i);
            lines.Add(string.Join(" | ", parts));
        }
        return lines;
    }
}
