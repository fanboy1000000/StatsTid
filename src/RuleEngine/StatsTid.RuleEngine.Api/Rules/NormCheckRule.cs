using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: checks whether an employee's registered hours fulfill
/// the norm for a configurable period (1, 2, 4, 8, or 12 weeks).
/// Default is 1 week (37h standard, pro rata for part-time).
/// No I/O, fully deterministic, version-aware via AgreementRuleConfig.
/// </summary>
public static class NormCheckRule
{
    public const string RuleId = "NORM_CHECK_37H";
    public const decimal StandardWeeklyNorm = 37.0m;

    /// <summary>
    /// Valid norm period lengths in weeks.
    /// </summary>
    public static readonly IReadOnlySet<int> ValidNormPeriodWeeks = new HashSet<int> { 1, 2, 4, 8, 12 };

    /// <summary>
    /// Config-aware Evaluate: uses NormPeriodWeeks from AgreementRuleConfig.
    /// </summary>
    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        return Evaluate(profile, entries, periodStart, periodEnd, config.NormPeriodWeeks);
    }

    /// <summary>
    /// Core Evaluate with explicit normPeriodWeeks parameter.
    /// Pure function, deterministic, no I/O.
    /// </summary>
    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        int normPeriodWeeks)
    {
        var effectiveNormPeriodWeeks = ValidNormPeriodWeeks.Contains(normPeriodWeeks) ? normPeriodWeeks : 1;
        var normHours = profile.WeeklyNormHours * profile.PartTimeFraction * effectiveNormPeriodWeeks;

        var periodEntries = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .ToList();

        var actualHours = periodEntries.Sum(e => e.Hours);

        var lineItems = periodEntries
            .Select(e => new CalculationLineItem
            {
                TimeType = "NORMAL_HOURS",
                Hours = e.Hours,
                Rate = 1.0m,
                Date = e.Date
            })
            .ToList();

        return new CalculationResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = true,
            LineItems = lineItems,
            NormPeriodWeeks = effectiveNormPeriodWeeks,
            NormHoursTotal = normHours,
            ActualHoursTotal = actualHours,
            Deviation = actualHours - normHours,
            NormFulfilled = actualHours >= normHours
        };
    }
}
