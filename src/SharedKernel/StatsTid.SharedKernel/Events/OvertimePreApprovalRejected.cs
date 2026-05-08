namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when an overtime pre-approval is rejected by a Leader+ role
/// (S26 / TASK-2602). Pattern C event — carries actor metadata in lieu of
/// a separate audit row. Stream: overtime-preapproval-{id}.
/// </summary>
public sealed class OvertimePreApprovalRejected : DomainEventBase
{
    public override string EventType => "OvertimePreApprovalRejected";
    public required Guid PreApprovalId { get; init; }
    public required string EmployeeId { get; init; }
    public required string RejectedBy { get; init; }
    public string? Reason { get; init; }
}
