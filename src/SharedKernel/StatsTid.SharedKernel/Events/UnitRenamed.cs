namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor) — emitted when a typed structural <c>unit</c> is renamed
/// (the unit identity / its memberships + leaders are unchanged). Evolves
/// <see cref="EnhedRenamed"/>. Projection: UPDATE the <c>units</c> row's <c>name</c> + bump
/// <c>version</c>. Stream: <c>unit-{unitId}</c>.
///
/// <para>
/// Carries no Organisation id (a rename is identity-bound to the unit, not an org-keyed fact),
/// so the audit mapper resolves <c>target_org_id</c> from
/// <c>AuditProjectionContext.ResolvedTargetOrgId</c> (the endpoint supplies the unit's owning
/// Organisation) — mirroring the <see cref="UserEnhederChanged"/> mapper. No writer emits this
/// yet; DEFINED + REGISTERED with the model.
/// </para>
/// </summary>
public sealed class UnitRenamed : DomainEventBase
{
    public override string EventType => "UnitRenamed";

    public required Guid UnitId { get; init; }

    public required string NewName { get; init; }
}
