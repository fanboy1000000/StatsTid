namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S97 / ADR-035 — emitted when a structured Enhed is SOFT-deleted (sets
/// <c>enheder.deleted_at</c>). Memberships in <c>user_enheder</c> are NOT fanned-out /
/// hard-deleted (the FK stays valid); they are PROJECTION-FILTERED at read time (a
/// membership to a soft-deleted enhed simply does not render — avoids the delete-vs-add
/// race, P4). Stream: <c>enhed-{enhedId}</c>.
/// </summary>
public sealed class EnhedDeleted : DomainEventBase
{
    public override string EventType => "EnhedDeleted";

    public required Guid EnhedId { get; init; }
}
