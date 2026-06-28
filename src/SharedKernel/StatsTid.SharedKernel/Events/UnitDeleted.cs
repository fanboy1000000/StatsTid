namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor) — emitted when a typed structural <c>unit</c> is SOFT-deleted
/// (sets <c>units.deleted_at</c>). Evolves <see cref="EnhedDeleted"/>. Surviving children are
/// re-parented up via per-child <see cref="UnitMoved"/> events (P3, not silent SQL); member
/// re-homing is a separate <see cref="UserUnitChanged"/> concern. The owning Organisation is
/// carried so the audit row's <c>target_org_id</c> resolves from the payload. Stream:
/// <c>unit-{unitId}</c>.
///
/// <para>No writer emits this yet; DEFINED + REGISTERED with the model.</para>
/// </summary>
public sealed class UnitDeleted : DomainEventBase
{
    public override string EventType => "UnitDeleted";

    public required Guid UnitId { get; init; }

    /// <summary>The owning Organisation (IMMUTABLE per unit row). The audit <c>target_org_id</c>.</summary>
    public required string OrganisationId { get; init; }
}
