using Npgsql;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S25 / TASK-2508 migration idempotency test for the <c>s25-d2-2-version</c> migration
/// (Parts A + B per ADR-019). Mirrors the
/// <see cref="Config.EndExclusiveMigrationTests.Migration_RunTwice_IsIdempotent"/> precedent
/// (S22 migration test pattern).
///
/// <para>
/// Verifies:
///   <list type="bullet">
///     <item>Re-running the <c>DO $$ ... END $$</c> block guarded by the
///       <c>schema_migrations</c> ledger is a no-op (no errors, no schema drift).</item>
///     <item>Part A — <c>version BIGINT NOT NULL DEFAULT 1</c> column is present on the
///       four S25-targeted state tables (<c>agreement_configs</c>,
///       <c>position_override_configs</c>, <c>wage_type_mappings</c>,
///       <c>entitlement_configs</c>).</item>
///     <item>Part B — <c>version_before BIGINT NULL</c> + <c>version_after BIGINT NULL</c>
///       columns are present on the three S25-touched audit tables.</item>
///     <item>Existing rows backfill to <c>version=1</c> via the <c>DEFAULT</c>; fresh
///       inserts pick up the same default automatically.</item>
///     <item>Running the same DO $$ block a SECOND time leaves the schema_migrations ledger
///       row unique (no duplicate inserts; no version-bump on existing rows).</item>
///   </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S25VersionMigrationTests : IAsyncLifetime
{
    /// <summary>
    /// Pre-S25 baseline schema for the four state tables + three audit tables, deliberately
    /// WITHOUT the <c>version</c> / <c>version_before</c> / <c>version_after</c> columns.
    /// Models the post-S22, pre-S25 production shape that the <c>s25-d2-2-version</c>
    /// migration converts. Includes <c>schema_migrations</c> ledger so the migration's
    /// idempotency guard fires correctly.
    /// </summary>
    private const string PreS25SchemaDdl = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );

        CREATE TABLE IF NOT EXISTS agreement_configs (
            config_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            agreement_code     TEXT        NOT NULL,
            ok_version         TEXT        NOT NULL,
            status             TEXT        NOT NULL DEFAULT 'DRAFT',
            weekly_norm_hours  DECIMAL     NOT NULL,
            created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS position_override_configs (
            override_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            agreement_code   TEXT        NOT NULL,
            ok_version       TEXT        NOT NULL,
            position_code    TEXT        NOT NULL,
            status           TEXT        NOT NULL DEFAULT 'ACTIVE',
            created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS wage_type_mappings (
            time_type       TEXT NOT NULL,
            wage_type       TEXT NOT NULL,
            ok_version      TEXT NOT NULL,
            agreement_code  TEXT NOT NULL,
            position        TEXT NOT NULL DEFAULT '',
            description     TEXT,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (time_type, ok_version, agreement_code, position)
        );

        CREATE TABLE IF NOT EXISTS entitlement_configs (
            config_id            UUID    PRIMARY KEY DEFAULT gen_random_uuid(),
            entitlement_type     TEXT    NOT NULL,
            agreement_code       TEXT    NOT NULL,
            ok_version           TEXT    NOT NULL,
            annual_quota         DECIMAL NOT NULL,
            UNIQUE (entitlement_type, agreement_code, ok_version)
        );

        CREATE TABLE IF NOT EXISTS agreement_config_audit (
            audit_id    BIGSERIAL   PRIMARY KEY,
            config_id   UUID        NOT NULL,
            action      TEXT        NOT NULL,
            actor_id    TEXT        NOT NULL,
            actor_role  TEXT        NOT NULL,
            timestamp   TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS position_override_config_audit (
            audit_id    BIGSERIAL   PRIMARY KEY,
            override_id UUID        NOT NULL,
            action      TEXT        NOT NULL,
            actor_id    TEXT        NOT NULL,
            actor_role  TEXT        NOT NULL,
            timestamp   TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS wage_type_mapping_audit (
            audit_id        BIGSERIAL   PRIMARY KEY,
            time_type       TEXT        NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            position        TEXT        NOT NULL DEFAULT '',
            action          TEXT        NOT NULL,
            actor_id        TEXT        NOT NULL,
            actor_role      TEXT        NOT NULL,
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    /// <summary>
    /// Verbatim copy of the <c>s25-d2-2-version</c> migration DO $$ block from
    /// <c>docker/postgres/init.sql</c> lines ~1317-1359 — the single canonical source of
    /// the S25 migration logic. Idempotent via <c>schema_migrations</c> ledger guard
    /// (the <c>IF NOT FOUND THEN RETURN</c> branch fires on re-run).
    /// </summary>
    private const string S25MigrationDdl = """
        DO $$
        BEGIN
            INSERT INTO schema_migrations (migration_id, notes)
            VALUES ('s25-d2-2-version', 'ADR-019 (pending): row-version on admin-config surfaces + audit version-transition columns')
            ON CONFLICT (migration_id) DO NOTHING;

            IF NOT FOUND THEN
                RETURN;
            END IF;

            ALTER TABLE agreement_configs
            ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

            ALTER TABLE position_override_configs
            ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

            ALTER TABLE wage_type_mappings
            ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

            ALTER TABLE entitlement_configs
            ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

            ALTER TABLE agreement_config_audit
            ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
            ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;

            ALTER TABLE position_override_config_audit
            ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
            ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;

            ALTER TABLE wage_type_mapping_audit
            ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
            ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;
        END
        $$;
        """;

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ApplyAsync(PreS25SchemaDdl);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Migration_RunTwice_IsIdempotent()
    {
        // 1. Insert pre-migration sentinel rows so we can verify they backfill cleanly.
        var sentinelAgreementCode = "MIG_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await SeedPreMigrationRowsAsync(sentinelAgreementCode);

        // 2. First migration run — adds the columns + records the ledger row.
        await ApplyAsync(S25MigrationDdl);

        // Part A — version BIGINT NOT NULL DEFAULT 1 on four state tables.
        Assert.True(await ColumnExistsAsync("agreement_configs", "version"));
        Assert.True(await ColumnExistsAsync("position_override_configs", "version"));
        Assert.True(await ColumnExistsAsync("wage_type_mappings", "version"));
        Assert.True(await ColumnExistsAsync("entitlement_configs", "version"));

        // Part B — version_before / version_after BIGINT NULL on three audit tables.
        Assert.True(await ColumnExistsAsync("agreement_config_audit", "version_before"));
        Assert.True(await ColumnExistsAsync("agreement_config_audit", "version_after"));
        Assert.True(await ColumnExistsAsync("position_override_config_audit", "version_before"));
        Assert.True(await ColumnExistsAsync("position_override_config_audit", "version_after"));
        Assert.True(await ColumnExistsAsync("wage_type_mapping_audit", "version_before"));
        Assert.True(await ColumnExistsAsync("wage_type_mapping_audit", "version_after"));

        // Existing pre-migration rows backfill to version = 1.
        var preMigrationVersion = await ReadVersionAsync(
            "agreement_configs", "agreement_code", sentinelAgreementCode);
        Assert.Equal(1L, preMigrationVersion);

        // Schema_migrations ledger gets exactly one row for s25-d2-2-version.
        Assert.Equal(1, await CountLedgerRowsAsync("s25-d2-2-version"));

        // 3. Insert a fresh row post-migration — picks up the DEFAULT 1.
        var postMigrationAgreementCode = "MIG_POST_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO agreement_configs (agreement_code, ok_version, weekly_norm_hours)
                VALUES (@code, 'OK24', 37)
                """, conn);
            cmd.Parameters.AddWithValue("code", postMigrationAgreementCode);
            await cmd.ExecuteNonQueryAsync();
        }
        var freshVersion = await ReadVersionAsync(
            "agreement_configs", "agreement_code", postMigrationAgreementCode);
        Assert.Equal(1L, freshVersion);

        // 4. Second migration run — idempotency guard fires; no errors, no schema drift,
        //    ledger row stays unique.
        await ApplyAsync(S25MigrationDdl);

        Assert.Equal(1, await CountLedgerRowsAsync("s25-d2-2-version"));

        // Pre-migration sentinel row's version stays at 1 (idempotency: re-run does NOT
        // re-apply DEFAULT bumps to existing rows — confirms the IF NOT FOUND RETURN guard).
        var afterReRun = await ReadVersionAsync(
            "agreement_configs", "agreement_code", sentinelAgreementCode);
        Assert.Equal(1L, afterReRun);

        // Columns still present (re-run was a no-op, not a drop+recreate).
        Assert.True(await ColumnExistsAsync("agreement_configs", "version"));
        Assert.True(await ColumnExistsAsync("entitlement_configs", "version"));
        Assert.True(await ColumnExistsAsync("wage_type_mapping_audit", "version_before"));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task SeedPreMigrationRowsAsync(string sentinelAgreementCode)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO agreement_configs (agreement_code, ok_version, weekly_norm_hours)
            VALUES (@code, 'OK24', 37)
            """, conn);
        cmd.Parameters.AddWithValue("code", sentinelAgreementCode);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ApplyAsync(string ddl)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column
            """, conn);
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("column", columnName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1L;
    }

    private async Task<long> ReadVersionAsync(string tableName, string filterColumn, string filterValue)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Table + column names cannot be parameterised in Npgsql; this method is internal
        // to the test (no untrusted input) so direct interpolation is safe.
        await using var cmd = new NpgsqlCommand(
            $"SELECT version FROM {tableName} WHERE {filterColumn} = @value", conn);
        cmd.Parameters.AddWithValue("value", filterValue);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountLedgerRowsAsync(string migrationId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @id", conn);
        cmd.Parameters.AddWithValue("id", migrationId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
