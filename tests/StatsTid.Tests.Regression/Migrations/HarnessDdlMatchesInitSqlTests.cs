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
/// that is not <c>'TIME_POST'</c>, a nullable <c>source</c>, a missing partial unique index, a
/// <c>hours</c> that is not <c>NUMERIC(8,4)</c>, or a renamed CHECK each put one named line on one
/// side of the diff and fail the row. Until TASK-14500's <c>init.sql</c> change is on the same branch
/// the copies are AHEAD of <c>init.sql</c> and the rows fail on the five new columns and two indexes:
/// that ordering is expected, and the Orchestrator reconciles the names at the merge.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class HarnessDdlMatchesInitSqlTests : IAsyncLifetime
{
    // Constant statements, no interpolation (CA2100 stays at its ratchet): the two database names are fixed.
    private const string CreateCopyDatabase = "CREATE DATABASE hdl_copy";
    private const string CreateInitSqlDatabase = "CREATE DATABASE hdl_initsql";
    private const string CopyDatabase = "hdl_copy";
    private const string InitSqlDatabase = "hdl_initsql";

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

        var copyConnection = await CreateFreshDatabaseAsync(CreateCopyDatabase, CopyDatabase);
        await using (var conn = new NpgsqlConnection(copyConnection))
        {
            await conn.OpenAsync();
            // CA2100-justified (QUAL-073 ratchet): the DDL is a compile-time constant of a test helper.
#pragma warning disable CA2100
            await using var cmd = new NpgsqlCommand(ddl, conn);
#pragma warning restore CA2100
            await cmd.ExecuteNonQueryAsync();
        }

        var initSqlConnection = await CreateFreshDatabaseAsync(CreateInitSqlDatabase, InitSqlDatabase);
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
    private async Task<string> CreateFreshDatabaseAsync(string createStatement, string databaseName)
    {
        await using (var admin = new NpgsqlConnection(_host.ConnectionString))
        {
            await admin.OpenAsync();
            // CA2100-justified (QUAL-073 ratchet): one of the two constant CREATE DATABASE statements above.
#pragma warning disable CA2100
            await using var cmd = new NpgsqlCommand(createStatement, admin);
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
