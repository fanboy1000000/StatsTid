using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S73 / TASK-7301 — migration pins for the <c>s73-full-day-only-schema</c> segment (SPRINT-73
/// R2; owner ruling D-A 2026-06-13: CARE_DAY + SENIOR_DAY are FULL-DAY-ONLY). The S71/S72
/// harness pattern (<see cref="SkemaRowPreferencesLegacyMigrationTests"/>): the LEGACY facts
/// EXTRACT the S73 segment VERBATIM from the canonical <c>docker/postgres/init.sql</c> between
/// the <c>S73-FULL-DAY-ONLY-SEGMENT-BEGIN/END</c> marker lines and run exactly what production
/// runs against a reconstructed pre-S73 schema, twice; the GREENFIELD facts apply the FULL
/// canonical init.sql (twice for the double-apply pin) and assert the seed flags EXHAUSTIVELY
/// (Step-0b B3: CARE_DAY + SENIOR_DAY × ALL agreement/OK pairs carry TRUE).
///
/// <para>The R2 ordering pins under test:</para>
/// <list type="bullet">
///   <item>greenfield seeds carry <c>full_day_only = TRUE</c> INLINE — the
///     <c>entitlement_configs_full_day_only_types</c> CHECK never rejects init.sql's own
///     seeds (proven by the full-init.sql apply succeeding + the flag assertions);</item>
///   <item>the legacy DO-block orders backfill-UPDATE BEFORE ADD CONSTRAINT
///     (remediate-then-constrain, the S68 precedent) — proven by the segment applying cleanly
///     over pre-existing FALSE-flag CARE_DAY/SENIOR_DAY rows (history rows included);</item>
///   <item>double-apply idempotence on BOTH paths (ledger-guarded; ON CONFLICT seeds).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FullDayOnlyMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s73-full-day-only-schema";
    private const string CheckConstraintName = "entitlement_configs_full_day_only_types";

    // The canonical seed dimensions (init.sql: 5 types × 5 agreement codes × 2 OK versions).
    private static readonly string[] AllAgreementCodes = ["AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING"];
    private static readonly string[] AllOkVersions = ["OK24", "OK26"];
    private static readonly string[] FullDayOnlyTypes = ["CARE_DAY", "SENIOR_DAY"];
    private static readonly string[] HoursBasedTypes = ["VACATION", "SPECIAL_HOLIDAY", "CHILD_SICK"];

    /// <summary>
    /// Pre-S73 baseline: <c>schema_migrations</c> + the post-S30 <c>entitlement_configs</c>
    /// shape (effective-dating + version, both partial-unique indexes, the S68 VACATION
    /// reset-month CHECK) WITHOUT <c>full_day_only</c>. Live legacy rows cover the backfill
    /// matrix: open CARE_DAY/SENIOR_DAY rows on two agreement/OK pairs, a CLOSED (history)
    /// CARE_DAY row (the backfill is type-keyed, NOT open-row-scoped — dated reads at any
    /// asOf must resolve the D-A rule), and a VACATION row that must STAY false.
    /// </summary>
    private const string PreS73SchemaDdl = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );

        CREATE TABLE IF NOT EXISTS entitlement_configs (
            config_id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            entitlement_type        TEXT        NOT NULL,
            agreement_code          TEXT        NOT NULL,
            ok_version              TEXT        NOT NULL,
            annual_quota            DECIMAL     NOT NULL,
            accrual_model           TEXT        NOT NULL DEFAULT 'IMMEDIATE',
            reset_month             INT         NOT NULL DEFAULT 1,
            carryover_max           DECIMAL     NOT NULL DEFAULT 0,
            pro_rate_by_part_time   BOOLEAN     NOT NULL DEFAULT true,
            is_per_episode          BOOLEAN     NOT NULL DEFAULT false,
            min_age                 INT,
            description             TEXT,
            created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            effective_from          DATE        NOT NULL DEFAULT '0001-01-01',
            effective_to            DATE,
            version                 BIGINT      NOT NULL DEFAULT 1,
            CONSTRAINT entitlement_configs_vacation_reset_month CHECK (
                entitlement_type <> 'VACATION' OR reset_month = 9
            )
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_ec_natural_key_open
            ON entitlement_configs (entitlement_type, agreement_code, ok_version)
            WHERE effective_to IS NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS idx_ec_natural_key_history
            ON entitlement_configs (entitlement_type, agreement_code, ok_version, effective_from);

        INSERT INTO entitlement_configs
            (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model,
             reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age,
             description, effective_from, effective_to) VALUES
            ('CARE_DAY',   'AC',    'OK24',  2, 'IMMEDIATE',       1, 0, false, false, NULL, 'legacy care',           '0001-01-01', NULL),
            ('CARE_DAY',   'PROSA', 'OK26',  2, 'IMMEDIATE',       1, 0, false, false, NULL, 'legacy care',           '0001-01-01', NULL),
            ('SENIOR_DAY', 'AC',    'OK24',  2, 'IMMEDIATE',       1, 0, false, false, 62,   'legacy senior',         '0001-01-01', NULL),
            ('SENIOR_DAY', 'HK',    'OK26',  2, 'IMMEDIATE',       1, 0, false, false, 62,   'legacy senior',         '0001-01-01', NULL),
            ('CARE_DAY',   'HK',    'OK24',  2, 'IMMEDIATE',       1, 0, false, false, NULL, 'legacy care CLOSED',    '0001-01-01', '2026-01-01'),
            ('CARE_DAY',   'HK',    'OK24',  3, 'IMMEDIATE',       1, 0, false, false, NULL, 'legacy care successor', '2026-01-01', NULL),
            ('VACATION',   'AC',    'OK24', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'legacy vacation',       '0001-01-01', NULL);
        """;

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // BARE harness only — each fact applies the schema it needs (the legacy facts the
        // pre-S73 DDL + the extracted segment; the greenfield facts the FULL canonical
        // init.sql). The harness baseline DDL does not touch entitlement_configs.
        _harness = await TestFixtures.DockerHarness.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // LEGACY path — the extracted segment against a reconstructed pre-S73 schema.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Migration_S73_LegacySegment_BackfillsThenConstrains_DoubleApplyIdempotent()
    {
        await ApplyAsync(PreS73SchemaDdl);
        var segment = ExtractS73SegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        // Column + ledger landed (ledger row exactly once).
        Assert.True(await ColumnExistsAsync("entitlement_configs", "full_day_only"));
        Assert.Equal(1, await CountLedgerRowsAsync());

        // Backfill correctness (remediate-then-constrain): every CARE_DAY/SENIOR_DAY row is
        // TRUE — the CLOSED history row included (type-keyed, not open-row-scoped) — and the
        // VACATION row stays FALSE.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type IN ('CARE_DAY','SENIOR_DAY') AND NOT full_day_only"));
        Assert.Equal(6, await CountAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type IN ('CARE_DAY','SENIOR_DAY') AND full_day_only"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type = 'VACATION' AND NOT full_day_only"));
        // The closed history row specifically (dated reads at pre-close asOf must see TRUE).
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type = 'CARE_DAY' AND effective_to IS NOT NULL AND full_day_only"));

        // The named CHECK exists and REJECTS a violating INSERT (construction-enforcement).
        Assert.True(await ConstraintExistsAsync());
        await AssertCheckRejectsViolatingInsertAsync();

        // ── Second apply: ledger-guarded no-op ───────────────────────────────
        await ApplyAsync(segment);
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.True(await ConstraintExistsAsync());
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type IN ('CARE_DAY','SENIOR_DAY') AND NOT full_day_only"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // GREENFIELD path — the FULL canonical init.sql, applied twice.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Migration_S73_Greenfield_FullInitSql_ExhaustiveSeedFlags_DoubleApplyIdempotent()
    {
        // First apply: the full canonical init.sql succeeding AT ALL proves the inline CHECK
        // never rejects init.sql's own seeds (the R2 ordering pin).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        await AssertExhaustiveSeedFlagsAsync();
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.True(await ConstraintExistsAsync());
        await AssertCheckRejectsViolatingInsertAsync();

        // Second apply (the standing down -v && up re-run): seeds ON CONFLICT no-op, the
        // ledger short-circuits the DO-block — flags and row count unchanged.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        await AssertExhaustiveSeedFlagsAsync();
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal(50, await CountAsync("SELECT COUNT(*) FROM entitlement_configs"));
    }

    /// <summary>
    /// Step-0b B3 — EXHAUSTIVE per-pair assertions: every (CARE_DAY|SENIOR_DAY) × (AC, HK,
    /// PROSA, AC_RESEARCH, AC_TEACHING) × (OK24, OK26) seed row carries TRUE (20 rows), and
    /// every hours-based type carries FALSE (30 rows; CHILD_SICK stays hours-based per D-A).
    /// </summary>
    private async Task AssertExhaustiveSeedFlagsAsync()
    {
        foreach (var type in FullDayOnlyTypes)
        foreach (var agreement in AllAgreementCodes)
        foreach (var ok in AllOkVersions)
        {
            var flag = await ReadFlagAsync(type, agreement, ok);
            Assert.True(flag, $"Seed ({type}, {agreement}, {ok}) must carry full_day_only = TRUE (D-A).");
        }

        foreach (var type in HoursBasedTypes)
        foreach (var agreement in AllAgreementCodes)
        foreach (var ok in AllOkVersions)
        {
            var flag = await ReadFlagAsync(type, agreement, ok);
            Assert.False(flag, $"Seed ({type}, {agreement}, {ok}) must carry full_day_only = FALSE (hours-based).");
        }
    }

    private async Task<bool> ReadFlagAsync(string type, string agreement, string ok)
    {
        var result = await ScalarAsync(
            """
            SELECT full_day_only FROM entitlement_configs
            WHERE entitlement_type = @p0 AND agreement_code = @p1 AND ok_version = @p2
              AND effective_to IS NULL
            """, type, agreement, ok);
        Assert.NotNull(result);
        return (bool)result!;
    }

    /// <summary>The CHECK rejects a CARE_DAY INSERT carrying full_day_only = FALSE (23514).</summary>
    private async Task AssertCheckRejectsViolatingInsertAsync()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(
            """
            INSERT INTO entitlement_configs
                (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model,
                 reset_month, carryover_max, pro_rate_by_part_time, is_per_episode,
                 effective_from, full_day_only)
            VALUES ('CARE_DAY', 'ZZ_S73_VIOLATION', 'OK99', 2, 'IMMEDIATE', 1, 0, false, false,
                    '0001-01-01', FALSE)
            """));
        Assert.Equal("23514", ex.SqlState); // check_violation
        Assert.Equal(CheckConstraintName, ex.ConstraintName);
    }

    // ─── segment extraction (the S72 harness pattern, markers verbatim) ──────

    private const string SegmentBeginMarker = "-- S73-FULL-DAY-ONLY-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S73-FULL-DAY-ONLY-SEGMENT-END";

    private static string ExtractS73SegmentFromCanonicalInitSql()
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

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        return Convert.ToInt64(await ScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = @p0 AND column_name = @p1
            """, tableName, columnName)) == 1L;
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
