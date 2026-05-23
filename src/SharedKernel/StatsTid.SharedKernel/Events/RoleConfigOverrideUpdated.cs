namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when an existing role-config override row is mutated same-day
/// (ADR-020 D2 Case B — UPDATE in place, no temporal supersession).
/// Carries row-version before/after for optimistic-concurrency audit trail.
/// ADR-024 D1. Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class RoleConfigOverrideUpdated : DomainEventBase
{
    public override string EventType => "RoleConfigOverrideUpdated";

    public required Guid OverrideId { get; init; }
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }

    // Mutable per-role fields (post-mutation values)
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
