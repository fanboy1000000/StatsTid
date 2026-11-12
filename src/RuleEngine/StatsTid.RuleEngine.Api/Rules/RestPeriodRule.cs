using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: validates working time compliance against EU directive 2003/88/EC
/// and Danish Arbejdstidsloven. Checks daily rest (11h), weekly rest, max daily hours,
/// and 48h/week ceiling. No I/O, fully deterministic, version-aware via AgreementRuleConfig.
///
/// The four checks carry separately registered classifications (ADR-016 D2) —
/// <see cref="MaxDailyRuleId"/>, <see cref="DailyRestRuleId"/>, <see cref="WeeklyRestRuleId"/>,
/// <see cref="Weekly48HCeilingRuleId"/>. <see cref="Evaluate"/> (the
/// <c>/api/rules/check-compliance</c> route) is the one entry point that runs the rest checks: it
/// runs all four checks and unions the findings under <see cref="RuleId"/>.
/// <see cref="EvaluateMaxDailyHours"/> and <see cref="Evaluate48HCeiling"/> each run one hours check.
///
/// <para>
/// <strong>Two inputs, one per kind of check (ADR-043 D10, QUAL-234).</strong> The hours checks
/// (max daily, 48h ceiling) sum the project entries' <see cref="TimeEntry.Hours"/>. The rest checks
/// (daily rest, weekly rest) measure between the clock intervals the employee typed in Skema
/// (<see cref="ComplianceWorkInterval"/>); they never read a time entry's clock times.
/// </para>
///
/// <para>
/// <strong>ADR-039 segmentation note.</strong> The rest checks join a night shift into one
/// continuous stint across midnight, and judge a rest that ends on the period's first day against
/// work on the day before (the compliance read's one-day widen, ADR-039 D5b). This is safe with
/// respect to OK-version segmentation for a simple reason: these rest checks run ONLY on the unsegmented
/// monthly compliance path (<c>/api/rules/check-compliance</c> → <see cref="Evaluate"/>).
/// PeriodCalculationService's per-segment payroll evaluation never invokes them — it evaluates only
/// NORM_CHECK / SUPPLEMENT / OVERTIME / ON_CALL (+ absence / flex). So a stint is never split across
/// two OK segments underneath these checks; there is no cross-segment continuity to preserve here.
/// (The earlier ADR-016 "aligned-window / RejectIfMultipleSegments" rationale was incorrect — that
/// mechanism governs how per-segment rule RESULTS merge, not these checks.)
/// </para>
/// </summary>
public static class RestPeriodRule
{
    /// <summary>
    /// Legacy RuleId — preserved for backward compatibility with the
    /// <c>/api/rules/check-compliance</c> endpoint and existing tests that assert against
    /// the unified compliance check.
    /// </summary>
    public const string RuleId = "REST_PERIOD_CHECK";

    // S20 / TASK-2006 — multi-mode decomposition (ADR-016 D2):
    public const string MaxDailyRuleId = "REST_PERIOD_MAX_DAILY";
    public const string DailyRestRuleId = "REST_PERIOD_DAILY_REST";
    public const string WeeklyRestRuleId = "REST_PERIOD_WEEKLY_REST";
    public const string Weekly48HCeilingRuleId = "REST_PERIOD_48H_CEILING";

