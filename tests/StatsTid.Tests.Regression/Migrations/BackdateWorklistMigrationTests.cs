using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S138 / TASK-13803 — migration pins for the <c>s138-backdate-worklist</c> segment (ADR-040 D8,
/// Increment 3: the HR diagnostic worklist table <c>hr_backdate_worklist</c>). The
/// S71/S72/S73/S136/S137 harness pattern: the LEGACY fact EXTRACTS the S138 segment VERBATIM from
/// the canonical <c>docker/postgres/init.sql</c> between the
/// <c>S138-BACKDATE-WORKLIST-SEGMENT-BEGIN/END</c> marker lines (<see cref="CanonicalInitSql"/>,
/// the S133/QUAL-014 single source of truth) and runs exactly what production runs against a
/// reconstructed pre-S138 schema, twice; the GREENFIELD fact applies the FULL canonical init.sql
/// twice.
///
/// <para>The S138 pins under test (RED-first from the refinement spec, not from observed code):</para>
/// <list type="bullet">
///   <item>the table lands with its two row KINDS under one CHECK-tied shape — an EXPORTED_MONTH
///     row must carry exactly (year, month, export_id), a SETTLED_YEAR row exactly
///     (entitlement_type, entitlement_year); mixed keys are refused (23514);</item>
///   <item>the two PARTIAL UNIQUE indexes enforce at most ONE OPEN row per key — a second open row
///     for the same month / year is refused (23505) — and RESOLVING a row frees its key;</item>
///   <item><c>triggers</c> must be a non-empty JSON ARRAY; resolution columns are paired;</item>
///   <item>NO foreign key to <c>payroll_export_records</c> (ADR-034 ownership line) — an
///     EXPORTED_MONTH row referencing an export_id that does not exist is accepted;</item>
///   <item>double-apply idempotence — the ledger short-circuits; no row is touched.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class BackdateWorklistMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s138-backdate-worklist";

    private const string SegmentBeginMarker = "-- S138-BACKDATE-WORKLIST-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S138-BACKDATE-WORKLIST-SEGMENT-END";

    /// <summary>
    /// Pre-S138 baseline: <c>schema_migrations</c> + a minimal <c>organizations</c> stub + a minimal
    /// <c>users</c> stub (the worklist's <c>employee_id</c> REFERENCES users — the
    /// vacation_settlements precedent) with one seeded employee. Deliberately NO
    /// payroll_export_records table: the worklist must be creatable WITHOUT it (no FK across the
    /// ADR-034 ownership line).
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
            VALUES ('STY_S138_MIG', 'S138 Migration Test Org')
            ON CONFLICT (org_id) DO NOTHING;

        CREATE TABLE IF NOT EXISTS users (
            user_id                 TEXT        PRIMARY KEY,
            username                TEXT        NOT NULL UNIQUE,
            password_hash           TEXT        NOT NULL,
            display_name            TEXT        NOT NULL,
            primary_org_id          TEXT        NOT NULL REFERENCES organizations(org_id),
            agreement_code          TEXT        NOT NULL DEFAULT 'AC',
            ok_version              TEXT        NOT NULL DEFAULT 'OK24',
            is_active               BOOLEAN     NOT NULL DEFAULT TRUE,
            version                 BIGINT      NOT NULL DEFAULT 1
        );

        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id)
            VALUES ('emp_s138_mig', 'emp_s138_mig', 'x', 'S138 worklist subject', 'STY_S138_MIG');
        """;

    private const string OneTriggerJson =
        """[{"kind":"PROFILE_CHANGE","eventId":"11111111-1111-1111-1111-111111111111","effectiveFrom":"2026-03-10","appendedAt":"2026-09-03T08:00:00+00:00","actorId":"hr01","baselineContentHash":"h1"}]""";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // BARE harness only — each fact applies the schema it needs (mirrors ProfileCategoryMigrationTests).
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

    [Fact]
    public async Task Migration_S138_LegacySegment_LandsTableKindChecksPartialUniques_DoubleApplyIdempotent()
    {
        await ApplyAsync(PreS138SchemaDdl);
        Assert.False(await TableExistsAsync()); // baseline sanity: pre-S138 has no worklist

        var segment = ExtractS138SegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        Assert.True(await TableExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal(2, await CountPartialUniqueIndexesAsync());
        Assert.Equal(0, await CountForeignKeysToPayrollExportRecordsAsync()); // ADR-034: a REFERENCE, never a FK

        // A well-formed EXPORTED_MONTH row referencing an export_id that exists NOWHERE (there is
        // no payroll_export_records table in this schema at all) — accepted: no FK by design.
        var openMonthId = Guid.NewGuid();
        await ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 3, @p1, @p2::jsonb, 'hr01')
            """, openMonthId, Guid.NewGuid(), OneTriggerJson);

        // Partial UNIQUE (EXPORTED_MONTH): a second OPEN row for the same (employee, year, month) → 23505.
        await AssertSqlStateAsync("23505", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 3, @p1, @p2::jsonb, 'hr01')
            """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson));

        // kind_keys CHECK: an EXPORTED_MONTH row carrying a settled-year key → 23514.
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, entitlement_type, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 4, @p1, 'VACATION', @p2::jsonb, 'hr01')
            """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson));

        // kind_keys CHECK: a SETTLED_YEAR row missing entitlement_year → 23514.
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, entitlement_type, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'SETTLED_YEAR', 'VACATION', @p1::jsonb, 'hr01')
            """, Guid.NewGuid(), OneTriggerJson));

        // kind CHECK: an unknown kind → 23514.
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'SOMETHING_ELSE', 2026, 5, @p1, @p2::jsonb, 'hr01')
            """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson));

        // triggers CHECK: an OBJECT (not an array) → 23514; an EMPTY array → 23514.
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 5, @p1, '{"kind":"PROFILE_CHANGE"}'::jsonb, 'hr01')
            """, Guid.NewGuid(), Guid.NewGuid()));
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 5, @p1, '[]'::jsonb, 'hr01')
            """, Guid.NewGuid(), Guid.NewGuid()));

        // resolution_paired CHECK: a resolution verb without resolved_at/by → 23514.
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by, resolution)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 5, @p1, @p2::jsonb, 'hr01', 'DISMISSED')
            """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson));

        // resolution CHECK: an unknown verb → 23514.
        await AssertSqlStateAsync("23514", () => ExecAsync(
            """
            UPDATE hr_backdate_worklist
            SET resolved_at = NOW(), resolved_by = 'hr01', resolution = 'IGNORED'
            WHERE worklist_id = @p0
            """, openMonthId));

        // A well-formed SETTLED_YEAR row; its duplicate open row → 23505.
        await ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'SETTLED_YEAR', 'VACATION', 2024, @p1::jsonb, 'hr01')
            """, Guid.NewGuid(), OneTriggerJson);
        await AssertSqlStateAsync("23505", () => ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'SETTLED_YEAR', 'VACATION', 2024, @p1::jsonb, 'hr01')
            """, Guid.NewGuid(), OneTriggerJson));

        // A SETTLED_YEAR (VACATION, 2026) row coexists with the open (2026, 3) month row — the
        // partial indexes are per kind, so keys never collide across kinds.
        await ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'SETTLED_YEAR', 'VACATION', 2026, @p1::jsonb, 'hr01')
            """, Guid.NewGuid(), OneTriggerJson);

        // RESOLVING the open month row frees the key: a new OPEN row for (2026, 3) is accepted.
        await ExecAsync(
            """
            UPDATE hr_backdate_worklist
            SET resolved_at = NOW(), resolved_by = 'hr01', resolution = 'RECALCULATED',
                resolution_reason = 'S138 migration pin', version = version + 1
            WHERE worklist_id = @p0
            """, openMonthId);
        await ExecAsync(
            """
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
            VALUES (@p0, 'emp_s138_mig', 'EXPORTED_MONTH', 2026, 3, @p1, @p2::jsonb, 'hr01')
            """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson);

        Assert.Equal(4, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));

        // ── Second apply: ledger-guarded no-op, nothing touched ──────────────
        await ApplyAsync(segment);
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal(4, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));
        Assert.Equal(2, await CountPartialUniqueIndexesAsync());
    }

    // ════════════════════════════════════════════════════════════════════════
    // GREENFIELD path — the FULL canonical init.sql, applied twice.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Migration_S138_Greenfield_FullInitSql_DoubleApply_TablePresent()
    {
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.True(await TableExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal(2, await CountPartialUniqueIndexesAsync());
        Assert.Equal(0, await CountForeignKeysToPayrollExportRecordsAsync());

        // Second apply (the standing down -v && up re-run): IF NOT EXISTS + the ledger short-circuit.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        Assert.True(await TableExistsAsync());
        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist")); // init.sql seeds no worklist rows
    }

    // ─── segment extraction (markers verbatim; CanonicalInitSql per S133/QUAL-014) ───

    private static string ExtractS138SegmentFromCanonicalInitSql()
    {
        var initSql = CanonicalInitSql.Read();

        var begin = initSql.IndexOf(SegmentBeginMarker, StringComparison.Ordinal);
        var end = initSql.IndexOf(SegmentEndMarker, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"init.sql is missing the '{SegmentBeginMarker}' marker line.");
        Assert.True(end > begin, $"init.sql is missing the '{SegmentEndMarker}' marker line after BEGIN.");

        var segment = initSql.Substring(begin + SegmentBeginMarker.Length, end - begin - SegmentBeginMarker.Length);
        Assert.Contains(MigrationId, segment);
        Assert.Contains("CREATE TABLE IF NOT EXISTS hr_backdate_worklist", segment);
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

    private static async Task AssertSqlStateAsync(string expectedSqlState, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(expectedSqlState, ex.SqlState);
    }

    private async Task<bool> TableExistsAsync()
    {
        return Convert.ToInt64(await ScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_name = 'hr_backdate_worklist'
            """)) == 1L;
    }

    private async Task<int> CountPartialUniqueIndexesAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            """
            SELECT COUNT(*) FROM pg_indexes
            WHERE tablename = 'hr_backdate_worklist'
              AND indexname IN ('idx_hr_backdate_worklist_open_month', 'idx_hr_backdate_worklist_open_year')
              AND indexdef LIKE 'CREATE UNIQUE INDEX%'
              AND indexdef LIKE '%WHERE%resolved_at IS NULL%'
            """));
    }

    private async Task<int> CountForeignKeysToPayrollExportRecordsAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            """
            SELECT COUNT(*)
            FROM information_schema.table_constraints tc
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
            WHERE tc.table_name = 'hr_backdate_worklist'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND ccu.table_name = 'payroll_export_records'
            """));
    }

    private async Task<int> CountLedgerRowsAsync()
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @p0", MigrationId));
    }

    private async Task<int> CountAsync(string sql)
        => Convert.ToInt32(await ScalarAsync(sql));

    private async Task ExecAsync(string sql, params object[] args)
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
        await cmd.ExecuteNonQueryAsync();
    }

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
