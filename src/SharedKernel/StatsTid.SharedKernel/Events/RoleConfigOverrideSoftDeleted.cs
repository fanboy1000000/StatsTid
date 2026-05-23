namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a role-config override is soft-deleted by closing effective_to
/// (no successor row inserted — distinct from <see cref="RoleConfigOverrideSuperseded"/>).
/// ADR-024 D1. Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class RoleConfigOverrideSoftDeleted : DomainEventBase
{
    public override string EventType => "RoleConfigOverrideSoftDeleted";

    public required Guid OverrideId { get; init; }
    public required DateOnly EffectiveTo { get; init; }
    public required string EmploymentCategory { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
