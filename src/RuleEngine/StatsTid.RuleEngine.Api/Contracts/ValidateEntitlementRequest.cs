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

    // Age gate (e.g. SENIOR_DAY). Nullable for backward compatibility: when MinAge is null
    // there is no age gate and behavior is unchanged. EmployeeAgeAsOfAbsenceDate is the integer
    // age the Backend computed as-of the absence date — DOB itself never crosses this boundary,
    // preserving rule-engine purity/determinism (ADR-002). Null age with a set MinAge ⇒ fail-closed.
    public int? MinAge { get; init; }
    public int? EmployeeAgeAsOfAbsenceDate { get; init; }
}

public sealed class ValidateEntitlementResponse
{
    public required bool Allowed { get; init; }
    public required string Status { get; init; }  // "ALLOWED", "WARNING", "REJECTED"
    public required decimal EffectiveQuota { get; init; }  // After pro-rating
    public required decimal RemainingAfter { get; init; }  // After this request
    public string? Message { get; init; }
}
