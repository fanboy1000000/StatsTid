namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when an admin soft-deletes an entitlement config (via
/// <c>SoftDeleteAsync</c>). The row remains in storage for audit/replay but
/// is excluded from active lookups. Sprint 30 / Phase 4d-2.
/// </summary>
public sealed class EntitlementConfigSoftDeleted : DomainEventBase
{
    public override string EventType => "EntitlementConfigSoftDeleted";

    // Natural-key / identity (post-mutation state)
    public required Guid ConfigId { get; init; }
    public required string EntitlementType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }

    // Temporal validity at time of soft-delete
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }

    // Post-mutation row-version
    public required long RowVersion { get; init; }
}
