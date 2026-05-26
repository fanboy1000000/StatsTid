using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: checks whether an employee's registered hours fulfill
/// the norm for a configurable period (1, 2, 4, 8, or 12 weeks).
/// Default is 1 week (37h standard, pro rata for part-time).
/// No I/O, fully deterministic, version-aware via AgreementRuleConfig.
///
/// S20 / TASK-2006: this rule decomposes into three separately-registered
/// classifications (<see cref="WeeklyRuleId"/>, <see cref="MultiWeekRuleId"/>,
/// <see cref="AnnualRuleId"/>). The legacy <see cref="RuleId"/> entry point is
/// retained verbatim so existing callers in WeeklyCalculationPipeline,
/// PeriodCalculationService, TimeEndpoints, and the smoke tests continue to work
/// without modification — it dispatches by <c>config.NormModel</c> as before.
/// </summary>
public static class NormCheckRule
{
    /// <summary>
    /// Legacy RuleId — preserved for backward compatibility with callers that pre-date
    /// the S20 segmentation classification (Orchestrator pipeline, payroll integration,
    /// backend smoke endpoints, and existing rule unit tests).
    /// </summary>
    public const string RuleId = "NORM_CHECK_37H";

    // S20 / TASK-2006 — multi-mode decomposition (ADR-016 D2):
    public const string WeeklyRuleId = "NORM_CHECK_WEEKLY";
    public const string MultiWeekRuleId = "NORM_CHECK_MULTIWEEK";
    public const string AnnualRuleId = "NORM_CHECK_ANNUAL";

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
    ///
    /// Backward-compatibility entry point — tags the produced result with the legacy
    /// <see cref="RuleId"/> ("NORM_CHECK_37H"). New callers that need decomposed-mode
    /// tagging should call <see cref="EvaluateWeekly"/>, <see cref="EvaluateMultiWeek"/>,
    /// or <see cref="EvaluateAnnual"/> directly.
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
            NormModel.ANNUAL_ACTIVITY => EvaluateAnnualCore(RuleId, profile, entries, periodStart, periodEnd, config),
            _ => EvaluateWeeksCore(RuleId, profile, entries, periodStart, periodEnd, config.NormPeriodWeeks, config.WeeklyNormHours),
        };
    }

    /// <summary>
    /// S20 — explicit single-week-norm entry point. Tags result with
    /// <see cref="WeeklyRuleId"/>. Logic identical to the legacy weeks-based path with
    /// <c>normPeriodWeeks = 1</c>.
    /// </summary>
    public static CalculationResult EvaluateWeekly(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config) =>
        EvaluateWeeksCore(WeeklyRuleId, profile, entries, periodStart, periodEnd, 1, config.WeeklyNormHours);

    /// <summary>
    /// S20 — multi-week-norm entry point (2/4/8/12 weeks per
    /// <see cref="ValidNormPeriodWeeks"/>). Tags result with
    /// <see cref="MultiWeekRuleId"/>. Reads the period count from
    /// <see cref="AgreementRuleConfig.NormPeriodWeeks"/>; values outside the valid set
    /// fall back to 1 to mirror legacy behaviour.
    /// </summary>
    public static CalculationResult EvaluateMultiWeek(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config) =>
        EvaluateWeeksCore(MultiWeekRuleId, profile, entries, periodStart, periodEnd, config.NormPeriodWeeks, config.WeeklyNormHours);

    /// <summary>
    /// S20 — annual activity (academic) norm entry point. Tags result with
    /// <see cref="AnnualRuleId"/>. Logic identical to the legacy
    /// <c>NormModel.ANNUAL_ACTIVITY</c> branch.
    /// </summary>
    public static CalculationResult EvaluateAnnual(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config) =>
        EvaluateAnnualCore(AnnualRuleId, profile, entries, periodStart, periodEnd, config);

    /// <summary>
    /// ANNUAL_ACTIVITY norm calculation: pro-rates annual norm hours to the period.
    /// Pure function, deterministic, no I/O. Result is tagged with the supplied
    /// <paramref name="ruleId"/> so the legacy and decomposed entry points share the
    /// same arithmetic without forking it.
    /// </summary>
    private static CalculationResult EvaluateAnnualCore(
        string ruleId,
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
            RuleId = ruleId,
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
    /// Core weeks-based norm Evaluate. Pure function, deterministic, no I/O. Result is
    /// tagged with the supplied <paramref name="ruleId"/> so the legacy, weekly, and
    /// multi-week entry points share the same arithmetic.
    /// </summary>
    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        int normPeriodWeeks) =>
        EvaluateWeeksCore(RuleId, profile, entries, periodStart, periodEnd, normPeriodWeeks, StandardWeeklyNorm);

    private static CalculationResult EvaluateWeeksCore(
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        int normPeriodWeeks,
        decimal weeklyNormHours)
    {
        var effectiveNormPeriodWeeks = ValidNormPeriodWeeks.Contains(normPeriodWeeks) ? normPeriodWeeks : 1;
        var normHours = weeklyNormHours * profile.PartTimeFraction * effectiveNormPeriodWeeks;

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
            RuleId = ruleId,
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
