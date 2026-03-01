namespace StatsTid.Orchestrator.Contracts;

public sealed class ExecuteRequest
{
    public required string TaskType { get; init; }
    public required Dictionary<string, object> Parameters { get; init; }
}
