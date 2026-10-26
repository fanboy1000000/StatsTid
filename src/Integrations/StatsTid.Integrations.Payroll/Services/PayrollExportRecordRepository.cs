using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S90 / TASK-9004 (ADR-034) — the data-access seam for the corrections path against the
/// <c>payroll_export_records</c> manifest. The atomic export path (TASK-9002) is the SOLE writer of
/// the row itself; this repository serves the corrections concern:
/// <list type="bullet">
/// <item><description><see cref="TryReadCurrentEffectiveLinesAsync"/> — reads
/// <c>current_effective_lines</c> as the diff baseline a <c>/recalculate</c> diffs against (B3),
/// returning <c>null</c> when the month was never exported (no row);</description></item>
/// <item><description><see cref="UpdateCurrentEffectiveLinesAsync"/> — rewrites
/// <c>current_effective_lines</c> + <c>content_hash</c> to the corrected lines INSIDE the caller's
/// correction tx, so a SECOND correction diffs against the FIRST correction's result, not the
/// original (B3 — the original-only baseline double-counts correction #1's delta).
/// <c>original_lines</c> is left immutable.</description></item>
/// </list>
///
/// <para>
/// <b>S146 / TASK-14605 (QUAL-188; R-14, R-15) — the flex ledger.</b> The record also carries the month's flex
/// balances (<c>opening_balance</c>, <c>closing_balance</c>, <c>balance_provenance</c>; all null or all set). This
/// type owns their reads and writes: <see cref="ReadFlexLedgerRecordsAsync"/> (the ledger read behind
/// <see cref="IFlexLedgerReader"/>, self-managed or in-transaction), <see cref="AcquireFlexLedgerLocksAsync"/> (the
/// per-employee ledger lock every writer of this table takes as the FIRST statement of its transaction),
/// <see cref="TryReadRecordForUpdateAsync"/> (the correction's locked baseline, now with the balances, reviewer c3
/// N4) and <see cref="UpdateCorrectedRecordAsync"/> (the correction's baseline update, now with the balances).
/// </para>
/// </summary>
public sealed class PayrollExportRecordRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public PayrollExportRecordRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Reads the evolving diff baseline (<c>current_effective_lines</c>) for the
    /// (employee, year, month) export record. Returns <c>null</c> when there is NO record — the
    /// month was never sent to payroll, so there is nothing to correct (the caller rejects with a
    /// clean 4xx; a never-exported month is reopened/edited, not corrected). Returns an (possibly
    /// empty) line list when the record exists.
    /// </summary>
    public async Task<IReadOnlyList<PayrollExportLine>?> TryReadCurrentEffectiveLinesAsync(
        string employeeId, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text
            FROM payroll_export_records
            WHERE employee_id = @employeeId AND year = @year AND month = @month
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull)
            return null;

        return PayrollExportManifest.Deserialize((string)result);
    }

    /// <summary>
    /// BLOCKER 3 — reads the evolving diff baseline (<c>current_effective_lines</c>) for the
    /// (employee, year, month) export record INSIDE the caller's correction <c>(conn, tx)</c> and
    /// takes a <c>SELECT … FOR UPDATE</c> row lock on the <c>payroll_export_records</c> row. The lock
    /// is HELD through the diff compute + the <see cref="UpdateCurrentEffectiveLinesAsync"/> + commit,
    /// so two concurrent <c>/recalculate</c> calls for the same (employee, month) SERIALIZE: the
    /// second blocks here until the first commits, then reads the FIRST correction's updated baseline
    /// (not the stale pre-correction baseline). Returns <c>null</c> when there is NO record (the month
    /// was never sent to payroll → nothing to correct; the caller rejects with a clean 4xx). Returns a
    /// (possibly empty) line list when the record exists.
    /// </summary>
    public static async Task<IReadOnlyList<PayrollExportLine>?> TryReadCurrentEffectiveLinesForUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, int year, int month, CancellationToken ct = default) =>
        (await TryReadRecordForUpdateAsync(conn, tx, employeeId, year, month, ct))?.Lines;

    /// <summary>
    /// S146 / TASK-14605 (C4, reviewer c3 N4) — the locked baseline read WITH the month's recorded flex balances:
    /// <see cref="TryReadCurrentEffectiveLinesForUpdateAsync"/>'s <c>SELECT … FOR UPDATE</c> (same row lock, same
    /// serialization of concurrent corrections) returning <c>current_effective_lines</c> together with
    /// <c>opening_balance</c>, <c>closing_balance</c> and <c>balance_provenance</c> (all null on a raw record). The
    /// correction reads the closing BEFORE it from here, inside its own transaction, to record it on the event and
    /// to decide whether the next month must be named. <c>null</c> when there is no record.
    /// </summary>
    public static async Task<LockedExportRecord?> TryReadRecordForUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, int year, int month, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text, opening_balance, closing_balance, balance_provenance
            FROM payroll_export_records
            WHERE employee_id = @employeeId AND year = @year AND month = @month
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new LockedExportRecord(
            PayrollExportManifest.Deserialize(reader.IsDBNull(0) ? null : reader.GetString(0)),
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    /// <summary>
    /// S146 / TASK-14605 (R-14) — the flex ledger read: every <c>payroll_export_records</c> row of
    /// <paramref name="employeeId"/> with its month and balance columns, ordered by (year, month). Self-managed
    /// when <paramref name="tx"/> is <c>null</c> (the caller's own open connection, admission), in-transaction
    /// otherwise (the under-lock recheck). Both <see cref="IFlexLedgerReader"/> surfaces run this one query.
    /// </summary>
    public static async Task<IReadOnlyList<FlexLedgerRecord>> ReadFlexLedgerRecordsAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string employeeId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT year, month, opening_balance, closing_balance, balance_provenance
            FROM payroll_export_records
            WHERE employee_id = @employeeId
            ORDER BY year, month
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);

        var records = new List<FlexLedgerRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            records.Add(new FlexLedgerRecord(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return records;
    }

    /// <summary>
    /// S146 / TASK-14605 (R-15; refinement C3) — the per-employee ledger lock. Every writer of
    /// <c>payroll_export_records</c> (<c>/export</c>, <c>/export-period</c>, <c>/calculate-and-export</c> through
    /// <see cref="PayrollExportService.ExportAsync"/>, and <c>/recalculate</c> through
    /// <see cref="RetroactiveCorrectionService"/>) calls this as the FIRST statement of its writing transaction,
    /// before any row lock. Transaction-scoped (<c>pg_advisory_xact_lock</c>): held to commit or rollback.
    ///
    /// <para>
    /// <b>One statement for several employees, ordered by the KEY</b> (reviewer c3 N2). <c>ExportAsync</c> writes
    /// every line group of its call in one transaction, so it locks every employee of the call at once, de-duplicated
    /// and ordered by the hashed key value: two ids whose <c>hashtext</c> collides are the same lock, so ordering by
    /// id could take two keys in opposite orders in two calls; ordering by key cannot. The keys are distinct by name
    /// (<c>payroll-ledger-</c> vs the <c>employee-</c> and org keys), and <c>hashtext</c> is 32-bit, so a collision is
    /// possible. A single collision is harmless: it only serializes two unrelated transactions, and no wait cycle can
    /// form, because every writer takes its advisory keys before any row lock (the Backend reopen does a plain,
    /// unlocked read first, <c>ReportingLineRepository.AcquireTreeLockForEmployeeAsync</c>, which takes no lock). A
    /// cycle among advisory locks alone would need two simultaneous collisions taken in opposite orders; Postgres's
    /// deadlock detector would then cancel one transaction, which is safe because both writers are transactional.
    /// Payroll transactions lock in one order: advisory, then approval rows, then records.
    /// </para>
    /// </summary>
    public static async Task AcquireFlexLedgerLocksAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyCollection<string> employeeIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT pg_advisory_xact_lock(k)
            FROM (SELECT DISTINCT hashtext('payroll-ledger-' || e) AS k FROM unnest(@employeeIds) AS e ORDER BY k) s;
            """, conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter("employeeIds", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = employeeIds.ToArray(),
        });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// S146 / TASK-14605 (C1; contract 9) — the correction's baseline update WITH the month's flex balances, in
    /// one statement inside the caller's correction <c>(conn, tx)</c>: <c>current_effective_lines</c> and
    /// <c>content_hash</c> exactly as <see cref="UpdateCurrentEffectiveLinesAsync"/> writes them, plus
    /// <c>opening_balance</c>, <c>closing_balance</c> and <c>balance_provenance</c> (all three set: the CHECK
    /// refuses a partial row and <c>UNTRACKED</c>). A correction whose balances stay unchanged (flex not tracked, or
    /// a month with no EMPLOYED day) calls <see cref="UpdateCurrentEffectiveLinesAsync"/> instead. Returns the
    /// number of rows updated.
    /// </summary>
    public static async Task<int> UpdateCorrectedRecordAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, int year, int month,
        IReadOnlyList<PayrollExportLine> newEffectiveLines,
        decimal openingBalance, decimal closingBalance, string provenance,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        var ordered = PayrollExportManifest.OrderLines(newEffectiveLines);
        var manifestJson = PayrollExportManifest.Serialize(ordered);
        var contentHash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE payroll_export_records
            SET current_effective_lines = @currentLines::jsonb,
                content_hash = @contentHash,
                opening_balance = @openingBalance,
                closing_balance = @closingBalance,
                balance_provenance = @balanceProvenance
            WHERE employee_id = @employeeId AND year = @year AND month = @month
            """, conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter("currentLines", NpgsqlDbType.Jsonb) { Value = manifestJson });
        cmd.Parameters.AddWithValue("contentHash", contentHash);
        cmd.Parameters.Add(new NpgsqlParameter("openingBalance", NpgsqlDbType.Numeric) { Value = openingBalance });
        cmd.Parameters.Add(new NpgsqlParameter("closingBalance", NpgsqlDbType.Numeric) { Value = closingBalance });
        cmd.Parameters.Add(new NpgsqlParameter("balanceProvenance", NpgsqlDbType.Text) { Value = provenance });
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// B3 — UPDATEs the evolving baseline (<c>current_effective_lines</c> + <c>content_hash</c>) to
    /// the correction's new effective lines, INSIDE the caller-supplied correction <c>(conn, tx)</c>
    /// so it commits atomically with the <c>RetroactiveCorrectionRequested</c> event + audit row.
    /// <c>original_lines</c> stays immutable. The lines are canonicalized (sorted) + serialized via
    /// <see cref="PayrollExportManifest"/> so the rewritten baseline matches the export-time hash
    /// semantics. Returns the number of rows updated (0 if the record vanished — the caller treats
    /// that as a never-exported / not-correctable condition).
    /// </summary>
    public static async Task<int> UpdateCurrentEffectiveLinesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, int year, int month,
        IReadOnlyList<PayrollExportLine> newEffectiveLines, CancellationToken ct = default)
    {
        var ordered = PayrollExportManifest.OrderLines(newEffectiveLines);
        var manifestJson = PayrollExportManifest.Serialize(ordered);
        var contentHash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE payroll_export_records
            SET current_effective_lines = @currentLines::jsonb,
                content_hash = @contentHash
            WHERE employee_id = @employeeId AND year = @year AND month = @month
            """, conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter("currentLines", NpgsqlDbType.Jsonb) { Value = manifestJson });
        cmd.Parameters.AddWithValue("contentHash", contentHash);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);

        return await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// S146 / TASK-14605 — the correction's locked baseline: <c>current_effective_lines</c> and the month's recorded
/// flex balances (all null on a raw record).
/// </summary>
public sealed record LockedExportRecord(
    IReadOnlyList<PayrollExportLine> Lines,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    string? Provenance);
