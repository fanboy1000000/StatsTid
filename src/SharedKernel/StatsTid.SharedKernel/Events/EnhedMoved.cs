namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S100 (ADR-036 amendment) — emitted when a structured Enhed is RE-PARENTED within its
/// Organisation: either an explicit move (<c>PUT /api/admin/enheder/{id}/move</c>), or a
/// per-child re-parent that the soft-delete of a parent enhed performs (each surviving child
/// is lifted up to the deleted enhed's parent — a state change that MUST be an event, not a
/// silent SQL UPDATE; P3). Projection: UPDATE <c>enheder.parent_enhed_id</c> + bump
/// <c>version</c> on the moved row. Stream: <c>enhed-{enhedId}</c>.
///
/// <para>
/// <c>NewParentEnhedId == null</c> = the enhed becomes a ROOT (directly under the Organisation).
/// The hierarchy is PURE DISPLAY metadata — ZERO authority/scope/approval meaning;
/// <c>parent_enhed_id</c> enters NO scope/approval path. Plain-outbox (no audit-projection
/// row — consistent with <c>EnhedCreated/Renamed/Deleted</c>; only <c>UserEnhederChanged</c>
/// is audit-registered). Replay: name-keyed, round-trips unchanged.
/// </para>
/// </summary>
public sealed class EnhedMoved : DomainEventBase
{
    public override string EventType => "EnhedMoved";

    public required Guid EnhedId { get; init; }

    /// <summary>The parent BEFORE the move (<c>null</c> = was a root). Carried for replay/audit.</summary>
    public Guid? OldParentEnhedId { get; init; }

    /// <summary>The parent AFTER the move (<c>null</c> = becomes a root).</summary>
    public Guid? NewParentEnhedId { get; init; }
}
