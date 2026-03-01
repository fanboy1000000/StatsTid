namespace StatsTid.SharedKernel.Events;

public abstract class DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public abstract string EventType { get; }
    public int Version { get; init; } = 1;
    public string? ActorId { get; init; }
    public string? ActorRole { get; init; }
    public Guid? CorrelationId { get; init; }
}
