using System.Globalization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14605 (QUAL-188; rulings R-14, transcribed, as amended by R-28) — the ONE total function that
/// gives a month its opening flex balance. Pure: it reads nothing, writes nothing and calls no clock.
///
/// <para>
/// <b>Plain language.</b> Flex is the running balance of hours worked above or below the norm. Before S146 the
/// caller of a payroll calculation said what the month opened at, the engine's closing balance was checked and
/// thrown away, and nothing recorded it (QUAL-188). Now every paid month's export record holds its opening and
/// closing balance and where the opening came from, and the next month opens at the previous month's recorded
/// closing. This function is the only place an opening comes from: the calculation service asks it before the
/// calculation (admission, self-managed reads) and again inside the writing transaction under the per-employee
/// ledger lock (the recheck, in-transaction reads). The same function both times, so the two answers can only
/// differ when the ledger itself changed in between.
/// </para>
///
/// <para>
/// <b>Terms</b> (refinement rev 3.2, C2). A <i>record</i> is any <c>payroll_export_records</c> row. A <i>ledger
/// record</i> is one with <c>opening_balance IS NOT NULL</c> (written or filled by a calculating route; the three
/// balance columns are all-null or all-set, a CHECK). <c>S</c> is the employee's earliest ledger month (the ledger
/// start); <c>P</c> is M−1. <c>EmployedIn(X)</c> is true when any day of calendar month X is an EMPLOYED day of the
/// windows (<see cref="EmployedIn"/>); it never consults the F1 machinery (<c>PriorMonthRequiringApproval</c>,
/// <c>EmployedContextDays</c>), which returns no predecessor for a month opening on a Monday.
/// </para>
///
/// <para>
/// <b>The link</b> (<see cref="DeriveLink"/>; precondition: M holds an EMPLOYED day of E — otherwise no flex is
/// settled, no record is written and the function is not called; a replay never calls it, R42). It reads only M's
/// record, P's record, S and the windows, and never walks:
/// <code>
/// 1. M ∈ Ledger and Ledger[M].provenance = LEDGER_START → (Ledger[M].opening, LEDGER_START)
/// 2. Ledger is empty                                    → (0, LEDGER_START)
/// 3. M &lt; S
///    3a. M ∉ Records                                    → REFUSE payroll-export-out-of-order, naming S
///    3b. otherwise (a raw record: re-run or correction) → (0, UNTRACKED)   never stored
/// 4. M &gt; S   (M = S is always case 1)
///    4a. EmployedIn(P) and P ∈ Ledger                   → (Ledger[P].closing, CHAINED)
///    4b. EmployedIn(P) and P ∉ Ledger                   → REFUSE payroll-prior-month-not-recorded, naming P
///                                                           ("export P first"; "correct P first" if P ∈ Records)
///    4c. not EmployedIn(P)                              → (0, REHIRE)
/// </code>
/// R-28: there is no <c>route</c> input. On the correction route M is always in Records at admission (the
/// correction's pre-check refuses an unrecorded month before the calculation is called), so "route = export and
/// M ∉ Records" collapses to "M ∉ Records".
/// </para>
///
/// <para>
/// <b>The walk</b> (<see cref="Derive(DateOnly, IReadOnlyList{FlexLedgerRecord}, IReadOnlyList{EmploymentWindow})"/>;
/// S147 TASK-14700, QUAL-229, rulings R-2 with R-11 … R-14). The month index
/// is built once. When M is after S, X walks ascending over S &lt; X ≤ P, and for every X that holds an EMPLOYED day
/// and has a ledger record, the link of X is compared with X's recorded (opening, provenance):
/// <code>
/// the earliest X whose link differs from its record → REFUSE payroll-prior-month-stale, naming X
/// the earliest X whose link itself refuses          → REFUSE payroll-prior-month-stale, naming X
///                                                      (the inner kind in the diagnostic, never in the body)
/// otherwise                                         → the link of M
/// </code>
/// Each link is evaluated once, so the walk is O(n) over the in-memory index (n = months since S). The walk always
/// starts at S, even when M's own stretch of employment would open at REHIRE 0 (R-12). S itself is never re-derived
/// (case 1). A month with no EMPLOYED day keeps the triple it was paid at and is not compared, because a correction
/// of it derives nothing and so could never repair it; its successor is compared, re-derives REHIRE 0 against a
/// recorded CHAINED, and is named (R-14, R-26). A month with no ledger record (never exported, or exported raw) has no
/// recorded pair to compare; a hole of that kind shows up as the refusing link of the next ledger month (case 4b
/// inside it), or, when it is P itself, as M's own case-4b refusal, exactly as before.
/// </para>
///
/// <para>
/// <b>The guarantee</b> (R-12). At every export and every correction, every recorded month of this employee is
/// consistent with its predecessor: its recorded opening is what its predecessor's record gives today (S needs no
/// predecessor; a month with no employed day keeps what it was paid at). So no month is ever opened on a closing
/// that a later correction made stale, however far back the correction was.
/// </para>
///
/// <para>
/// <b>The repair sequence.</b> A correction of month X can change X's closing (or remove X's last employed day),
/// which makes X+1's recorded opening stale; the correction names X+1 (HRP-003). Until X+1 is recalculated, every
/// export or correction of a later month is refused with <c>payroll-prior-month-stale</c>, naming the EARLIEST stale
/// month. The administrator recalculates that month through <c>/recalculate</c>: its correction re-derives the
/// opening from the now-current predecessor and records it. The next attempt names the next stale month, if any,
/// and so on up to P; then M is paid. A recalculation of the named month never refuses itself on this rule: every
/// month before it is consistent, which is why it was the earliest. (When a month is named because its own link
/// refuses — its predecessor is employed but has no ledger record, a hole an employment-dates edit can open — its
/// recalculation answers that case-4b refusal instead, naming the predecessor to export or correct first.)
/// </para>
///
/// <para>
/// <b>Consequences, stated rather than rediscovered</b> (C2). Within a stretch of consecutive employed months,
/// months are paid in order from the ledger start; a stretch that begins after a month with no employed day
/// starts at 0 (REHIRE). A leaving month left unrecorded does not block a rehire month: it has no record, so the
/// walk does not compare it, and the rehire month's own link reads only its own P, which holds no employed day.
/// </para>
/// </summary>
public static class FlexLedgerOpening
{
    /// <summary>The employee's first ledger month: R-14 cases 1 and 2.</summary>
    public const string LedgerStart = "LEDGER_START";

