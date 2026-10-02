using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// S145 / R34 — the norm mode a legacy caller forces on the core. <c>null</c> (no force)
/// dispatches on each covered week's <see cref="NormModel"/>, as <c>NORM_CHECK_37H</c> always has.
/// </summary>
public enum TimelineNormMode
{
    /// <summary>Per-day norm sum; reports <c>NormPeriodWeeks</c> 1 (<c>NORM_CHECK_WEEKLY</c>).</summary>
    Weekly,

    /// <summary>Per-day norm sum; reports the config's <c>NormPeriodWeeks</c> (<c>NORM_CHECK_MULTIWEEK</c>).</summary>
    MultiWeek,

    /// <summary>Annual-activity norm, days ÷ 365 per piece; reports no <c>NormPeriodWeeks</c> (<c>NORM_CHECK_ANNUAL</c>).</summary>
    Annual,
}

/// <summary>
/// One stretch of the timeline under one resolved config: the days
/// <c>[From, To]</c> (inclusive), the part-time fraction in force on them, and the agreement
/// config already resolved for them (agreement, OK version of those dates, position). The core
/// never resolves config itself; the HTTP layer splits request pieces at OK-version dates.
/// </summary>
public sealed record ResolvedPiece(
    DateOnly From,
    DateOnly To,
    decimal PartTimeFraction,
    AgreementRuleConfig Config);

/// <summary>
/// Everything the weekly core evaluates. <c>PeriodStart..PeriodEnd</c> is the pay period whose
/// weeks are settled; <c>SettlementCutoff</c> is the last day evaluated when it falls inside
/// the period's last week (a leaver's last day, or a legacy call's period end), else null.
/// <c>Pieces</c> cover the days to evaluate, including context days before the period.
/// </summary>
public sealed record TimelineCoreInput(
    string EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? SettlementCutoff,
    IReadOnlyList<ResolvedPiece> Pieces,
    IReadOnlyList<TimeEntry> Entries,
    IReadOnlyList<AbsenceEntry> Absences,
    decimal PreviousFlexBalance,
    TimelineNormMode? ForcedNormMode = null,
    string NormRuleId = NormCheckRule.RuleId);

/// <summary>
/// The core's three results: overtime/merarbejde (<c>OVERTIME_CALC</c>), the norm check
/// (tagged with the input's <c>NormRuleId</c>), and flex, plus the flex payout line (null when
/// nothing exceeds the cap).
/// </summary>
public sealed record TimelineCoreResult(
    CalculationResult Overtime,
    CalculationResult Norm,
    FlexBalanceResult Flex,
    CalculationLineItem? FlexPayout);

/// <summary>A structurally invalid core input (inverted period, overlapping pieces, …).</summary>
public sealed class TimelineInputException : ArgumentException
{
    public TimelineInputException(string message) : base(message)
    {
    }

    public TimelineInputException()
    {
    }

