namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor) — emitted when a typed structural <c>unit</c> is RE-PARENTED
/// within its Organisation: either an explicit move, or a per-child re-parent that a parent
/// unit's delete performs (each surviving child lifted up — a state change that MUST be an
/// event, not a silent SQL UPDATE; P3). Evolves <see cref="EnhedMoved"/>. Units NEVER move
/// cross-Organisation (<c>units.organisation_id</c> IMMUTABLE, D8), so the owning Organisation
/// is carried for audit/replay. Projection: UPDATE <c>units.parent_unit_id</c> + bump
/// <c>version</c> on the moved row. Stream: <c>unit-{unitId}</c>.
///
/// <para>
/// <c>NewParentUnitId == null</c> = the unit becomes TOP-LEVEL (directly under the Organisation).
/// The deep tree is structure + reporting only and grants NO scope (D5 LOCKED). No writer emits
/// this yet; DEFINED + REGISTERED with the model.
/// </para>
/// </summary>
public sealed class UnitMoved : DomainEventBase
{
    public override string EventType => "UnitMoved";

    public required Guid UnitId { get; init; }

    /// <summary>The owning Organisation (IMMUTABLE per unit row). Carried for audit/replay.</summary>
    public required string OrganisationId { get; init; }

    /// <summary>The parent BEFORE the move (<c>null</c> = was top-level). Carried for replay/audit.</summary>
    public Guid? OldParentUnitId { get; init; }

    /// <summary>The parent AFTER the move (<c>null</c> = becomes top-level).</summary>
    public Guid? NewParentUnitId { get; init; }
}
