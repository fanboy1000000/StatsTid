namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D2 (Enhedsspor) — emitted when a user's single structural membership
/// (<c>users.unit_id</c>, nullable) changes. Replaces the S97 multi-enhed-tag
/// <see cref="UserEnhederChanged"/> model with a single-unit home. Projection: UPDATE
/// <c>users.unit_id</c> + recompute the derived <c>users.primary_org_id</c> (= the unit's owning
/// Organisation, or — when <see cref="NewUnitId"/> is null — the Organisation the person homes at
/// directly, D2). Stream: <c>user-{userId}</c> (mirroring <see cref="UserEnhederChanged"/>).
///
/// <para>
/// <see cref="OldUnitId"/>/<see cref="NewUnitId"/> are nullable (a person may home directly at the
/// Organisation). <see cref="OrganisationId"/> is the DERIVED Organisation AFTER the change — the
/// always-present authority/attribution home the ~100 Organisation-keyed consumers read (D2), and
/// the audit row's <c>target_org_id</c>. No writer emits this yet; DEFINED + REGISTERED with the model.
/// </para>
/// </summary>
public sealed class UserUnitChanged : DomainEventBase
{
    public override string EventType => "UserUnitChanged";

    public required string UserId { get; init; }

    /// <summary>The unit the user belonged to BEFORE the change (<c>null</c> = homed at the Organisation).</summary>
    public Guid? OldUnitId { get; init; }

    /// <summary>The unit the user belongs to AFTER the change (<c>null</c> = homes at the Organisation).</summary>
    public Guid? NewUnitId { get; init; }

    /// <summary>The DERIVED Organisation home after the change (= <c>primary_org_id</c>; the audit <c>target_org_id</c>).</summary>
    public required string OrganisationId { get; init; }
}