    public TimelineInputException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// S145 / TASK-14505 — the week-by-week Rule Engine: one pure function that evaluates
/// overtime / merarbejde, the norm check and flex over a timeline of days (QUAL-186).
///
/// <para>
/// <b>Why.</b> Before S145 the three rules compared a whole period's hours with ONE week's
/// norm, so payroll judged a calendar month as a single giant week: a full-time HK employee
/// working exactly 37 h every week of September 2026 was exported with 125.8 h of overtime and
/// a 25.8 h flex payout. The rules now work day by day and week by week.
/// </para>
///
/// <para><b>The rules encoded (refinement rev 3.4 A2, owner rulings 2026-10-01):</b></para>
/// <list type="bullet">
///   <item><description><b>Per working day (Q2 = A).</b> Every weekday carries its own norm and
///   thresholds from that day's contract: <c>DayNorm.ForWeekday(weekly, fraction)</c> =
///   <c>round(weekly × fraction ÷ 5, 2)</c>; weekends carry 0. Week and month figures are sums
///   of per-day amounts.</description></item>
///   <item><description><b>Settle when the week closes (Q3 = F1).</b> Overtime and merarbejde
///   are judged per Monday–Sunday week, and a week is settled by the period containing its
///   settlement day (<see cref="WeeklyContext.SettlementDay"/>: the Sunday, or the cutoff inside
///   the week). All its lines are dated on that day; the week's context days before the period
///   count toward its total.</description></item>
///   <item><description><b>Window contract (R6).</b> The norm totals and the <c>NORMAL_HOURS</c>
///   lines cover only the covered days inside <c>[PeriodStart, PeriodEnd]</c>, never a context
///   day. Weekly and multi-week modes take the per-day sum; annual mode takes days ÷ 365 per
///   piece. A week's mode, regime and reported <c>NormPeriodWeeks</c> come from its governing day
///   (the Sunday, or the latest evaluated day of the week when the Sunday is not evaluated);
///   <c>NormPeriodWeeks</c> is reported for the last covered day of the period.</description></item>
///   <item><description><b>Flex settles once</b>, on the last covered day of the period, at the
///   cap in force that day: <c>delta = worked + credits − (per-day norm − reductions)</c> over
///   the period's own covered days; one <c>FLEX_PAYOUT</c> dated that day.</description></item>
///   <item><description><b>Days no piece covers are not evaluated</b>, nor days after the
///   cutoff or after <c>PeriodEnd</c>. That is how a starter's opening week counts norm only from
///   the first day, and how a legacy call's opening partial week counts only in-period days. A day
///   past the period never decides the closing week's mode or reported <c>NormPeriodWeeks</c>:
///   the closing week's in-period days still count toward this period's norm, so only an
///   in-period day may decide how; the week's overtime settles in the period holding its
///   settlement day (F1).</description></item>
/// </list>
///
/// <para>
/// <b>Purity (ADR-002).</b> No I/O, no clock, no ambient state: every input is a parameter and
/// configs arrive already resolved. Same input, same output, byte for byte.
/// </para>
/// </summary>
public static class WeeklyTimelineCore
{
    /// <summary>
    /// The evaluation-model marker recorded with every payroll calculation (replay contract,
    /// refinement A7): a replay whose marker differs from the stored one is refused by name.
    /// Bump it whenever the core's arithmetic changes.
    /// </summary>
    public const string EvaluationModel = "weekly-timeline-v1";

    public static TimelineCoreResult Evaluate(TimelineCoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var timeline = Timeline.Create(input);

        var overtime = EvaluateOvertime(input, timeline);
        var inPeriodDays = timeline.InPeriodDays();
        var norm = EvaluateNorm(input, timeline, inPeriodDays);
        var (flex, payout) = EvaluateFlex(input, timeline, inPeriodDays);

        return new TimelineCoreResult(overtime, norm, flex, payout);
    }

    // -------------------------------------------------------------------------------------
    // Overtime / merarbejde — per settled Monday–Sunday week, dated on its settlement day.
    // -------------------------------------------------------------------------------------
    private static CalculationResult EvaluateOvertime(TimelineCoreInput input, Timeline timeline)
    {
        var lineItems = new List<CalculationLineItem>();

        foreach (var (monday, sunday) in WeeklyContext.SettledWeeks(
                     input.PeriodStart, input.PeriodEnd, input.SettlementCutoff))
        {
            var totalHours = 0m;
            var normHours = 0m;
            var threshold50 = 0m;
            var threshold100 = 0m;
            ResolvedPiece? governing = null;

            for (var day = monday; day <= sunday; day = day.AddDays(1))
            {
                var piece = timeline.PieceOn(day);
                if (piece is null)
                    continue;

                governing = piece; // the latest evaluated day of the week governs its regime
                totalHours += timeline.HoursOn(day);
                normHours += DayNorm.ForDay(day, piece.Config.WeeklyNormHours, piece.PartTimeFraction);
                threshold50 += DayNorm.ForDay(day, piece.Config.OvertimeThreshold50, piece.PartTimeFraction);
                threshold100 += DayNorm.ForDay(day, piece.Config.OvertimeThreshold100, piece.PartTimeFraction);
            }

            if (governing is null)
                continue; // no evaluated day in this week: nothing to settle

            lineItems.AddRange(OvertimeRule.SettleWeek(
                totalHours, normHours, threshold50, threshold100, governing.Config,
                WeeklyContext.SettlementDay(monday, input.SettlementCutoff)));
        }

        return new CalculationResult
        {
            RuleId = OvertimeRule.RuleId,
            EmployeeId = input.EmployeeId,
            Success = true,
            LineItems = lineItems
        };
    }

