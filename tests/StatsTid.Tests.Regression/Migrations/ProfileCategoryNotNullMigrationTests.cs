using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S138 / TASK-13804 — migration pins for the <c>s138-profile-category-not-null</c> segment
/// (ADR-040 D4, Increment 3: <c>employee_profiles.employment_category</c> is tightened to
/// NOT NULL and the S137 <c>COALESCE(dated, live)</c> read fail-safe is retired). The
/// S71/S72/S73/S136/S137 harness pattern: the LEGACY facts EXTRACT the S138 segment VERBATIM
/// from the canonical <c>docker/postgres/init.sql</c> between the
/// <c>S138-PROFILE-CATEGORY-NOTNULL-SEGMENT-BEGIN/END</c> marker lines (via
/// <see cref="CanonicalInitSql"/>, the S133/QUAL-014 single source of truth) and run exactly
/// what production runs against a reconstructed pre-S138 (= post-S137) schema; the GREENFIELD
/// fact applies the FULL canonical init.sql twice.
///
/// <para>
/// <b>Why the posture flips (the pin's whole point).</b> S137 landed the dated category column
/// NULLABLE on purpose: only the four INSERT paths had been taught to fill it, so a missed
/// write degraded to the live <c>users.employment_category</c> — always the right answer then,
/// because that column was write-once and dated == live held by construction. S138 makes the
/// category EDITABLE per date: a backdated change is a NEW dated row and the live column
/// becomes merely the CACHE of the row covering TODAY. Falling back to it would now MISLABEL
/// history instead of rescuing it, so the column becomes NOT NULL and a missed write fails at
/// INSERT (23502) — the house fail-loud default.
/// </para>
///
/// <para>The S138 pins under test (RED-first from the spec, not from observed behaviour):</para>
/// <list type="bullet">
///   <item>clean legacy DB (every row valued) — the column is tightened to NOT NULL, a NULL
///     write is rejected 23502 from then on, and NO existing value is touched: a history row
///     deliberately DIVERGED from its user's live category survives byte-identical (the
///     Increment-3 write-once precondition — a category change is a new dated row, never an
///     edit of a migrated one, and this segment carries no UPDATE at all);</item>
///   <item>double-apply idempotence — the <c>schema_migrations</c> ledger short-circuits the
///     second apply (exactly one ledger row; the column stays NOT NULL);</item>
///   <item><b>the RED path</b> — a legacy DB that still holds a NULL category row makes the
///     census RAISE (P0001) NAMING the offending profile_id; the column stays NULLABLE, the
///     ledger row rolls back WITH the exception, and after the operator values the named row a
///     plain re-run executes the segment in full (fix-then-rerun, never
///     fix-then-unstick-the-ledger — the S136 employment-window precedent, deliberately unlike
///     the S137 category segment, which ran no census because the COALESCE read absorbed a
///     NULL);</item>
///   <item>greenfield — the full init.sql applies twice and the column is NOT NULL from the
///     file-scope ADD COLUMN (employee_profiles is empty when init.sql runs; the app-boot
///     EmployeeProfileSeeder populates it, category included).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileCategoryNotNullMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s138-profile-category-not-null";

    private const string SegmentBeginMarker = "-- S138-PROFILE-CATEGORY-NOTNULL-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S138-PROFILE-CATEGORY-NOTNULL-SEGMENT-END";

    /// <summary>The profile_id the RED path expects to see NAMED in the census RAISE.</summary>
    private const string NullRowProfileId = "0f5c1d38-5b2a-4a71-9f1e-13804d000001";

    /// <summary>
    /// Pre-S138 baseline = the POST-S137 shape: <c>schema_migrations</c> + a minimal
    /// <c>organizations</c> stub (satisfies the <c>users.primary_org_id</c> FK, the S35/S136/S137
    /// precedent) + <c>users</c> with the LIVE <c>employment_category</c> + <c>employee_profiles</c>
    /// carrying the S137 column NULLABLE, both house unique indexes, and the S137 ledger row
    /// already recorded (a database that has been through S137 is exactly what this segment
    /// upgrades).
    ///
    /// <para>
    /// Seed rows are the S138 world, not the S137 one: the closed HISTORY row is deliberately
    /// DIVERGED from its user's live category ('Fuldmægtig' in history, 'Chefkonsulent' live) —
    /// the shape a backdated category change leaves behind. Under S137's dated == live invariant
    /// that could not happen; pinning it here is what makes "the segment rewrites no history"
    /// falsifiable rather than vacuous.
    /// </para>
    /// </summary>
    private const string PreS138SchemaDdl = """
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
            VALUES ('STY_S138_NN', 'S138 NOT-NULL Migration Test Org')
            ON CONFLICT (org_id) DO NOTHING;

        CREATE TABLE IF NOT EXISTS users (
            user_id                 TEXT        PRIMARY KEY,
            username                TEXT        NOT NULL UNIQUE,
            password_hash           TEXT        NOT NULL,
            display_name            TEXT        NOT NULL,
            email                   TEXT,
            primary_org_id          TEXT        NOT NULL REFERENCES organizations(org_id),
            agreement_code          TEXT        NOT NULL DEFAULT 'AC',
            ok_version              TEXT        NOT NULL DEFAULT 'OK24',
            employment_category     TEXT        NOT NULL DEFAULT 'Standard',
            is_active               BOOLEAN     NOT NULL DEFAULT TRUE,
            version                 BIGINT      NOT NULL DEFAULT 1,
            created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS employee_profiles (
            profile_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id         TEXT        NOT NULL REFERENCES users(user_id),
            part_time_fraction  NUMERIC(4,3) NOT NULL DEFAULT 1.000,
            position            TEXT        NULL,
            effective_from      DATE        NOT NULL DEFAULT '0001-01-01',
            effective_to        DATE        NULL,
            version             BIGINT      NOT NULL DEFAULT 1,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            employment_category TEXT        NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_profiles_live
            ON employee_profiles (employee_id)
            WHERE effective_to IS NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_profiles_history
            ON employee_profiles (employee_id, effective_from);

        INSERT INTO schema_migrations (migration_id, notes)
            VALUES ('s137-profile-category-dating', 'test baseline: this DB has been through S137')
            ON CONFLICT (migration_id) DO NOTHING;

        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id,
                           employment_category) VALUES
            ('emp_s138_nn_hist', 'emp_s138_nn_hist', 'x', 'Promoted employee', 'STY_S138_NN', 'Chefkonsulent'),
            ('emp_s138_nn_std',  'emp_s138_nn_std',  'x', 'Live-only employee', 'STY_S138_NN', 'Standard');

        -- emp_s138_nn_hist: a CLOSED history row whose dated category DIVERGES from the live
        -- users value (what a backdated category change leaves behind) + the live successor.
        INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, employment_category) VALUES
            ('emp_s138_nn_hist', 1.000, '2025-01-01', '2026-03-01', 'Fuldmægtig');
        INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, employment_category) VALUES
            ('emp_s138_nn_hist', 0.800, '2026-03-01', NULL,         'Chefkonsulent');
        INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, employment_category) VALUES
            ('emp_s138_nn_std',  1.000, '2025-06-01', NULL,         'Standard');
        """;

    /// <summary>
    /// The RED-path seed: ONE profile row that no known write path can explain — a NULL dated
    /// category (a fixed profile_id so the census RAISE can be asserted to NAME it).
    /// </summary>
    private const string NullCategoryRowDdl = $"""
        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id,
                           employment_category)
            VALUES ('emp_s138_nn_null', 'emp_s138_nn_null', 'x', 'Row with no category',
                    'STY_S138_NN', 'Specialkonsulent');

        INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction,
                                       effective_from, effective_to, employment_category)
            VALUES ('{NullRowProfileId}', 'emp_s138_nn_null', 1.000, '2025-02-01', NULL, NULL);
        """;

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // BARE harness only — each fact applies the schema it needs (the legacy facts the
        // pre-S138 DDL + the extracted segment; the greenfield fact the FULL canonical
        // init.sql). Mirrors ProfileCategoryMigrationTests / EmploymentWindowCheckMigrationTests.
        _harness = await TestFixtures.DockerHarness.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // LEGACY path — the extracted segment against a reconstructed pre-S138 schema.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clean legacy DB (the S137 backfill has valued every row): the census passes, the column
    /// is tightened to NOT NULL, a NULL write is refused 23502 from then on, and NOTHING stored
    /// is rewritten — the diverged history row keeps its own value. A second apply is a
    /// ledger-guarded no-op.
    /// </summary>
    [Fact]
    public async Task Migration_S138_LegacySegment_ValuedRows_TightensToNotNull_NoHistoryRewrite_DoubleApplyIdempotent()
    {
        await ApplyAsync(PreS138SchemaDdl);
        Assert.True(await ColumnIsNullableAsync()); // baseline sanity: post-S137 = nullable
        Assert.Equal(0, await CountLedgerRowsAsync());

        var segment = ExtractS138NotNullSegmentFromCanonicalInitSql();

        // ── First apply: the legacy tightening path ──────────────────────────
        await ApplyAsync(segment);

        Assert.False(await ColumnIsNullableAsync()); // the whole point of the segment
        Assert.Equal(1, await CountLedgerRowsAsync());

        // The segment carries NO UPDATE: the history row diverged from its user's live
        // category survives byte-identical (Increment 3's write-once precondition — a
        // category change is a NEW dated row, never an edit of a migrated one).
        Assert.Equal("Fuldmægtig", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s138_nn_hist' AND effective_to IS NOT NULL
            """));
        Assert.Equal("Chefkonsulent", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s138_nn_hist' AND effective_to IS NULL
            """));
        Assert.Equal("Standard", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s138_nn_std'
            """));

        // A write that forgets the category now FAILS AT INSERT (23502 not_null_violation)
        // instead of degrading to the live users value at read time — the retired fail-safe.
        await AssertSqlStateAsync("23502", () => ApplyAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from)
            VALUES ('emp_s138_nn_std', 1.000, '2027-01-01')
            """));

        // ── Second apply: ledger-guarded no-op ───────────────────────────────
        await ApplyAsync(segment);
        Assert.False(await ColumnIsNullableAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal("Fuldmægtig", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s138_nn_hist' AND effective_to IS NOT NULL
            """));
    }

    /// <summary>
    /// RED path — a legacy DB that still holds a NULL category row: the census FAILS LOUD
    /// (P0001) naming the offending profile_id, the column stays NULLABLE, and the ledger row
    /// rolls back WITH the RAISE, so after the operator values the named row a plain re-run
    /// executes the segment in full. NO auto-repair: which category held over that row's window
    /// is business history (the S136 deviation from S73's deterministic remediate-then-constrain),
    /// and guessing "the live value" is exactly the mislabel this task removes.
    /// </summary>
    [Fact]
    public async Task Migration_S138_LegacySegment_NullCategoryRow_RaisesNamingProfileId_ChangesNothing_RerunsAfterFix()
    {
        await ApplyAsync(PreS138SchemaDdl);
        await ApplyAsync(NullCategoryRowDdl);

        var segment = ExtractS138NotNullSegmentFromCanonicalInitSql();

        // ── The census raises: operator-actionable, the row NAMED, nothing changed ──
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(segment));
        Assert.Equal("P0001", ex.SqlState); // raise_exception — the census, not a raw 23502
        Assert.Contains(MigrationId, ex.MessageText, StringComparison.Ordinal);
        Assert.Contains(NullRowProfileId, ex.MessageText, StringComparison.Ordinal);

        // The column was NOT tightened...
        Assert.True(await ColumnIsNullableAsync());
        // ...and the ledger INSERT rolled back with the RAISE (fix-then-rerun, never
        // fix-then-unstick-the-ledger).
        Assert.Equal(0, await CountLedgerRowsAsync());

        // ── The operator values the named row, then re-runs ──────────────────
        await ApplyAsync(
            $"""
            UPDATE employee_profiles SET employment_category = 'Specialkonsulent'
            WHERE profile_id = '{NullRowProfileId}'
            """);

        await ApplyAsync(segment);
        Assert.False(await ColumnIsNullableAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        await AssertSqlStateAsync("23502", () => ApplyAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from)
            VALUES ('emp_s138_nn_null', 1.000, '2027-02-01')
            """));
    }

    // ════════════════════════════════════════════════════════════════════════
    // GREENFIELD path — the FULL canonical init.sql, applied twice.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Greenfield: the column is NOT NULL from the file-scope ADD COLUMN after the base CREATE
    /// (employee_profiles is EMPTY when init.sql runs — the app-boot EmployeeProfileSeeder is
    /// the greenfield populator, category included), and the guarded segment coexists with it:
    /// the census sees zero rows and SET NOT NULL is a no-op on an already-NOT-NULL column. The
    /// second apply proves the ledger short-circuit and the idempotent file-scope ALTER agree.
    /// </summary>
    [Fact]
    public async Task Migration_S138_Greenfield_FullInitSql_DoubleApply_ColumnNotNull()
    {
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.False(await ColumnIsNullableAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        // init.sql seeds no employee_profiles rows — the census had nothing to inspect.
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM employee_profiles"));

        // Second apply (the standing down -v && up re-run).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.False(await ColumnIsNullableAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
    }

    // ─── segment extraction (markers verbatim; CanonicalInitSql per S133/QUAL-014) ───

    private static string ExtractS138NotNullSegmentFromCanonicalInitSql()
    {
        var initSql = CanonicalInitSql.Read();

        var begin = initSql.IndexOf(SegmentBeginMarker, StringComparison.Ordinal);
        var end = initSql.IndexOf(SegmentEndMarker, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"init.sql is missing the '{SegmentBeginMarker}' marker line.");
        Assert.True(end > begin, $"init.sql is missing the '{SegmentEndMarker}' marker line after BEGIN.");

        var segment = initSql.Substring(begin + SegmentBeginMarker.Length, end - begin - SegmentBeginMarker.Length);
        Assert.Contains(MigrationId, segment, StringComparison.Ordinal);

        // Ordering pin: the NOT-NULL segment must sit AFTER the S137 segment that lands and
        // backfills the column (otherwise the census would raise on rows S137 is about to
        // fill) and after the S138 worklist segment.
        var s137End = initSql.IndexOf("-- S137-PROFILE-CATEGORY-SEGMENT-END", StringComparison.Ordinal);
        var worklistEnd = initSql.IndexOf("-- S138-BACKDATE-WORKLIST-SEGMENT-END", StringComparison.Ordinal);
        Assert.True(s137End >= 0 && begin > s137End,
            "The S138 NOT-NULL segment must be placed AFTER the S137 column+backfill segment.");
        Assert.True(worklistEnd >= 0 && begin > worklistEnd,
            "The S138 NOT-NULL segment must be placed AFTER the S138 backdate-worklist segment.");

        return segment;
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task ApplyAsync(string ddl)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the DDL is either a marker-extracted verbatim
        // init.sql segment or a compile-time test constant — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(ddl, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task AssertSqlStateAsync(string expectedSqlState, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(act);
        Assert.Equal(expectedSqlState, ex.SqlState);
    }

    private async Task<bool> ColumnIsNullableAsync()
    {
        return string.Equals("YES", (string?)await ScalarAsync(
            """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_name = 'employee_profiles' AND column_name = 'employment_category'
            """), StringComparison.Ordinal);
    }

    private async Task<int> CountLedgerRowsAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @p0", MigrationId));
    }

    private async Task<int> CountAsync(string sql)
        => Convert.ToInt32(await ScalarAsync(sql));

    private async Task<string?> ScalarStringAsync(string sql)
        => (string?)await ScalarAsync(sql);

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }
}
