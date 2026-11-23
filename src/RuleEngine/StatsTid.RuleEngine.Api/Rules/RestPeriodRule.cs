using System.Globalization;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: validates working time compliance against EU directive 2003/88/EC
/// and Danish Arbejdstidsloven. Checks daily rest (the agreement's minimum), weekly rest, max
/// daily hours, and 48h/week ceiling. No I/O, fully deterministic; the agreement's values arrive
/// as a parameter, one configuration per day (below).
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
/// <strong>One configuration per day (ADR-043 D12, RR-7).</strong> The caller hands the agreement's
/// values in as a function of the day, because one period can hold two values of a setting (a
/// minimum rest of 11 h until a Sunday and 12 h from the Monday). Each check reads the value of its
/// own day: a work end the minimum rest and derogation setting of its date; a day its maximum hours
/// and its voluntary flag; a 7-day window the derogation setting of its last day, while the finding
/// is dated on the window's first day. The 48h/week ceiling reads no configured value.
/// </para>
///
/// <para>
/// <strong>ADR-039 segmentation note.</strong> The rest checks join a night shift into one
/// continuous stint across midnight, and the daily check judges each work end in the period on the
/// 24 hours before it, which can reach two days before the period (the compliance read starts
/// there; ADR-039 D5b, QUAL-253). This is safe with
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
    /// The shortest daily rest a derogation may leave. Where the agreement allows derogation
    /// (<see cref="AgreementRuleConfig.RestPeriodDerogationAllowed"/>), a daily rest of at least
    /// this many hours is a WARNING and a shorter one a VIOLATION. It is the order's number, not
    /// an agreement setting: BEK nr. 606 af 29/06/2026 § 19 and Statens arbejdstidsaftale § 12
    /// (CIR1H nr. 9594 af 18/06/2026, Medst. 022-26; identical in OK21's CIR1H 9551). How often a
    /// cut is used and the compensatory rest after it are not checked (QUAL-254).
    /// </summary>
    public const decimal DerogationFloorHours = 8.0m;

    /// <summary>The window every work end is judged on: the 24 elapsed hours before it.</summary>
    private static readonly TimeSpan DailyRestWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// What a derogation WARNING adds to its message: the limits on a cut that StatsTid does not
    /// check (BEK nr. 606 § 19; Statens arbejdstidsaftale § 12; QUAL-254).
    /// </summary>
    private const string DerogationNotCheckedSuffix =
        ". Hvilen kan undtagelsesvis nedsættes til 8 timer, højst 1 gang pr. 7 døgn (2 gange efter lokal aftale) og kun med tilsvarende kompenserende hvile umiddelbart efter. StatsTid kontrollerer ingen af delene.";

    /// <summary>
    /// The four compliance checks with one agreement configuration in force on every day: the
    /// per-day form below with <paramref name="config"/> for each date.
    /// </summary>
    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        IReadOnlyList<ComplianceWorkInterval>? workIntervals = null,
        TimeZoneInfo? copenhagenZone = null) =>
        Evaluate(profile, entries, periodStart, periodEnd, _ => config, workIntervals, copenhagenZone);

    /// <summary>
    /// Evaluates all four compliance checks for the given period, tagged with <see cref="RuleId"/>;
    /// the one entry point for the rest checks. Pure function, deterministic, no I/O.
    /// </summary>
    /// <param name="entries">
    /// The project entries, which the compliance read takes from two days before
    /// <paramref name="periodStart"/> (ADR-039 D5b, QUAL-253). The hours checks sum their hours
    /// inside the period; the rest checks read only their voluntary-unsocial-hours flag, per day.
    /// </param>
    /// <param name="configForDay">
    /// The agreement configuration in force on a date. The checks ask for a date that carries an
    /// entry or an interval, never another: on or before <paramref name="periodEnd"/> for every
    /// value, after it only for a day's voluntary flag. The route builds it from the request's
    /// dated runs (<see cref="RuleRegistry.ComplianceConfigForDay"/>).
    /// </param>
    /// <param name="workIntervals">
    /// The clock intervals typed in Skema over the same dates, each on the date it was entered on.
    /// The rest checks build their work periods from these alone. An interval dated after
    /// <paramref name="periodEnd"/> is ignored by the daily rest check; the weekly check still
    /// reads it. Absent means no interval: no rest is measured, and every day of the period with
    /// project hours is listed in <see cref="ComplianceCheckResult.RestNotCheckedDays"/>.
    /// </param>
    /// <param name="copenhagenZone">
    /// Denmark's time zone (Europe/Copenhagen), passed by the host. The daily rest check measures
    /// elapsed hours in it, so the night the clocks go forward is one hour shorter and the night
    /// they go back one hour longer. The rule never looks a zone up itself (ADR-002: external data
    /// is a parameter, never fetched). Required whenever at least one interval is kept.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// At least one interval is kept and <paramref name="copenhagenZone"/> is null.
    /// </exception>
    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        Func<DateOnly, AgreementRuleConfig> configForDay,
        IReadOnlyList<ComplianceWorkInterval>? workIntervals = null,
        TimeZoneInfo? copenhagenZone = null)
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

        // Rest is measured in elapsed time, which needs the zone; a caller that sends clock times
        // without it is refused rather than measured in wall-clock hours.
        if (intervals.Count > 0)
            ArgumentNullException.ThrowIfNull(copenhagenZone);

        // 1. Max daily hours check (works for all entries, including hours-only). Hours-summing
        //    checks consume the PERIOD-FILTERED per-day rows so out-of-period hours never leak
        //    into period totals (ADR-039 BLOCKER-2).
        CheckMaxDailyHours(periodEntries, configForDay, violations, warnings);

        // 2 + 3. The rest checks measure work periods built from the intervals alone
        //    (ADR-043 D10); each check then decides which of them the period owns. The daily
        //    check's are built without the intervals dated after the period, so work running past
        //    its last midnight is judged as the period sees it whatever the caller sends
        //    (QUAL-253); the weekly check's from every kept interval. The zone is non-null
        //    whenever a stint exists (checked above).
        var voluntaryDays = VoluntaryDays(entries, configForDay);
        var stints = ReconstructStints(intervals, voluntaryDays, periodStart);
        var dailyStints = ReconstructStints(
            intervals.Where(iv => iv.Date <= periodEnd).ToList(), voluntaryDays, periodStart);
        CheckDailyRest(dailyStints, periodStart, configForDay, copenhagenZone!, violations, warnings);
        CheckWeeklyRest(stints, periodStart, periodEnd, configForDay, violations, warnings);

        // 4. 48h/week ceiling (works for all entries) — period-filtered per-day rows. It reads no
        //    configured value (its 48 hours and its averaging are fixed; O-24), so no day's
        //    configuration is chosen for it and its unused config parameter is handed none.
        CheckWeeklyMaxHours(periodEntries, periodStart, periodEnd, config: null!, violations, warnings);

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
        CheckMaxDailyHours(periodEntries, _ => config, violations, warnings);
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
    /// off the clock. The list covers the period's own days only, so a finding whose 24 hours reach
    /// into the days before the period does not say whether those days were fully clocked; the
    /// previous period's list does.
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
    /// (SYSTEM_TARGET "Voluntary Unsocial Hours"): the day has at least one project entry, every
    /// entry of that day carries <see cref="TimeEntry.VoluntaryUnsocialHours"/>, and the agreement in
    /// force that day allows it (<see cref="AgreementRuleConfig.VoluntaryUnsocialHoursAllowed"/>).
    /// Intervals carry no such flag, so the day's entries decide for the day's intervals.
    /// </summary>
    private static IReadOnlySet<DateOnly> VoluntaryDays(
        IReadOnlyList<TimeEntry> entries, Func<DateOnly, AgreementRuleConfig> configForDay) =>
        entries
            .GroupBy(e => e.Date)
            .Where(day => day.All(e => e.VoluntaryUnsocialHours)
                && configForDay(day.Key).VoluntaryUnsocialHoursAllowed)
            .Select(day => day.Key)
            .ToHashSet();

    /// <summary>
    /// Check 1: Max daily hours. Sum hours per day, flag a day above the MaxDailyHours in force that
    /// day.
    /// </summary>
    private static void CheckMaxDailyHours(
        IReadOnlyList<TimeEntry> entries,
        Func<DateOnly, AgreementRuleConfig> configForDay,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        var dailyHours = entries
            .GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, TotalHours = g.Sum(e => e.Hours) });

        foreach (var day in dailyHours)
        {
            var maxDailyHours = configForDay(day.Date).MaxDailyHours;
            if (day.TotalHours > maxDailyHours)
            {
                violations.Add(new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.MAX_DAILY_HOURS,
                    Date = day.Date,
                    ActualValue = day.TotalHours,
                    ThresholdValue = maxDailyHours,
                    Severity = ComplianceSeverity.VIOLATION,
                    Message = $"Daglig arbejdstid {day.TotalHours:F1}t overstiger maksimum {maxDailyHours:F1}t"
                });
            }
        }
    }

    /// <summary>
    /// Check 2: Daily rest, the authority's test (arbejdsmiljøloven § 50, BEK nr. 606 § 3,
    /// AT-meddelelse 5.01.1; QUAL-253): every 24 hours must hold at least
    /// <see cref="AgreementRuleConfig.MinimumRestHours"/> unbroken hours of rest. Each work end is
    /// judged on the 24 elapsed hours before it, against the minimum in force on the end's own date
    /// (RR-7). Judging at the ends is enough, because the longest rest in the window only shrinks
    /// while work goes on.
    ///
    /// <list type="bullet">
    /// <item><b>Work.</b> Every interval of every stint that is not wholly voluntary, as elapsed
    /// instants in the zone the host passes (<see cref="ToInstant"/>); overlapping or touching
    /// intervals are one block, and an interval with no elapsed length (typed inside March's missing
    /// hour) breaks no rest. A wholly voluntary stint (SYSTEM_TARGET "Voluntary Unsocial Hours") is
    /// neither judged nor work in another end's window. Time with no registered work is rest; a
    /// day whose project hours are not all on the clock is listed in
    /// <see cref="ComplianceCheckResult.RestNotCheckedDays"/>.</item>
    /// <item><b>Judged ends.</b> One per stint: its latest end instant, dated the day of the
    /// interval that carries it (as typed: an end typed 23:59 on 31 March is 31 March's). The
    /// period judges the ends dated inside it. The check ignores the intervals dated after the
    /// period (<see cref="Evaluate"/> builds its stints without them), so a night shift over the
    /// period's last midnight is judged as ending at 24:00 on the last day, and again in full by
    /// the next period. Those intervals begin at or after every end the period judges, so they lie
    /// outside every window it measures.</item>
    /// <item><b>The value.</b> The longest single unbroken rest inside the window; a rest that began
    /// before the window counts from the window's start, and short rests never add up. Below the
    /// minimum is a finding; exactly the minimum passes. The 13 hours this allows is 24 minus the
    /// minimum, not a setting of its own.</item>
    /// <item><b>One finding per day.</b> Among a date's failing ends, the lowest value, ties to the
    /// earliest end; its message names that end. The verdict compares the true value; the shown
    /// value is rounded down to one decimal, so a rest of 10 h 59 min reads 10.9, never "11.0"
    /// against a minimum of 11.0.</item>
    /// <item><b>Derogation</b> (<see cref="AgreementRuleConfig.RestPeriodDerogationAllowed"/>, of the
    /// end's date): a WARNING when the rest is at least <see cref="DerogationFloorHours"/>, else a
    /// VIOLATION; the warning says what StatsTid does not check.</item>
    /// <item><b>The value of the end's date.</b> The minimum and the derogation setting a judged end
    /// is held to are those in force on its date, so a period whose minimum changes on a Monday
    /// judges a Sunday end by Sunday's value and a Monday end by Monday's, though their 24 hours
    /// overlap (ADR-043 D12; RR-7). The finding's threshold and message name that value.</item>
    /// </list>
    /// </summary>
    private static void CheckDailyRest(
        IReadOnlyList<WorkStint> stints,
        DateOnly periodStart,
        Func<DateOnly, AgreementRuleConfig> configForDay,
        TimeZoneInfo copenhagenZone,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        var takingPart = stints.Where(s => !s.AllVoluntary).ToList();

        var work = takingPart
            .SelectMany(s => s.Intervals)
            .Select(iv => (
                Start: ToInstant(iv.Date, iv.Start, isEnd: false, copenhagenZone),
                End: ToInstant(iv.Date, iv.End, isEnd: true, copenhagenZone)))
            .Where(w => w.End > w.Start)
            .OrderBy(w => w.Start)
            .ToList();

        var judgedEnds = takingPart
            .Select(s => s.Intervals
                .Select(iv => (iv.Date, Typed: iv.End, Instant: ToInstant(iv.Date, iv.End, isEnd: true, copenhagenZone)))
                .MaxBy(end => (end.Instant, end.Date, end.Typed)))
            .Where(end => end.Date >= periodStart);

        var failingByDay = judgedEnds
            .Select(end => (End: end, RestHours: LongestRestHours(work, end.Instant), Config: configForDay(end.Date)))
            .Where(judged => judged.RestHours < judged.Config.MinimumRestHours)
            .GroupBy(judged => judged.End.Date)
            .Select(day => day.OrderBy(judged => judged.RestHours).ThenBy(judged => judged.End.Instant).First())
            .OrderBy(judged => judged.End.Date);

        foreach (var (end, restHours, config) in failingByDay)
        {
            var severity = config.RestPeriodDerogationAllowed && restHours >= DerogationFloorHours
                ? ComplianceSeverity.WARNING
                : ComplianceSeverity.VIOLATION;

            // Shown rounded down, so the figure agrees with the verdict at every threshold that is
            // a multiple of 0.1; the message is formatted from the same value.
            var shown = Math.Floor(restHours * 10m) / 10m;
            var message =
                $"Længste sammenhængende hvile i de 24 timer før arbejdets afslutning " +
                $"{end.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} kl. " +
                $"{end.Typed.ToString("HH:mm", CultureInfo.InvariantCulture)} er {shown:F1}t — " +
                $"minimum er {config.MinimumRestHours:F1}t";
            if (severity == ComplianceSeverity.WARNING)
                message += DerogationNotCheckedSuffix;

            var finding = new ComplianceViolation
            {
                ViolationType = ComplianceViolationType.DAILY_REST,
                Date = end.Date,
                ActualValue = shown,
                ThresholdValue = config.MinimumRestHours,
                Severity = severity,
                Message = message
            };

            if (severity == ComplianceSeverity.WARNING)
                warnings.Add(finding);
            else
                violations.Add(finding);
        }
    }

    /// <summary>
    /// The longest single unbroken rest, in hours, inside the 24 hours before
    /// <paramref name="windowEnd"/>: the parts of the window that <paramref name="work"/> (elapsed
    /// intervals, ordered by start) does not cover. Exact (from ticks); never negative.
    /// </summary>
    private static decimal LongestRestHours(
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> work, DateTimeOffset windowEnd)
    {
        var restFrom = windowEnd - DailyRestWindow;
        var longest = TimeSpan.Zero;
        foreach (var (start, end) in work)
        {
            if (start >= windowEnd)
                break;
            if (end <= restFrom)
                continue;
            if (start > restFrom && start - restFrom > longest)
                longest = start - restFrom;
            restFrom = end;
        }

        if (windowEnd - restFrom > longest)
            longest = windowEnd - restFrom;
        return (decimal)longest.Ticks / TimeSpan.TicksPerHour;
    }

    /// <summary>
    /// The elapsed-time instant at which a wall clock in <paramref name="zone"/> shows
    /// <paramref name="time"/> on <paramref name="date"/>. The zone is a parameter: this rule never
    /// looks one up (ADR-002), so the same input and zone always give the same instant. Where the
    /// wall clock is not one-to-one, the choice errs toward reporting a short rest:
    /// <list type="bullet">
    /// <item>an end typed 23:59 or later is the end of its day, the instant of 00:00 on the next
    /// date: Skema cannot hold 24:00, and the midnight join already counts that minute as work;</item>
    /// <item>a time in the hour that occurs twice (the last Sunday of October) is the earlier instant
    /// for a start and the later one for an end;</item>
    /// <item>a time in the hour that does not occur (the last Sunday of March) is the changeover
    /// instant, so an interval with both ends in that hour has no elapsed length.</item>
    /// </list>
    /// </summary>
    private static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, bool isEnd, TimeZoneInfo zone)
    {
        var wallClock = isEnd && time >= LastMinuteOfDay
            ? date.AddDays(1).ToDateTime(TimeOnly.MinValue)
            : date.ToDateTime(time);

        if (zone.IsInvalidTime(wallClock))
            return ChangeoverInstant(wallClock, zone);

        if (zone.IsAmbiguousTime(wallClock))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(wallClock);
            return new DateTimeOffset(wallClock, isEnd ? offsets.Min() : offsets.Max());
        }

        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
    }

    /// <summary>
    /// The instant the clocks jump over <paramref name="wallClock"/>, a time that does not occur in
    /// <paramref name="zone"/>: the first instant whose offset is no longer the one in force a day
    /// earlier, found by halving the two days around it down to one tick.
    /// </summary>
    private static DateTimeOffset ChangeoverInstant(DateTime wallClock, TimeZoneInfo zone)
    {
        var before = new DateTimeOffset(wallClock.AddDays(-1), TimeSpan.Zero);
        var after = new DateTimeOffset(wallClock.AddDays(1), TimeSpan.Zero);
        var offsetBefore = zone.GetUtcOffset(before);
        while (after.UtcTicks - before.UtcTicks > 1)
        {
            var middle = before.AddTicks((after.UtcTicks - before.UtcTicks) / 2);
            if (zone.GetUtcOffset(middle) == offsetBefore)
                before = middle;
            else
                after = middle;
        }

        return after;
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
    ///
    /// <para>
    /// A flagged window is a WARNING or a VIOLATION by the
    /// <see cref="AgreementRuleConfig.RestPeriodDerogationAllowed"/> in force on the window's LAST
    /// day, the same later-day rule the daily check follows (RR-7), while the finding is dated on the
    /// window's FIRST day: when the setting changes inside a window, the date shown and the day whose
    /// setting decided differ.
    /// </para>
    /// </summary>
    private static void CheckWeeklyRest(
        IReadOnlyList<WorkStint> stints,
        DateOnly periodStart,
        DateOnly periodEnd,
        Func<DateOnly, AgreementRuleConfig> configForDay,
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
                var severity = configForDay(windowEnd).RestPeriodDerogationAllowed
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
                // One decimal, halves up: the same rounding as the message's F1, so the two agree.
                ActualValue = Math.Round(avgWeeklyHours, 1, MidpointRounding.AwayFromZero),
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
    /// A single continuous work stint. A midnight-crossing shift is ONE stint spanning two calendar
    /// days (ADR-039), never two day-buckets, so the midnight boundary is never read as a rest.
    /// <c>AbsStart</c>/<c>AbsEnd</c> are its wall-clock span, from which the weekly check counts
    /// worked days; the daily check measures elapsed time from <c>Intervals</c>, the stint's own
    /// intervals in time order (<see cref="ToInstant"/>), never from the wall-clock span.
    /// </summary>
    private readonly record struct WorkStint(
        DateTime AbsStart, DateTime AbsEnd, DateOnly StartDate, bool AllVoluntary,
        IReadOnlyList<ComplianceWorkInterval> Intervals);

    /// <summary>
    /// The last minute of a day. Skema accepts hours 0–23 only, so a shift that runs to midnight
    /// is typed as ending at 23:59; an interval ending here or later continues into a 00:00 start
    /// on the next day, and the daily check reads such an end as midnight.
    /// </summary>
    private static readonly TimeOnly LastMinuteOfDay = new(23, 59);

    /// <summary>
    /// Builds the stints the rest checks measure between, from the kept intervals (end after
    /// start) alone: one stint per interval, except across midnight. <b>Midnight continuity:</b> an
    /// interval ending at 23:59 or later on day D and an interval starting at exactly 00:00 on
    /// D+1 are one stint, and the join repeats, so a chain over several midnights is one stint.
    /// Nothing else joins: two intervals touching inside a day (12:00 end, 12:00 start) are two
    /// stints, and the daily check's work is their union, so no rest lies between them. A stint
    /// starts at its first interval's start, ends at its last interval's end, and its start day is
    /// its first interval's date. It is all-voluntary only when every date it touches is in
    /// <paramref name="voluntaryDays"/> and it does not start at exactly 00:00 on a date before
    /// <paramref name="periodStart"/>: the compliance read begins before the period and the rule
    /// does not know on which day, so such a stint may continue work from a day the read cut off,
    /// and its beginning is unknown. The rule errs toward reporting the rest, never toward hiding it.
    /// </summary>
    private static List<WorkStint> ReconstructStints(
        IReadOnlyList<ComplianceWorkInterval> intervals,
        IReadOnlySet<DateOnly> voluntaryDays,
        DateOnly periodStart)
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

        return groups.Select(g => BuildStint(g, voluntaryDays, periodStart)).ToList();
    }

    /// <summary>
    /// One stint from its intervals, which arrive in time order: its wall-clock span, from the first
    /// interval's start to the latest end, and the intervals themselves. A stint cut at the read
    /// edge (first interval at exactly 00:00 on a date before <paramref name="periodStart"/>) is
    /// never voluntary; see <see cref="ReconstructStints"/>.
    /// </summary>
    private static WorkStint BuildStint(
        IReadOnlyList<ComplianceWorkInterval> group,
        IReadOnlySet<DateOnly> voluntaryDays,
        DateOnly periodStart)
    {
        var first = group[0];
        var cutAtReadEdge = first.Start == TimeOnly.MinValue && first.Date < periodStart;
        return new WorkStint(
            AbsStart: first.Date.ToDateTime(first.Start),
            AbsEnd: group.Max(iv => iv.Date.ToDateTime(iv.End)),
            StartDate: first.Date,
            AllVoluntary: !cutAtReadEdge && group.All(iv => voluntaryDays.Contains(iv.Date)),
            Intervals: group);
    }

    /// <summary>
    /// True when a stint overlaps the (closed) calendar period <c>[periodStart, periodEnd]</c> in
    /// wall-clock terms. The weekly-rest check counts only these: a night shift starting on
    /// <c>periodStart-1</c> (supplied by the compliance read, which starts before the period,
    /// ADR-039 D5b) overlaps the period through its post-midnight part and is kept — under its true
    /// start day <c>periodStart-1</c>, so it never marks <c>periodStart</c> as worked. A stint wholly
    /// before or after the period is dropped. The daily-rest check does not use this filter: it
    /// judges the work ends dated inside the period (<see cref="CheckDailyRest"/>).
    /// </summary>
    private static bool IsPeriodRelevant(WorkStint stint, DateOnly periodStart, DateOnly periodEnd)
    {
        var periodStartInstant = periodStart.ToDateTime(TimeOnly.MinValue);          // periodStart 00:00
        var periodEndExclusive = periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);  // periodEnd+1 00:00
        return stint.AbsStart < periodEndExclusive && stint.AbsEnd > periodStartInstant;
    }
}
