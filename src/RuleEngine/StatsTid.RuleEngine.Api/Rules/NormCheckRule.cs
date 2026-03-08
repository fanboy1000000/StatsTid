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
    /// Config-aware Evaluate: dispatches based on NormModel.
    /// WEEKLY_HOURS: delegates to the explicit normPeriodWeeks overload.
    /// ANNUAL_ACTIVITY: pro-rates annual norm to the period length.
    /// Pure function, deterministic, no I/O.
    /// </summary>
    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        return config.NormModel switch
        {
            NormModel.ANNUAL_ACTIVITY => EvaluateAnnualActivity(profile, entries, periodStart, periodEnd, config),
            _ => Evaluate(profile, entries, periodStart, periodEnd, config.NormPeriodWeeks),
        };
    }

    /// <summary>
    /// ANNUAL_ACTIVITY norm calculation: pro-rates annual norm hours to the period.
    /// Pure function, deterministic, no I/O.
    /// </summary>
    private static CalculationResult EvaluateAnnualActivity(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var periodDays = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var annualNorm = config.AnnualNormHours * profile.PartTimeFraction;
        var periodNorm = annualNorm * periodDays / 365m;

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
            NormPeriodWeeks = null,
            NormHoursTotal = periodNorm,
            ActualHoursTotal = actualHours,
            Deviation = actualHours - periodNorm,
            NormFulfilled = actualHours >= periodNorm
        };
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
