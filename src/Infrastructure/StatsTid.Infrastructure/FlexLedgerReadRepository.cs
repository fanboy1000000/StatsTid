using System.Globalization;
using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S146 / TASK-14607 — one employee's latest RECORDED flex closing: the newest ledger record on
/// <c>payroll_export_records</c> for that employee, as the three flex screens show it.
///
/// <para>
/// <b>Plain language.</b> Flex is the running balance of hours worked above or below the norm.
/// Since TASK-14605 each paid month's export record carries what the month opened at and closed
/// at. The employee's "Flex saldo", the year overview's flex tile and the leader's team overview
/// show the closing of the newest such month, labelled with that month — "the balance as of the
/// last settled month" — instead of a flex-balance event that nothing ever wrote (S146 deletes the
/// event type), which made every screen read 0.
/// </para>
/// </summary>
/// <param name="Year">The recorded month's year.</param>
/// <param name="Month">The recorded month (1–12).</param>
/// <param name="OpeningBalance">The flex balance the month opened at (<c>opening_balance</c>).</param>
/// <param name="ClosingBalance">The flex balance the month closed at (<c>closing_balance</c>) — the
/// figure the screens show.</param>
public sealed record RecordedFlexClosing(int Year, int Month, decimal OpeningBalance, decimal ClosingBalance)
{
    /// <summary>The recorded month as <c>"YYYY-MM"</c> (invariant digits) — the ONE encoding of the
    /// month label every flex screen puts on the wire, so the three cannot drift apart.</summary>
    public string MonthLabel =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");

    /// <summary>The month's movement, <c>closing − opening</c>: the change that produced
    /// <see cref="ClosingBalance"/>, so <c>ClosingBalance − Delta</c> is the balance before it.</summary>
    public decimal Delta => ClosingBalance - OpeningBalance;
}

/// <summary>
/// S146 / TASK-14607 (refinement C5, AC-C9; ADR-034 D4) — the Backend's read-only view of the flex
/// ledger. ONE query serves both shapes: the single read (<c>/summary</c>, the year overview) is
/// the batch read with one id, so a person's own screen and their leader's team overview can never
/// pick a different "latest" month.
///
/// <para>
/// <b>Cross-context, read-only.</b> <c>payroll_export_records</c> belongs to the Payroll context,
/// which is its only writer (ADR-034 D4). The Backend already reads it read-only for the reopen
/// lock and the team overview's <c>payrollExported</c> flag; this is one more SELECT under the same
/// contract. This file holds no statement that changes any table, and it does not reference the
/// Payroll context's own ledger reader (a separate bounded context): the column semantics are
/// restated here, and the shared database schema is the contract between the two.
/// </para>
///
/// <para>
/// <b>What "latest recorded closing" means</b> (TASK-14601 / TASK-14605 terms):
/// <list type="bullet">
///   <item>A <i>ledger record</i> is a row with <c>opening_balance IS NOT NULL</c>. A CHECK
///   (<c>payroll_export_records_balances_paired</c>) makes the three balance columns all-null or
///   all-set, so <c>closing_balance</c> is set on every ledger record.</item>
///   <item>A <i>raw</i> record (written by the raw export routes, or before the ledger existed)
///   carries no balances and is ignored, even when it is the employee's newest month.</item>
///   <item>The latest one is the ledger record with the greatest <c>(year, month)</c>. The key
///   <c>UNIQUE (employee_id, year, month)</c> makes that order total, so the pick is deterministic.
///   Its index narrows the read to the requested employees, but it is ascending while the query
///   sorts year and month DESCENDING and filters on <c>opening_balance IS NOT NULL</c>, so the
///   planner may still sort the matched rows — immaterial at roster size (a few dozen employees,
///   one row per paid month each).</item>
///   <item>A month in which the employee was employed but registered nothing still writes a ledger
///   record (no lines, balances set); it is a valid latest closing like any other.</item>
/// </list>
/// An employee with no ledger record has no entry: the caller shows "ingen afregnet måned"
/// ("no settled month"), never a made-up 0.
/// </para>
///
/// <para>
/// <b>What kind of figure it is.</b> Two Payroll routes put balances on a record. The calculating
/// export writes them only for a month it re-asserts APPROVED under the approval row's lock
/// (ADR-034 D4). The correction route (GlobalAdmin only, no approval read) recomputes a month
/// already sent to payroll, and may fill the balances on a month the raw export routes recorded —
/// those bypass approval by design and are GlobalAdmin only too. Either way the figure is a
/// payroll fact fixed when that route committed, for a month already sent to payroll (which the
/// reopen refuses from then on, ADR-034 D5), not a running total of what the employee is typing.
/// Who may read whose figure is decided by the existing scope-checked handlers that call this,
/// not here.
/// </para>
///
/// <para>
/// <b>Shape.</b> A singleton over the connection factory (stateless; no clock, no writes, no
/// events, no employment dates). The single read opens its own connection; the batch read rides
/// the caller's open connection, because the team overview already holds one for its other
/// set-based reads (the shape of the <c>PostgresEventStore</c> batch read it replaces there).
/// </para>
/// </summary>
public sealed class FlexLedgerReadRepository
{
    // The ONE query. Parameterized (CA2100): the employee ids travel as a bound array.
    private const string LatestClosingSql =
        """
        SELECT DISTINCT ON (employee_id) employee_id, year, month, opening_balance, closing_balance
        FROM payroll_export_records
        WHERE employee_id = ANY(@ids)
          AND opening_balance IS NOT NULL
        ORDER BY employee_id, year DESC, month DESC
        """;

    private readonly DbConnectionFactory _dbFactory;

    public FlexLedgerReadRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// The single read: <paramref name="employeeId"/>'s latest recorded closing, or <c>null</c> when
    /// the employee has no ledger record. Runs the batch query with one id.
    /// </summary>
    public async Task<RecordedFlexClosing?> GetLatestClosingAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        var byEmployee = await GetLatestClosingsAsync(conn, new[] { employeeId }, ct);
        return byEmployee.TryGetValue(employeeId, out var closing) ? closing : null;
    }

    /// <summary>
    /// The batch read: <c>employee_id → latest recorded closing</c> for each of
    /// <paramref name="employeeIds"/> that has a ledger record, in ONE round-trip on the caller's
    /// open <paramref name="conn"/> (never committed, rolled back or closed here). An employee with
    /// no ledger record is absent from the result; an empty input returns an empty map without
    /// touching the database.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, RecordedFlexClosing>> GetLatestClosingsAsync(
        NpgsqlConnection conn, IReadOnlyCollection<string> employeeIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(employeeIds);

        var result = new Dictionary<string, RecordedFlexClosing>(StringComparer.Ordinal);
        if (employeeIds.Count == 0)
            return result;

        await using var cmd = new NpgsqlCommand(LatestClosingSql, conn);
        cmd.Parameters.AddWithValue("ids", employeeIds.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = new RecordedFlexClosing(
                Year: reader.GetInt32(1),
                Month: reader.GetInt32(2),
                OpeningBalance: reader.GetDecimal(3),
                ClosingBalance: reader.GetDecimal(4));
        }

        return result;
    }
}
