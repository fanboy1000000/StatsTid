using Npgsql;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S43 / TASK-4306 — Phase E Test #6 (Legacy migration safety).
///
/// <para>
/// Added per Step 0b cycle 1 Codex BLOCKER B3 absorption. Earlier draft
/// verified greenfield init only via psql <c>\d audit_projection</c>;
/// the legacy upgrade path (deploy against an existing DB without
/// audit_projection) was unvalidated. S35 cycle-2 absorption showed that
/// ledger/guard ordering matters for legacy upgrades — this test pins the
/// invariant.
/// </para>
///
/// <para>
/// Pre-S43 baseline: audit_projection table absent + ledger entry absent.
/// Apply the schema block twice. Assert table + all 5 indexes + the CHECK
/// constraint + ledger entry <c>s43-d1-audit-projection-table</c> are
/// present exactly once after both applications (no duplicate indexes,
/// no duplicate ledger entry, no CHECK constraint errors). Idempotency
/// is guaranteed by <c>CREATE TABLE IF NOT EXISTS</c> + <c>CREATE INDEX IF
/// NOT EXISTS</c> + <c>INSERT ... ON CONFLICT DO NOTHING</c> on the ledger.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionLegacyMigrationTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    // Mirror of init.sql:2008-2086 (S43 / ADR-026 D1) — same DDL, plus the
    // ledger insert block. Test schema fixture includes the FK target
    // (organizations) so the audit_projection FK validates.
    private const string AuditProjectionMigrationBlock = """
        CREATE TABLE IF NOT EXISTS audit_projection (
            projection_id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
            event_id                 UUID         NOT NULL UNIQUE,
            outbox_id                BIGINT       NOT NULL,
            event_type               TEXT         NOT NULL,
            visibility_scope         TEXT         NOT NULL CHECK (visibility_scope IN ('TENANT_TARGETED', 'GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY')),
            target_org_id            TEXT         NULL REFERENCES organizations(org_id),
            target_resource_id       TEXT         NULL,
            actor_id                 TEXT         NULL,
            actor_primary_org_id     TEXT         NULL,
            occurred_at              TIMESTAMPTZ  NOT NULL,
            correlation_id           UUID         NULL,
            details                  JSONB        NOT NULL,
            projected_at             TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            CONSTRAINT chk_target_org_required_when_tenant
                CHECK (
                    (visibility_scope = 'TENANT_TARGETED'      AND target_org_id IS NOT NULL) OR
                    (visibility_scope IN ('GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY'))
                )
        );

        CREATE INDEX IF NOT EXISTS idx_audit_projection_target_org_time
            ON audit_projection (target_org_id, occurred_at DESC)
            WHERE target_org_id IS NOT NULL;

        CREATE INDEX IF NOT EXISTS idx_audit_projection_global_visible
            ON audit_projection (occurred_at DESC)
            WHERE visibility_scope = 'GLOBAL_TENANT_VISIBLE';

        CREATE INDEX IF NOT EXISTS idx_audit_projection_actor_org_time
            ON audit_projection (actor_primary_org_id, occurred_at DESC)
            WHERE actor_primary_org_id IS NOT NULL;

        CREATE INDEX IF NOT EXISTS idx_audit_projection_event_type_time
            ON audit_projection (event_type, occurred_at DESC);

        CREATE INDEX IF NOT EXISTS idx_audit_projection_outbox_id
            ON audit_projection (outbox_id);

        DO $$
        BEGIN
            INSERT INTO schema_migrations (migration_id, notes)
            VALUES ('s43-d1-audit-projection-table', 'ADR-026 D1: audit_projection table + 5 partial indexes + chk_target_org_required_when_tenant CHECK; Sub-Sprint 1 plumbing per path C event-projection')
            ON CONFLICT (migration_id) DO NOTHING;
        END
        $$;
        """;

    // Pre-S43 baseline: schema_migrations + organizations (FK target),
    // but NO audit_projection.
    private const string PreS43BaselineDdl = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );

        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL,
            parent_org_id       TEXT        REFERENCES organizations(org_id),
            materialized_path   TEXT        NOT NULL,
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();

        // Drop anything DockerHarness initialized that would interfere,
        // then apply the pre-S43 baseline only (deliberately omits
        // audit_projection).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var dropCmd = new NpgsqlCommand(
            "DROP TABLE IF EXISTS audit_projection CASCADE", conn);
        await dropCmd.ExecuteNonQueryAsync();
        await using var baselineCmd = new NpgsqlCommand(PreS43BaselineDdl, conn);
        await baselineCmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Apply the migration block once → assert table + all 5 indexes + CHECK
    /// + ledger entry land. Apply a second time → assert NO duplicate
    /// indexes, NO duplicate ledger entry, NO errors raised.
    /// </summary>
    [Fact]
    public async Task LegacyUpgrade_AppliedTwice_IsIdempotent()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Sanity check: audit_projection does NOT exist pre-migration.
        Assert.Equal(0L, await CountTableAsync(conn, "audit_projection"));

        // Apply 1 — table + indexes + ledger entry land.
        await using (var cmd = new NpgsqlCommand(AuditProjectionMigrationBlock, conn))
            await cmd.ExecuteNonQueryAsync();

        Assert.Equal(1L, await CountTableAsync(conn, "audit_projection"));
        Assert.Equal(5L, await CountIndexesAsync(conn, "audit_projection", "idx_audit_projection_"));
        Assert.True(await ConstraintExistsAsync(conn, "chk_target_org_required_when_tenant"));
        Assert.Equal(1L, await CountLedgerEntryAsync(conn, "s43-d1-audit-projection-table"));

        // Apply 2 — must be idempotent (no exceptions; no duplicates).
        await using (var cmd = new NpgsqlCommand(AuditProjectionMigrationBlock, conn))
            await cmd.ExecuteNonQueryAsync();

        Assert.Equal(1L, await CountTableAsync(conn, "audit_projection"));
        Assert.Equal(5L, await CountIndexesAsync(conn, "audit_projection", "idx_audit_projection_"));
        Assert.True(await ConstraintExistsAsync(conn, "chk_target_org_required_when_tenant"));
        Assert.Equal(1L, await CountLedgerEntryAsync(conn, "s43-d1-audit-projection-table"));
    }

    private static async Task<long> CountTableAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @t", conn);
        cmd.Parameters.AddWithValue("t", tableName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<long> CountIndexesAsync(NpgsqlConnection conn, string tableName, string prefix)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM pg_indexes
              WHERE tablename = @t AND indexname LIKE @p", conn);
        cmd.Parameters.AddWithValue("t", tableName);
        cmd.Parameters.AddWithValue("p", prefix + "%");
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> ConstraintExistsAsync(NpgsqlConnection conn, string constraintName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_name = @c", conn);
        cmd.Parameters.AddWithValue("c", constraintName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<long> CountLedgerEntryAsync(NpgsqlConnection conn, string migrationId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @m", conn);
        cmd.Parameters.AddWithValue("m", migrationId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
