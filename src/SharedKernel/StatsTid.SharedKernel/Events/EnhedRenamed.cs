namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S97 / ADR-035 — emitted when a structured Enhed is renamed (rename-once-updates-all;
/// the Enhed identity / its user memberships are unchanged). Projection: UPDATE the
/// <c>enheder</c> row's <c>name</c> + bump <c>version</c>. Stream: <c>enhed-{enhedId}</c>.
/// </summary>
public sealed class EnhedRenamed : DomainEventBase
{
    public override string EventType => "EnhedRenamed";

    public required Guid EnhedId { get; init; }
    public required string Name { get; init; }
}
