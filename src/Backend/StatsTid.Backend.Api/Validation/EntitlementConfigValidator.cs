namespace StatsTid.Backend.Api.Validation;

/// <summary>
/// SEC-033 server-side range validation for the admin entitlement-config write surfaces
/// (POST create + PUT update). These are PURE input-range checks — non-negativity plus a
/// calendar-month range — returned as a <c>(IsValid, Error)</c> pair the endpoint maps to
/// <b>400 Bad Request</b>. This is deliberately kept DISTINCT from the endpoints' statutory /
/// product-rule guards (VACATION <c>reset_month == 9</c>, the full-day-only day-shape rule),
/// which stay as <b>422 Unprocessable Entity</b>: a 400 says "this number is out of range",
/// a 422 says "this value is in range but breaks a domain rule".
///
/// Bounds are non-negativity + domain sets ONLY — no invented upper ceilings (owner ruling,
/// SEC-033: fat-finger ceilings are a deferred follow-up needing agreement-truth). Mirrors the
/// shared-validator pattern of <see cref="RequestValidator"/> so it is unit-testable without a
/// database container.
/// </summary>
public static class EntitlementConfigValidator
{
    /// <summary>
    /// Validates the numeric range fields common to the create and update request bodies.
    /// <paramref name="entitlementType"/> is needed only to exempt VACATION from the generic
    /// <c>reset_month ∈ 1..12</c> range check (VACATION's reset_month is statutorily fixed at 9
    /// and enforced by the endpoint's own 422 guard + a DB CHECK).
    /// </summary>
    public static (bool IsValid, string? Error) ValidateRanges(
        string? entitlementType, decimal annualQuota, decimal carryoverMax, int resetMonth, int? minAge)
    {
        if (annualQuota < 0)
            return (false, "AnnualQuota must be >= 0");
        // CarryoverMax == 0 is a common, legitimate value (e.g. SPECIAL_HOLIDAY carries nothing over,
        // and 0 is the column default) — ONLY negative is rejected.
        if (carryoverMax < 0)
            return (false, "CarryoverMax must be >= 0");
        // For every non-VACATION type, reset_month is a free calendar month → must be 1..12.
        // VACATION is exempt here: its reset_month is a statutory constant (9), guarded separately
        // with a 422 so a wrong VACATION month reports as a rule violation, not a range error.
        if (!string.Equals(entitlementType, "VACATION", StringComparison.Ordinal)
            && (resetMonth < 1 || resetMonth > 12))
            return (false, "ResetMonth must be between 1 and 12");
        // MinAge is nullable — null means "no minimum-age gate" and is legitimate; validate the
        // value only when supplied.
        if (minAge is < 0)
            return (false, "MinAge must be >= 0");
        return (true, null);
    }
}
