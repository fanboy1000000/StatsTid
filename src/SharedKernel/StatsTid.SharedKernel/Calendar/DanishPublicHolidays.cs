namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// Pure static functions for Danish public holiday calculation.
/// No I/O, no DB — fully deterministic and version-aware.
/// </summary>
public static class DanishPublicHolidays
{
    /// <summary>
    /// Computes Easter Sunday for a given year using the Anonymous Gregorian algorithm (Computus).
    /// </summary>
    public static DateOnly ComputeEasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// Returns all Danish public holidays for a given year.
    /// Store Bededag is included only for years before OK24 takes effect (before 2024).
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, string Name)> GetHolidays(int year, string okVersion = "OK24")
    {
        var easter = ComputeEasterSunday(year);
        var holidays = new List<(DateOnly, string)>
        {
            (new DateOnly(year, 1, 1), "Nytaarsdag"),
            (easter.AddDays(-3), "Skaertorsdag"),
            (easter.AddDays(-2), "Langfredag"),
            (easter, "Paaskedag"),
            (easter.AddDays(1), "2. Paaskedag"),
            (easter.AddDays(39), "Kristi Himmelfartsdag"),
            (easter.AddDays(49), "Pinsedag"),
            (easter.AddDays(50), "2. Pinsedag"),
            (new DateOnly(year, 6, 5), "Grundlovsdag"),
            (new DateOnly(year, 12, 25), "Juledag"),
            (new DateOnly(year, 12, 26), "2. Juledag"),
        };

        // Store Bededag: 4th Friday after Easter — removed from OK24 onwards
        if (!IsOk24OrLater(okVersion))
        {
            holidays.Add((easter.AddDays(26), "Store Bededag"));
        }

        holidays.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return holidays;
    }

    /// <summary>
    /// Version-aware check: is the given date a public holiday under the specified OK version?
    /// </summary>
    public static bool IsPublicHoliday(DateOnly date, string okVersion)
    {
        var holidays = GetHolidays(date.Year, okVersion);
        return holidays.Any(h => h.Date == date);
    }

    public static bool IsWeekend(DateOnly date)
    {
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
    }

    public static bool IsWorkingDay(DateOnly date, string okVersion)
    {
        return !IsWeekend(date) && !IsPublicHoliday(date, okVersion);
    }

    /// <summary>
    /// Counts working days in the range [from, to] inclusive.
    /// </summary>
    public static int CountWorkingDays(DateOnly from, DateOnly to, string okVersion)
    {
        int count = 0;
        var current = from;
        while (current <= to)
        {
            if (IsWorkingDay(current, okVersion))
                count++;
            current = current.AddDays(1);
        }
        return count;
    }

    private static bool IsOk24OrLater(string okVersion)
    {
        // OK24, OK26, OK28... are all "OK24 or later"
        // Pre-OK24 would be e.g. "OK21"
        if (okVersion.StartsWith("OK", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(okVersion[2..], out var versionNumber))
        {
            return versionNumber >= 24;
        }
        return true; // Default to current rules
    }
}
