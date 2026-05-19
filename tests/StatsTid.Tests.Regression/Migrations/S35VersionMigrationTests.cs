using Npgsql;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S35 / TASK-3509 migration idempotency test for the <c>s35-d1-users-version-and-audit</c>
/// migration. Mirrors the <see cref="S25VersionMigrationTests"/> precedent (S25 D2.2 row-
/// version + audit version-transition columns), adapted for the greenfield-baked S35 shape:
/// <c>users.version BIGINT NOT NULL DEFAULT 1</c> baked into the base <c>users</c> CREATE +
/// <c>users_audit</c> CREATE TABLE IF NOT EXISTS + two indexes + a single ledger INSERT
/// guarded by <c>ON CONFLICT (migration_id) DO NOTHING</c>.
///
/// <para>
/// Verifies:
///   <list type="bullet">
///     <item>Re-applying the canonical S35 D1 block is a no-op (no errors, no schema drift,
///       no duplicate ledger rows).</item>
///     <item><c>users.version BIGINT NOT NULL DEFAULT 1</c> column is present and existing
///       rows backfill to <c>version=1</c> via the DEFAULT.</item>
///     <item><c>users_audit</c> table exists with the expected columns (action CHECK enum
///       includes CREATED/UPDATED/DELETED/SUPERSEDED for forward-compat per init.sql:613).</item>
///     <item>Both <c>idx_users_audit_user_id</c> and <c>idx_users_audit_at</c> indexes
///       exist (per init.sql:622-623).</item>
///     <item><c>schema_migrations</c> ledger has exactly 1 row with
///       <c>migration_id='s35-d1-users-version-and-audit'</c> after running the block twice.</item>
///   </list>
/// </para>
///
/// <para>
/// <b>Migration block source.</b> Verbatim copy of the S35 D1 segment from
/// <c>docker/postgres/init.sql</c> lines ~598-629. Unlike the S25 / S22 migrations the S35
/// D1 segment is NOT a guarded <c>DO $$ ... END $$</c> block — instead it's greenfield-baked:
/// <c>users.version</c> is part of the base <c>users</c> CREATE (init.sql:467) and the
/// <c>users_audit</c> table uses <c>CREATE TABLE IF NOT EXISTS</c>. Idempotency is therefore
/// intrinsic to the <c>IF NOT EXISTS</c> guards + the <c>ON CONFLICT DO NOTHING</c> ledger
/// insert; this test pins that intrinsic idempotency.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S35VersionMigrationTests : IAsyncLifetime
{
    /// <summary>
    /// Pre-S35 baseline schema. Includes <c>schema_migrations</c> ledger so the migration's
    /// idempotency ledger-insert fires correctly. The <c>users</c> table is deliberately
    /// created WITHOUT the <c>version</c> column (models the pre-S35 production shape that
    /// the s35-d1 segment converts). Existing rows are inserted so we can assert they
    /// backfill cleanly to <c>version=1</c> when the column is added.
    ///
    /// <para>
    /// Includes a minimal <c>organizations</c> stub to satisfy the FK on
    /// <c>users.primary_org_id</c>. The seed row uses a sentinel org_id that lines up with
    /// the test's pre-migration user row.
    /// </para>
    /// </summary>
    private const string PreS35SchemaDdl = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );

        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            parent_org_id       TEXT        NULL,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL DEFAULT 'STY',
            materialized_path   TEXT        NOT NULL DEFAULT '/',
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        INSERT INTO organizations (org_id, org_name)
            VALUES ('STY_S35_MIG', 'S35 Migration Test Org')
            ON CONFLICT (org_id) DO NOTHING;

        -- Pre-S35 users WITHOUT version column. Mirrors init.sql:456-470 minus
        -- the version line at L467. Pre-migration users will backfill version=1
        -- via the DEFAULT when the column lands.
        CREATE TABLE IF NOT EXISTS users (
            user_id             TEXT        PRIMARY KEY,
            username            TEXT        NOT NULL UNIQUE,
            password_hash       TEXT        NOT NULL,
            display_name        TEXT        NOT NULL,
            email               TEXT,
            primary_org_id      TEXT        NOT NULL REFERENCES organizations(org_id),
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            employment_category TEXT        NOT NULL DEFAULT 'Standard',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    /// <summary>
    /// Verbatim copy of the S35 D1 segment from <c>docker/postgres/init.sql</c> lines
    /// ~598-629. Greenfield-baked (no DO $$ block): an ALTER guarded by
    /// <c>ADD COLUMN IF NOT EXISTS</c> bakes in <c>users.version</c> on existing schemas;
    /// <c>CREATE TABLE IF NOT EXISTS</c> handles <c>users_audit</c>; the ledger INSERT
    /// is guarded by <c>ON CONFLICT (migration_id) DO NOTHING</c>.
    ///
    /// <para>
    /// The init.sql production form bakes <c>users.version</c> directly into the base
    /// <c>users</c> CREATE at L467 (greenfield path). For this idempotency test we apply
    /// the same column via <c>ALTER TABLE ... ADD COLUMN IF NOT EXISTS</c> — semantically
    /// equivalent on a freshly-created users table without the column, and equivalent to
    /// the forward-compat path the init.sql ledger entry at L627-629 documents.
    /// </para>
    /// </summary>
    private const string S35MigrationDdl = """
        ALTER TABLE users
        ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

        CREATE TABLE IF NOT EXISTS users_audit (
            audit_id          BIGSERIAL    PRIMARY KEY,
            user_id           TEXT         NOT NULL,
            action            TEXT         NOT NULL CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')),
            previous_data     JSONB        NULL,
            new_data          JSONB        NULL,
            version_before    BIGINT       NULL,
            version_after     BIGINT       NULL,
            actor_id          TEXT         NOT NULL,
            actor_role        TEXT         NOT NULL,
            audit_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_users_audit_user_id ON users_audit(user_id);
        CREATE INDEX IF NOT EXISTS idx_users_audit_at ON users_audit(audit_at);

        INSERT INTO schema_migrations (migration_id, applied_at)
            VALUES ('s35-d1-users-version-and-audit', NOW())
            ON CONFLICT (migration_id) DO NOTHING;
        """;

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ApplyAsync(PreS35SchemaDdl);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Migration_S35_D1_UsersVersionAndAudit_Idempotent()
    {
        // 1. Insert a pre-migration users row (no version column yet) so we can verify
        //    it backfills cleanly to version=1 via the DEFAULT once the ALTER runs.
        var sentinelUserId = "MIG_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await SeedPreMigrationUserAsync(sentinelUserId);

        // 2. First migration run — adds the column + creates the audit table + indexes +
        //    records the ledger row.
        await ApplyAsync(S35MigrationDdl);

        // users.version column present.
        Assert.True(await ColumnExistsAsync("users", "version"));

        // users_audit table + expected columns present.
        Assert.True(await TableExistsAsync("users_audit"));
        Assert.True(await ColumnExistsAsync("users_audit", "version_before"));
        Assert.True(await ColumnExistsAsync("users_audit", "version_after"));
        Assert.True(await ColumnExistsAsync("users_audit", "previous_data"));
        Assert.True(await ColumnExistsAsync("users_audit", "new_data"));
        Assert.True(await ColumnExistsAsync("users_audit", "audit_at"));

        // Both indexes present.
        Assert.True(await IndexExistsAsync("idx_users_audit_user_id"));
        Assert.True(await IndexExistsAsync("idx_users_audit_at"));

        // Pre-migration users row backfilled to version=1 via the DEFAULT.
        Assert.Equal(1L, await ReadUserVersionAsync(sentinelUserId));

        // Ledger row landed exactly once.
        Assert.Equal(1, await CountLedgerRowsAsync("s35-d1-users-version-and-audit"));

        // 3. Insert a fresh post-migration users row — picks up version=1 via DEFAULT.
        var postMigrationUserId = "MIG_POST_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await SeedPreMigrationUserAsync(postMigrationUserId);
        Assert.Equal(1L, await ReadUserVersionAsync(postMigrationUserId));

        // 4. Second migration run — idempotency guards fire (ADD COLUMN IF NOT EXISTS,
        //    CREATE TABLE IF NOT EXISTS, ON CONFLICT DO NOTHING). No errors, no schema
        //    drift, ledger row stays unique.
        await ApplyAsync(S35MigrationDdl);

        Assert.Equal(1, await CountLedgerRowsAsync("s35-d1-users-version-and-audit"));

        // Pre-migration sentinel row's version stays at 1 (idempotency: re-run does NOT
        // re-apply DEFAULT bumps to existing rows).
        Assert.Equal(1L, await ReadUserVersionAsync(sentinelUserId));

        // Columns + table + indexes still present (re-run was a no-op).
        Assert.True(await ColumnExistsAsync("users", "version"));
        Assert.True(await TableExistsAsync("users_audit"));
        Assert.True(await IndexExistsAsync("idx_users_audit_user_id"));
        Assert.True(await IndexExistsAsync("idx_users_audit_at"));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task SeedPreMigrationUserAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@userId, @userId, 'dev-only', @displayName, NULL,
                    'STY_S35_MIG', 'AC', 'OK24', TRUE)
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("displayName", "S35 Migration Test User " + userId);
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

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_name = @table AND table_schema = 'public'
            """, conn);
        cmd.Parameters.AddWithValue("table", tableName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1L;
    }

    private async Task<bool> IndexExistsAsync(string indexName)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_indexes
            WHERE indexname = @indexName AND schemaname = 'public'
            """, conn);
        cmd.Parameters.AddWithValue("indexName", indexName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1L;
    }

    private async Task<long> ReadUserVersionAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT version FROM users WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
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