    /// <summary>
    /// Evaluates all four compliance checks for the given period, tagged with <see cref="RuleId"/>;
    /// the one entry point for the rest checks. Pure function, deterministic, no I/O.
    /// </summary>
    /// <param name="entries">
    /// The project entries, which the compliance read takes from the day before
    /// <paramref name="periodStart"/> (ADR-039 D5b). The hours checks sum their hours inside the
    /// period; the rest checks read only their voluntary-unsocial-hours flag, per day.
    /// </param>
    /// <param name="workIntervals">
    /// The clock intervals typed in Skema over the same dates, each on the date it was entered on.
    /// The rest checks build their work periods from these alone. Absent means no interval: no
    /// rest is measured, and every day of the period with project hours is listed in
    /// <see cref="ComplianceCheckResult.RestNotCheckedDays"/>.
    /// </param>
    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        IReadOnlyList<ComplianceWorkInterval>? workIntervals = null)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();

        var periodEntries = NormalizeEntries(entries, periodStart, periodEnd);

        // Skema's positive-duration rule, applied to the engine's own input too: an interval whose
        // end is not after its start is ignored. It forms no stint, so a raw caller cannot
        // manufacture a midnight crossing, and it adds no clock hours, so its day is more likely
        // to be listed below (the safe direction).
        var intervals = (workIntervals ?? Array.Empty<ComplianceWorkInterval>())
            .Where(iv => iv.End > iv.Start)
            .ToList();

        // 1. Max daily hours check (works for all entries, including hours-only). Hours-summing
        //    checks consume the PERIOD-FILTERED per-day rows so out-of-period hours never leak
        //    into period totals (ADR-039 BLOCKER-2).
        CheckMaxDailyHours(periodEntries, config, violations, warnings);

        // 2 + 3. The rest checks measure between work periods built from the intervals alone
        //    (ADR-043 D10); each check then decides which of them the period owns.
        var stints = ReconstructStints(intervals, VoluntaryDays(entries, config));
        CheckDailyRest(stints, periodStart, periodEnd, config, violations, warnings);
        CheckWeeklyRest(stints, periodStart, periodEnd, config, violations, warnings);

        // 4. 48h/week ceiling (works for all entries) — period-filtered per-day rows.
        CheckWeeklyMaxHours(periodEntries, periodStart, periodEnd, config, violations, warnings);

        // 5. The days whose project hours are not all on the clock (QUAL-234; owner ruling R-22
        //    keeps the list), so the result never looks compliant for a day the rest checks could
        //    not fully measure. Reported only; it adds no violation and does not change Success.
        var restNotCheckedDays = FindRestNotCheckedDays(periodEntries, intervals);

        return new ComplianceCheckResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = violations.Count == 0,
            Violations = violations,
            Warnings = warnings,
            RestNotCheckedDays = restNotCheckedDays
        };
    }

    /// <summary>
    /// S20 — single-check entry point for max daily hours
    /// (<see cref="MaxDailyRuleId"/>). Reuses the existing <see cref="CheckMaxDailyHours"/>
    /// helper; logic is bit-identical to the corresponding branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult EvaluateMaxDailyHours(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();
        var periodEntries = NormalizeEntries(entries, periodStart, periodEnd);
        CheckMaxDailyHours(periodEntries, config, violations, warnings);
        return BuildResult(MaxDailyRuleId, profile, violations, warnings);
    }

    /// <summary>
    /// S20 — single-check entry point for the 48h/week ceiling
    /// (<see cref="Weekly48HCeilingRuleId"/>). Reuses the existing
    /// <see cref="CheckWeeklyMaxHours"/> helper; logic is bit-identical to the
    /// corresponding branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult Evaluate48HCeiling(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();
        var periodEntries = NormalizeEntries(entries, periodStart, periodEnd);
        CheckWeeklyMaxHours(periodEntries, periodStart, periodEnd, config, violations, warnings);
        return BuildResult(Weekly48HCeilingRuleId, profile, violations, warnings);
    }

    /// <summary>
    /// Keeps the entries dated from <paramref name="periodStart"/> to <paramref name="periodEnd"/>
    /// (both inclusive), sorted by date then start time. It does NOT split an entry that crosses
    /// midnight. The Backend splits those before the call (<c>MidnightCrossingNormalizer</c>, in
    /// <c>ComplianceEndpoints</c>' compliance read), so a caller passing raw entries would see an
    /// overnight shift only on its start date.
    /// </summary>
    private static IReadOnlyList<TimeEntry> NormalizeEntries(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd) =>
        entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.StartTime)
            .ToList();

    private static ComplianceCheckResult BuildResult(
        string ruleId,
        EmploymentProfile profile,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings) =>
        new()
        {
            RuleId = ruleId,
            EmployeeId = profile.EmployeeId,
            Success = violations.Count == 0,
            Violations = violations,
            Warnings = warnings
        };

    /// <summary>
    /// The days the rest checks cannot vouch for (QUAL-234; owner ruling R-22 keeps the list): each
    /// date of the period whose project hours are above zero and whose clock hours fall short of
    /// them. Project hours are the day's entries' <see cref="TimeEntry.Hours"/> summed, every
    /// activity type (the entries the max-daily check sums); clock hours are the day's kept
    /// intervals, end minus start, summed. Both are rounded to hundredths before they are compared
    /// (<c>Math.Round(x, 2)</c>, the <c>AllocationBalance</c> convention the send check uses), so a
    /// day counts as analysed exactly when every one of its hours is on the clock. Distinct dates,
    /// ascending.
    ///
    /// <para>
    /// A listed day's intervals still form stints, so a real short rest on a partly clocked day is
    /// still reported. A day with intervals and no project hours is not listed: nothing on it is
    /// off the clock. The list covers the period's own days only, so a short rest reported against
    /// the day before the period does not say whether that day was fully clocked; the previous
    /// period's list does.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DateOnly> FindRestNotCheckedDays(
        IReadOnlyList<TimeEntry> periodEntries, IReadOnlyList<ComplianceWorkInterval> intervals)
    {
        var clockTicksByDay = intervals
            .GroupBy(iv => iv.Date)
            .ToDictionary(day => day.Key, day => day.Sum(iv => (iv.End - iv.Start).Ticks));

        return periodEntries
            .GroupBy(e => e.Date)
            .Select(day => (Date: day.Key, ProjectHours: day.Sum(e => e.Hours)))
            .Where(day =>
            {
                if (day.ProjectHours <= 0m)
                    return false;
                var clockHours = (decimal)clockTicksByDay.GetValueOrDefault(day.Date) / TimeSpan.TicksPerHour;
                return Math.Round(clockHours, 2) < Math.Round(day.ProjectHours, 2);
            })
            .Select(day => day.Date)
            .OrderBy(date => date)
            .ToList();
    }

    /// <summary>
    /// The days whose work is voluntary unsocial hours, exempt from the rest checks
    /// (SYSTEM_TARGET "Voluntary Unsocial Hours"): the agreement allows it
    /// (<see cref="AgreementRuleConfig.VoluntaryUnsocialHoursAllowed"/>), the day has at least one
    /// project entry, and every entry of that day carries
    /// <see cref="TimeEntry.VoluntaryUnsocialHours"/>. Intervals carry no such flag, so the day's
    /// entries decide for the day's intervals.
    /// </summary>
    private static IReadOnlySet<DateOnly> VoluntaryDays(
        IReadOnlyList<TimeEntry> entries, AgreementRuleConfig config) =>
        config.VoluntaryUnsocialHoursAllowed
            ? entries
                .GroupBy(e => e.Date)
                .Where(day => day.All(e => e.VoluntaryUnsocialHours))
                .Select(day => day.Key)
                .ToHashSet()
            : new HashSet<DateOnly>();

    /// <summary>
    /// Check 1: Max daily hours. Sum hours per day, flag if exceeding MaxDailyHours.
    /// </summary>
    private static void CheckMaxDailyHours(
        IReadOnlyList<TimeEntry> entries,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        var dailyHours = entries
            .GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, TotalHours = g.Sum(e => e.Hours) });

        foreach (var day in dailyHours)
        {
            if (day.TotalHours > config.MaxDailyHours)
            {
                violations.Add(new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.MAX_DAILY_HOURS,
                    Date = day.Date,
                    ActualValue = day.TotalHours,
                    ThresholdValue = config.MaxDailyHours,
                    Severity = ComplianceSeverity.VIOLATION,
                    Message = $"Daglig arbejdstid {day.TotalHours:F1}t overstiger maksimum {config.MaxDailyHours:F1}t"
                });
            }
        }
    }

    /// <summary>
    /// Check 2: Daily rest (11h minimum between the end of one working day and the start of the
    /// next). The rest is measured between stints built from the Skema clock intervals
    /// (<see cref="ReconstructStints"/>); a day whose project hours are not all on the clock is
    /// also listed in <see cref="ComplianceCheckResult.RestNotCheckedDays"/>. Voluntary unsocial
    /// hours skip this check (but not 48h ceiling). Derogation-allowed agreements get WARNING
    /// instead of VIOLATION.
    ///
    /// <para>
    /// ADR-039 D5 — the gap is computed from the ABSOLUTE INSTANTS of continuous stints, never from
    /// a per-day view: a night shift typed as 22:00–23:59 plus 00:00–06:00 is one stint, so its
    /// midnight is no one-minute rest, and the rest after it runs from 06:00 on the second day.
    /// </para>
    /// </summary>
    private static void CheckDailyRest(
        IReadOnlyList<WorkStint> stints,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        // Collapse stints into day-clusters keyed by the calendar day of the absolute START, so a
        // working day is one unit: a lunch-split day (09:00–12:00 + 13:00–17:00) stays ONE
        // cluster and an intra-day break is never mistaken for a rest-period breach. A night shift
        // clusters under its START day, so its post-midnight portion does not open a spurious
        // next-day working period. Every stint takes part, including those wholly before the
        // period: the pair selection below decides which rests this period owns.
        var clusters = stints
            .GroupBy(s => s.StartDate)
            .Select(g => new
            {
                Date = g.Key,
                Start = g.Min(s => s.AbsStart),
                End = g.Max(s => s.AbsEnd),
                AllVoluntary = g.All(s => s.AllVoluntary)
            })
            .OrderBy(c => c.Start)
            .ToList();

        for (int i = 0; i < clusters.Count - 1; i++)
        {
            var current = clusters[i];
            var next = clusters[i + 1];

            // A rest is judged in the period in which it ENDS, i.e. where the next working day
            // starts, so across consecutive periods each rest is judged exactly once: a rest from
            // 31 March 22:00 to 1 April 07:00 is April's, dated 31 March, and a rest ending after
            // periodEnd is the next period's. The earlier side may lie wholly before the period;
            // the compliance read supplies the day before (ADR-039 D5b).
            // Recorded limit: that read starts at periodStart − 1, so a chain of night shifts that
            // began earlier arrives cut at that day. The rest after it is still measured right,
            // but the finding is dated periodStart − 1, not the day the chain began.
            if (next.Date < periodStart || next.Date > periodEnd)
                continue;

            // Voluntary unsocial hours skip the rest check: if either side is wholly
            // voluntary-and-allowed, no daily-rest breach is raised across that pair.
            if (current.AllVoluntary || next.AllVoluntary)
                continue;

            // Absolute rest gap: from one working day's end instant to the next day's start instant.
            var restHours = (decimal)(next.Start - current.End).TotalHours;

            if (restHours < config.MinimumRestHours)
            {
                var severity = config.RestPeriodDerogationAllowed
                    ? ComplianceSeverity.WARNING
                    : ComplianceSeverity.VIOLATION;

                // NOTE(b): overlapping stints (the Skema save refuses overlapping intervals, but a
                // direct caller of this rule can send them) drive restHours negative — keep the
                // VIOLATION (comparison uses the true value) but clamp the DISPLAYED value to 0 so
                // the message never reads an ugly "-1.0t".
                var displayRest = Math.Max(0m, restHours);

                var finding = new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.DAILY_REST,
                    Date = current.Date,
                    ActualValue = Math.Round(displayRest, 1),
                    ThresholdValue = config.MinimumRestHours,
                    Severity = severity,
                    Message = $"Hvileperiode mellem {current.Date:yyyy-MM-dd} og {next.Date:yyyy-MM-dd} er {displayRest:F1}t — minimum er {config.MinimumRestHours:F1}t"
                };

                if (severity == ComplianceSeverity.WARNING)
                    warnings.Add(finding);
                else
                    violations.Add(finding);
            }
        }
    }

    /// <summary>
    /// Check 3: Weekly rest — at least one 24-hour uninterrupted rest per 7-day period.
    /// Simplified check: if every day in a 7-day window has work, flag as a potential weekly rest
    /// violation.
    ///
    /// <para>
    /// ADR-039 (S132 TASK-1b-3) — a working day is counted from the START day of each reconstructed
    /// stint. A midnight-crossing shift is ONE stint starting on day D, so it counts D only and does
    /// NOT manufacture a false extra worked-day on D+1 that could flip a compliant 6-day week into a
    /// flagged 7-day week (research §1 interim ruling: count the shift under its start-day only).
    /// </para>
    /// </summary>
    private static void CheckWeeklyRest(
        IReadOnlyList<WorkStint> stints,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        // Worked days = the START day of each non-voluntary stint (a night shift's two halves are
        // one stint, counted under its start day only — no false extra worked-day). Only stints
        // overlapping the period count, so a night shift starting on periodStart-1 counts under
        // periodStart-1 (outside every window here) and does NOT mark periodStart as worked via
        // its post-midnight part.
        var workDays = stints
            .Where(s => IsPeriodRelevant(s, periodStart, periodEnd))
            .Where(s => !s.AllVoluntary)
            .Select(s => s.StartDate)
            .ToHashSet();

        // Slide a 7-day window across the period
        var windowStart = periodStart;
        while (windowStart.AddDays(6) <= periodEnd)
        {
            var windowEnd = windowStart.AddDays(6);
            var daysWorked = 0;
            for (var d = windowStart; d <= windowEnd; d = d.AddDays(1))
            {
                if (workDays.Contains(d))
                    daysWorked++;
            }

            // If all 7 days have work, no weekly rest day exists
            if (daysWorked == 7)
            {
                var severity = config.RestPeriodDerogationAllowed
                    ? ComplianceSeverity.WARNING
                    : ComplianceSeverity.VIOLATION;

                var finding = new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.WEEKLY_REST,
                    Date = windowStart,
                    ActualValue = 0,
                    ThresholdValue = 1,
                    Severity = severity,
                    Message = $"Ingen ugentlig hviledag i perioden {windowStart:yyyy-MM-dd} til {windowEnd:yyyy-MM-dd}"
                };

                if (severity == ComplianceSeverity.WARNING)
                    warnings.Add(finding);
                else
                    violations.Add(finding);
            }

            windowStart = windowStart.AddDays(7);
        }
    }

    /// <summary>
    /// Check 4: 48h/week ceiling over reference period.
    /// Average weekly hours must not exceed 48h.
    /// Voluntary unsocial hours STILL count (EU directive maximum is absolute).
    /// </summary>
    private static void CheckWeeklyMaxHours(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        var totalHours = entries.Sum(e => e.Hours);
        var periodDays = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var periodWeeks = periodDays / 7.0m;

        // Only check if we have at least 1 week of data
        if (periodWeeks < 1)
            return;

        var avgWeeklyHours = totalHours / periodWeeks;

        if (avgWeeklyHours > 48.0m)
        {
            violations.Add(new ComplianceViolation
            {
                ViolationType = ComplianceViolationType.WEEKLY_MAX_HOURS,
                Date = periodStart,
                ActualValue = Math.Round(avgWeeklyHours, 1),
                ThresholdValue = 48.0m,
                Severity = ComplianceSeverity.VIOLATION,
                Message = $"Gennemsnitlig ugentlig arbejdstid {avgWeeklyHours:F1}t overstiger EU-loftet paa 48t/uge over referenceperioden"
            });
        }
    }

    // -------------------------------------------------------------------
    // ADR-039 D4/D5 — the continuous stints the rest checks measure, built from the Skema
    // clock intervals (ADR-043 D10).
    // -------------------------------------------------------------------

    /// <summary>
    /// A single continuous work stint expressed as ABSOLUTE instants. A midnight-crossing shift is
    /// ONE stint spanning two calendar days (ADR-039), never two day-buckets — so rest gaps and
    /// worked-day counts are computed from real wall-clock instants, not from a per-day view that
    /// mis-reads the midnight boundary.
    /// </summary>
    private readonly record struct WorkStint(
        DateTime AbsStart, DateTime AbsEnd, DateOnly StartDate, bool AllVoluntary);

    /// <summary>
    /// The last minute of a day. Skema accepts hours 0–23 only, so a shift that runs to midnight
    /// is typed as ending at 23:59; an interval ending here or later continues into a 00:00 start
    /// on the next day.
    /// </summary>
    private static readonly TimeOnly LastMinuteOfDay = new(23, 59);

    /// <summary>
    /// Builds the stints the rest checks measure between, from the kept intervals (end after
    /// start) alone: one stint per interval, except across midnight. <b>Midnight continuity:</b> an
    /// interval ending at 23:59 or later on day D and an interval starting at exactly 00:00 on
    /// D+1 are one stint, and the join repeats, so a chain over several midnights is one stint.
    /// Nothing else joins: two intervals touching inside a day (12:00 end, 12:00 start) are two
    /// stints, which the daily check then clusters as one working day. A stint starts at its first
    /// interval's start, ends at its last interval's end, and its start day is its first
    /// interval's date. It is all-voluntary only when every date it touches is in
    /// <paramref name="voluntaryDays"/>.
    /// </summary>
    private static List<WorkStint> ReconstructStints(
        IReadOnlyList<ComplianceWorkInterval> intervals, IReadOnlySet<DateOnly> voluntaryDays)
    {
        var groups = new List<List<ComplianceWorkInterval>>();
        // Day D → the stint whose interval on D runs to 23:59 or later, which a 00:00 start on
        // D+1 continues.
        var runsToMidnight = new Dictionary<DateOnly, List<ComplianceWorkInterval>>();

        var inTimeOrder = intervals.OrderBy(i => i.Date).ThenBy(i => i.Start).ThenBy(i => i.End);
        foreach (var iv in inTimeOrder)
        {
            if (iv.Start != TimeOnly.MinValue
                || !runsToMidnight.TryGetValue(iv.Date.AddDays(-1), out var group))
            {
                group = new List<ComplianceWorkInterval>();
                groups.Add(group);
            }
            group.Add(iv);

            if (iv.End >= LastMinuteOfDay)
                runsToMidnight[iv.Date] = group;
        }

        return groups.Select(g => BuildStint(g, voluntaryDays)).ToList();
    }

    /// <summary>
    /// One stint's absolute span from its intervals, which arrive in time order: from the first
    /// interval's start instant to the latest end instant.
    /// </summary>
    private static WorkStint BuildStint(
        IReadOnlyList<ComplianceWorkInterval> group, IReadOnlySet<DateOnly> voluntaryDays)
    {
        var first = group[0];
        return new WorkStint(
            AbsStart: first.Date.ToDateTime(first.Start),
            AbsEnd: group.Max(iv => iv.Date.ToDateTime(iv.End)),
            StartDate: first.Date,
            AllVoluntary: group.All(iv => voluntaryDays.Contains(iv.Date)));
    }

    /// <summary>
    /// True when a stint overlaps the (closed) calendar period <c>[periodStart, periodEnd]</c> in
    /// absolute terms. The weekly-rest check counts only these: a night shift starting on
    /// <c>periodStart-1</c> (supplied by the compliance read's one-day widen, ADR-039 D5b) overlaps
    /// the period through its post-midnight part and is kept — under its true start day
    /// <c>periodStart-1</c>, so it never marks <c>periodStart</c> as worked. A stint wholly before
    /// or after the period is dropped. The daily-rest check does not use this filter: it selects
    /// rests by the day they end (<see cref="CheckDailyRest"/>).
    /// </summary>
    private static bool IsPeriodRelevant(WorkStint stint, DateOnly periodStart, DateOnly periodEnd)
    {
        var periodStartInstant = periodStart.ToDateTime(TimeOnly.MinValue);          // periodStart 00:00
        var periodEndExclusive = periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);  // periodEnd+1 00:00
        return stint.AbsStart < periodEndExclusive && stint.AbsEnd > periodStartInstant;
    }
}
