using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: calculates on-call duty (radighedsvagt) compensation.
/// On-call hours are compensated at a reduced rate (typically 1/3 of normal).
/// AC: disabled by default. HK/PROSA: enabled at 0.33 rate.
/// Filters time entries with ActivityType == "ON_CALL" and produces
/// ON_CALL_DUTY line items at the configured reduced rate.
/// </summary>
public static class OnCallDutyRule
{
    public const string RuleId = "ON_CALL_DUTY";

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        if (!config.OnCallDutyEnabled)
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

        var onCallEntries = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd
                     && e.ActivityType == "ON_CALL")
            .ToList();

        foreach (var entry in onCallEntries)
        {
            lineItems.Add(new CalculationLineItem
            {
                TimeType = "ON_CALL_DUTY",
                Hours = entry.Hours,
                Rate = config.OnCallDutyRate,
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
