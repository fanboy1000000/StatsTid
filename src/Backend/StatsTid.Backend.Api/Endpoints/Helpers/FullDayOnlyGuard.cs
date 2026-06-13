namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// S73 / TASK-7301 (SPRINT-73 R2 construction-enforcement, the S68-B1 uniform-by-construction
/// lesson) — the admin-surface 422 guard for the D-A owner ruling (2026-06-13): CARE_DAY +
/// SENIOR_DAY entitlement configs are FULL-DAY-ONLY and an admin write must not be able to
/// silently un-rule it. ONE shared predicate because TWO admin surfaces write entitlement
/// configs (<c>EntitlementConfigEndpoints</c> POST/PUT and the sub-resource
/// <c>AgreementEntitlementEndpoints</c> POST/PUT) — a guard on only one of them would be
/// exactly the wiring-drift class this sprint exists to close. The DB CHECK
/// <c>entitlement_configs_full_day_only_types</c> remains the data-layer backstop for any
/// write path that bypasses both endpoints.
/// </summary>
internal static class FullDayOnlyGuard
{
    /// <summary>The entitlement types the D-A ruling pins to full-day-only (whole days).</summary>
    private static readonly string[] FullDayOnlyTypes = ["CARE_DAY", "SENIOR_DAY"];

    /// <summary>
    /// Returns <c>true</c> (with a structured 422 body in <paramref name="error"/>) when
    /// <paramref name="entitlementType"/> is a D-A full-day-only type and the request's flag is
    /// false — which includes ABSENT, since the request DTOs default a missing
    /// <c>fullDayOnly</c> to false.
    /// </summary>
    public static bool IsViolated(string entitlementType, bool suppliedFullDayOnly, out object? error)
    {
        if (suppliedFullDayOnly || !FullDayOnlyTypes.Contains(entitlementType, StringComparer.Ordinal))
        {
            error = null;
            return false;
        }

        error = new
        {
            error = "CARE_DAY and SENIOR_DAY entitlement configs must carry full_day_only = true (owner product ruling D-A, SPRINT-73: omsorgsdage/seniordage are whole days).",
            entitlementType,
            suppliedFullDayOnly,
        };
        return true;
    }
}
