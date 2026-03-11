namespace StatsTid.SharedKernel.Events;

public sealed class RestPeriodViolationDetected : DomainEventBase
{
    public override string EventType => "RestPeriodViolationDetected";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal RestHoursActual { get; init; }
    public required decimal RestHoursRequired { get; init; }
    public bool IsVoluntary { get; init; }
    public bool IsDerogation { get; init; }
}
