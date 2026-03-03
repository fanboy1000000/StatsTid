namespace StatsTid.SharedKernel.Events;

public sealed class RoleAssignmentGranted : DomainEventBase
{
    public override string EventType => "RoleAssignmentGranted";

    public required Guid AssignmentId { get; init; }
    public required string UserId { get; init; }
    public required string RoleId { get; init; }
    public string? OrgId { get; init; }
    public required string ScopeType { get; init; }
}
