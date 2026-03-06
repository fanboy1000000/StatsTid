using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: calculates travel time compensation.
/// Working travel (TRAVEL_WORK): employee works during travel, counted at full rate (1.0).
/// Non-working travel (TRAVEL_NON_WORK): employee travels but does not work, counted at reduced rate (default 0.5).
/// Config-controlled via TravelTimeEnabled toggle.
/// </summary>
public static class TravelTimeRule
{
    public const string RuleId = "TRAVEL_TIME";

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        if (!config.TravelTimeEnabled)
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

        var travelEntries = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd
                     && (e.ActivityType == "TRAVEL_WORK" || e.ActivityType == "TRAVEL_NON_WORK"))
            .ToList();

        foreach (var entry in travelEntries)
        {
            var isWorkingTravel = entry.ActivityType == "TRAVEL_WORK";

            lineItems.Add(new CalculationLineItem
            {
                TimeType = isWorkingTravel ? "TRAVEL_WORK" : "TRAVEL_NON_WORK",
                Hours = entry.Hours,
                Rate = isWorkingTravel ? config.WorkingTravelRate : config.NonWorkingTravelRate,
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
