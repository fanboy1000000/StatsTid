namespace StatsTid.SharedKernel.Events;

public sealed class TimerCheckedIn : DomainEventBase
{
    public override string EventType => "TimerCheckedIn";
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required DateTime CheckInAt { get; init; }
}
