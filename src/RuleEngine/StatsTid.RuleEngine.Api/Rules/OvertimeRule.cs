using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Overtime (HK/PROSA) or merarbejde (AC) for ONE settled Monday–Sunday week.
/// The critical AC vs HK/PROSA distinction:
///   - AC: HasMerarbejde=true, HasOvertime=false → excess over the norm → MERARBEJDE at 1.0x
///   - HK/PROSA: HasOvertime=true → norm..threshold100 → OVERTIME_50 at 1.5x, above threshold100
///     → OVERTIME_100 at 2.0x (full-time: 37–40 h and &gt; 40 h; danish-agreements.md
///     "Overtime Thresholds", SR-HK-OK24-006/007).
///
/// <para>
/// <b>S145 / TASK-14505 (R8).</b> The single-period entry point <c>Evaluate</c> is retired: it
/// compared a whole period's hours with ONE week's norm, so a calendar month was judged as one
/// giant week (QUAL-186). The weekly windowing now lives in <see cref="WeeklyTimelineCore"/>,
/// which builds each week's norm and thresholds day by day (<c>DayNorm</c>, owner ruling Q2 = A)
/// and calls <see cref="SettleWeek"/> once per settled week. What stays here is only the tier
/// arithmetic, unchanged from the retired method, so there is one arithmetic path.
/// </para>
/// </summary>
public static class OvertimeRule
{
    public const string RuleId = "OVERTIME_CALC";

    /// <summary>
    /// Pure: the overtime or merarbejde lines for one settled week, dated on its settlement day.
    /// <paramref name="normHours"/>, <paramref name="threshold50"/> and
    /// <paramref name="threshold100"/> are the week's sums of per-day amounts; the
    /// <paramref name="regime"/> config decides AC merarbejde vs HK/PROSA tiers. Returns no line
    /// when the week's hours do not exceed its norm.
    /// </summary>
    public static IReadOnlyList<CalculationLineItem> SettleWeek(
        decimal totalHours,
        decimal normHours,
        decimal threshold50,
        decimal threshold100,
        AgreementRuleConfig regime,
        DateOnly settlementDay)
    {
        ArgumentNullException.ThrowIfNull(regime);

        var lineItems = new List<CalculationLineItem>();

        if (totalHours <= normHours)
            return lineItems;

        var excessHours = totalHours - normHours;

        // AC path: merarbejde only (no overtime)
        if (regime.HasMerarbejde && !regime.HasOvertime)
        {
            lineItems.Add(new CalculationLineItem
            {
                TimeType = OvertimeTypes.Merarbejde,
                Hours = excessHours,
                Rate = 1.0m,
                Date = settlementDay
            });
        }
        // HK/PROSA path: tiered overtime
        else if (regime.HasOvertime)
        {
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
                    Date = settlementDay
                });
            }

            if (overtime100Hours > 0)
            {
                lineItems.Add(new CalculationLineItem
                {
                    TimeType = OvertimeTypes.Overtime100,
                    Hours = overtime100Hours,
                    Rate = 2.0m,
                    Date = settlementDay
                });
            }
        }

        return lineItems;
    }
}
