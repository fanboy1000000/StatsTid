namespace StatsTid.SharedKernel.Events;

public sealed class PeriodReopened : DomainEventBase
{
    public override string EventType => "PeriodReopened";
    public required Guid PeriodId { get; init; }
    public required string EmployeeId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public string? Reason { get; init; }
}