    /// <summary>The opening is the previous month's recorded closing: R-14 case 4a.</summary>
    public const string Chained = "CHAINED";

    /// <summary>A stretch of employment after a month with no employed day starts at 0: R-14 case 4c.</summary>
    public const string Rehire = "REHIRE";

    /// <summary>
    /// A raw-recorded month before the ledger start: flex is not tracked (R-14 case 3b). Allowed on a result and
    /// on a correction event; NEVER stored on a record (the CHECK refuses it): the balances stay null and any
    /// <c>FLEX_PAYOUT</c> line is dropped.
    /// </summary>
    public const string Untracked = "UNTRACKED";

    /// <summary>
    /// The time type of the flex payout line (<c>FlexBalanceRule.GetPayoutLineItem</c> in the Rule Engine), which
    /// <see cref="PayrollMappingService.BuildLine"/> copies into <see cref="PayrollExportLine.SourceTimeType"/>.
    /// Under <see cref="Untracked"/> every export line carrying it is dropped.
    /// </summary>
    public const string FlexPayoutTimeType = "FLEX_PAYOUT";

    /// <summary>
    /// R-28 / R-36, widened by S147 R-13: the ONE range the derivation reads the employment windows over, at
    /// admission and in the recheck alike. With a ledger start S (<paramref name="ledgerStart"/>, taken from the
    /// records the caller already holds, <see cref="LedgerStartOf"/>) it is <c>[min(S, P).start, M.end]</c>, so the
    /// walk can test <see cref="EmployedIn"/> for every month from S to P; without one (an empty ledger, or
    /// <c>null</c>) it is today's <c>[P.start, M.end]</c>, the first day of the previous month to the last day of this
    /// month. When M is after S the range starts at S; otherwise (M ≤ S) it is the same as without one. Never the
    /// calculation's own <c>[periodStart, periodEnd]</c> read, which scopes the windows to spells overlapping the
    /// period and so can miss a spell that ended inside P. Both callers read the records first and pass S through
    /// this one helper, so their two reads cannot drift apart.
    /// </summary>
    public static (DateOnly From, DateOnly To) WindowsReadRange(DateOnly month, DateOnly? ledgerStart = null)
    {
        var m = MonthStart(month);
        var p = m.AddMonths(-1);
        var from = ledgerStart is { } s && MonthStart(s) < p ? MonthStart(s) : p;
        return (from, m.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// S147 R-13: the ledger start S the records give — the earliest month holding a ledger record (one with an
    /// opening) — or <c>null</c> when there is none (raw records do not count, as in case 2). The callers pass it to
    /// <see cref="WindowsReadRange"/>; it is the same S <see cref="BuildIndex"/> computes.
    /// </summary>
    public static DateOnly? LedgerStartOf(IReadOnlyList<FlexLedgerRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return EarliestLedgerMonth(records);
    }

    /// <summary>
    /// S147 R-11: the month index <see cref="DeriveLink"/> reads: the records keyed by month, with S computed once.
    /// Throws <see cref="InvalidOperationException"/> on two records for one month (the table's UNIQUE key makes that
    /// impossible, so the input is not a read of it).
    /// </summary>
    public static FlexLedgerIndex BuildIndex(IReadOnlyList<FlexLedgerRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var byMonth = new Dictionary<DateOnly, FlexLedgerRecord>(records.Count);
        foreach (var record in records)
        {
            if (!byMonth.TryAdd(record.MonthStart, record))
            {
                throw new InvalidOperationException(
                    $"FlexLedgerOpening.BuildIndex: two export records for {Format(record.MonthStart)}; the table's UNIQUE " +
                    "(employee_id, year, month) key makes that impossible, so the input is not a read of it.");
            }
        }

        return new FlexLedgerIndex(byMonth, EarliestLedgerMonth(byMonth.Values));
    }

    /// <summary>
    /// <c>EmployedIn(X)</c>: true when any day of calendar month <paramref name="month"/> is an EMPLOYED day of
    /// <paramref name="windows"/>, bounds inclusive (ADR-040 D1/D2). <c>null</c> windows (no window resolver
    /// wired, R-37) mean every day is EMPLOYED, exactly as the calculation service's <c>IsEmployedOn</c> and the
    /// planner treat a windowless employee. "Some day d of X is inside a window" is tested as "a window overlaps
    /// [X.start, X.end]" through <see cref="EmploymentWindow.Overlaps"/>, the one shared predicate — the same
    /// answer as asking <c>IsEmployedOn</c> day by day.
    /// </summary>
    public static bool EmployedIn(DateOnly month, IReadOnlyList<EmploymentWindow>? windows)
    {
        if (windows is null)
            return true;

        var start = MonthStart(month);
        var end = start.AddMonths(1).AddDays(-1);
        foreach (var window in windows)
        {
            if (window.Overlaps(start, end))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The function (see the type's doc): the walk, then the link of M. <paramref name="month"/> may be any day of M;
    /// <paramref name="records"/> are ALL of the employee's export records, ledger and raw alike (at most one per
    /// month: the table's UNIQUE key); <paramref name="windows"/> are the employee's employment windows read over
    /// <see cref="WindowsReadRange(DateOnly, DateOnly?)"/> with the S of these records (<c>null</c> when no window
    /// resolver is wired). Returns the opening, or a refusal: the link's own (3a, 4b) or the walk's
    /// <see cref="FlexLedgerRefusalException.PriorMonthStale"/>. Throws <see cref="InvalidOperationException"/> only on
    /// input that the schema makes impossible (two records for one month; a ledger record without a closing; a
    /// ledger-start month that is not <see cref="LedgerStart"/>).
    /// </summary>
    public static FlexLedgerDecision Derive(
        DateOnly month,
        IReadOnlyList<FlexLedgerRecord> records,
        IReadOnlyList<EmploymentWindow>? windows) =>
        Derive(month, records, windows, onLinkEvaluated: null);

    /// <summary>
    /// <see cref="Derive(DateOnly, IReadOnlyList{FlexLedgerRecord}, IReadOnlyList{EmploymentWindow})"/> with a counter
    /// seam: <paramref name="onLinkEvaluated"/>, when given, is called once with the month of every link evaluation,
    /// in evaluation order (the walk's months, then M), so a test can observe that each link is evaluated once.
    /// Internal: it is not part of the public signature, and production passes <c>null</c>.
    /// </summary>
    internal static FlexLedgerDecision Derive(
        DateOnly month,
        IReadOnlyList<FlexLedgerRecord> records,
        IReadOnlyList<EmploymentWindow>? windows,
        Action<DateOnly>? onLinkEvaluated)
    {
        ArgumentNullException.ThrowIfNull(records);

        // The month index, built once (R-11): every link below reads it, none rebuilds it.
        var index = BuildIndex(records);
        var m = MonthStart(month);

        // The walk (R-2, R-12): only when M is after S, from S+1 up to P, ascending. S itself is never re-derived
        // (case 1); an empty ledger (case 2) and M ≤ S (cases 1, 3, and the damaged-ledger throw) have nothing to walk.
        if (index.LedgerStart is { } s && m > s)
        {
            var p = m.AddMonths(-1);
            for (var x = s.AddMonths(1); x <= p; x = x.AddMonths(1))
            {
                // R-14: a month with no EMPLOYED day keeps the triple it was paid at and is not compared; its successor
                // is (it re-derives REHIRE 0 against its recorded CHAINED and is named).
                if (!EmployedIn(x, windows))
                    continue;

                // A month with no ledger record has no recorded (opening, provenance) to compare. An unrecorded or
                // raw month before P surfaces as the next ledger month's refusing link (its case 4b); P itself as
                // M's own case 4b below, exactly as before the walk.
                if (!index.TryGetRecord(x, out var recordX) || !recordX.IsLedgerRecord)
                    continue;

                var link = DeriveLinkCore(x, index, windows, onLinkEvaluated);

                // The earliest X whose link itself refuses: named, with the inner kind in the diagnostic only.
                if (link.Refusal is { } inner)
                {
                    return FlexLedgerDecision.Refuse(new FlexLedgerRefusal(
                        FlexLedgerRefusalException.PriorMonthStale, x, PriorMonthStaleSentence,
                        $"The month {Format(m)} cannot be opened: the recorded month {Format(x)} no longer follows from " +
                        $"its predecessor, because re-deriving its opening itself refuses ({inner.Kind}, naming " +
                        $"{Format(inner.Month)}: {inner.Diagnostic}). {Format(x)} is the earliest such month; recalculate " +
                        "it first (S147 R-2, the refusing link)."));
                }

                // The earliest X whose link differs from its record: named.
                var derived = link.Opening!;
                if (derived.Balance != recordX.OpeningBalance!.Value
                    || !string.Equals(derived.Provenance, recordX.Provenance, StringComparison.Ordinal))
                {
                    return FlexLedgerDecision.Refuse(new FlexLedgerRefusal(
                        FlexLedgerRefusalException.PriorMonthStale, x, PriorMonthStaleSentence,
                        $"The month {Format(m)} cannot be opened: the recorded month {Format(x)} opened at " +
                        $"{recordX.Provenance ?? "(none)"} {recordX.OpeningBalance.Value.ToString(CultureInfo.InvariantCulture)}, " +
                        $"but its predecessor's record now gives {derived.Provenance} " +
                        $"{derived.Balance.ToString(CultureInfo.InvariantCulture)}. {Format(x)} is the earliest stale month; " +
                        "recalculate it first, then each later month in order (S147 R-2)."));
                }
            }
        }

        return DeriveLinkCore(m, index, windows, onLinkEvaluated);
    }

    /// <summary>
    /// S147 R-11 — the link: cases 1 … 4c of the type's doc for ONE month X, reading only X's record, X−1's record,
    /// S (from <paramref name="index"/>) and <paramref name="windows"/>. It never walks, so the walk in
    /// <see cref="Derive(DateOnly, IReadOnlyList{FlexLedgerRecord}, IReadOnlyList{EmploymentWindow})"/> evaluates each
    /// link once. For a chain with no earlier month to walk it gives exactly what <c>Derive</c> gives (the S146
    /// function, unchanged). <paramref name="month"/> may be any day of X. Throws <see cref="InvalidOperationException"/>
    /// only on input the schema makes impossible (a ledger record of X−1 without a closing; X = S whose record is not
    /// <see cref="LedgerStart"/>).
    /// </summary>
    public static FlexLedgerDecision DeriveLink(
        DateOnly month,
        FlexLedgerIndex index,
        IReadOnlyList<EmploymentWindow>? windows) =>
        DeriveLinkCore(month, index, windows, onLinkEvaluated: null);

    private static FlexLedgerDecision DeriveLinkCore(
        DateOnly month,
        FlexLedgerIndex index,
        IReadOnlyList<EmploymentWindow>? windows,
        Action<DateOnly>? onLinkEvaluated)
    {
        ArgumentNullException.ThrowIfNull(index);

        var m = MonthStart(month);
        var p = m.AddMonths(-1);
        onLinkEvaluated?.Invoke(m);

        // 1. M is a ledger-start record: a start is never re-derived (re-run and correction alike).
        if (index.TryGetRecord(m, out var recordM)
            && recordM.IsLedgerRecord
            && string.Equals(recordM.Provenance, LedgerStart, StringComparison.Ordinal))
        {
            return FlexLedgerDecision.Open(new FlexOpening(recordM.OpeningBalance!.Value, LedgerStart, m));
        }

        // 2. No ledger record at all: the first month the system pays, whatever month it is. Raw records do not
        //    count, so raw history never dead-ends.
        if (index.LedgerStart is not { } s)
            return FlexLedgerDecision.Open(new FlexOpening(0m, LedgerStart, m));

        // 3. M precedes the ledger start.
        if (m < s)
        {
            // 3a. No record of M: a first export of a month before the start is out of order.
            if (recordM is null)
            {
                return FlexLedgerDecision.Refuse(new FlexLedgerRefusal(
                    FlexLedgerRefusalException.ExportOutOfOrder, s, ExportOutOfOrderSentence,
                    $"The month {Format(m)} has no export record and precedes the employee's flex ledger start " +
                    $"{Format(s)} (R-14 case 3a)."));
            }

            // 3b. A raw record of M (a re-run or a correction): flex is not tracked, never stored.
            return FlexLedgerDecision.Open(new FlexOpening(0m, Untracked, s));
        }

        // 4. M follows the ledger start (M = S is always case 1).
        if (m > s)
        {
            if (EmployedIn(p, windows))
            {
                // 4a. P is employed and recorded: chain from its closing.
                var pHasRecord = index.TryGetRecord(p, out var recordP);
                if (pHasRecord && recordP!.IsLedgerRecord)
                {
                    var closing = recordP.ClosingBalance ?? throw new InvalidOperationException(
                        $"FlexLedgerOpening.DeriveLink: the ledger record of {Format(p)} has an opening but no closing; the " +
                        "CHECK payroll_export_records_balances_paired makes that impossible.");
                    return FlexLedgerDecision.Open(new FlexOpening(closing, Chained, s));
                }

                // 4b. P is employed but not a ledger record: refuse, naming P.
                return FlexLedgerDecision.Refuse(new FlexLedgerRefusal(
                    FlexLedgerRefusalException.PriorMonthNotRecorded, p,
                    pHasRecord ? PriorMonthRawSentence : PriorMonthUnrecordedSentence,
                    $"The month {Format(m)} opens at the recorded closing of {Format(p)}, which was employed but " +
                    (pHasRecord
                        ? "was exported without a flex balance (a raw export record); correct it first (R-14 case 4b)."
                        : "has no export record; export it first (R-14 case 4b).")));
            }

            // 4c. P has no employed day: a new stretch of employment starts at 0.
            return FlexLedgerDecision.Open(new FlexOpening(0m, Rehire, s));
        }

        // M = S, but M's record is not a LEDGER_START ledger record (case 1 did not match). S is the earliest ledger
        // month, and a ledger is only ever started by case 2, so this is a damaged ledger, never a month.
        throw new InvalidOperationException(
            $"FlexLedgerOpening.DeriveLink: {Format(m)} is the employee's ledger start but its record's provenance is " +
            $"'{recordM?.Provenance ?? "(none)"}', not {LedgerStart}. The ledger is damaged; nothing is derived from it.");
    }

    /// <summary>The fixed body sentence of <see cref="FlexLedgerRefusalException.ExportOutOfOrder"/>.</summary>
    public const string ExportOutOfOrderSentence =
        "This month cannot be exported: it has no export record and comes before the first month of this " +
        "employee's flex ledger, named by month. Months are paid in order from the ledger start; a month before it " +
        "can only enter as a raw export, and its flex is then not tracked. Nothing was calculated, written or exported.";

    /// <summary>The fixed body sentence of <see cref="FlexLedgerRefusalException.PriorMonthNotRecorded"/> when P has no record.</summary>
    public const string PriorMonthUnrecordedSentence =
        "This month cannot be calculated: it opens at the recorded closing flex balance of the previous month, " +
        "named by month, which has no export record. Export that month first. Nothing was calculated, written or " +
        "exported.";

    /// <summary>The fixed body sentence of <see cref="FlexLedgerRefusalException.PriorMonthNotRecorded"/> when P has a raw record.</summary>
    public const string PriorMonthRawSentence =
        "This month cannot be calculated: it opens at the recorded closing flex balance of the previous month, " +
        "named by month, which was exported without a flex balance. Correct that month first; the correction " +
        "records its balance. Nothing was calculated, written or exported.";

    /// <summary>
    /// S147 TASK-14700 (R-2) — the fixed body sentence of <see cref="FlexLedgerRefusalException.PriorMonthStale"/>, the
    /// same on the export and the correction route: what is wrong, what to do, and that nothing happened. No employee
    /// id, no date, no digit; the named month travels in the body's <c>month</c> member.
    /// </summary>
    public const string PriorMonthStaleSentence =
        "This month cannot be calculated: the recorded flex balance of an earlier month, named by month, is out of " +
        "date, because a month before it was corrected, or the employment dates changed, after it was paid. " +
        "Recalculate the named month first; the next attempt names the next month to recalculate, if any. Nothing " +
        "was calculated, written or exported.";

    /// <summary>The fixed body sentence of <see cref="FlexLedgerRefusalException.LedgerChanged"/> on the export routes.</summary>
    public const string LedgerChangedExportSentence =
        "The previous month's balance changed; retry. It changed while this month was being calculated, so " +
        "nothing was exported and no export record was written.";

    /// <summary>The fixed body sentence of <see cref="FlexLedgerRefusalException.LedgerChanged"/> on the correction route.</summary>
    public const string LedgerChangedCorrectionSentence =
        "Retry: the previous month's balance changed while this correction was computed. Nothing was corrected " +
        "or written.";

    /// <summary>
    /// The "flex not tracked" statement R-14 case 3b requires on the response and the correction result:
    /// <c>flex not tracked: the month precedes the ledger start S</c>.
    /// </summary>
    public static string UntrackedNote(DateOnly ledgerStart) =>
        $"flex not tracked: the month precedes the ledger start {Format(ledgerStart)}";

    internal static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>S: the earliest month holding a ledger record, or <c>null</c> (the one definition both S readers use).</summary>
    private static DateOnly? EarliestLedgerMonth(IEnumerable<FlexLedgerRecord> records)
    {
        DateOnly? earliest = null;
        foreach (var record in records)
        {
            if (record.IsLedgerRecord && (earliest is null || record.MonthStart < earliest.Value))
                earliest = record.MonthStart;
        }
        return earliest;
    }

    /// <summary>
    /// R-47: true when [<paramref name="start"/>, <paramref name="end"/>] is exactly one calendar month, the only period a
    /// ledger write may be made for: the record is keyed by the calendar month and its closing is the MONTH's closing,
    /// which the next month opens from. A partial period has no place in the chain. Compared field by field, with no
    /// date arithmetic, so a malformed request at the end of the calendar (December 9999) is refused, never thrown on
    /// (S146 5a cycle 2, Codex W).
    /// </summary>
    public static bool IsWholeCalendarMonth(DateOnly start, DateOnly end) =>
        start.Day == 1
        && end.Year == start.Year
        && end.Month == start.Month
        && end.Day == DateTime.DaysInMonth(start.Year, start.Month);

    private static string Format(DateOnly month) =>
        month.ToString(FlexLedgerRefusalException.MonthFormat, CultureInfo.InvariantCulture);
}

/// <summary>
/// S146 / TASK-14605 — one <c>payroll_export_records</c> row as the flex ledger reads it: its month and its three
/// balance columns. A <i>ledger record</i> has an opening (<see cref="IsLedgerRecord"/>); a raw record has none of
/// the three (the CHECK <c>payroll_export_records_balances_paired</c>).
/// </summary>
public sealed record FlexLedgerRecord(
    int Year,
    int Month,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    string? Provenance)
{
    /// <summary>The first day of the record's month.</summary>
    public DateOnly MonthStart => new(Year, Month, 1);

    /// <summary>True when the record carries balances (written or filled by a calculating route).</summary>
    public bool IsLedgerRecord => OpeningBalance is not null;
}

/// <summary>
/// S147 / TASK-14700 (QUAL-229; ruling R-11) — the employee's export records keyed by month, with the ledger start
/// S computed once: what <see cref="FlexLedgerOpening.DeriveLink"/> reads, so that a link never scans the records
/// and the walk stays O(n). Built only by <see cref="FlexLedgerOpening.BuildIndex"/> (which refuses two records for
/// one month); immutable.
/// </summary>
public sealed class FlexLedgerIndex
{
    private readonly IReadOnlyDictionary<DateOnly, FlexLedgerRecord> _byMonth;

    internal FlexLedgerIndex(IReadOnlyDictionary<DateOnly, FlexLedgerRecord> byMonth, DateOnly? ledgerStart)
    {
        _byMonth = byMonth;
        LedgerStart = ledgerStart;
    }

    /// <summary>S: the first day of the earliest month holding a ledger record, or <c>null</c> when the ledger is empty.</summary>
    public DateOnly? LedgerStart { get; }

    /// <summary>The number of records (ledger and raw) in the index.</summary>
    public int Count => _byMonth.Count;

    /// <summary>The record of the month holding <paramref name="month"/> (any day of it), ledger or raw, if any.</summary>
    public bool TryGetRecord(DateOnly month, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FlexLedgerRecord? record) =>
        _byMonth.TryGetValue(FlexLedgerOpening.MonthStart(month), out record);
}

/// <summary>
/// S146 / TASK-14605 — a derived opening: the balance the month opens at, where it came from
/// (<see cref="FlexLedgerOpening.LedgerStart"/>, <see cref="FlexLedgerOpening.Chained"/>,
/// <see cref="FlexLedgerOpening.Rehire"/> or <see cref="FlexLedgerOpening.Untracked"/>) and the ledger start S the
/// derivation saw (M itself when M starts the ledger). The recheck compares <see cref="Balance"/> and
/// <see cref="Provenance"/> only.
/// </summary>
public sealed record FlexOpening(decimal Balance, string Provenance, DateOnly LedgerStart)
{
    /// <summary>True under <see cref="FlexLedgerOpening.Untracked"/>: nothing is stored and no payout is made.</summary>
    public bool IsUntracked => string.Equals(Provenance, FlexLedgerOpening.Untracked, StringComparison.Ordinal);

    /// <summary>The (opening, provenance) pair the under-lock recheck requires to be unchanged (R-14).</summary>
    public bool SamePairAs(FlexOpening other) =>
        Balance == other.Balance && string.Equals(Provenance, other.Provenance, StringComparison.Ordinal);
}

/// <summary>S146 / TASK-14605 — a refusal of <see cref="FlexLedgerOpening.Derive"/>, as data.</summary>
public sealed record FlexLedgerRefusal(string Kind, DateOnly Month, string ErrorSentence, string Diagnostic)
{
    /// <summary>The refusal as the exception the routes map to 409.</summary>
    public FlexLedgerRefusalException ToException(string employeeId) =>
        new(Kind, Month, ErrorSentence, $"Employee '{employeeId}': {Diagnostic}");
}

/// <summary>
/// S146 / TASK-14605 — what <see cref="FlexLedgerOpening.Derive"/> returns: exactly one of an
/// <see cref="Opening"/> or a <see cref="Refusal"/>.
/// </summary>
public sealed record FlexLedgerDecision
{
    private FlexLedgerDecision(FlexOpening? opening, FlexLedgerRefusal? refusal)
    {
        Opening = opening;
        Refusal = refusal;
    }

    /// <summary>The derived opening, or <c>null</c> when the function refused.</summary>
    public FlexOpening? Opening { get; }

    /// <summary>The refusal, or <c>null</c> when the function derived an opening.</summary>
    public FlexLedgerRefusal? Refusal { get; }

    internal static FlexLedgerDecision Open(FlexOpening opening) => new(opening, null);

    internal static FlexLedgerDecision Refuse(FlexLedgerRefusal refusal) => new(null, refusal);

    /// <summary>The opening, or the refusal thrown as <see cref="FlexLedgerRefusalException"/> (admission).</summary>
    public FlexOpening OpeningOrThrow(string employeeId) =>
        Opening ?? throw Refusal!.ToException(employeeId);
}
