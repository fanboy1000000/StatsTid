using System.Globalization;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14605 (QUAL-188; rulings R-14, R-28) — a month whose opening flex balance cannot be derived
/// from the recorded ledger, refused by name instead of being opened at 0.
///
/// <para>
/// <b>Plain language.</b> Each paid month's export record holds the flex balance it opened and closed at.
/// The next month opens at the previous month's recorded closing (<see cref="FlexLedgerOpening"/>). When that
/// chain cannot be followed (the previous month was never recorded, or the month lies before the employee's
/// first recorded month) the system refuses and names the month the operator must deal with, because
/// guessing an opening is exactly the defect this sprint removes. A refusal leaves nothing behind.
/// </para>
///
/// <para>
/// <b>The four kinds.</b>
/// <list type="bullet">
/// <item><see cref="ExportOutOfOrder"/> (<c>payroll-export-out-of-order</c>) — admission: a month with no
/// export record that precedes the ledger start S; <see cref="Month"/> is S (R-14 case 3a).</item>
/// <item><see cref="PriorMonthNotRecorded"/> (<c>payroll-prior-month-not-recorded</c>) — admission: the
/// previous month P was employed but has no ledger record; <see cref="Month"/> is P, and the sentence says
/// "export it first", or "correct it first" when P has a raw export record (R-14 case 4b).</item>
/// <item><see cref="LedgerChanged"/> (<c>payroll-ledger-changed</c>) — the under-lock recheck inside the
/// writing transaction derived a different opening, or a refusal, than the calculation used; nothing was
/// written and the caller should retry. <see cref="Month"/> is the month whose write was refused.</item>
/// <item><see cref="PriorMonthStale"/> (<c>payroll-prior-month-stale</c>) — S147 TASK-14700 (QUAL-229, R-2):
/// admission's walk over the employee's recorded chain found a month X after the ledger start and up to P whose
/// recorded opening no longer follows from its predecessor (a later correction, or an employment-dates edit, made
/// it stale), or whose own re-derivation refuses; <see cref="Month"/> is the EARLIEST such X, the month to
/// recalculate first. Raised on both calculating routes; the under-lock recheck reports a chain that went stale
/// while the month was being computed as <see cref="LedgerChanged"/>, and the retry then names the month.</item>
/// </list>
/// The host maps every kind to a 409 <c>{ success:false, error, kind, month }</c>: <c>error</c> is the fixed
/// <see cref="ErrorSentence"/> (no employee id, no date, no digit), <c>month</c> the named month as
/// <c>yyyy-MM</c> (a pay month, never an employment date). <see cref="Exception.Message"/> is for diagnosis and
/// never reaches a response.
/// </para>
/// </summary>
public sealed class FlexLedgerRefusalException : Exception
{
    /// <summary>R-14 case 3a: an unrecorded month earlier than the ledger start S. Names S.</summary>
    public const string ExportOutOfOrder = "payroll-export-out-of-order";

    /// <summary>R-14 case 4b: the previous month P is employed but has no ledger record. Names P.</summary>
    public const string PriorMonthNotRecorded = "payroll-prior-month-not-recorded";

    /// <summary>R-14 "the under-lock recheck": the ledger changed between the calculation and the write.</summary>
    public const string LedgerChanged = "payroll-ledger-changed";

    /// <summary>
    /// S147 R-2: a recorded month X after the ledger start and up to P is out of date with its predecessor, or its
    /// re-derivation refuses. Names the earliest such X: recalculate it first.
    /// </summary>
    public const string PriorMonthStale = "payroll-prior-month-stale";

    /// <summary>The <c>yyyy-MM</c> form the host's 409 body carries in <c>month</c>.</summary>
    public const string MonthFormat = "yyyy-MM";

    public FlexLedgerRefusalException(string kind, DateOnly month, string errorSentence, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorSentence);
        Kind = kind;
        Month = new DateOnly(month.Year, month.Month, 1);
        ErrorSentence = errorSentence;
    }

    /// <summary>The refusal's discriminator, one of the constants on this type.</summary>
    public string Kind { get; }

    /// <summary>The first day of the month the refusal names (S, P, the earliest stale month, or the month whose write was refused).</summary>
    public DateOnly Month { get; }

    /// <summary>The named month as the host's 409 body carries it (<c>yyyy-MM</c>).</summary>
    public string MonthText => Month.ToString(MonthFormat, CultureInfo.InvariantCulture);

    /// <summary>The fixed sentence of the host's 409 body: no employee id, no date, no digit.</summary>
    public string ErrorSentence { get; }
}
