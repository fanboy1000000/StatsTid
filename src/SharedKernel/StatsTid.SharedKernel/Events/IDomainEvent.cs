namespace StatsTid.SharedKernel.Events;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    string EventType { get; }
    int Version { get; }
    string? ActorId { get; }
    string? ActorRole { get; }
    Guid? CorrelationId { get; }
}
