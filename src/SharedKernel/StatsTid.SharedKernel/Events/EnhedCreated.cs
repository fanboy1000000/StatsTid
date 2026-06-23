namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S97 / ADR-035 — emitted when a structured Enhed (pure display metadata, ZERO
/// authority/scope/approval meaning) is created under exactly one ORGANISATION-typed
/// org. Projection: latest-wins INSERT into <c>enheder</c> (non-temporal — model after
/// <c>work_time_projection</c> / ADR-028 D1, NOT the ADR-022 temporal profile). Stream:
/// <c>enhed-{enhedId}</c>.
/// </summary>
public sealed class EnhedCreated : DomainEventBase
{
    public override string EventType => "EnhedCreated";

    public required Guid EnhedId { get; init; }
    public required string OrganisationId { get; init; }
    public required string Name { get; init; }
}
