namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// Resolves the Danish public-sector collective-agreement (OK) version that applies on a given date.
///
/// The OK version is determined by when the work was performed — not by today's date — so replays,
/// retroactive corrections, and payroll export remain deterministic (ADR-003).
///
/// This is the single source of truth. Previously the date-range table was duplicated across
/// RuleEngine, Backend API, and Payroll Integration. TASK-1801 (Sprint 18) consolidated them here:
/// a pure calendar constant with no dependencies is the textbook SharedKernel citizen, and callers
/// on the write/calc boundaries can reach it without violating integration isolation (PAT-005).
/// </summary>
public static class OkVersionResolver
{
    private static readonly (DateOnly Start, DateOnly End, string Version)[] VersionPeriods =
    {
        (new DateOnly(2024, 4, 1), new DateOnly(2026, 3, 31), "OK24"),
        (new DateOnly(2026, 4, 1), new DateOnly(2028, 3, 31), "OK26"),
    };

    /// <summary>
    /// Resolves the OK version for a specific date. Dates before the earliest known version clamp to
    /// the earliest; dates after the latest clamp to the latest.
    /// </summary>
    public static string ResolveVersion(DateOnly date)
    {
        foreach (var (start, end, version) in VersionPeriods)
        {
            if (date >= start && date <= end)
                return version;
        }

        if (date < VersionPeriods[0].Start)
            return VersionPeriods[0].Version;

        return VersionPeriods[^1].Version;
    }

    /// <summary>
    /// Returns all OK versions that apply within a date range, with each segment's effective bounds
    /// clipped to the input period. Useful for periods that straddle an OK transition boundary.
    /// </summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End, string Version)> ResolveVersionsForPeriod(
        DateOnly periodStart, DateOnly periodEnd)
    {
        var result = new List<(DateOnly, DateOnly, string)>();

        foreach (var (start, end, version) in VersionPeriods)
        {
            var overlapStart = periodStart > start ? periodStart : start;
            var overlapEnd = periodEnd < end ? periodEnd : end;

            if (overlapStart <= overlapEnd)
            {
                result.Add((overlapStart, overlapEnd, version));
            }
        }

        if (result.Count == 0)
        {
            var fallbackVersion = ResolveVersion(periodStart);
            result.Add((periodStart, periodEnd, fallbackVersion));
        }

        return result;
    }
}
