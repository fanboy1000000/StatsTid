using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: calculates evening, night, weekend, and holiday supplements.
/// Precedence: Holiday > Weekend > Evening/Night (no double-dipping).
/// AC: all supplements disabled via config → returns empty.
/// </summary>
public static class SupplementRule
{
    public const string RuleId = "SUPPLEMENT_CALC";

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var lineItems = new List<CalculationLineItem>();

        var periodEntries = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .ToList();

        foreach (var entry in periodEntries)
        {
            if (entry.StartTime is null || entry.EndTime is null)
                continue;

            var supplements = CalculateSupplements(entry, config);
            lineItems.AddRange(supplements);
        }

        return new CalculationResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = true,
            LineItems = lineItems
        };
    }

    public static List<CalculationLineItem> CalculateSupplements(
        TimeEntry entry, AgreementRuleConfig config)
    {
        var items = new List<CalculationLineItem>();
        var start = entry.StartTime!.Value;
        var end = entry.EndTime!.Value;
        var date = entry.Date;

        // Holiday check (highest precedence)
        if (config.HolidaySupplementEnabled &&
            DanishPublicHolidays.IsPublicHoliday(date, config.OkVersion))
        {
            items.Add(new CalculationLineItem
            {
                TimeType = SupplementTypes.Holiday,
                Hours = entry.Hours,
                Rate = config.HolidayRate,
                Date = date
            });
            return items; // No double-dipping
        }

        // Weekend check (second precedence)
        if (config.WeekendSupplementEnabled && DanishPublicHolidays.IsWeekend(date))
        {
            var rate = date.DayOfWeek == DayOfWeek.Saturday
                ? config.WeekendSaturdayRate
                : config.WeekendSundayRate;

            items.Add(new CalculationLineItem
            {
                TimeType = SupplementTypes.Weekend,
                Hours = entry.Hours,
                Rate = rate,
                Date = date
            });
            return items; // No double-dipping
        }

        // Evening/Night supplements (can coexist on the same entry)
        var eveningHours = config.EveningSupplementEnabled
            ? CalculateOverlapHours(start, end, config.EveningStart, config.EveningEnd)
            : 0m;

        var nightHours = config.NightSupplementEnabled
            ? CalculateOverlapHours(start, end, config.NightStart, config.NightEnd)
            : 0m;

        if (eveningHours > 0)
        {
            items.Add(new CalculationLineItem
            {
                TimeType = SupplementTypes.Evening,
                Hours = eveningHours,
                Rate = config.EveningRate,
                Date = date
            });
        }

        if (nightHours > 0)
        {
            items.Add(new CalculationLineItem
            {
                TimeType = SupplementTypes.Night,
                Hours = nightHours,
                Rate = config.NightRate,
                Date = date
            });
        }

        return items;
    }

    /// <summary>
    /// Calculates hours of overlap between a work period [workStart, workEnd]
    /// and a supplement window [windowStart, windowEnd].
    /// Handles midnight crossing for both work period and window.
    /// </summary>
    public static decimal CalculateOverlapHours(
        TimeOnly workStart, TimeOnly workEnd, int windowStartHour, int windowEndHour)
    {
        // Convert to minutes since midnight for easier math
        int wsMin = workStart.Hour * 60 + workStart.Minute;
        int weMin = workEnd.Hour * 60 + workEnd.Minute;
        int winStartMin = windowStartHour * 60;
        int winEndMin = windowEndHour * 60;

        // Handle midnight-crossing work period (e.g., 22:00 - 02:00)
        var workSegments = new List<(int Start, int End)>();
        if (weMin <= wsMin)
        {
            // Crosses midnight
            workSegments.Add((wsMin, 24 * 60));
            workSegments.Add((0, weMin));
        }
        else
        {
            workSegments.Add((wsMin, weMin));
        }

        // Handle midnight-crossing window (e.g., 23:00 - 06:00)
        var windowSegments = new List<(int Start, int End)>();
        if (winEndMin <= winStartMin)
        {
            windowSegments.Add((winStartMin, 24 * 60));
            windowSegments.Add((0, winEndMin));
        }
        else
        {
            windowSegments.Add((winStartMin, winEndMin));
        }

        // Calculate total overlap
        decimal totalMinutes = 0;
        foreach (var ws in workSegments)
        {
            foreach (var win in windowSegments)
            {
                int overlapStart = Math.Max(ws.Start, win.Start);
                int overlapEnd = Math.Min(ws.End, win.End);
                if (overlapEnd > overlapStart)
                    totalMinutes += overlapEnd - overlapStart;
            }
        }

        return totalMinutes / 60m;
    }
}
