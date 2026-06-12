using Npgsql;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7105 — legacy-ALTER idempotence for the <c>s71-inbox-composite-bucket-key</c>
/// migration (SPRINT-71 R7), the <see cref="Slice3bLegacyMigrationTests"/> verbatim-extraction
/// harness pattern: the S71 inbox segment is EXTRACTED from the canonical
/// <c>docker/postgres/init.sql</c> between the <c>S71-INBOX-SEGMENT-BEGIN/END</c> marker lines and
/// run EXACTLY as production runs it, twice, against a reconstructed PRE-S71 (S69-shape)
/// <c>settlement_payroll_inbox</c> carrying live legacy rows.
///
/// <para>The R7 contract under test, in order:</para>
/// <list type="number">
///   <item>PRE-FLIGHT validate-or-abort (SPRINT-71 Step-5a cycle-1 W1): the poison discriminator
///     below encodes a writer CONVENTION the S69 schema never enforced, so every NULL-bucket row
///     must FIRST prove to be EITHER all-identity-NULL (canonical poison) OR fully-identity-
///     populated (canonical normal) — any HYBRID row aborts the whole block loudly (RAISE
///     EXCEPTION naming the offending <c>source_event_id</c>s; ledger row rolled back, NOTHING
///     mutated) for manual remediation;</item>
///   <item>poison NULL-bucket rows (identity columns NULL — the only legal NULL-bucket shape the
///     S69 writers produced) backfill to the <c>'_EVENT'</c> sentinel;</item>
///   <item>any REMAINING NULL-bucket row (defensive — a normal-identity row) backfills to the §24
///     bucket <c>AUTO_PAYOUT_24</c>;</item>
///   <item><c>bucket</c> becomes NOT NULL;</item>
///   <item>the old single-column PK is dropped;</item>
///   <item>the composite PK <c>(source_event_id, bucket)</c> lands under the SAME pinned name —
///     multiple rows per event (different buckets) are now admitted, duplicates per
///     (event, bucket) are rejected;</item>
///   <item>the <c>processing_status</c> CHECK is widened with <c>SKIPPED_VOIDED</c> (named
///     constraint replacing the S69 inline auto-name; strict superset — legacy rows validate with
///     no remediation).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementInboxMigrationTests : IAsyncLifetime
{
    private const string MigrationId = "s71-inbox-composite-bucket-key";
    private const string CheckViolation = "23514";
    private const string NotNullViolation = "23502";
    private const string UniqueViolation = "23505";
    private const string RaiseException = "P0001"; // the W1 pre-flight RAISE EXCEPTION

    // Stable ids for the legacy population.
    private static readonly Guid PoisonEventId = Guid.Parse("a4f0c6f2-0000-4000-8000-000000007105");
    private static readonly Guid ProcessedEventId = Guid.Parse("a4f0c6f2-0001-4000-8000-000000007105");
    private static readonly Guid RetryEventId = Guid.Parse("a4f0c6f2-0002-4000-8000-000000007105");
    private static readonly Guid NullBucketNormalEventId = Guid.Parse("a4f0c6f2-0003-4000-8000-000000007105");

    /// <summary>
    /// Pre-S71 baseline: the S69 <c>settlement_payroll_inbox</c> base CREATE — PK
    /// <c>source_event_id</c> alone, <c>bucket</c> NULLABLE, the 4-value INLINE
    /// <c>processing_status</c> CHECK (PostgreSQL auto-names it
    /// <c>settlement_payroll_inbox_processing_status_check</c> — the exact name the guarded block
    /// must drop on a legacy DB). Live legacy rows: a poison DEAD_LETTER (identity + bucket NULL —
    /// S69 Step-7a FIX 1), a normal PROCESSED §24 row, a normal RETRY_PENDING §24 row, and a
    /// DEFENSIVE normal-identity row with a NULL bucket (never produced by the S69 writers, but
    /// the R7 step-2 backfill is pinned, not assumed).
    /// </summary>
    private const string PreS71SchemaDdl = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );

        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        INSERT INTO organizations (org_id, org_name)
            VALUES ('STY_S71_INBOX', 'S71 Inbox Migration Test Org')
            ON CONFLICT (org_id) DO NOTHING;

        CREATE TABLE IF NOT EXISTS users (
            user_id             TEXT        PRIMARY KEY,
            username            TEXT        NOT NULL UNIQUE,
            password_hash       TEXT        NOT NULL,
            display_name        TEXT        NOT NULL,
            primary_org_id      TEXT        NOT NULL REFERENCES organizations(org_id),
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id)
            VALUES ('emp_s71_inbox', 'emp_s71_inbox', 'dev-only', 'S71 Inbox Migration Employee', 'STY_S71_INBOX')
            ON CONFLICT (user_id) DO NOTHING;

        -- S69 settlement_payroll_inbox shape (pre-S71): single-column PK, nullable bucket,
        -- 4-value inline processing_status CHECK (auto-named ..._processing_status_check).
        CREATE TABLE IF NOT EXISTS settlement_payroll_inbox (
            source_event_id     UUID          PRIMARY KEY,
            employee_id         TEXT          NULL REFERENCES users(user_id),
            entitlement_type    TEXT          NULL,
            entitlement_year    INT           NULL,
            sequence            INT           NULL,
            bucket              TEXT          NULL,
            processing_status   TEXT          NOT NULL CHECK (processing_status IN ('RETRY_PENDING', 'PROCESSED', 'SKIPPED_RECONCILED', 'DEAD_LETTER')),
            attempts            INT           NOT NULL DEFAULT 0 CHECK (attempts >= 0),
            last_error          TEXT          NULL,
            processed_at        TIMESTAMPTZ   NULL,
            created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_settlement_payroll_inbox_retry_pending
            ON settlement_payroll_inbox (processing_status)
            WHERE processing_status = 'RETRY_PENDING';

        CREATE INDEX IF NOT EXISTS idx_settlement_payroll_inbox_settlement
            ON settlement_payroll_inbox (employee_id, entitlement_type, entitlement_year, sequence, bucket);

        -- Live legacy rows.
        INSERT INTO settlement_payroll_inbox
            (source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket,
             processing_status, attempts, last_error)
        VALUES
            -- (a) poison: identity + bucket NULL, DEAD_LETTER (S69 Step-7a FIX 1).
            ('a4f0c6f2-0000-4000-8000-000000007105', NULL, NULL, NULL, NULL, NULL,
             'DEAD_LETTER', 3, 'poison: cannot deserialize'),
            -- (b) normal PROCESSED §24 row (bucket populated — the S69 writer shape).
            ('a4f0c6f2-0001-4000-8000-000000007105', 'emp_s71_inbox', 'VACATION', 2024, 1, 'AUTO_PAYOUT_24',
             'PROCESSED', 0, NULL),
            -- (c) normal RETRY_PENDING §24 row (bucket populated).
            ('a4f0c6f2-0002-4000-8000-000000007105', 'emp_s71_inbox', 'VACATION', 2023, 1, 'AUTO_PAYOUT_24',
             'RETRY_PENDING', 2, 'no mapping yet'),
            -- (d) DEFENSIVE: a normal-identity row with NULL bucket (the R7 step-2 pinned backfill).
            ('a4f0c6f2-0003-4000-8000-000000007105', 'emp_s71_inbox', 'VACATION', 2022, 1, NULL,
             'PROCESSED', 0, NULL);
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
    public async Task Migration_S71_InboxCompositeBucketKey_LegacyAlter_Idempotent()
    {
        var segment = ExtractInboxSegmentFromCanonicalInitSql();

        // ── First apply: the legacy upgrade path ─────────────────────────────
        await ApplyAsync(segment);

        // R7 step 1 — the poison row moved to the '_EVENT' sentinel; identity still NULL,
        // status/attempts untouched.
        var poison = await ReadInboxRowAsync(PoisonEventId);
        Assert.Equal("_EVENT", poison.Bucket);
        Assert.Equal("DEAD_LETTER", poison.Status);
        Assert.Equal(3, poison.Attempts);
        Assert.Null(poison.EmployeeId);

        // R7 step 2 — the defensive NULL-bucket NORMAL row backfilled to the §24 bucket.
        var defensive = await ReadInboxRowAsync(NullBucketNormalEventId);
        Assert.Equal("AUTO_PAYOUT_24", defensive.Bucket);
        Assert.Equal("PROCESSED", defensive.Status);
        Assert.Equal("emp_s71_inbox", defensive.EmployeeId);

        // The populated normal rows are untouched.
        Assert.Equal("AUTO_PAYOUT_24", (await ReadInboxRowAsync(ProcessedEventId)).Bucket);
        Assert.Equal("RETRY_PENDING", (await ReadInboxRowAsync(RetryEventId)).Status);

        // R7 step 3 — bucket NOT NULL is enforced.
        var exNull = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            """
            INSERT INTO settlement_payroll_inbox (source_event_id, bucket, processing_status)
            VALUES (gen_random_uuid(), NULL, 'PROCESSED')
            """));
        Assert.Equal(NotNullViolation, exNull.SqlState);

        // R7 steps 4+5 — the composite PK: TWO rows for ONE source_event_id (different buckets)
        // are admitted (the multi-row-per-event admission)…
        var multiEvent = Guid.NewGuid();
        await ExecAsync(
            $"""
            INSERT INTO settlement_payroll_inbox
                (source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket, processing_status)
            VALUES
                ('{multiEvent}', 'emp_s71_inbox', 'VACATION', 2024, 2, 'AUTO_PAYOUT_24', 'PROCESSED'),
                ('{multiEvent}', 'emp_s71_inbox', 'VACATION', 2024, 2, 'TERMINATION_PAYOUT_26', 'PROCESSED')
            """);
        // …while a duplicate (event, bucket) is rejected on the pinned PK name.
        var exDup = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            $"""
            INSERT INTO settlement_payroll_inbox (source_event_id, bucket, processing_status)
            VALUES ('{multiEvent}', 'AUTO_PAYOUT_24', 'PROCESSED')
            """));
        Assert.Equal(UniqueViolation, exDup.SqlState);
        Assert.Equal("settlement_payroll_inbox_pkey", exDup.ConstraintName);

        // R7 — the widened CHECK admits SKIPPED_VOIDED and still rejects an unknown status.
        await ExecAsync(
            """
            INSERT INTO settlement_payroll_inbox (source_event_id, bucket, processing_status)
            VALUES (gen_random_uuid(), 'TERMINATION_PAYOUT_26', 'SKIPPED_VOIDED')
            """);
        var exCheck = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            """
            INSERT INTO settlement_payroll_inbox (source_event_id, bucket, processing_status)
            VALUES (gen_random_uuid(), 'AUTO_PAYOUT_24', 'BOGUS_STATUS')
            """));
        Assert.Equal(CheckViolation, exCheck.SqlState);

        // The S69 inline auto-named CHECK is gone; the pinned name exists exactly once.
        Assert.Equal(0, await CountConstraintAsync("settlement_payroll_inbox_processing_status_check"));
        Assert.Equal(1, await CountConstraintAsync("settlement_payroll_inbox_processing_status"));
        Assert.Equal(1, await CountConstraintAsync("settlement_payroll_inbox_pkey"));
        Assert.Equal(1, await CountLedgerRowsAsync());

        // ── Second apply: ledger-guarded no-op (double-apply idempotence) ────
        await ApplyAsync(segment);

        Assert.Equal(1, await CountLedgerRowsAsync());
        Assert.Equal(1, await CountConstraintAsync("settlement_payroll_inbox_processing_status"));
        Assert.Equal(1, await CountConstraintAsync("settlement_payroll_inbox_pkey"));

        // Data written between the two applies — incl. the multi-bucket pair and the legacy
        // backfills — is untouched by the re-run.
        Assert.Equal("_EVENT", (await ReadInboxRowAsync(PoisonEventId)).Bucket);
        Assert.Equal("AUTO_PAYOUT_24", (await ReadInboxRowAsync(NullBucketNormalEventId)).Bucket);
        Assert.Equal(2, await CountRowsForEventAsync(multiEvent));
    }

    /// <summary>SPRINT-71 Step-5a cycle-1 W1 — the pre-flight validates-or-aborts (fail-closed):
    /// a NULL-bucket row in a HYBRID identity shape (here: <c>employee_id</c> populated, every
    /// other identity column NULL — schema-admitted pre-S71, produced by NO canonical writer) makes
    /// the migration block RAISE EXCEPTION naming the offending <c>source_event_id</c> and roll
    /// back ENTIRELY — no ledger row, no backfill, the old single-column PK and the S69 inline
    /// auto-named CHECK still standing. Silent misclassification (the hybrid row would otherwise
    /// have been backfilled to the §24 bucket as if normal) must never happen.</summary>
    [Fact]
    public async Task Migration_S71_HybridNullBucketRow_PreflightAborts_NothingMutated()
    {
        var hybridId = Guid.Parse("a4f0c6f2-0004-4000-8000-000000007105");
        await ExecAsync(
            $"""
            INSERT INTO settlement_payroll_inbox
                (source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket,
                 processing_status, attempts, last_error)
            VALUES ('{hybridId}', 'emp_s71_inbox', NULL, NULL, NULL, NULL,
                    'RETRY_PENDING', 1, 'hybrid identity shape - neither poison nor normal')
            """);

        var segment = ExtractInboxSegmentFromCanonicalInitSql();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(segment));
        Assert.Equal(RaiseException, ex.SqlState);          // the explicit pre-flight RAISE, not an incidental error
        Assert.Contains(hybridId.ToString(), ex.Message);    // the offending row is NAMED for remediation

        // NOTHING mutated — the whole DO-block rolled back, ledger row included:
        Assert.Equal(0, await CountLedgerRowsAsync());
        // …no backfill ran (both legacy NULL buckets are still NULL, the hybrid row untouched)…
        Assert.Null((await ReadInboxRowAsync(PoisonEventId)).Bucket);
        Assert.Null((await ReadInboxRowAsync(NullBucketNormalEventId)).Bucket);
        Assert.Null((await ReadInboxRowAsync(hybridId)).Bucket);
        Assert.Equal("RETRY_PENDING", (await ReadInboxRowAsync(hybridId)).Status);
        // …the S69 inline auto-named CHECK still stands and the widened named form was never added…
        Assert.Equal(1, await CountConstraintAsync("settlement_payroll_inbox_processing_status_check"));
        Assert.Equal(0, await CountConstraintAsync("settlement_payroll_inbox_processing_status"));
        // …and the OLD single-column PK still governs: a second bucket row for an existing event
        // (admitted by the composite key) is REFUSED.
        var exOldPk = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            $"""
            INSERT INTO settlement_payroll_inbox (source_event_id, bucket, processing_status)
            VALUES ('{ProcessedEventId}', 'TERMINATION_PAYOUT_26', 'PROCESSED')
            """));
        Assert.Equal(UniqueViolation, exOldPk.SqlState);
    }

    // ─── segment extraction (the Slice3bLegacyMigrationTests pattern) ───────

    private const string SegmentBeginMarker = "-- S71-INBOX-SEGMENT-BEGIN";
    private const string SegmentEndMarker = "-- S71-INBOX-SEGMENT-END";

    private static string ExtractInboxSegmentFromCanonicalInitSql()
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

    private readonly record struct InboxRow(string? Bucket, string Status, int Attempts, string? EmployeeId);

    private async Task<InboxRow> ReadInboxRowAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT bucket, processing_status, attempts, employee_id
            FROM settlement_payroll_inbox WHERE source_event_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", eventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Expected an inbox row for {eventId}.");
        return new InboxRow(
            reader.IsDBNull(0) ? null : reader.GetString(0), // bucket stays NULL on the aborted-migration path
            reader.GetString(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async Task<int> CountRowsForEventAsync(Guid eventId)
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM settlement_payroll_inbox WHERE source_event_id = @p0", eventId));
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
