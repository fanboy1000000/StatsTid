using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S136 / TASK-13601 — migration pins for the <c>s136-employment-window-check</c> segment
/// (ADR-040 D1 data-layer backstop: <c>users_employment_window_check</c> — an employment
/// window may never end before it starts; the NULL disjuncts are ADR-040 D2's
/// "NULL means unbounded" rule in SQL). The S71/S72/S73 harness pattern
/// (<see cref="Skema.FullDayOnlyMigrationTests"/>): the LEGACY facts EXTRACT the S136 segment
/// VERBATIM from the canonical <c>docker/postgres/init.sql</c> between the
/// <c>S136-EMPLOYMENT-WINDOW-CHECK-SEGMENT-BEGIN/END</c> marker lines (via
/// <see cref="CanonicalInitSql"/>, the S133/QUAL-014 single source of truth) and run exactly
/// what production runs against a reconstructed pre-S136 schema; the GREENFIELD fact applies
/// the FULL canonical init.sql twice.
///
/// <para>The S136 pins under test:</para>
/// <list type="bullet">
///   <item>clean legacy DB — the census passes over the full D2 NULL-unbounded matrix
///     (both-NULL / start-only / end-only / start &lt; end / start = end one-day window),
///     the named CHECK lands, and a violating write is rejected (23514) from then on;</item>
///   <item>double-apply idempotence — the <c>schema_migrations</c> ledger short-circuits
///     the second apply (exactly one ledger row, constraint still present);</item>
///   <item>violating legacy DB — the census FAILS LOUD (P0001) naming EVERY violating
///     user_id, the constraint is NOT added, and the ledger row ROLLS BACK with the RAISE
///     so an operator's fix-then-rerun executes the segment again in full (NO auto-repair:
///     which employment boundary is wrong is business history an operator must decide —
///     the deliberate S136 deviation from S73's deterministic remediate-then-constrain);</item>
///   <item>greenfield — the full init.sql applies twice (the census never rejects
///     init.sql's own seeds; the unconditional post-segment re-land ALTER is idempotent)
///     and the constraint enforces on the real seeded users table.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentWindowCheckMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s136-employment-window-check";
    private const string CheckConstraintName = "users_employment_window_check";

    private const string SegmentBeginMarker = "-- S136-EMPLOYMENT-WINDOW-CHECK-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S136-EMPLOYMENT-WINDOW-CHECK-SEGMENT-END";

    /// <summary>
    /// Pre-S136 baseline: <c>schema_migrations</c> + a minimal <c>organizations</c> stub
    /// (satisfies the <c>users.primary_org_id</c> FK, the S35 precedent) + the post-S70
    /// <c>users</c> shape — BOTH employment columns present (start via the S60 file-scope
    /// ALTER, end via the s70 guarded block), NO window CHECK. <c>unit_id</c> is elided
    /// (it would drag in the <c>units</c> table and the segment never touches it).
    ///
    /// Seed rows cover the FULL ADR-040 D2 matrix the census + CHECK must pass through:
    /// both-NULL (unbounded), start-only (open-ended), end-only (employed since the
    /// beginning of time), start &lt; end (a real window), and start = end (a legal
    /// ONE-DAY employment — end date is the LAST day employed, inclusive, per ADR-033 /
    /// S70 R1; this pins the <c>&gt;=</c> in the CHECK).
    /// </summary>
    private const string PreS136SchemaDdl = """
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
            VALUES ('STY_S136_MIG', 'S136 Migration Test Org')
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
            birth_date              DATE        NULL,
            employment_start_date   DATE        NULL,
            employment_end_date     DATE        NULL,
            end_date_deactivated    BOOLEAN     NOT NULL DEFAULT FALSE,
            version                 BIGINT      NOT NULL DEFAULT 1,
            created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id,
                           employment_start_date, employment_end_date) VALUES
            ('emp_unbounded',  'emp_unbounded',  'x', 'Both NULL (D2 unbounded)', 'STY_S136_MIG', NULL,         NULL),
            ('emp_start_only', 'emp_start_only', 'x', 'Start only (open-ended)',  'STY_S136_MIG', '2025-01-01', NULL),
            ('emp_end_only',   'emp_end_only',   'x', 'End only (since ever)',    'STY_S136_MIG', NULL,         '2026-06-30'),
            ('emp_window',     'emp_window',     'x', 'Real window',              'STY_S136_MIG', '2025-01-01', '2026-06-30'),
            ('emp_one_day',    'emp_one_day',    'x', 'One-day window (>=)',      'STY_S136_MIG', '2026-03-15', '2026-03-15');
        """;

    /// <summary>Two inverted windows — the census must name BOTH ids, alphabetically.</summary>
    private const string ViolatingRowsDdl = """
        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id,
                           employment_start_date, employment_end_date) VALUES
            ('emp_bad_a', 'emp_bad_a', 'x', 'End before start A', 'STY_S136_MIG', '2026-05-01', '2026-04-01'),
            ('emp_bad_b', 'emp_bad_b', 'x', 'End before start B', 'STY_S136_MIG', '2025-12-31', '2025-01-01');
        """;

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // BARE harness only — each fact applies the schema it needs (the legacy facts the
        // pre-S136 DDL + the extracted segment; the greenfield fact the FULL canonical
        // init.sql). The harness baseline DDL does not touch users.
        _harness = await TestFixtures.DockerHarness.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // LEGACY path — the extracted segment against a reconstructed pre-S136 schema.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clean legacy DB: the census passes over the full D2 NULL matrix, the named CHECK
    /// lands, a violating write is rejected from then on, and a second apply is a
    /// ledger-guarded no-op.
    /// </summary>
    [Fact]
    public async Task Migration_S136_LegacySegment_CleanCensus_Constrains_DoubleApplyIdempotent()
    {
        await ApplyAsync(PreS136SchemaDdl);
        Assert.False(await ConstraintExistsAsync()); // baseline sanity: pre-S136 has no CHECK

        var segment = ExtractS136SegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        Assert.True(await ConstraintExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());

        // The census remediated NOTHING — all five D2-matrix rows are untouched.
        Assert.Equal(5, await CountAsync("SELECT COUNT(*) FROM users"));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM users WHERE employment_start_date IS NULL"));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM users WHERE employment_end_date IS NULL"));

        // The CHECK now rejects an inverted window on BOTH write paths (23514).
        await AssertCheckRejectsViolatingInsertAsync();
        await AssertCheckRejectsViolatingUpdateAsync();

        // ── Second apply: ledger-guarded no-op ───────────────────────────────
        await ApplyAsync(segment);
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.True(await ConstraintExistsAsync());
    }

    /// <summary>
    /// Violating legacy DB: the census FAILS LOUD (P0001) naming every violating user_id,
    /// the constraint is NOT added, and the ledger row rolls back with the RAISE — so after
    /// the operator corrects the named rows, a plain re-run executes the segment in full
    /// (fix-then-rerun; NO auto-repair, the deliberate S136 deviation from S73's
    /// deterministic remediate-then-constrain).
    /// </summary>
    [Fact]
    public async Task Migration_S136_LegacySegment_ViolatingRows_RaisesWithUserIds_AddsNothing_RerunsAfterFix()
    {
        await ApplyAsync(PreS136SchemaDdl);
        await ApplyAsync(ViolatingRowsDdl);

        var segment = ExtractS136SegmentFromCanonicalInitSql();

        // ── The census raises: operator-actionable, ids named, nothing changed ──
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(segment));
        Assert.Equal("P0001", ex.SqlState); // raise_exception — the census, not a raw 23514
        Assert.Contains(MigrationId, ex.MessageText);
        Assert.Contains("emp_bad_a", ex.MessageText);
        Assert.Contains("emp_bad_b", ex.MessageText);

        // The constraint was NOT added...
        Assert.False(await ConstraintExistsAsync());
        // ...and the ledger INSERT rolled back with the RAISE (fix-then-rerun, never
        // fix-then-unstick-the-ledger).
        Assert.Equal(0, await CountLedgerRowsAsync());

        // ── The operator corrects the business history, then re-runs ─────────
        await ApplyAsync("""
            UPDATE users SET employment_end_date = '2026-06-01' WHERE user_id = 'emp_bad_a';
            UPDATE users SET employment_start_date = '2025-01-01' WHERE user_id = 'emp_bad_b';
            """);

        await ApplyAsync(segment);
        Assert.True(await ConstraintExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        await AssertCheckRejectsViolatingInsertAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // GREENFIELD path — the FULL canonical init.sql, applied twice.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The full canonical init.sql succeeding AT ALL proves the census never rejects
    /// init.sql's own seeds; the second apply proves the ledger short-circuit + the
    /// unconditional post-segment re-land ALTER are idempotent together; the violating
    /// UPDATE proves the CHECK enforces on the real seeded users table.
    /// </summary>
    [Fact]
    public async Task Migration_S136_Greenfield_FullInitSql_DoubleApply_ConstraintEnforces()
    {
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.True(await ConstraintExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());

        // Second apply (the standing down -v && up re-run): the segment short-circuits on
        // the ledger; the unconditional re-land DROP-then-ADD lands the same named CHECK.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.True(await ConstraintExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());

        // The CHECK enforces on the real seeded table: inverting a seed user's window is
        // rejected (23514) — emp001 carries NULL dates in the seed, so set both at once.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(
            """
            UPDATE users
               SET employment_start_date = '2026-05-01', employment_end_date = '2026-04-01'
             WHERE user_id = 'emp001'
            """));
        Assert.Equal("23514", ex.SqlState); // check_violation
        Assert.Equal(CheckConstraintName, ex.ConstraintName);
    }

    // ─── segment extraction (markers verbatim; CanonicalInitSql per S133/QUAL-014) ───

    private static string ExtractS136SegmentFromCanonicalInitSql()
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

    /// <summary>The CHECK rejects an INSERT carrying an inverted window (23514).</summary>
    private async Task AssertCheckRejectsViolatingInsertAsync()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id,
                               employment_start_date, employment_end_date)
            VALUES ('zz_s136_violation', 'zz_s136_violation', 'x', 'Inverted window',
                    'STY_S136_MIG', '2026-02-01', '2026-01-01')
            """));
        Assert.Equal("23514", ex.SqlState); // check_violation
        Assert.Equal(CheckConstraintName, ex.ConstraintName);
    }

    /// <summary>The CHECK rejects an UPDATE inverting an existing valid window (23514).</summary>
    private async Task AssertCheckRejectsViolatingUpdateAsync()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(
            """
            UPDATE users SET employment_end_date = '2024-12-31' WHERE user_id = 'emp_window'
            """));
        Assert.Equal("23514", ex.SqlState);
        Assert.Equal(CheckConstraintName, ex.ConstraintName);
    }

    private async Task ApplyAsync(string ddl)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> ConstraintExistsAsync()
    {
        return Convert.ToInt64(await ScalarAsync(
            """
            SELECT COUNT(*) FROM pg_constraint
            WHERE conname = @p0 AND contype = 'c'
            """, CheckConstraintName)) == 1L;
    }

    private async Task<int> CountLedgerRowsAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @p0", MigrationId));
    }

    private async Task<int> CountAsync(string sql)
        => Convert.ToInt32(await ScalarAsync(sql));

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
