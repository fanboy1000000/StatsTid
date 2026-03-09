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

        // 2. For per-episode types, validate against episode limit only
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

        // 3. Calculate total available (quota + carryover)
        var totalAvailable = effectiveQuota + request.CarryoverIn;
        var currentUsed = request.Used + request.Planned;
        var remainingBefore = totalAvailable - currentUsed;
        var remainingAfter = remainingBefore - request.RequestedDays;

        // 4. Check if rejected (would exceed quota)
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

        // 5. Check if warning (remaining <= 20% of quota after request)
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

        // 6. Allowed
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
