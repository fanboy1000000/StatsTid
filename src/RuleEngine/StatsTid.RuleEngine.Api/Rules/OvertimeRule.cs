using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: calculates overtime (HK/PROSA) or merarbejde (AC).
/// The critical AC vs HK/PROSA distinction:
///   - AC: HasMerarbejde=true, HasOvertime=false → excess → MERARBEJDE at 1.0x
///   - HK/PROSA: HasOvertime=true → 37-40h → OVERTIME_50 at 1.5x, >40h → OVERTIME_100 at 2.0x
/// Part-time: pro-rated thresholds.
/// </summary>
public static class OvertimeRule
{
    public const string RuleId = "OVERTIME_CALC";

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var lineItems = new List<CalculationLineItem>();

        var totalHours = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .Sum(e => e.Hours);

        var normHours = config.WeeklyNormHours * profile.PartTimeFraction;

        if (totalHours <= normHours)
        {
            return new CalculationResult
            {
                RuleId = RuleId,
                EmployeeId = profile.EmployeeId,
                Success = true,
                LineItems = lineItems
            };
        }

        var excessHours = totalHours - normHours;

        // AC path: merarbejde only (no overtime)
        if (config.HasMerarbejde && !config.HasOvertime)
        {
            lineItems.Add(new CalculationLineItem
            {
                TimeType = OvertimeTypes.Merarbejde,
                Hours = excessHours,
                Rate = 1.0m,
                Date = periodStart
            });
        }
        // HK/PROSA path: tiered overtime
        else if (config.HasOvertime)
        {
            var threshold50 = config.OvertimeThreshold50 * profile.PartTimeFraction;
            var threshold100 = config.OvertimeThreshold100 * profile.PartTimeFraction;

            // Hours between norm and threshold100
            var overtime50Hours = Math.Min(excessHours, threshold100 - threshold50);
            overtime50Hours = Math.Max(overtime50Hours, 0);

            // Hours above threshold100
            var overtime100Hours = Math.Max(totalHours - threshold100, 0);

            if (overtime50Hours > 0)
            {
                lineItems.Add(new CalculationLineItem
                {
                    TimeType = OvertimeTypes.Overtime50,
                    Hours = overtime50Hours,
                    Rate = 1.5m,
                    Date = periodStart
                });
            }

            if (overtime100Hours > 0)
            {
                lineItems.Add(new CalculationLineItem
                {
                    TimeType = OvertimeTypes.Overtime100,
                    Hours = overtime100Hours,
                    Rate = 2.0m,
                    Date = periodStart
                });
            }
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
