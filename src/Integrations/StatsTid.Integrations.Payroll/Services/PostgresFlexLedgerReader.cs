using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14605 — the production <see cref="IFlexLedgerReader"/>: both surfaces run the ONE query owned by
/// <see cref="PayrollExportRecordRepository.ReadFlexLedgerRecordsAsync"/> over <c>payroll_export_records</c>
/// (Payroll owns the table, ADR-034), so admission and the under-lock recheck cannot read different columns.
/// </summary>
public sealed class PostgresFlexLedgerReader : IFlexLedgerReader
{
    private readonly DbConnectionFactory _connectionFactory;

    public PostgresFlexLedgerReader(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await PayrollExportRecordRepository.ReadFlexLedgerRecordsAsync(conn, tx: null, employeeId, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        return PayrollExportRecordRepository.ReadFlexLedgerRecordsAsync(conn, tx, employeeId, ct);
    }
}
