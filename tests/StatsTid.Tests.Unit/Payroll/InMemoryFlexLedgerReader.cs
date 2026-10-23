using Npgsql;
using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S146 / TASK-14605-T (R-14) — the in-memory <see cref="IFlexLedgerReader"/> the Unit suites give
/// <see cref="PeriodCalculationService"/>.
///
/// <para>
/// Plain language: flex is the running balance of hours above or below the norm. Payroll no longer takes the month's
/// opening balance from the caller; it derives it from the employee's recorded months (the "ledger"). A test that
/// is not about that chain hands this reader with NO records: the ledger is empty, so the month opens at
/// LEDGER_START 0 (R-14 case 2), which is exactly what the former <c>previousFlexBalance: 0m</c> said. A test that
/// needs a non-zero opening hands it ONE record for the previous month (an employed P with a recorded closing), and
/// the derivation yields that closing as a CHAINED opening (R-14 case 4a).
/// </para>
///
/// <para>
/// Both interface surfaces answer from the same list, ordered by (year, month), like the Postgres reader. The
/// <c>(conn, tx)</c> surface is the under-lock recheck read; a Unit fact never reaches it with a real connection, so
/// it answers from the same list and ignores the connection.
/// </para>
/// </summary>
internal sealed class InMemoryFlexLedgerReader : IFlexLedgerReader
{
    private readonly List<FlexLedgerRecord> _records = new();

    /// <summary>The reader that answers "no ledger records" (an empty ledger: LEDGER_START 0).</summary>
    public InMemoryFlexLedgerReader(params FlexLedgerRecord[] records) => _records.AddRange(records);

    /// <summary>The employee's reads, in call order, for a fact that asserts how often the ledger was read.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Adds one record.</summary>
    public InMemoryFlexLedgerReader Add(FlexLedgerRecord record)
    {
        _records.Add(record);
        return this;
    }

    /// <summary>
    /// A LEDGER record for <paramref name="month"/> (any day of it) that closed at <paramref name="closing"/>: what
    /// a calculating export of that month stored. The opening is a placeholder 0 (only the closing is read by 4a).
    /// </summary>
    public static FlexLedgerRecord LedgerRecord(DateOnly month, decimal closing, decimal opening = 0m) =>
        new(month.Year, month.Month, opening, closing, FlexLedgerOpening.LedgerStart);

    /// <summary>The reader whose only record is the ledger record of the month before <paramref name="month"/>.</summary>
    public static InMemoryFlexLedgerReader ClosedAt(DateOnly month, decimal closing) =>
        new(LedgerRecord(month.AddMonths(-1), closing));

    public Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(string employeeId, CancellationToken ct = default)
    {
        ReadCount++;
        return Task.FromResult<IReadOnlyList<FlexLedgerRecord>>(
            _records.OrderBy(r => r.Year).ThenBy(r => r.Month).ToList());
    }

    public Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default) =>
        ReadRecordsAsync(employeeId, ct);
}
