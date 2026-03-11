using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: validates working time compliance against EU directive 2003/88/EC
/// and Danish Arbejdstidsloven. Checks daily rest (11h), weekly rest, max daily hours,
/// and 48h/week ceiling. No I/O, fully deterministic, version-aware via AgreementRuleConfig.
/// </summary>
public static class RestPeriodRule
{
    public const string RuleId = "REST_PERIOD_CHECK";

    /// <summary>
    /// Evaluates all compliance checks for the given period.
    /// Pure function, deterministic, no I/O.
    /// </summary>
    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();

        var periodEntries = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.StartTime)
            .ToList();

        // 1. Max daily hours check (works for all entries, including hours-only)
        CheckMaxDailyHours(periodEntries, config, violations, warnings);

        // 2. Daily rest check (requires StartTime/EndTime on adjacent days)
        CheckDailyRest(periodEntries, config, violations, warnings);

        // 3. Weekly rest check (requires StartTime/EndTime)
        CheckWeeklyRest(periodEntries, periodStart, periodEnd, config, violations, warnings);

        // 4. 48h/week ceiling (works for all entries)
        CheckWeeklyMaxHours(periodEntries, periodStart, periodEnd, config, violations, warnings);

        return new ComplianceCheckResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = violations.Count == 0,
            Violations = violations,
            Warnings = warnings
        };
    }

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
    /// Check 2: Daily rest (11h minimum between end of work day N and start of day N+1).
    /// Only entries with both StartTime and EndTime can be analyzed.
    /// Voluntary unsocial hours skip this check (but not 48h ceiling).
    /// Derogation-allowed agreements get WARNING instead of VIOLATION.
    /// </summary>
    private static void CheckDailyRest(
        IReadOnlyList<TimeEntry> entries,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        // Get the latest EndTime per day and earliest StartTime per day
        var dayBounds = entries
            .Where(e => e.StartTime.HasValue && e.EndTime.HasValue)
            .GroupBy(e => e.Date)
            .Select(g => new
            {
                Date = g.Key,
                EarliestStart = g.Min(e => e.StartTime!.Value),
                LatestEnd = g.Max(e => e.EndTime!.Value),
                AllVoluntary = g.All(e => e.VoluntaryUnsocialHours && config.VoluntaryUnsocialHoursAllowed)
            })
            .OrderBy(d => d.Date)
            .ToList();

        for (int i = 0; i < dayBounds.Count - 1; i++)
        {
            var currentDay = dayBounds[i];
            var nextDay = dayBounds[i + 1];

            // Skip if days are not adjacent
            if (nextDay.Date.DayNumber - currentDay.Date.DayNumber != 1)
                continue;

            // If the next day's entries are all voluntary, skip rest check
            if (nextDay.AllVoluntary)
                continue;

            // If the current day's entries are all voluntary, skip rest check
            if (currentDay.AllVoluntary)
                continue;

            // Compute rest gap: from current day's latest end to next day's earliest start
            var endMinutes = currentDay.LatestEnd.Hour * 60 + currentDay.LatestEnd.Minute;
            var startMinutes = nextDay.EarliestStart.Hour * 60 + nextDay.EarliestStart.Minute;

            // Handle overnight: add 24 hours to start if it's the next day
            var restMinutes = (24 * 60 - endMinutes) + startMinutes;
            var restHours = restMinutes / 60.0m;

            if (restHours < config.MinimumRestHours)
            {
                var severity = config.RestPeriodDerogationAllowed
                    ? ComplianceSeverity.WARNING
                    : ComplianceSeverity.VIOLATION;

                var finding = new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.DAILY_REST,
                    Date = currentDay.Date,
                    ActualValue = Math.Round(restHours, 1),
                    ThresholdValue = config.MinimumRestHours,
                    Severity = severity,
                    Message = $"Hvileperiode mellem {currentDay.Date:yyyy-MM-dd} og {nextDay.Date:yyyy-MM-dd} er {restHours:F1}t — minimum er {config.MinimumRestHours:F1}t"
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
    /// Simplified check: if every day in a 7-day window has time entries with StartTime/EndTime,
    /// flag as a potential weekly rest violation.
    /// </summary>
    private static void CheckWeeklyRest(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        // Collect days that have work entries with time-of-day data (non-voluntary)
        var workDays = entries
            .Where(e => e.StartTime.HasValue && e.EndTime.HasValue)
            .Where(e => !(e.VoluntaryUnsocialHours && config.VoluntaryUnsocialHoursAllowed))
            .Select(e => e.Date)
            .Distinct()
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
}
