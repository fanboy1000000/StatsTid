namespace StatsTid.Orchestrator.Contracts;

public sealed class OrchestratorTask
{
    public Guid TaskId { get; init; } = Guid.NewGuid();
    public required string TaskType { get; init; }
    public required string Status { get; set; }
    public string? AssignedAgent { get; set; }
    public Dictionary<string, object>? InputData { get; init; }
    public object? OutputData { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
