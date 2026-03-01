namespace StatsTid.RuleEngine.Api.Config;

/// <summary>
/// Resolves OK version based on entry date (not today).
/// Ensures deterministic replay — the version is always determined by when the work was performed.
/// </summary>
public static class OkVersionResolver
{
    private static readonly (DateOnly Start, DateOnly End, string Version)[] VersionPeriods =
    {
        (new DateOnly(2024, 4, 1), new DateOnly(2026, 3, 31), "OK24"),
        (new DateOnly(2026, 4, 1), new DateOnly(2028, 3, 31), "OK26"),
    };

    /// <summary>
    /// Resolves the OK version for a specific date.
    /// </summary>
    public static string ResolveVersion(DateOnly date)
    {
        foreach (var (start, end, version) in VersionPeriods)
        {
            if (date >= start && date <= end)
                return version;
        }

        // Default: if before all known versions, use earliest; if after, use latest
        if (date < VersionPeriods[0].Start)
            return VersionPeriods[0].Version;

        return VersionPeriods[^1].Version;
    }

    /// <summary>
    /// Returns all OK versions that apply within a date range.
    /// Useful for periods that span an OK transition boundary.
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

        // If no overlap found, return the resolved version for the period start
        if (result.Count == 0)
        {
            var fallbackVersion = ResolveVersion(periodStart);
            result.Add((periodStart, periodEnd, fallbackVersion));
        }

        return result;
    }
}
