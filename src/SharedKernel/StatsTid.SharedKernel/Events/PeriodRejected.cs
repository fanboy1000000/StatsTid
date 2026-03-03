namespace StatsTid.SharedKernel.Events;

public sealed class PeriodRejected : DomainEventBase
{
    public override string EventType => "PeriodRejected";

    public required Guid PeriodId { get; init; }
    public required string EmployeeId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string RejectedBy { get; init; }
    public string? RejectionReason { get; init; }
}