    // -------------------------------------------------------------------------------------
    // Norm check — the period's own covered days only (R6).
    // -------------------------------------------------------------------------------------
    private static CalculationResult EvaluateNorm(
        TimelineCoreInput input, Timeline timeline, IReadOnlyList<CoveredDay> inPeriodDays)
    {
        var perDayNorm = 0m;
        var annualDaysByPiece = new int[timeline.PieceCount];

        foreach (var covered in inPeriodDays)
        {
            if (ModeFor(input, timeline, covered.Day) == TimelineNormMode.Annual)
                annualDaysByPiece[covered.PieceIndex]++;
            else
                perDayNorm += DayNorm.ForDay(
                    covered.Day, covered.Piece.Config.WeeklyNormHours, covered.Piece.PartTimeFraction);
        }

        var normHours = perDayNorm;
        for (var i = 0; i < annualDaysByPiece.Length; i++)
        {
            if (annualDaysByPiece[i] == 0)
                continue;
            var piece = timeline.PieceAt(i);
            normHours += NormCheckRule.AnnualNorm(piece.Config, piece.PartTimeFraction, annualDaysByPiece[i]);
        }

        var lineItems = new List<CalculationLineItem>();
        var actualHours = 0m;
        foreach (var entry in input.Entries)
        {
            if (!timeline.IsInPeriodAndEvaluated(entry.Date))
                continue;
            actualHours += entry.Hours;
            lineItems.Add(NormCheckRule.NormalHoursLine(entry));
        }

        return NormCheckRule.BuildResult(
            input.NormRuleId,
            input.EmployeeId,
            lineItems,
            ReportedNormPeriodWeeks(input, timeline, inPeriodDays),
            normHours,
            actualHours);
    }

    /// <summary>
    /// The mode a covered day is evaluated under: the forced mode, else its week's governing
    /// config's <see cref="NormModel"/> (annual activity → annual; weekly hours → per-day sum
    /// reported with the config's norm-period length).
    /// </summary>
    private static TimelineNormMode ModeFor(TimelineCoreInput input, Timeline timeline, DateOnly day)
    {
        if (input.ForcedNormMode is { } forced)
            return forced;

        return timeline.GoverningPieceOfWeek(day).Config.NormModel == NormModel.ANNUAL_ACTIVITY
            ? TimelineNormMode.Annual
            : TimelineNormMode.MultiWeek;
    }

    private static int? ReportedNormPeriodWeeks(
        TimelineCoreInput input, Timeline timeline, IReadOnlyList<CoveredDay> inPeriodDays)
    {
        if (inPeriodDays.Count == 0)
            return input.ForcedNormMode == TimelineNormMode.Weekly ? 1 : null;

        var lastDay = inPeriodDays[^1].Day;
        return ModeFor(input, timeline, lastDay) switch
        {
            TimelineNormMode.Annual => null,
            TimelineNormMode.Weekly => 1,
            _ => NormCheckRule.EffectiveNormPeriodWeeks(
                timeline.GoverningPieceOfWeek(lastDay).Config.NormPeriodWeeks),
        };
    }

