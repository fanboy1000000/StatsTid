namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a role-config override is superseded cross-day
/// (ADR-020 D2 Case C — predecessor's effective_to closed + successor row inserted).
/// The nullable mutable fields carry the SUCCESSOR's new values.
/// ADR-024 D1. Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class RoleConfigOverrideSuperseded : DomainEventBase
{
    public override string EventType => "RoleConfigOverrideSuperseded";

    public required Guid PredecessorOverrideId { get; init; }
    public required Guid SuccessorOverrideId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required string EmploymentCategory { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }

    // Successor's new values
    public string? MerarbejdeCompensationRight { get; init; }
    public bool? HasMerarbejde { get; init; }
    public bool? HasOvertime { get; init; }
    public bool? HasEveningSupplement { get; init; }
    public bool? HasNightSupplement { get; init; }
    public bool? HasWeekendSupplement { get; init; }
    public bool? HasHolidaySupplement { get; init; }
    public decimal? MaxFlexBalance { get; init; }
    public decimal? FlexCarryoverMax { get; init; }
    public int? NormPeriodWeeks { get; init; }
    public decimal? WeeklyNormHours { get; init; }
}
