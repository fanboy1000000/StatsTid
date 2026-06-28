namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D3/D4 (Enhedsspor) — emitted when a user's leader designation on a typed
/// structural <c>unit</c> is removed: either an explicit removal, or the in-tx re-sync when the
/// person's <c>unit_id</c> changes away from this unit (D3 — a moved-away leader MUST lose
/// approval over the old unit's members). Projection: DELETE the
/// <c>unit_leaders(unit_id, user_id)</c> row. Stream: <c>unit-{unitId}</c>.
///
/// <para>
/// Withdraws the D4 path-2 approval authority. The owning Organisation is carried so the audit
/// row's <c>target_org_id</c> resolves from the payload. No writer emits this yet; DEFINED +
/// REGISTERED with the model.
/// </para>
/// </summary>
public sealed class UnitLeaderRemoved : DomainEventBase
{
    public override string EventType => "UnitLeaderRemoved";

    public required Guid UnitId { get; init; }

    /// <summary>The user whose leader designation is removed.</summary>
    public required string UserId { get; init; }

    /// <summary>The unit's owning Organisation. The audit <c>target_org_id</c>.</summary>
    public required string OrganisationId { get; init; }
}
