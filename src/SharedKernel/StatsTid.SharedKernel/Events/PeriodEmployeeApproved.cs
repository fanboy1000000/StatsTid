namespace StatsTid.SharedKernel.Events;

public sealed class PeriodEmployeeApproved : DomainEventBase
{
    public override string EventType => "PeriodEmployeeApproved";
    public required Guid PeriodId { get; init; }
    public required string EmployeeId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
}
