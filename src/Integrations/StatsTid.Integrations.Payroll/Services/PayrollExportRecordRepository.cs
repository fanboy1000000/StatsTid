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
        string employeeId, int year, int month, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text
            FROM payroll_export_records
            WHERE employee_id = @employeeId AND year = @year AND month = @month
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull)
            return null;

        return PayrollExportManifest.Deserialize((string)result);
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
