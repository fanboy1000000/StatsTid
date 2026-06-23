namespace StatsTid.SharedKernel.Events;

public sealed class PeriodApproved : DomainEventBase
{
    public override string EventType => "PeriodApproved";

    public required Guid PeriodId { get; init; }
    public required string EmployeeId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string ApprovedBy { get; init; }
}
