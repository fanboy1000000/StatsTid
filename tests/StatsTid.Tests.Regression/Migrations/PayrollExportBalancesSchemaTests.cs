using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S146 / TASK-14601-T (AC-C7 schema half, AC-C8) — schema pins for the flex ledger on
/// <c>payroll_export_records</c>, run on the FULL canonical schema
/// (<see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/>).
///
/// <para><b>Plain language.</b> Each paid month's export record now remembers the flex balance the
/// month opened at, the one it closed at, and where the opening came from (LEDGER_START = the
/// employee's first ledger month, CHAINED = the previous month's closing, REHIRE = a new
/// employment spell). The three values travel together: all empty (a raw export, or a record from
/// before the ledger) or all filled in. The database itself enforces that, so no code path can
/// store a balance with no stated origin or an origin with no balance. UNTRACKED is a state of a
/// calculation result, never a stored origin, so the database refuses it too. The old
/// <c>flex_balance_snapshots</c> table, which this replaces, must be gone.</para>
///
/// <para>Spec: refinement rev 3.2 C1 and C2 Terms (three nullable columns, all null or all set,
/// enforced by a CHECK), R-33 item 3 (NUMERIC without scale; <c>balance_provenance</c> TEXT with a
/// CHECK on exactly LEDGER_START, CHAINED, REHIRE; the harness copy text-identical), AC-C8
/// (<c>flex_balance_snapshots</c> dropped in the upgrade segment). Constraint name:
/// <c>payroll_export_records_balances_paired</c>.</para>
///
/// <para><b>Kinds and CI status.</b> Every fact here is kind F (it fails on the pre-S146 schema) and
/// Docker-gated: they are CI-pending (cannot run locally) and flip to CI-verified only at CI-W1.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollExportBalancesSchemaTests : IAsyncLifetime
{
    private const string SegmentBegin = "-- S146-FLEX-LEDGER-SEGMENT-BEGIN";
    private const string SegmentEnd = "-- S146-FLEX-LEDGER-SEGMENT-END";

    private TestFixtures.DockerHarness _harness = null!;
    private int _row;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // A literal statement with parameters only for the values under test (no interpolation, CA2100 untouched).
    // The three balance columns are named explicitly, so on a schema without them the insert fails 42703.
    private const string InsertRecordSql =
        """
        INSERT INTO payroll_export_records
            (export_id, employee_id, year, month, original_lines, current_effective_lines, content_hash,
             opening_balance, closing_balance, balance_provenance)
        VALUES
            (gen_random_uuid(), @emp, 2026, @month, '[]'::jsonb, '[]'::jsonb, 'hash',
             @opening, @closing, @provenance)
        """;

    private async Task<int> InsertAsync(decimal? opening, decimal? closing, string? provenance)
    {
        // A fresh (employee, month) per call: UNIQUE (employee_id, year, month) must never be what fails.
        var n = Interlocked.Increment(ref _row);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // constant SQL, values travel as parameters
        await using var cmd = new NpgsqlCommand(InsertRecordSql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("emp", "emp_s146_bal_" + n);
        cmd.Parameters.AddWithValue("month", (n % 12) + 1);
        cmd.Parameters.Add(new NpgsqlParameter("opening", NpgsqlTypes.NpgsqlDbType.Numeric) { Value = (object?)opening ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("closing", NpgsqlTypes.NpgsqlDbType.Numeric) { Value = (object?)closing ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("provenance", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)provenance ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task AssertRejected23514Async(decimal? opening, decimal? closing, string? provenance)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(opening, closing, provenance));
        Assert.Equal("23514", ex.SqlState);
        Assert.Equal("payroll_export_records_balances_paired", ex.ConstraintName);
    }

    /// <summary>
    /// <b>Kind F, CI-pending.</b> A record with all three balance columns NULL (a raw export, or a
    /// record written before the ledger) is accepted and reads back as three NULLs.
    ///
    /// <para><b>Red conditions.</b> On the pre-S146 schema the insert names <c>opening_balance</c>,
    /// which does not exist, so it throws <c>42703 undefined_column</c> and the first assertion
    /// (<c>Assert.Equal(1, rows)</c>) is never reached: the fact fails with that exception. (Not a
    /// missing CHECK: the columns themselves are what is absent.) It also fails if the CHECK is
    /// written so strictly that the all-NULL shape is refused (23514 instead of acceptance).</para>
    /// </summary>
    [Fact]
    public async Task Balances_AllNull_Accepted()
    {
        Assert.Equal(1, await InsertAsync(null, null, null));

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM payroll_export_records WHERE opening_balance IS NULL AND closing_balance IS NULL AND balance_provenance IS NULL",
            conn);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// <b>Kind F, CI-pending.</b> A record with all three set is accepted for each of the three stored
    /// provenances, and the balances read back exactly: a negative balance and more than four decimals
    /// are kept as written (NUMERIC without scale, R-33 item 3: no silent rounding).
    ///
    /// <para><b>Red conditions.</b> On the pre-S146 schema: <c>42703 undefined_column</c> at the insert.
    /// With a CHECK whose provenance list omits one of the three values: <c>23514</c> for that row.
    /// With <c>NUMERIC(8,2)</c> or similar: the read-back assertion fails (<c>12.3456789</c> comes back
    /// rounded).</para>
    /// </summary>
    [Theory]
    [InlineData("LEDGER_START")]
    [InlineData("CHAINED")]
    [InlineData("REHIRE")]
    public async Task Balances_AllSet_Accepted(string provenance)
    {
        Assert.Equal(1, await InsertAsync(-2.25m, 12.3456789m, provenance));

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT opening_balance, closing_balance, balance_provenance FROM payroll_export_records WHERE balance_provenance = @p",
            conn);
        cmd.Parameters.AddWithValue("p", provenance);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(-2.25m, reader.GetDecimal(0));
        Assert.Equal(12.3456789m, reader.GetDecimal(1));
        Assert.Equal(provenance, reader.GetString(2));
    }

    /// <summary>
    /// <b>Kind F, CI-pending.</b> Any partial shape is refused by the named CHECK: opening only,
    /// closing only, provenance only, and the three two-of-three shapes (opening and closing without provenance,
    /// opening and provenance without closing, closing and provenance without opening).
    /// SqlState <c>23514</c>, constraint <c>payroll_export_records_balances_paired</c>.
    ///
    /// <para><b>Red conditions.</b> TODAY (pre-S146 schema) every row fails with <c>42703
    /// undefined_column</c>, NOT with a missing CHECK: the columns do not exist, so
    /// <c>Assert.ThrowsAsync</c> returns the exception but <c>Assert.Equal("23514", ex.SqlState)</c>
    /// fails with <c>"42703"</c>. After the columns exist, it goes red again if the CHECK is dropped
    /// (the insert is accepted, no exception, <c>ThrowsAsync</c> fails), is renamed (the
    /// <c>ConstraintName</c> assertion fails), or is written with only the all-NULL branch loosened
    /// (any shape that now passes).</para>
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task Balances_Partial_Rejected23514(bool hasOpening, bool hasClosing, bool hasProvenance)
    {
        await AssertRejected23514Async(
            hasOpening ? 1.5m : null,
            hasClosing ? 2.5m : null,
            hasProvenance ? "CHAINED" : null);
    }

    /// <summary>
    /// <b>Kind F, CI-pending.</b> A fully set record whose provenance is not one of the three stored
    /// values is refused: <c>UNTRACKED</c> (a calculation-result state, never stored), another
    /// result-state name, a different letter case, and the empty string. SqlState <c>23514</c>,
    /// constraint <c>payroll_export_records_balances_paired</c>.
    ///
    /// <para><b>Red conditions.</b> TODAY (pre-S146 schema): <c>42703 undefined_column</c>, so the
    /// <c>SqlState</c> assertion fails with <c>"42703"</c> (the columns are absent; this is not a
    /// missing CHECK). After the columns exist: a CHECK that does not constrain the provenance list
    /// (the insert is accepted and <c>ThrowsAsync</c> fails), or one that lists UNTRACKED (that row
    /// is accepted).</para>
    /// </summary>
    [Theory]
    [InlineData("UNTRACKED")]
    [InlineData("FLEX_PAYOUT")]
    [InlineData("ledger_start")]
    [InlineData("")]
    public async Task Balances_UntrackedProvenance_Rejected23514(string provenance)
    {
        await AssertRejected23514Async(1.5m, 2.5m, provenance);
    }

    /// <summary>
    /// <b>Kind F, CI-pending (AC-C8).</b> The retired <c>flex_balance_snapshots</c> table does not
    /// exist on a database built from <c>init.sql</c>, and still does not after the S146 segment
    /// (the text between its BEGIN and END marker lines) is run a second time. The second run must
    /// also succeed and leave exactly one <c>payroll_export_records_balances_paired</c> constraint
    /// (the segment drops and re-adds it).
    ///
    /// <para><b>Red conditions.</b> TODAY (pre-S146 <c>init.sql</c>) the table is created by the base
    /// schema, so <c>to_regclass</c> returns a non-null name and the first <c>Assert.Null</c> fails; the
    /// marker lookup also throws (the S146 segment does not exist yet). After TASK-14601: removing
    /// <c>DROP TABLE IF EXISTS flex_balance_snapshots</c> from the segment while a base
    /// <c>CREATE TABLE</c> remains fails the first assertion; making the segment non-idempotent
    /// (a plain <c>ADD CONSTRAINT</c> without the drop, or <c>ADD COLUMN</c> without
    /// <c>IF NOT EXISTS</c>) makes the second run throw.</para>
    /// </summary>
    [Fact]
    public async Task SnapshotsTable_Absent_AfterSegment()
    {
        Assert.Null(await ToRegclassAsync("flex_balance_snapshots"));

        var segment = CanonicalInitSql.ExtractInclusiveRange(SegmentBegin, SegmentEnd);
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
#pragma warning disable CA2100 // the DDL is the shipped segment of init.sql, read from the repository file
            await using var cmd = new NpgsqlCommand(segment, conn);
#pragma warning restore CA2100
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Null(await ToRegclassAsync("flex_balance_snapshots"));

        await using var check = new NpgsqlConnection(_harness.ConnectionString);
        await check.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_constraint WHERE conname = 'payroll_export_records_balances_paired'", check);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    // The pre-S146 shape of the parts the segment touches: the old snapshots table (as the base schema
    // created it at 5dfce75d, with its two indexes) and a payroll_export_records without balances.
    private const string PutBackToPreS146ShapeSql =
        """
        ALTER TABLE payroll_export_records DROP CONSTRAINT payroll_export_records_balances_paired;
        ALTER TABLE payroll_export_records DROP COLUMN opening_balance;
        ALTER TABLE payroll_export_records DROP COLUMN closing_balance;
        ALTER TABLE payroll_export_records DROP COLUMN balance_provenance;

        CREATE TABLE flex_balance_snapshots (
            snapshot_id     UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id     TEXT        NOT NULL,
            period_start    DATE        NOT NULL,
            period_end      DATE        NOT NULL,
            balance_hours   DECIMAL     NOT NULL,
            delta           DECIMAL     NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX idx_flex_snapshots_employee ON flex_balance_snapshots(employee_id);
        CREATE INDEX idx_flex_snapshots_period ON flex_balance_snapshots(period_start, period_end);

        INSERT INTO payroll_export_records
            (export_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
        VALUES
            (gen_random_uuid(), 'emp_s146_pre', 2026, 3, '[]'::jsonb, '[]'::jsonb, 'pre-s146-hash');
        """;

    /// <summary>
    /// <b>Kind F, CI-pending (AC-C8, upgrade half).</b> On a database in the pre-S146 shape (the snapshots
    /// table exists; <c>payroll_export_records</c> has no balance columns and no CHECK; one record is
    /// already stored), running the S146 segment drops the snapshots table, adds the three columns,
    /// leaves the existing record intact with all three balances NULL (which the CHECK accepts), and
    /// leaves exactly one <c>payroll_export_records_balances_paired</c> constraint.
    ///
    /// <para><b>Plain language.</b> The other segment fact starts from a database that is already in
    /// the new shape, so it would stay green even if the segment forgot to drop the old table. This one
    /// starts from the old shape, so the segment has real work to do.</para>
    ///
    /// <para><b>Red conditions.</b> Remove <c>DROP TABLE IF EXISTS flex_balance_snapshots</c> from the
    /// segment: the "snapshots table is gone" assertion fails (the table is still there). Remove one
    /// of the three <c>ADD COLUMN</c> statements: the segment's own CHECK statement throws
    /// <c>42703 undefined_column</c> when it references the missing column, so the run fails before
    /// any assertion (and the column-count assertion would fail otherwise). Remove the CHECK statement:
    /// the constraint-count assertion reads 0 instead of 1. Add a NOT NULL default to the columns: the
    /// all-NULL assertion on the surviving record fails. (The base CREATE of the table in the test
    /// database is the post-S146 one; the pre-S146 shape is put back by the test itself.)</para>
    /// </summary>
    [Fact]
    public async Task Segment_OnPreS146Database_DropsSnapshotsAndKeepsRowsAllNull()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

#pragma warning disable CA2100 // constant DDL of this test
        await using (var prep = new NpgsqlCommand(PutBackToPreS146ShapeSql, conn))
            await prep.ExecuteNonQueryAsync();
#pragma warning restore CA2100

        // Preconditions: the old shape is really in place, so the assertions below prove the segment did the work.
        Assert.NotNull(await ToRegclassAsync("flex_balance_snapshots"));
        Assert.Equal(0L, await ScalarLongAsync(conn,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'payroll_export_records' AND column_name IN ('opening_balance','closing_balance','balance_provenance')"));

        var segment = CanonicalInitSql.ExtractInclusiveRange(SegmentBegin, SegmentEnd);
#pragma warning disable CA2100 // the DDL is the shipped segment of init.sql, read from the repository file
        await using (var run = new NpgsqlCommand(segment, conn))
            await run.ExecuteNonQueryAsync();
#pragma warning restore CA2100

        Assert.Null(await ToRegclassAsync("flex_balance_snapshots"));
        Assert.Equal(3L, await ScalarLongAsync(conn,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'payroll_export_records' AND column_name IN ('opening_balance','closing_balance','balance_provenance')"));
        Assert.Equal(1L, await ScalarLongAsync(conn,
            "SELECT count(*) FROM pg_constraint WHERE conname = 'payroll_export_records_balances_paired'"));
        Assert.Equal(1L, await ScalarLongAsync(conn,
            "SELECT count(*) FROM payroll_export_records WHERE employee_id = 'emp_s146_pre' AND content_hash = 'pre-s146-hash' AND opening_balance IS NULL AND closing_balance IS NULL AND balance_provenance IS NULL"));
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
#pragma warning disable CA2100 // constant SQL literals at the call sites above
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> ToRegclassAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT to_regclass('public.' || @t)::text", conn);
        cmd.Parameters.AddWithValue("t", table);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }
}
