namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when an existing role-config override row is mutated same-day
/// (ADR-020 D2 Case B — UPDATE in place, no temporal supersession).
/// Carries row-version before/after for optimistic-concurrency audit trail.
/// ADR-024 D1. Sprint 40 / Phase 4d-4.
/// The flex-carryover member was retired in S148 (C-7, ADR-043 D6); a stored payload that
/// still carries it deserialises, the member being ignored (EventSerializer skips unknown members; DEP-003).
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
    public int? NormPeriodWeeks { get; init; }
    public decimal? WeeklyNormHours { get; init; }
}