    // -------------------------------------------------------------------------------------
    // Flex — settled once, on the last covered day of the period, at that day's cap.
    // -------------------------------------------------------------------------------------
    private static (FlexBalanceResult Flex, CalculationLineItem? Payout) EvaluateFlex(
        TimelineCoreInput input, Timeline timeline, IReadOnlyList<CoveredDay> inPeriodDays)
    {
        if (inPeriodDays.Count == 0)
        {
            // Nothing evaluated in the period: no settlement day, so no clamp and no payout.
            return (new FlexBalanceResult
            {
                EmployeeId = input.EmployeeId,
                PreviousBalance = input.PreviousFlexBalance,
                NewBalance = input.PreviousFlexBalance,
                Delta = 0m,
                WorkedHours = 0m,
                AbsenceNormCredits = 0m,
                NormHours = 0m,
                ExcessForPayout = 0m,
                Success = true
            }, null);
        }

        // Flex uses the weekday per-day norm for every NormModel, as both flex and overtime used
        // the weekly norm for every mode before S145 (refinement A2 "Flex").
        var normHours = 0m;
        foreach (var covered in inPeriodDays)
        {
            normHours += DayNorm.ForDay(
                covered.Day, covered.Piece.Config.WeeklyNormHours, covered.Piece.PartTimeFraction);
        }

        var workedHours = 0m;
        foreach (var entry in input.Entries)
        {
            if (timeline.IsInPeriodAndEvaluated(entry.Date))
                workedHours += entry.Hours;
        }

        var credits = 0m;
        var reduction = 0m;
        foreach (var absence in input.Absences)
        {
            if (!timeline.IsInPeriodAndEvaluated(absence.Date))
                continue;
            var piece = timeline.PieceOn(absence.Date)!;
            if (AbsenceRule.GrantsNormCredit(absence.AbsenceType))
                credits += AbsenceRule.EffectiveHours(absence, piece.PartTimeFraction);
            else if (AbsenceRule.ReducesNorm(absence.AbsenceType))
                reduction += AbsenceRule.EffectiveHours(absence, piece.PartTimeFraction);
        }

        var settlement = inPeriodDays[^1];
        var flex = FlexBalanceRule.Settle(
            input.EmployeeId,
            input.PreviousFlexBalance,
            workedHours,
            credits,
            normHours,
            reduction,
            settlement.Piece.Config.MaxFlexBalance);

        return (flex, FlexBalanceRule.GetPayoutLineItem(flex, settlement.Day));
    }

    // -------------------------------------------------------------------------------------
    // The timeline: which piece covers a day, the hours on it, and each week's governing day.
    // -------------------------------------------------------------------------------------
    private readonly record struct CoveredDay(DateOnly Day, ResolvedPiece Piece, int PieceIndex);

    private sealed class Timeline
    {
        private readonly ResolvedPiece[] _pieces;
        private readonly DateOnly _periodStart;
        private readonly DateOnly _periodEnd;
        private readonly DateOnly? _cutoff;
        private readonly Dictionary<DateOnly, decimal> _hoursByDay;
        private readonly Dictionary<DateOnly, ResolvedPiece> _governingByMonday = new();

        private Timeline(
            ResolvedPiece[] pieces,
            DateOnly periodStart,
            DateOnly periodEnd,
            DateOnly? cutoff,
            Dictionary<DateOnly, decimal> hoursByDay)
        {
            _pieces = pieces;
            _periodStart = periodStart;
            _periodEnd = periodEnd;
            _cutoff = cutoff;
            _hoursByDay = hoursByDay;
        }

        public int PieceCount => _pieces.Length;

        public ResolvedPiece PieceAt(int index) => _pieces[index];

