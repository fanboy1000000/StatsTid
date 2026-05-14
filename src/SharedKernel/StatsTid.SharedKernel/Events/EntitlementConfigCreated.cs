namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a fresh entitlement config row is INSERTED. Covers both
/// ADR-020 D2 Case A (no predecessor) and Case B (fresh INSERT after the
/// predecessor was closed via <see cref="EntitlementConfigSuperseded"/>).
/// Sprint 30 / Phase 4d-2.
/// </summary>
public sealed class EntitlementConfigCreated : DomainEventBase
{
    public override string EventType => "EntitlementConfigCreated";

    // Natural-key / identity
    public required Guid ConfigId { get; init; }
    public required string EntitlementType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }

    // Temporal validity (post-mutation state)
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }

    // Optimistic-concurrency row-version (post-mutation). Distinct from
    // DomainEventBase.Version which carries the event-schema version.
    public required long RowVersion { get; init; }

    // Payload — full new-row fields needed by downstream consumers + replay
    public required decimal AnnualQuota { get; init; }
    public required string AccrualModel { get; init; }
    public required int ResetMonth { get; init; }
    public required decimal CarryoverMax { get; init; }
    public required bool ProRateByPartTime { get; init; }
    public required bool IsPerEpisode { get; init; }
    public int? MinAge { get; init; }
    public string? Description { get; init; }
}
