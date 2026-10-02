using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Whether an employee's registered hours fulfil the norm for a period. Pure, deterministic,
/// no I/O (ADR-002).
///
/// <para>
/// S20 / TASK-2006: the rule decomposes into three separately-registered classifications
/// (<see cref="WeeklyRuleId"/>, <see cref="MultiWeekRuleId"/>, <see cref="AnnualRuleId"/>).
/// The legacy <see cref="RuleId"/> (<c>NORM_CHECK_37H</c>) dispatches on the config's
/// <c>NormModel</c>; it is the id payroll and the Orchestrator pipeline call.
/// </para>
///
/// <para>
/// <b>S145 / TASK-14505 (R8, R34).</b> Every public evaluate method (<c>Evaluate</c>,
/// <c>EvaluateWeekly</c>, <c>EvaluateMultiWeek</c>, <c>EvaluateAnnual</c>) is retired: the weekly
/// and multi-week modes computed <c>weekly × fraction × NormPeriodWeeks</c> whatever the period
/// length, so a calendar month's norm was a fixed multiple of a week (148 h on a 4-week config,
/// 37 h on a 1-week one, where September 2026's own 22 weekdays give 162.80 h). Every norm id
/// now runs through <see cref="WeeklyTimelineCore"/>: weekly and multi-week take the SUM of
/// per-day norms over the period's own covered days (<c>DayNorm</c>, owner ruling Q2 = A);
/// annual keeps days ÷ 365, per piece. The legacy dispatch passes a forced mode for the three
/// decomposed ids (<see cref="TimelineNormMode"/>) and tags the result with the requested id.
/// What stays here is the ids and the pure helpers the core uses.
/// </para>
/// </summary>
public static class NormCheckRule
{
    /// <summary>
    /// Legacy RuleId — preserved for callers that pre-date the S20 segmentation classification
    /// (Orchestrator pipeline, payroll integration, smoke tests). Dispatches on NormModel.
    /// </summary>
    public const string RuleId = "NORM_CHECK_37H";

    // S20 / TASK-2006 — multi-mode decomposition (ADR-016 D2):
    public const string WeeklyRuleId = "NORM_CHECK_WEEKLY";
    public const string MultiWeekRuleId = "NORM_CHECK_MULTIWEEK";
    public const string AnnualRuleId = "NORM_CHECK_ANNUAL";

    /// <summary>The day count the annual-activity norm is pro-rated over (days ÷ 365).</summary>
    public const decimal DaysPerYear = 365m;

    /// <summary>
    /// Pure: the annual-activity (academic) norm for <paramref name="days"/> calendar days of one
    /// piece: <c>AnnualNormHours × fraction × days ÷ 365</c>, unrounded — the same arithmetic,
    /// in the same order, as the retired <c>NORM_CHECK_ANNUAL</c> path.
    /// </summary>
    public static decimal AnnualNorm(AgreementRuleConfig config, decimal partTimeFraction, int days)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.AnnualNormHours * partTimeFraction * days / DaysPerYear;
    }

    /// <summary>
    /// The norm-period length a config is honoured with: its <c>NormPeriodWeeks</c> when that is
    /// in <see cref="AgreementRuleConfig.ValidNormPeriodWeeks"/> (1, 2, 4, 8, 12), else 1 — the
    /// legacy fallback.
    /// </summary>
    public static int EffectiveNormPeriodWeeks(int configuredNormPeriodWeeks) =>
        AgreementRuleConfig.ValidNormPeriodWeeks.Contains(configuredNormPeriodWeeks)
            ? configuredNormPeriodWeeks
            : 1;

    /// <summary>Pure: the <c>NORMAL_HOURS</c> line one time entry produces (its own hours and date).</summary>
    public static CalculationLineItem NormalHoursLine(TimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new CalculationLineItem
        {
            TimeType = "NORMAL_HOURS",
            Hours = entry.Hours,
            Rate = 1.0m,
            Date = entry.Date
        };
    }

    /// <summary>Pure: the norm result shape (deviation = actual − norm; fulfilled when actual ≥ norm).</summary>
    public static CalculationResult BuildResult(
        string ruleId,
        string employeeId,
        List<CalculationLineItem> lineItems,
        int? normPeriodWeeks,
        decimal normHours,
        decimal actualHours) =>
        new()
        {
            RuleId = ruleId,
            EmployeeId = employeeId,
            Success = true,
            LineItems = lineItems,
            NormPeriodWeeks = normPeriodWeeks,
            NormHoursTotal = normHours,
            ActualHoursTotal = actualHours,
            Deviation = actualHours - normHours,
            NormFulfilled = actualHours >= normHours
        };
}
