namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D3/D4 (Enhedsspor) — emitted when a user is explicitly designated a leader of
/// a typed structural <c>unit</c> (the design's "promote to leader"). Projection: INSERT into
/// <c>unit_leaders(unit_id, user_id)</c>. Stream: <c>unit-{unitId}</c> (leadership is a property
/// of the unit aggregate, like <see cref="UnitMoved"/>).
///
/// <para>
/// AUTHORITY-BEARING (unlike the zero-authority S97 Enhed): a secondary/peer unit-leader can
/// approve the unit's OWN direct members (D4 path 2), BOUNDED to that unit — it grants NO
/// deep-tree / leader-of-leader scope (D5 LOCKED). The owning Organisation is carried so the
/// audit row's <c>target_org_id</c> resolves from the payload + so the write floor
/// (LocalHR-over-the-Organisation, D3) is attributable. No writer emits this yet; DEFINED +
/// REGISTERED with the model.
/// </para>
/// </summary>
public sealed class UnitLeaderDesignated : DomainEventBase
{
    public override string EventType => "UnitLeaderDesignated";

    public required Guid UnitId { get; init; }

    /// <summary>The user designated as a leader (must be a member of the unit, D3 invariant).</summary>
    public required string UserId { get; init; }

    /// <summary>The unit's owning Organisation. The audit <c>target_org_id</c> + the write-floor anchor.</summary>
    public required string OrganisationId { get; init; }
}
