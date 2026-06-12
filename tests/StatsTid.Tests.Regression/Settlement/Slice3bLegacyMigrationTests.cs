using Npgsql;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7100 — legacy-ALTER idempotence for the
/// <c>s71-slice3b-termination-emission-schema</c> migration (the S35
/// <see cref="Migrations.S35VersionMigrationTests"/> harness pattern, hardened: instead of
/// embedding a copy of the migration DDL that could drift, this test EXTRACTS the S71
/// segment VERBATIM from the canonical <c>docker/postgres/init.sql</c> between the
/// <c>S71-SLICE3B-SEGMENT-BEGIN/END</c> marker lines and runs exactly what production runs).
///
/// <para>
/// Models a PRE-S71 database: the S68 <c>vacation_settlements</c> shape (inline
/// auto-named <c>review_disposition</c> CHECK + the narrow S68
/// <c>vacation_settlements_disposition_state</c> coupling) and the S69
/// <c>settlement_export_lines</c> shape (no <c>line_kind</c>/<c>reverses_line_id</c>),
/// each carrying live legacy rows — including a PENDING_REVIEW+DEFER settlement row and a
/// staged §24 export line, the populations the widened CHECKs must re-validate WITHOUT
/// remediation. Applies the S71 segment twice and verifies the 3-path contract:
/// </para>
///
/// <list type="bullet">
///   <item>First apply (the legacy upgrade): new columns land with their backfills
///     (claim NULL / marker FALSE / line_kind ORIGINAL); the request table + partial-unique
///     index exist; the old auto-named inline CHECK is gone and every pinned S71 constraint
///     name is present exactly once; legacy rows are untouched and still valid.</item>
///   <item>The widened semantics are ACTIVE: the legacy DEFER row can move to REVERSED
///     keeping its DEFER marker (impossible under the S68 CHECK); a marker on a SETTLED row
///     is rejected 23514; a MODREGNING+quantity row is accepted.</item>
///   <item>Second apply (re-run): the schema_migrations ledger short-circuits — no errors,
///     no duplicate ledger row, no constraint duplication, data untouched.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Slice3bLegacyMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s71-slice3b-termination-emission-schema";
    private const string CheckViolation = "23514";

    /// <summary>
    /// Pre-S71 baseline. <c>users</c>/<c>organizations</c> are the minimal FK targets (the
    /// S35 precedent shape); <c>vacation_settlements</c> mirrors the S68 base CREATE
    /// (init.sql S68 section) BEFORE the S71 widening — review_disposition's value set as an
    /// INLINE column CHECK (auto-named <c>vacation_settlements_review_disposition_check</c>
    /// by PostgreSQL, the exact name the guarded block must drop on a legacy DB) and the
    /// narrow DEFER⇒PENDING_REVIEW / FORFEIT⇒¬PENDING_REVIEW coupling;
    /// <c>settlement_export_lines</c> mirrors the S69 base CREATE before line_kind.
    /// </summary>
    private const string PreS71SchemaDdl = """
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
            VALUES ('STY_S71_MIG', 'S71 Migration Test Org')
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
            VALUES ('emp_s71_mig', 'emp_s71_mig', 'dev-only', 'S71 Migration Leaver', 'STY_S71_MIG')
            ON CONFLICT (user_id) DO NOTHING;

        -- S68 vacation_settlements shape (pre-S71): inline review_disposition CHECK
        -- (PostgreSQL auto-names it vacation_settlements_review_disposition_check) +
        -- the narrow S68 disposition/state coupling.
        CREATE TABLE IF NOT EXISTS vacation_settlements (
            employee_id             TEXT          NOT NULL REFERENCES users(user_id),
            entitlement_type        TEXT          NOT NULL,
            entitlement_year        INT           NOT NULL,
            sequence                INT           NOT NULL,
            settlement_state        TEXT          NOT NULL CHECK (settlement_state IN ('PENDING_REVIEW', 'SETTLED', 'REVERSED')),
            trigger                 TEXT          NOT NULL CHECK (trigger IN ('YEAR_END', 'TERMINATION')),
            snapshot                JSONB         NOT NULL,
            transfer_days           NUMERIC(6,2)  NOT NULL DEFAULT 0,
            payout_days             NUMERIC(6,2)  NOT NULL DEFAULT 0,
            forfeit_days            NUMERIC(6,2)  NOT NULL DEFAULT 0,
            payout_reconciled_at    TIMESTAMPTZ   NULL,
            payout_reconciled_by    TEXT          NULL,
            review_disposition      TEXT          NULL CHECK (review_disposition IS NULL OR review_disposition IN ('FORFEIT', 'DEFER')),
            version                 BIGINT        NOT NULL DEFAULT 1,
            created_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            updated_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            PRIMARY KEY (employee_id, entitlement_type, entitlement_year, sequence),
            CONSTRAINT vacation_settlements_payout_reconciled_paired CHECK (
                (payout_reconciled_at IS NULL AND payout_reconciled_by IS NULL)
                OR (payout_reconciled_at IS NOT NULL AND payout_reconciled_by IS NOT NULL)
            ),
            CONSTRAINT vacation_settlements_nonneg_buckets CHECK (
                transfer_days >= 0 AND payout_days >= 0 AND forfeit_days >= 0
            ),
            CONSTRAINT vacation_settlements_positive_counters CHECK (sequence >= 1 AND version >= 1),
            CONSTRAINT vacation_settlements_disposition_state CHECK (
                review_disposition IS NULL
                OR (review_disposition = 'DEFER'   AND settlement_state =  'PENDING_REVIEW')
                OR (review_disposition = 'FORFEIT' AND settlement_state <> 'PENDING_REVIEW')
            )
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_vacation_settlements_active
            ON vacation_settlements (employee_id, entitlement_type, entitlement_year)
            WHERE settlement_state <> 'REVERSED';

        -- S69 settlement_export_lines shape (pre-S71): no line_kind / reverses_line_id.
        CREATE TABLE IF NOT EXISTS settlement_export_lines (
            line_id             BIGSERIAL     PRIMARY KEY,
            employee_id         TEXT          NOT NULL REFERENCES users(user_id),
            entitlement_type    TEXT          NOT NULL,
            entitlement_year    INT           NOT NULL,
            sequence            INT           NOT NULL,
            bucket              TEXT          NOT NULL,
            wage_type           TEXT          NOT NULL,
            hours               NUMERIC(8,2)  NOT NULL CHECK (hours >= 0),
            amount              NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (amount = 0),
            ok_version          TEXT          NOT NULL,
            agreement_code      TEXT          NOT NULL,
            position            TEXT          NOT NULL DEFAULT '',
            period_start        DATE          NOT NULL,
            period_end          DATE          NOT NULL,
            source_event_id     UUID          NOT NULL,
            created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            created_by          TEXT          NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_settlement_export_lines_bucket
            ON settlement_export_lines (employee_id, entitlement_type, entitlement_year, sequence, bucket);

        -- Legacy populations the widened CHECKs must re-validate without remediation:
        -- a SETTLED+FORFEIT row, a PENDING_REVIEW+DEFER row, and a staged §24 line.
        INSERT INTO vacation_settlements
            (employee_id, entitlement_type, entitlement_year, sequence,
             settlement_state, trigger, snapshot, forfeit_days, review_disposition)
        VALUES
            ('emp_s71_mig', 'VACATION', 2021, 1, 'SETTLED',        'YEAR_END', '{}'::jsonb, 5, 'FORFEIT'),
            ('emp_s71_mig', 'VACATION', 2022, 1, 'PENDING_REVIEW', 'YEAR_END', '{}'::jsonb, 0, 'DEFER');

        INSERT INTO settlement_export_lines
            (employee_id, entitlement_type, entitlement_year, sequence, bucket,
             wage_type, hours, amount, ok_version, agreement_code, position,
             period_start, period_end, source_event_id, created_by)
        VALUES
            ('emp_s71_mig', 'VACATION', 2021, 1, 'AUTO_PAYOUT_24',
             'SLS_TBD_S24', 3.00, 0, 'OK24', 'AC', '',
             '2021-09-01', '2022-08-31', 'a4f0c6f2-0000-4000-8000-000000007100'::uuid, 'emitter');
        """;

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await ApplyAsync(PreS71SchemaDdl);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task Migration_S71_Slice3b_LegacyAlter_Idempotent()
    {
        var segment = ExtractS71SegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        // New columns landed.
        Assert.True(await ColumnExistsAsync("vacation_settlements", "claim_disposition_days"));
        Assert.True(await ColumnExistsAsync("vacation_settlements", "bare_reversal_not_due"));
        Assert.True(await ColumnExistsAsync("settlement_export_lines", "line_kind"));
        Assert.True(await ColumnExistsAsync("settlement_export_lines", "reverses_line_id"));

        // The §26 request table + its partial-unique live-request index landed.
        Assert.True(await TableExistsAsync("termination_payout_requests"));
        Assert.True(await IndexExistsAsync("idx_termination_payout_requests_nonvoided"));
        Assert.True(await IndexExistsAsync("idx_termination_payout_requests_employee"));

        // Ledger row landed exactly once.
        Assert.Equal(1, await CountLedgerRowsAsync());

        // The S68 inline auto-named CHECK was dropped; every pinned S71 constraint
        // name is present exactly once.
        Assert.Equal(0, await CountConstraintAsync("vacation_settlements_review_disposition_check"));
        foreach (var constraint in PinnedS71ConstraintNames)
            Assert.Equal(1, await CountConstraintAsync(constraint));

        // Legacy rows untouched + backfilled: claim NULL, marker FALSE, the DEFER row
        // still PENDING_REVIEW+DEFER; the staged §24 line backfilled to ORIGINAL.
        Assert.Equal(("SETTLED", "FORFEIT", false, (decimal?)null), await ReadSettlementAsync(2021));
        Assert.Equal(("PENDING_REVIEW", "DEFER", false, (decimal?)null), await ReadSettlementAsync(2022));
        Assert.Equal(("ORIGINAL", (long?)null), await ReadLegacyLineAsync());

        // ── Widened semantics ACTIVE after the upgrade ───────────────────────
        // (a) The legacy DEFER row can now be REVERSED with DEFER history preserved —
        //     the exact transition the S68 CHECK forbade (SPRINT-71 R5).
        await ExecAsync(
            """
            UPDATE vacation_settlements
            SET settlement_state = 'REVERSED', bare_reversal_not_due = TRUE, version = version + 1
            WHERE employee_id = 'emp_s71_mig' AND entitlement_type = 'VACATION'
              AND entitlement_year = 2022 AND sequence = 1
            """);
        Assert.Equal(("REVERSED", "DEFER", true, (decimal?)null), await ReadSettlementAsync(2022));

        // (b) The R3 marker CHECK rejects TRUE on a non-REVERSED row.
        var exMarker = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            """
            UPDATE vacation_settlements
            SET bare_reversal_not_due = TRUE
            WHERE employee_id = 'emp_s71_mig' AND entitlement_type = 'VACATION'
              AND entitlement_year = 2021 AND sequence = 1
            """));
        Assert.Equal(CheckViolation, exMarker.SqlState);

        // (c) The widened disposition set + paired claim quantity is writable.
        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, review_disposition, claim_disposition_days)
            VALUES
                ('emp_s71_mig', 'VACATION', 2023, 1, 'SETTLED', 'TERMINATION', '{}'::jsonb,
                 'MODREGNING', 2.50)
            """);

        // (d) The request table accepts an OPEN request keyed to the exact new row.
        await ExecAsync(
            """
            INSERT INTO termination_payout_requests
                (employee_id, entitlement_type, entitlement_year, settlement_sequence,
                 state, request_date, recorded_by, version)
            VALUES ('emp_s71_mig', 'VACATION', 2023, 1, 'OPEN', '2023-10-01', 'hr001', 1)
            """);

        // ── Second apply: ledger-guarded no-op ───────────────────────────────
        await ApplyAsync(segment);

        Assert.Equal(1, await CountLedgerRowsAsync());
        foreach (var constraint in PinnedS71ConstraintNames)
            Assert.Equal(1, await CountConstraintAsync(constraint));

        // Data written between the two applies is untouched by the re-run.
        Assert.Equal(("REVERSED", "DEFER", true, (decimal?)null), await ReadSettlementAsync(2022));
        Assert.Equal(("SETTLED", "MODREGNING", false, (decimal?)2.50m), await ReadSettlementAsync(2023));
        Assert.Equal(("ORIGINAL", (long?)null), await ReadLegacyLineAsync());
    }

    // ─── segment extraction ─────────────────────────────────────────────────

    private static readonly string[] PinnedS71ConstraintNames =
    [
        "vacation_settlements_review_disposition",
        "vacation_settlements_disposition_state",
        "vacation_settlements_bare_reversal_reversed_only",
        "vacation_settlements_claim_disposition_nonneg",
        "vacation_settlements_claim_disposition_paired",
        "settlement_export_lines_line_kind",
        "settlement_export_lines_reversal_pairing",
        "settlement_export_lines_reverses_line_fk",
        "termination_payout_requests_state",
        "termination_payout_requests_positive_version",
        "termination_payout_requests_settlement_fk",
    ];

    private const string SegmentBeginMarker = "-- S71-SLICE3B-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S71-SLICE3B-SEGMENT-END";

    /// <summary>
    /// Locates the canonical <c>docker/postgres/init.sql</c> (the
    /// <c>StatsTidWebApplicationFactory.LocateInitSql</c> walk-up; the csproj copies
    /// init.sql alongside the test bin) and extracts the S71 segment between the marker
    /// lines — the test runs EXACTLY what production runs, so the migration under test
    /// can never drift from the embedded copy the S35-era tests carried.
    /// </summary>
    private static string ExtractS71SegmentFromCanonicalInitSql()
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

    private async Task<bool> IndexExistsAsync(string indexName)
    {
        return Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = @p0 AND schemaname = 'public'",
            indexName)) == 1L;
    }

    private async Task<int> CountConstraintAsync(string constraintName)
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM pg_constraint WHERE conname = @p0", constraintName));
    }

    private async Task<int> CountLedgerRowsAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @p0", MigrationId));
    }

    private async Task<(string State, string Disposition, bool BareMarker, decimal? ClaimDays)>
        ReadSettlementAsync(int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, review_disposition, bare_reversal_not_due, claim_disposition_days
            FROM vacation_settlements
            WHERE employee_id = 'emp_s71_mig' AND entitlement_type = 'VACATION'
              AND entitlement_year = @year AND sequence = 1
            """, conn);
        cmd.Parameters.AddWithValue("year", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Expected a settlement row for year {year}.");
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3));
    }

    private async Task<(string LineKind, long? ReversesLineId)> ReadLegacyLineAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT line_kind, reverses_line_id FROM settlement_export_lines
            WHERE employee_id = 'emp_s71_mig' AND entitlement_year = 2021 AND sequence = 1
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected the legacy §24 export line.");
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
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
