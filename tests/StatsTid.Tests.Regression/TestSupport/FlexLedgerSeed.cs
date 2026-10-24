using Npgsql;
using NpgsqlTypes;
using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S146 / TASK-14605-T (R-14) — the two ways a Regression fact gives <see cref="PeriodCalculationService"/> a
/// flex ledger now that the opening balance is DERIVED from the recorded months and no longer handed in.
///
/// <para>
/// Plain language: flex is the running balance of hours above or below the norm. Before S146 a test told the
/// calculation "this month opens at X". Now payroll reads the employee's recorded months (the "ledger") and the
/// opening is the previous month's recorded closing (R-14 case 4a, CHAINED), or 0 when the ledger is empty
/// (case 2, LEDGER_START). A fact that is not about that chain hands <see cref="InMemoryFlexLedgerReader"/> with
/// no records ("empty ledger, opens at 0", exactly what <c>previousFlexBalance: 0m</c> said). A fact that needs a
/// non-zero opening hands it an employed previous month with a recorded closing: through the in-memory reader
/// where the fact writes no record, or through <see cref="SeedAsync"/> (a real <c>payroll_export_records</c> row)
/// where the fact runs over the database.
/// </para>
/// </summary>
internal static class FlexLedgerSeed
{
    /// <summary>
    /// Writes ONE ledger record (a calculating route's row: all three balance columns set) for
    /// <paramref name="month"/>, so the next month's derivation reads <paramref name="closing"/> as its CHAINED
    /// opening. The harness must have <c>payroll_export_records</c> (<c>PayrollExportRecordsTestSchema</c>).
    /// Throws when a record for (employee, year, month) already exists (S146 TASK-14606, N4): a conflicting seed is loud.
    /// </summary>
    public static async Task SeedAsync(
        string connectionString, string employeeId, DateOnly month, decimal closing,
        decimal opening = 0m, string provenance = FlexLedgerOpening.LedgerStart, CancellationToken ct = default,
        string? contentHash = null)
    {
        // S146 TASK-14606: <paramref name="contentHash"/> lets a fact seed a record whose lines hash equals what a
        // calculation of the same (empty) line set computes, so a re-run reaches the triple compare instead of the
        // hash compare. Default: the placeholder the earlier facts used.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source,
                opening_balance, closing_balance, balance_provenance
            ) VALUES (
                @id, NULL, @emp, @year, @month, NOW(),
                '[]'::jsonb, '[]'::jsonb, @hash, 'CALCULATE_AND_EXPORT',
                @opening, @closing, @prov
            )
            ON CONFLICT (employee_id, year, month) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        cmd.Parameters.AddWithValue("hash", contentHash ?? "s146-flex-ledger-seed");
        cmd.Parameters.Add(new NpgsqlParameter("opening", NpgsqlDbType.Numeric) { Value = opening });
        cmd.Parameters.Add(new NpgsqlParameter("closing", NpgsqlDbType.Numeric) { Value = closing });
        cmd.Parameters.AddWithValue("prov", provenance);
        // S146 TASK-14606 (Step 5a N4): a conflicting seed must be LOUD. ON CONFLICT DO NOTHING alone would leave the
        // earlier row in place and let the fact run on a ledger it did not write.
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows != 1)
        {
            throw new InvalidOperationException(
                $"FlexLedgerSeed.SeedAsync expected to insert one record for {employeeId} {month:yyyy-MM}, inserted {rows}: " +
                "a record for that month already exists (use FlexLedgerFill.FillBalancesAsync to give it balances).");
        }
    }
}

/// <summary>
/// S146 / TASK-14605-T — turns an existing raw-recorded month into a LEDGER record in place: one UPDATE sets
/// the three balance columns together (the CHECK needs all or none) and leaves the record's lines, hash and source
/// exactly as the fact wrote them. Returns once the row is updated; fails the fact when no row exists.
/// </summary>
internal static class FlexLedgerFill
{
    public static async Task FillBalancesAsync(
        string connectionString, string employeeId, DateOnly month, decimal opening, decimal closing,
        string provenance = FlexLedgerOpening.LedgerStart, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE payroll_export_records
               SET opening_balance = @opening, closing_balance = @closing, balance_provenance = @prov
             WHERE employee_id = @emp AND year = @year AND month = @month
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        cmd.Parameters.Add(new NpgsqlParameter("opening", NpgsqlDbType.Numeric) { Value = opening });
        cmd.Parameters.Add(new NpgsqlParameter("closing", NpgsqlDbType.Numeric) { Value = closing });
        cmd.Parameters.AddWithValue("prov", provenance);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows != 1)
            throw new InvalidOperationException($"FillBalancesAsync expected one record for {employeeId} {month:yyyy-MM}, updated {rows}.");
    }
}

/// <summary>
/// S146 / TASK-14605-T (R-14) — the in-memory <see cref="IFlexLedgerReader"/> for Regression facts that calculate
/// but never write a record (the Regression twin of <c>StatsTid.Tests.Unit.Payroll.InMemoryFlexLedgerReader</c>).
/// Built with no records it is the "empty ledger" (the month opens at LEDGER_START 0, R-14 case 2); built with one
/// <see cref="LedgerRecord"/> of the previous month it yields that closing as a CHAINED opening (case 4a). Both
/// surfaces answer from the same list.
/// </summary>
internal sealed class InMemoryFlexLedgerReader : IFlexLedgerReader
{
    private readonly List<FlexLedgerRecord> _records = new();

    public InMemoryFlexLedgerReader(params FlexLedgerRecord[] records) => _records.AddRange(records);

    /// <summary>Adds one record (a fact that seeds the ledger after the service is built).</summary>
    public InMemoryFlexLedgerReader Add(FlexLedgerRecord record)
    {
        _records.Add(record);
        return this;
    }

    /// <summary>A ledger record for the month holding <paramref name="month"/> that closed at <paramref name="closing"/>.</summary>
    public static FlexLedgerRecord LedgerRecord(DateOnly month, decimal closing, decimal opening = 0m) =>
        new(month.Year, month.Month, opening, closing, FlexLedgerOpening.LedgerStart);

    /// <summary>The reader whose only record is the ledger record of the month BEFORE <paramref name="month"/>.</summary>
    public static InMemoryFlexLedgerReader ClosedAt(DateOnly month, decimal closing) =>
        new(LedgerRecord(month.AddMonths(-1), closing));

    public Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(string employeeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FlexLedgerRecord>>(
            _records.OrderBy(r => r.Year).ThenBy(r => r.Month).ToList());

    public Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default) =>
        ReadRecordsAsync(employeeId, ct);
}
