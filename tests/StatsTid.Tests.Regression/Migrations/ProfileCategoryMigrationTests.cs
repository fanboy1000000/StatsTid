using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S137 / TASK-13704 — migration pins for the <c>s137-profile-category-dating</c> segment
/// (ADR-040 D4: <c>employee_profiles.employment_category</c> becomes a DATED column so
/// "what category was this employee in March?" is answerable). The S71/S72/S73/S136 harness
/// pattern: the LEGACY facts EXTRACT the S137 segment VERBATIM from the canonical
/// <c>docker/postgres/init.sql</c> between the <c>S137-PROFILE-CATEGORY-SEGMENT-BEGIN/END</c>
/// marker lines (via <see cref="CanonicalInitSql"/>, the S133/QUAL-014 single source of
/// truth) and run exactly what production runs against a reconstructed pre-S137 schema;
/// the GREENFIELD fact applies the FULL canonical init.sql twice.
///
/// <para>The S137 pins under test:</para>
/// <list type="bullet">
///   <item>clean legacy DB — the column lands NULLABLE (no CHECK — the ruled fail-safe
///     posture: reads COALESCE to <c>users.employment_category</c>, so NULL degrades to the
///     live value; the NOT-NULL tightening is Increment 3) and the backfill is
///     HISTORY-COVERING: closed predecessor rows (<c>effective_to IS NOT NULL</c> — they
///     exist by construction of <c>idx_employee_profiles_history</c>) receive the owner's
///     live <c>users.employment_category</c> exactly like the live row does. Copying live
///     onto history is CORRECT, not approximate: users' category is write-once until
///     Increment 3, so the live value IS the value that held over every historical
///     window;</item>
///   <item>double-apply idempotence — the <c>schema_migrations</c> ledger short-circuits the
///     second apply (exactly one ledger row; no value is overwritten);</item>
///   <item>greenfield — the full init.sql applies twice (the file-scope ADD COLUMN after the
///     base CREATE + the ledger-guarded segment converge; the backfill no-ops because
///     init.sql deliberately does NOT pre-seed employee_profiles — the app-boot seeder owns
///     that, category included) and the column exists nullable on the real table.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileCategoryMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s137-profile-category-dating";

    private const string SegmentBeginMarker = "-- S137-PROFILE-CATEGORY-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S137-PROFILE-CATEGORY-SEGMENT-END";

    /// <summary>
    /// Pre-S137 baseline: <c>schema_migrations</c> + a minimal <c>organizations</c> stub
    /// (satisfies the <c>users.primary_org_id</c> FK, the S35/S136 precedent) + <c>users</c>
    /// with the LIVE <c>employment_category</c> column + <c>employee_profiles</c> in its
    /// pre-S137 shape — NO <c>employment_category</c> column, but BOTH house unique indexes
    /// (the history index is what guarantees closed predecessor rows exist to backfill).
    ///
    /// Seed rows cover the backfill matrix: a NON-default category user with a closed
    /// HISTORY row + a live row (the history-covering half — the load-bearing pin), and a
    /// default-category user with a live row only (proves the backfill copies per-employee
    /// values, not one constant).
    /// </summary>
    private const string PreS137SchemaDdl = """
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
            VALUES ('STY_S137_MIG', 'S137 Migration Test Org')
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
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_profiles_live
            ON employee_profiles (employee_id)
            WHERE effective_to IS NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_profiles_history
            ON employee_profiles (employee_id, effective_from);

        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id,
                           employment_category) VALUES
            ('emp_s137_fm',  'emp_s137_fm',  'x', 'Fuldmaegtig with history', 'STY_S137_MIG', 'Fuldmægtig'),
            ('emp_s137_std', 'emp_s137_std', 'x', 'Standard live-only',       'STY_S137_MIG', 'Standard');

        -- emp_s137_fm: a CLOSED history row + the live successor (the supersession shape the
        -- history unique index exists for). emp_s137_std: live row only.
        INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to) VALUES
            ('emp_s137_fm', 1.000, '2025-01-01', '2026-03-01');
        INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to) VALUES
            ('emp_s137_fm', 0.800, '2026-03-01', NULL);
        INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to) VALUES
            ('emp_s137_std', 1.000, '2025-06-01', NULL);
        """;

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // BARE harness only — each fact applies the schema it needs (the legacy facts the
        // pre-S137 DDL + the extracted segment; the greenfield fact the FULL canonical
        // init.sql). Mirrors EmploymentWindowCheckMigrationTests.
        _harness = await TestFixtures.DockerHarness.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // LEGACY path — the extracted segment against a reconstructed pre-S137 schema.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clean legacy DB: the column lands nullable, the backfill covers EVERY row — the
    /// closed HISTORY row included (the load-bearing S137 pin: the history unique index
    /// means closed predecessors exist, and each must answer "what category then?") — with
    /// the OWNER's per-employee live category, and a second apply is a ledger-guarded no-op
    /// that overwrites nothing.
    /// </summary>
    [Fact]
    public async Task Migration_S137_LegacySegment_BackfillCoversHistoryRows_DoubleApplyIdempotent()
    {
        await ApplyAsync(PreS137SchemaDdl);
        Assert.False(await ColumnExistsAsync()); // baseline sanity: pre-S137 has no column

        var segment = ExtractS137SegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        Assert.True(await ColumnExistsAsync());
        Assert.True(await ColumnIsNullableAsync()); // NULLABLE by ruled design (no CHECK)
        Assert.Equal(1, await CountLedgerRowsAsync());

        // HISTORY-COVERING: the closed predecessor row carries the owner's live category.
        Assert.Equal("Fuldmægtig", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s137_fm' AND effective_to IS NOT NULL
            """));
        // ...and the live successor does too (dated == live after backfill).
        Assert.Equal("Fuldmægtig", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s137_fm' AND effective_to IS NULL
            """));
        // Per-employee copy, not one constant: the second user's row got ITS user's value.
        Assert.Equal("Standard", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s137_std'
            """));
        // Census: zero NULLs remain anywhere.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM employee_profiles WHERE employment_category IS NULL"));

        // ── Second apply: ledger-guarded no-op, nothing overwritten ──────────
        // Diverge one dated cell first: if the second apply re-ran the backfill
        // (or worse, an unconditional UPDATE), this sentinel would be clobbered.
        await ApplyAsync(
            """
            UPDATE employee_profiles SET employment_category = 'S137Sentinel'
            WHERE employee_id = 'emp_s137_std'
            """);

        await ApplyAsync(segment);
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal("S137Sentinel", await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = 'emp_s137_std'
            """));
    }

    // ════════════════════════════════════════════════════════════════════════
    // GREENFIELD path — the FULL canonical init.sql, applied twice.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The full canonical init.sql succeeding AT ALL proves the file-scope ADD COLUMN and
    /// the guarded segment coexist; the second apply proves the ledger short-circuit is
    /// idempotent alongside the (idempotent) file-scope ALTER. The backfill no-ops on
    /// greenfield BY DESIGN — init.sql does not pre-seed employee_profiles (the app-boot
    /// EmployeeProfileSeeder owns that, category included), pinned by the zero-row census.
    /// </summary>
    [Fact]
    public async Task Migration_S137_Greenfield_FullInitSql_DoubleApply_ColumnPresentNullable()
    {
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.True(await ColumnExistsAsync());
        Assert.True(await ColumnIsNullableAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());

        // Second apply (the standing down -v && up re-run): file-scope ALTER no-ops, the
        // segment short-circuits on the ledger.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.True(await ColumnExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());

        // Greenfield init.sql seeds no employee_profiles rows — the backfill had nothing to
        // do; the app-boot seeder is the greenfield populator (pinned in
        // ProfileCategoryDatingTests' census fact).
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM employee_profiles"));
    }

    // ─── segment extraction (markers verbatim; CanonicalInitSql per S133/QUAL-014) ───

    private static string ExtractS137SegmentFromCanonicalInitSql()
    {
        var initSql = CanonicalInitSql.Read();

        var begin = initSql.IndexOf(SegmentBeginMarker, StringComparison.Ordinal);
        var end = initSql.IndexOf(SegmentEndMarker, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"init.sql is missing the '{SegmentBeginMarker}' marker line.");
        Assert.True(end > begin, $"init.sql is missing the '{SegmentEndMarker}' marker line after BEGIN.");

        var segment = initSql.Substring(begin + SegmentBeginMarker.Length, end - begin - SegmentBeginMarker.Length);
        Assert.Contains(MigrationId, segment);
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

    private async Task<bool> ColumnExistsAsync()
    {
        return Convert.ToInt64(await ScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = 'employee_profiles' AND column_name = 'employment_category'
            """)) == 1L;
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
