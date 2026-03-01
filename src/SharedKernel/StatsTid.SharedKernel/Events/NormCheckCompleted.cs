namespace StatsTid.SharedKernel.Events;

public sealed class NormCheckCompleted : DomainEventBase
{
    public override string EventType => "NormCheckCompleted";

    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal NormHours { get; init; }
    public required decimal ActualHours { get; init; }
    public required decimal Deviation { get; init; }
    public required bool NormFulfilled { get; init; }
    public required string OkVersion { get; init; }
    public required string RuleId { get; init; }
}
