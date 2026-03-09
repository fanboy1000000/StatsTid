namespace StatsTid.RuleEngine.Api.Contracts;

public sealed class ValidateEntitlementRequest
{
    public required decimal AnnualQuota { get; init; }
    public required decimal Used { get; init; }
    public required decimal Planned { get; init; }
    public required decimal CarryoverIn { get; init; }
    public required decimal RequestedDays { get; init; }
    public required decimal PartTimeFraction { get; init; }  // 1.0 = full-time
    public required bool ProRateByPartTime { get; init; }
    public required bool IsPerEpisode { get; init; }
    public decimal? PerEpisodeLimit { get; init; }  // For CHILD_SICK: max days per episode
}

public sealed class ValidateEntitlementResponse
{
    public required bool Allowed { get; init; }
    public required string Status { get; init; }  // "ALLOWED", "WARNING", "REJECTED"
    public required decimal EffectiveQuota { get; init; }  // After pro-rating
    public required decimal RemainingAfter { get; init; }  // After this request
    public string? Message { get; init; }
}
