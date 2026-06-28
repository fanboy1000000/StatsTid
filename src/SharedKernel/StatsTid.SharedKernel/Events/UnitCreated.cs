namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S103 / ADR-038 D10 (Enhedsspor unit-hierarchy model) — emitted when a typed structural
/// <c>unit</c> (one of the 5 sub-Organisation levels: direktion/omrade/kontor/team/enhed) is
/// created beneath exactly one ORGANISATION-typed org. Evolves <see cref="EnhedCreated"/>
/// (adds <see cref="Type"/>; keeps the optional parent). Projection: latest-wins INSERT into
/// <c>units</c> (non-temporal — model after the S100 hierarchical-enhed projection). Stream:
/// <c>unit-{unitId}</c>.
///
/// <para>
/// Unlike the zero-authority S97/S100 Enhed, a unit participates in structure + reporting
/// (D3/D4) but role-SCOPE stays anchored at the Organisation (D5 LOCKED invariant): a unit's
/// <c>parent_unit_id</c> grants NO scope. No writer emits this yet — the units CRUD that emits
/// it ships in a later sprint; this event is DEFINED + REGISTERED with the model.
/// </para>
/// </summary>
public sealed class UnitCreated : DomainEventBase
{
    public override string EventType => "UnitCreated";

    public required Guid UnitId { get; init; }

    /// <summary>The owning Organisation (the derived authority anchor; IMMUTABLE per unit row).</summary>
    public required string OrganisationId { get; init; }

    /// <summary>
    /// The OPTIONAL parent unit under which this unit is created. <c>null</c> = a TOP-LEVEL unit
    /// (directly under the Organisation). A non-null parent is another active unit in the SAME
    /// Organisation (validated in-tx under the per-Organisation <c>unit-org-</c> advisory lock at
    /// create time, D8). Level is DERIVED (depth), as in the S100 enhed precedent.
    /// </summary>
    public Guid? ParentUnitId { get; init; }

    /// <summary>The unit sub-type (direktion/omrade/kontor/team/enhed); the strict CHILD ordering is app-enforced.</summary>
    public required string Type { get; init; }

    public required string Name { get; init; }
}
