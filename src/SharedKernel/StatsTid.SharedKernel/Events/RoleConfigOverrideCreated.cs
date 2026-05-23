namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a fresh role-within-agreement override row is INSERTED. Covers
/// ADR-020 D2 Case A (no predecessor) and Case B (fresh INSERT after the
/// predecessor was closed via <see cref="RoleConfigOverrideSuperseded"/>).
/// ADR-024 D1 role-within-agreement scope. Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class RoleConfigOverrideCreated : DomainEventBase
{
    public override string EventType => "RoleConfigOverrideCreated";

    // Natural-key / identity
    public required Guid OverrideId { get; init; }
    public required string EmploymentCategory { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateOnly EffectiveFrom { get; init; }

    // Mutable per-role fields — nullable: a NULL means "inherit from agreement-level config"
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
