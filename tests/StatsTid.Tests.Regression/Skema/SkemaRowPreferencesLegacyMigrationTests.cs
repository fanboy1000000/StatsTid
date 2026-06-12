using Npgsql;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S72 / TASK-7200 — legacy-ALTER idempotence for the
/// <c>s72-skema-row-preferences-schema</c> migration (the S71
/// <see cref="Settlement.Slice3bLegacyMigrationTests"/> harness pattern: instead of
/// embedding a copy of the migration DDL that could drift, this test EXTRACTS the S72
/// segment VERBATIM from the canonical <c>docker/postgres/init.sql</c> between the
/// <c>S72-ROW-PREFERENCES-SEGMENT-BEGIN/END</c> marker lines and runs exactly what
/// production runs).
///
/// <para>
/// Models a PRE-S72 database: the SPRINT-9 <c>projects</c> +
/// <c>user_project_selections</c> shape (no <c>sort_order</c> on the selections; no
/// preference container; no absence selections) carrying LIVE legacy selection rows —
/// including two selections whose projects share an org-level <c>sort_order</c>, the
/// population that proves the R4 "duplicates are expected" backfill posture. Applies the
/// S72 segment twice and verifies the 3-path contract:
/// </para>
///
/// <list type="bullet">
///   <item>First apply (the legacy upgrade): <c>sort_order</c> lands on
///     <c>user_project_selections</c> and every legacy row is backfilled from the
///     MATCHING project's <c>projects.sort_order</c> (duplicates included); the two new
///     preference tables exist and are usable against the legacy <c>users</c> rows; the
///     ledger row lands exactly once.</item>
///   <item>Between applies, an employee "uses the modal": one selection's
///     <c>sort_order</c> is rewritten to a user-chosen value and new preference rows are
///     written.</item>
///   <item>Second apply (re-run): the schema_migrations ledger short-circuits — no
///     errors, no duplicate ledger row, and crucially the ONE-SHOT backfill does NOT
///     re-run (the user-chosen ordering survives; a re-applied init.sql must never
///     clobber preferences chosen since the upgrade).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaRowPreferencesLegacyMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s72-skema-row-preferences-schema";

    // Fixed project ids so the backfill assertions are direct lookups.
    private const string ProjectAlpha = "a72c0001-0000-4000-8000-000000007200"; // org sort_order 10
    private const string ProjectBeta = "a72c0002-0000-4000-8000-000000007200";  // org sort_order 20
    private const string ProjectGamma = "a72c0003-0000-4000-8000-000000007200"; // org sort_order 20 (duplicate of Beta)

    /// <summary>
    /// Pre-S72 baseline. <c>users</c>/<c>organizations</c> are the minimal FK targets (the
    /// S71 precedent shape); <c>projects</c> + <c>user_project_selections</c> mirror the
    /// SPRINT-9 base CREATEs (init.sql "SPRINT 9: Skema tables" section) BEFORE the S72
    /// column — no <c>sort_order</c> on the selections. Live legacy rows: employee A
    /// selected Alpha+Beta (distinct org orders 10/20); employee B selected Beta+Gamma
    /// (both org order 20 — the duplicate-backfill population).
    /// </summary>
    private const string PreS72SchemaDdl = """
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
            VALUES ('STY_S72_MIG', 'S72 Migration Test Org')
            ON CONFLICT (org_id) DO NOTHING;

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

        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id)
            VALUES
                ('emp_s72_mig_a', 'emp_s72_mig_a', 'dev-only', 'S72 Migration Employee A', 'STY_S72_MIG'),
                ('emp_s72_mig_b', 'emp_s72_mig_b', 'dev-only', 'S72 Migration Employee B', 'STY_S72_MIG')
            ON CONFLICT (user_id) DO NOTHING;

        -- SPRINT-9 projects shape (unchanged by S72 — included because the backfill
        -- UPDATE joins it).
        CREATE TABLE IF NOT EXISTS projects (
            project_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            org_id          TEXT        NOT NULL REFERENCES organizations(org_id),
            project_code    TEXT        NOT NULL,
            project_name    TEXT        NOT NULL,
            is_active       BOOLEAN     NOT NULL DEFAULT TRUE,
            sort_order      INT         NOT NULL DEFAULT 0,
            created_by      TEXT        NOT NULL,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (org_id, project_code)
        );
        CREATE INDEX IF NOT EXISTS idx_projects_org ON projects(org_id);

        -- SPRINT-9 user_project_selections shape (pre-S72): NO sort_order column.
        CREATE TABLE IF NOT EXISTS user_project_selections (
            employee_id     TEXT        NOT NULL,
            project_id      UUID        NOT NULL REFERENCES projects(project_id),
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (employee_id, project_id)
        );
        CREATE INDEX IF NOT EXISTS idx_user_project_sel_employee ON user_project_selections(employee_id);

        -- Live legacy rows. Beta and Gamma deliberately SHARE org sort_order 20 so the
        -- backfill produces duplicate per-user values for employee B (R4: expected).
        INSERT INTO projects (project_id, org_id, project_code, project_name, sort_order, created_by) VALUES
            ('a72c0001-0000-4000-8000-000000007200', 'STY_S72_MIG', 'ALPHA', 'Project Alpha', 10, 'seed'),
            ('a72c0002-0000-4000-8000-000000007200', 'STY_S72_MIG', 'BETA',  'Project Beta',  20, 'seed'),
            ('a72c0003-0000-4000-8000-000000007200', 'STY_S72_MIG', 'GAMMA', 'Project Gamma', 20, 'seed');

        INSERT INTO user_project_selections (employee_id, project_id) VALUES
            ('emp_s72_mig_a', 'a72c0001-0000-4000-8000-000000007200'),
            ('emp_s72_mig_a', 'a72c0002-0000-4000-8000-000000007200'),
            ('emp_s72_mig_b', 'a72c0002-0000-4000-8000-000000007200'),
            ('emp_s72_mig_b', 'a72c0003-0000-4000-8000-000000007200');
        """;

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await ApplyAsync(PreS72SchemaDdl);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task Migration_S72_RowPreferences_LegacyAlter_Idempotent()
    {
        var segment = ExtractS72SegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        // The additive column landed; both new preference tables landed.
        Assert.True(await ColumnExistsAsync("user_project_selections", "sort_order"));
        Assert.True(await TableExistsAsync("user_skema_preferences"));
        Assert.True(await TableExistsAsync("user_absence_selections"));

        // Ledger row landed exactly once.
        Assert.Equal(1, await CountLedgerRowsAsync());

        // Backfill correctness: every legacy selection row gained the MATCHING
        // project's org-level sort_order — including the R4 duplicate pair for
        // employee B (Beta and Gamma both 20).
        Assert.Equal(10, await ReadSelectionSortOrderAsync("emp_s72_mig_a", ProjectAlpha));
        Assert.Equal(20, await ReadSelectionSortOrderAsync("emp_s72_mig_a", ProjectBeta));
        Assert.Equal(20, await ReadSelectionSortOrderAsync("emp_s72_mig_b", ProjectBeta));
        Assert.Equal(20, await ReadSelectionSortOrderAsync("emp_s72_mig_b", ProjectGamma));

        // The new tables are USABLE against the legacy users rows (FK targets hold):
        // employee A configures their rows — container + an absence selection.
        await ExecAsync(
            "INSERT INTO user_skema_preferences (employee_id) VALUES ('emp_s72_mig_a')");
        await ExecAsync(
            """
            INSERT INTO user_absence_selections (employee_id, absence_type, sort_order)
            VALUES ('emp_s72_mig_a', 'VACATION', 0), ('emp_s72_mig_a', 'SICK', 1)
            """);

        // ── Between applies: employee A reorders in the modal ────────────────
        // A user-chosen ordering the one-shot backfill must NEVER overwrite on re-apply.
        await ExecAsync(
            """
            UPDATE user_project_selections SET sort_order = 999
            WHERE employee_id = 'emp_s72_mig_a' AND project_id = 'a72c0001-0000-4000-8000-000000007200'
            """);

        // ── Second apply: ledger-guarded no-op ───────────────────────────────
        await ApplyAsync(segment);

        Assert.Equal(1, await CountLedgerRowsAsync());

        // The one-shot backfill did NOT re-run: the user-chosen 999 survives
        // (a re-backfill would have reset it to Alpha's org order 10).
        Assert.Equal(999, await ReadSelectionSortOrderAsync("emp_s72_mig_a", ProjectAlpha));

        // Everything else untouched by the re-run.
        Assert.Equal(20, await ReadSelectionSortOrderAsync("emp_s72_mig_a", ProjectBeta));
        Assert.Equal(20, await ReadSelectionSortOrderAsync("emp_s72_mig_b", ProjectBeta));
        Assert.Equal(20, await ReadSelectionSortOrderAsync("emp_s72_mig_b", ProjectGamma));
        Assert.Equal(1, Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = 'emp_s72_mig_a'")));
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM user_absence_selections WHERE employee_id = 'emp_s72_mig_a'")));
    }

    // ─── segment extraction ─────────────────────────────────────────────────

    private const string SegmentBeginMarker = "-- S72-ROW-PREFERENCES-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S72-ROW-PREFERENCES-SEGMENT-END";

    /// <summary>
    /// Locates the canonical <c>docker/postgres/init.sql</c> (the
    /// <c>StatsTidWebApplicationFactory.LocateInitSql</c> walk-up; the csproj copies
    /// init.sql alongside the test bin) and extracts the S72 segment between the marker
    /// lines — the test runs EXACTLY what production runs, so the migration under test
    /// can never drift from an embedded copy.
    /// </summary>
    private static string ExtractS72SegmentFromCanonicalInitSql()
    {
        var initSql = File.ReadAllText(LocateInitSql());

        var begin = initSql.IndexOf(SegmentBeginMarker, StringComparison.Ordinal);
        var end = initSql.IndexOf(SegmentEndMarker, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"init.sql is missing the '{SegmentBeginMarker}' marker line.");
        Assert.True(end > begin, $"init.sql is missing the '{SegmentEndMarker}' marker line after BEGIN.");

        var segment = initSql.Substring(begin + SegmentBeginMarker.Length, end - begin - SegmentBeginMarker.Length);
        Assert.Contains(MigrationId, segment);
        return segment;
    }

    private static string LocateInitSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docker", "postgres", "init.sql");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate docker/postgres/init.sql by walking up from " +
            $"AppContext.BaseDirectory='{AppContext.BaseDirectory}'.");
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task ApplyAsync(string ddl)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task ExecAsync(string sql) => ApplyAsync(sql);

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        return Convert.ToInt64(await ScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = @p0 AND column_name = @p1
            """, tableName, columnName)) == 1L;
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        return Convert.ToInt64(await ScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_name = @p0 AND table_schema = 'public'
            """, tableName)) == 1L;
    }

    private async Task<int> CountLedgerRowsAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @p0", MigrationId));
    }

    private async Task<int> ReadSelectionSortOrderAsync(string employeeId, string projectId)
    {
        var result = await ScalarAsync(
            """
            SELECT sort_order FROM user_project_selections
            WHERE employee_id = @p0 AND project_id = @p1::uuid
            """, employeeId, projectId);
        Assert.NotNull(result);
        return Convert.ToInt32(result);
    }

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }
}
