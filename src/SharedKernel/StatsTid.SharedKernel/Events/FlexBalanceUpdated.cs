namespace StatsTid.SharedKernel.Events;

public sealed class FlexBalanceUpdated : DomainEventBase
{
    public override string EventType => "FlexBalanceUpdated";
    public required string EmployeeId { get; init; }
    public required decimal PreviousBalance { get; init; }
    public required decimal NewBalance { get; init; }
    public required decimal Delta { get; init; }
    public required string Reason { get; init; }
}
