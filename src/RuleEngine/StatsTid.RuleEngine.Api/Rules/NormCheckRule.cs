using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: checks whether an employee's registered hours fulfill
/// the weekly norm (37h standard, pro rata for part-time).
/// No I/O, fully deterministic.
/// </summary>
public static class NormCheckRule
{
    public const string RuleId = "NORM_CHECK_37H";
    public const decimal StandardWeeklyNorm = 37.0m;

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var normHours = profile.WeeklyNormHours * profile.PartTimeFraction;
        var actualHours = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .Sum(e => e.Hours);

        var deviation = actualHours - normHours;
        var fulfilled = actualHours >= normHours;

        var lineItems = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
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
            LineItems = lineItems
        };
    }

    public static NormCheckSummary Summarize(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var normHours = profile.WeeklyNormHours * profile.PartTimeFraction;
        var actualHours = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .Sum(e => e.Hours);

        return new NormCheckSummary
        {
            EmployeeId = profile.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            NormHours = normHours,
            ActualHours = actualHours,
            Deviation = actualHours - normHours,
            NormFulfilled = actualHours >= normHours
        };
    }
}

public sealed class NormCheckSummary
{
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal NormHours { get; init; }
    public required decimal ActualHours { get; init; }
    public required decimal Deviation { get; init; }
    public required bool NormFulfilled { get; init; }
}
