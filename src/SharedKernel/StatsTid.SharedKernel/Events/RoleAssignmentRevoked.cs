namespace StatsTid.SharedKernel.Events;

public sealed class RoleAssignmentRevoked : DomainEventBase
{
    public override string EventType => "RoleAssignmentRevoked";

    public required Guid AssignmentId { get; init; }
    public required string UserId { get; init; }
    public required string RoleId { get; init; }
    public string? Reason { get; init; }
}
