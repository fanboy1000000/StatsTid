using Npgsql;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14605 (QUAL-188; ruling R-14, reviewer c3 W2) — the flex ledger's one read: an employee's
/// <c>payroll_export_records</c> rows with their balance columns, as <see cref="FlexLedgerOpening.Derive"/>
/// consumes them (ledger and raw records alike).
///
/// <para>
/// <b>Two surfaces, one answer.</b> <see cref="PeriodCalculationService"/>'s admission reads through the
/// SELF-MANAGED surface before the calculation (no lock is held while the Rule Engine is called); the
/// under-lock recheck reads through the <c>(conn, tx)</c> surface inside the writing transaction, after the
/// per-employee ledger lock, so it sees every write committed while it waited. The same function runs on both
/// reads (R-14).
/// </para>
///
/// <para>
/// <b>Optional but never silent.</b> A constructor dependency of <see cref="PeriodCalculationService"/>, optional
/// so plan-only instances need none; a calculation that reaches the function without one throws
/// <see cref="InvalidOperationException"/> naming the registration. A fixture not about the chain passes a fake
/// answering "no ledger records" (the ledger is empty, so the month opens at <c>LEDGER_START 0</c>) and says so in
/// a comment. Defaulting silently to 0 would be QUAL-188 again.
/// </para>
/// </summary>
public interface IFlexLedgerReader
{
    /// <summary>
    /// Every export record of <paramref name="employeeId"/>, on a connection of its own (admission). Ordered by
    /// (year, month); empty when the employee has none.
    /// </summary>
    Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(string employeeId, CancellationToken ct = default);

    /// <summary>
    /// The same read riding the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> (the under-lock
    /// recheck); never commits, rolls back or closes them. Under READ COMMITTED, after the ledger lock, it sees
    /// every record committed before the lock was granted.
    /// </summary>
    Task<IReadOnlyList<FlexLedgerRecord>> ReadRecordsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default);
}
