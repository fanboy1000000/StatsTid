namespace StatsTid.SharedKernel.Models;

public sealed class DeliveryStatus
{
    public required Guid MessageId { get; init; }
    public required string Destination { get; init; }
    public required string Status { get; init; }
    public int AttemptCount { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public string? ErrorMessage { get; init; }
}
