namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted on the predecessor's stream when a cross-day supersession closes
/// the predecessor row (effective_to set) and a new history row is inserted
/// via <see cref="EntitlementConfigCreated"/>. This event carries the
/// audit-trail of the close on the predecessor stream; the new row's INSERT
/// is recorded on its own stream as <c>EntitlementConfigCreated</c>.
/// Per ADR-020 D2 Case B + Sprint 30 / Phase 4d-2.
/// </summary>
public sealed class EntitlementConfigSuperseded : DomainEventBase
{
    public override string EventType => "EntitlementConfigSuperseded";

    // Natural-key / identity of the predecessor (post-mutation state)
    public required Guid ConfigId { get; init; }
    public required string EntitlementType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }

    // Temporal validity — EffectiveTo is set by the emission site when the
    // predecessor is closed (that is the whole point of supersession);
    // typed as nullable for JSON round-trip compatibility with the
    // serializer's WhenWritingNull policy.
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }

    // Predecessor's post-mutation row-version
    public required long RowVersion { get; init; }

    // Forward-pointer to the new row that replaced this predecessor
    public required Guid SupersededByConfigId { get; init; }

    /// <summary>
    /// S73 / TASK-7301 (SPRINT-73 R2, owner ruling D-A): the PREDECESSOR's full-day-only flag at
    /// close time. ADDITIVE-NULLABLE for replay compatibility — pre-S73 stored payloads
    /// deserialize with <c>null</c>; post-S73 emissions always carry the closed row's value.
    /// </summary>
    public bool? FullDayOnly { get; init; }
}