        public static Timeline Create(TimelineCoreInput input)
        {
            if (input.EmployeeId is null)
                throw new TimelineInputException("EmployeeId is required.");
            if (string.IsNullOrWhiteSpace(input.NormRuleId))
                throw new TimelineInputException("NormRuleId is required.");
            if (input.Pieces is null || input.Entries is null || input.Absences is null)
                throw new TimelineInputException("Pieces, Entries and Absences are required.");
            if (input.PeriodStart > input.PeriodEnd)
                throw new TimelineInputException("PeriodStart is after PeriodEnd.");

            foreach (var piece in input.Pieces)
            {
                if (piece is null || piece.Config is null)
                    throw new TimelineInputException("Every piece needs a resolved config.");
                if (piece.From > piece.To)
                    throw new TimelineInputException("A piece's From is after its To.");
            }

            // Stable order by start date; adjacent pieces may touch but never share a day.
            var sorted = input.Pieces.OrderBy(p => p.From).ToArray();
            for (var i = 1; i < sorted.Length; i++)
            {
                if (sorted[i].From <= sorted[i - 1].To)
                    throw new TimelineInputException("Timeline pieces overlap.");
            }

            var hoursByDay = new Dictionary<DateOnly, decimal>();
            foreach (var entry in input.Entries)
            {
                if (entry is null)
                    throw new TimelineInputException("Entries may not contain null.");
                hoursByDay[entry.Date] = hoursByDay.TryGetValue(entry.Date, out var sum)
                    ? sum + entry.Hours
                    : entry.Hours;
            }

            foreach (var absence in input.Absences)
            {
                if (absence is null)
                    throw new TimelineInputException("Absences may not contain null.");
            }

            return new Timeline(sorted, input.PeriodStart, input.PeriodEnd, input.SettlementCutoff, hoursByDay);
        }

        /// <summary>The piece covering <paramref name="day"/>, or null when no piece covers it or it is after the cutoff or after <c>PeriodEnd</c>.</summary>
        public ResolvedPiece? PieceOn(DateOnly day)
        {
            var index = PieceIndexOn(day);
            return index < 0 ? null : _pieces[index];
        }

        public bool IsInPeriodAndEvaluated(DateOnly day) =>
            day >= _periodStart && day <= _periodEnd && PieceIndexOn(day) >= 0;

        public decimal HoursOn(DateOnly day) =>
            _hoursByDay.TryGetValue(day, out var hours) ? hours : 0m;

        /// <summary>The evaluated days inside <c>[PeriodStart, PeriodEnd]</c>, in calendar order.</summary>
        public IReadOnlyList<CoveredDay> InPeriodDays()
        {
            var days = new List<CoveredDay>();
            for (var day = _periodStart; day <= _periodEnd; day = day.AddDays(1))
            {
                var index = PieceIndexOn(day);
                if (index >= 0)
                    days.Add(new CoveredDay(day, _pieces[index], index));
            }
            return days;
        }

        /// <summary>
        /// The piece of the governing day of the Monday–Sunday week containing
        /// <paramref name="dayInWeek"/>: the latest evaluated day of that week (its Sunday when
        /// the Sunday is evaluated). The caller guarantees <paramref name="dayInWeek"/> is
        /// evaluated, so the week has at least one evaluated day.
        /// </summary>
        public ResolvedPiece GoverningPieceOfWeek(DateOnly dayInWeek)
        {
            var monday = WeeklyContext.WeekStart(dayInWeek);
            if (_governingByMonday.TryGetValue(monday, out var cached))
                return cached;

            for (var day = monday.AddDays(6); day >= monday; day = day.AddDays(-1))
            {
                var piece = PieceOn(day);
                if (piece is not null)
                {
                    _governingByMonday[monday] = piece;
                    return piece;
                }
            }

            throw new InvalidOperationException("A governing day was requested for a week with no evaluated day.");
        }

        private int PieceIndexOn(DateOnly day)
        {
            if (_cutoff is { } cutoff && day > cutoff)
                return -1;
            if (day > _periodEnd)
                return -1;

            for (var i = 0; i < _pieces.Length; i++)
            {
                if (day < _pieces[i].From)
                    return -1; // sorted: no later piece can cover an earlier day
                if (day <= _pieces[i].To)
                    return i;
            }

            return -1;
        }
    }
}
