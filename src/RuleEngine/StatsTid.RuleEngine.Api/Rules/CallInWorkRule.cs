using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: calculates call-in work (tilkald) compensation.
/// When an employee is called in outside scheduled hours, they receive
/// a minimum hours guarantee (typically 3 hours).
/// AC: disabled. HK/PROSA: enabled with 3h minimum at 1.0 rate.
/// Filters time entries with ActivityType == "CALL_IN" and produces
/// CALL_IN_WORK line items with Math.Max(actual, minimum) hours.
/// </summary>
public static class CallInWorkRule
{
    public const string RuleId = "CALL_IN_WORK";

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        if (!config.CallInWorkEnabled)
        {
            return new CalculationResult
            {
                RuleId = RuleId,
                EmployeeId = profile.EmployeeId,
                Success = true,
                LineItems = []
            };
        }

        var lineItems = new List<CalculationLineItem>();

        var callInEntries = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd
                     && e.ActivityType == "CALL_IN")
            .ToList();

        foreach (var entry in callInEntries)
        {
            var creditedHours = Math.Max(entry.Hours, config.CallInMinimumHours);

            lineItems.Add(new CalculationLineItem
            {
                TimeType = "CALL_IN_WORK",
                Hours = creditedHours,
                Rate = config.CallInRate,
                Date = entry.Date
            });
        }

        return new CalculationResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = true,
            LineItems = lineItems
        };
    }
}
