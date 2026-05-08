namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when an overtime pre-approval is approved by a Leader+ role
/// (S26 / TASK-2602). Pattern C event — carries actor metadata in lieu of
/// a separate audit row (no overtime_pre_approval_audit table; ADR-019
/// row-version + If-Match contract is OUT OF SCOPE for this surface per
/// SPRINT-26.md Assumption #6). Stream: overtime-preapproval-{id}.
/// </summary>
public sealed class OvertimePreApprovalApproved : DomainEventBase
{
    public override string EventType => "OvertimePreApprovalApproved";
    public required Guid PreApprovalId { get; init; }
    public required string EmployeeId { get; init; }
    public required string ApprovedBy { get; init; }
    public string? Reason { get; init; }
}
