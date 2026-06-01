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

    // Monthly accrual (ADR-030, Ferieloven samtidighedsferie). All nullable so existing
    // IMMEDIATE/per-episode/age-gated callers are byte-for-byte unaffected.
    //
    // AccrualModel selects behavior: "MONTHLY_ACCRUAL" (or any non-null BookableLimit) switches
    // the rejection cap from (effectiveQuota + carryoverIn) to the Backend-supplied BookableLimit.
    // The remaining fields (FerieaarStart/EmploymentStart/AsOfDate) are carried so the rule CAN
    // re-derive earned-to-date via AccrualCalculator.EarnedToDate when a caller does not pre-compute
    // BookableLimit; the validation rule itself prefers the supplied BookableLimit (keeping the rule
    // a pure function of its inputs — Backend owns the dated part-time/employmentStart/as-of data).
    public string? AccrualModel { get; init; }
    public DateOnly? FerieaarStart { get; init; }
    public DateOnly? EmploymentStart { get; init; }
    public DateOnly? AsOfDate { get; init; }

    // The carryover-INCLUSIVE business cap the user may book (per-type, computed by the Backend):
    //   VACATION        = earned + stillAccruableInFerieår + carryoverIn (forskud allowed)
    //   SPECIAL_HOLIDAY = earned + carryoverIn                           (no forskud, §13 stk.4)
    // Distinct from EffectiveQuota (annual) — do NOT overload the quota fields. When set, it is the
    // rejection cap for MONTHLY_ACCRUAL. The 20%-warning + per-episode branches stay on the ANNUAL
    // effectiveQuota so no spurious early-ferieår warnings fire.
    public decimal? BookableLimit { get; init; }
}

public sealed class ValidateEntitlementResponse
{
    public required bool Allowed { get; init; }
    public required string Status { get; init; }  // "ALLOWED", "WARNING", "REJECTED"
    public required decimal EffectiveQuota { get; init; }  // After pro-rating
    public required decimal RemainingAfter { get; init; }  // After this request
    public string? Message { get; init; }
}
