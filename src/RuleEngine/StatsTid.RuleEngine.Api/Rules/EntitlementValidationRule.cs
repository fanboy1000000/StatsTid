using StatsTid.RuleEngine.Api.Contracts;

namespace StatsTid.RuleEngine.Api.Rules;

public static class EntitlementValidationRule
{
    public static ValidateEntitlementResponse Evaluate(ValidateEntitlementRequest request)
    {
        // 1. Calculate effective quota (pro-rate if applicable)
        var effectiveQuota = request.ProRateByPartTime
            ? request.AnnualQuota * request.PartTimeFraction
            : request.AnnualQuota;

        // 2. Age gate (e.g. SENIOR_DAY min_age). Evaluated BEFORE per-episode/quota so it
        //    cannot be skipped. Only active when MinAge is set (null ⇒ no gate, unchanged
        //    behavior). Null age with a set MinAge is fail-closed (rejected): the Backend
        //    passes a null age when the employee has no recorded DOB, and an unknown age must
        //    never default to a passing value. Pure/deterministic — age is a passed-in integer.
        if (request.MinAge.HasValue &&
            (!request.EmployeeAgeAsOfAbsenceDate.HasValue ||
             request.EmployeeAgeAsOfAbsenceDate.Value < request.MinAge.Value))
        {
            return new ValidateEntitlementResponse
            {
                Allowed = false,
                Status = "REJECTED",
                EffectiveQuota = effectiveQuota,
                RemainingAfter = 0m,
                Message = $"Below minimum age of {request.MinAge.Value} for entitlement"
            };
        }

        // 3. For per-episode types, validate against episode limit only
        if (request.IsPerEpisode && request.PerEpisodeLimit.HasValue)
        {
            if (request.RequestedDays > request.PerEpisodeLimit.Value)
            {
                return new ValidateEntitlementResponse
                {
                    Allowed = false,
                    Status = "REJECTED",
                    EffectiveQuota = request.PerEpisodeLimit.Value,
                    RemainingAfter = request.PerEpisodeLimit.Value - request.RequestedDays,
                    Message = $"Requested {request.RequestedDays} days exceeds per-episode limit of {request.PerEpisodeLimit.Value} days"
                };
            }
            return new ValidateEntitlementResponse
            {
                Allowed = true,
                Status = "ALLOWED",
                EffectiveQuota = request.PerEpisodeLimit.Value,
                RemainingAfter = request.PerEpisodeLimit.Value - request.RequestedDays,
                Message = null
            };
        }

        // 4. Calculate total available (quota + carryover)
        var totalAvailable = effectiveQuota + request.CarryoverIn;
        var currentUsed = request.Used + request.Planned;
        var remainingBefore = totalAvailable - currentUsed;
        var remainingAfter = remainingBefore - request.RequestedDays;

        // 5. Check if rejected (would exceed quota)
        if (remainingAfter < 0)
        {
            return new ValidateEntitlementResponse
            {
                Allowed = false,
                Status = "REJECTED",
                EffectiveQuota = effectiveQuota,
                RemainingAfter = remainingAfter,
                Message = $"Requested {request.RequestedDays} days exceeds remaining {remainingBefore:F1} days"
            };
        }

        // 6. Check if warning (remaining <= 20% of quota after request)
        var warningThreshold = effectiveQuota * 0.2m;
        if (remainingAfter <= warningThreshold && remainingAfter >= 0)
        {
            return new ValidateEntitlementResponse
            {
                Allowed = true,
                Status = "WARNING",
                EffectiveQuota = effectiveQuota,
                RemainingAfter = remainingAfter,
                Message = $"After this registration, only {remainingAfter:F1} days remaining of {effectiveQuota:F1}"
            };
        }

        // 7. Allowed
        return new ValidateEntitlementResponse
        {
            Allowed = true,
            Status = "ALLOWED",
            EffectiveQuota = effectiveQuota,
            RemainingAfter = remainingAfter,
            Message = null
        };
    }
}
