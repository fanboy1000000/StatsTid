namespace StatsTid.SharedKernel.Events;

public sealed class PeriodSubmitted : DomainEventBase
{
    public override string EventType => "PeriodSubmitted";

    public required Guid PeriodId { get; init; }
    public required string EmployeeId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string PeriodType { get; init; }
}
