namespace StatsTid.SharedKernel.Models;

public sealed class AuditLogEntry
{
    public long LogId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? ActorId { get; init; }
    public string? ActorRole { get; init; }
    public required string Action { get; init; }
    public required string Resource { get; init; }
    public string? ResourceId { get; init; }
    public Guid? CorrelationId { get; init; }
    public string? HttpMethod { get; init; }
    public string? HttpPath { get; init; }
    public int? HttpStatus { get; init; }
    public string Result { get; init; } = "success";
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
}
