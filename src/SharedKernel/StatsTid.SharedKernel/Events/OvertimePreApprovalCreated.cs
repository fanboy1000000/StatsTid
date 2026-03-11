namespace StatsTid.SharedKernel.Events;

public sealed class OvertimePreApprovalCreated : DomainEventBase
{
    public override string EventType => "OvertimePreApprovalCreated";
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal MaxHours { get; init; }
    public required string Status { get; init; }
}
