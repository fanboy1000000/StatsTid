namespace StatsTid.SharedKernel.Events;

public sealed class TimerCheckedOut : DomainEventBase
{
    public override string EventType => "TimerCheckedOut";
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required DateTime CheckOutAt { get; init; }
    public required decimal ClockedHours { get; init; }
}
