namespace StatsTid.SharedKernel.Events;

public sealed class FallbackTraversalWarning : DomainEventBase
{
    public override string EventType => "FallbackTraversalWarning";

    public required string EmployeeId { get; init; }
    public string? ResolvedManagerId { get; init; }
    public required int Depth { get; init; }
    public required string TreeRootOrgId { get; init; }
}
