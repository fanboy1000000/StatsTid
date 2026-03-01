namespace StatsTid.SharedKernel.Events;

public sealed class IntegrationDeliveryTracked : DomainEventBase
{
    public override string EventType => "IntegrationDeliveryTracked";

    public required Guid MessageId { get; init; }
    public required string Destination { get; init; }
    public required string Status { get; init; }
    public int AttemptCount { get; init; }
    public string? ErrorMessage { get; init; }
}
