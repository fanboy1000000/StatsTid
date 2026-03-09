namespace StatsTid.SharedKernel.Events;

public sealed class EntitlementBalanceAdjusted : DomainEventBase
{
    public override string EventType => "EntitlementBalanceAdjusted";

    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }
    public required decimal DeltaDays { get; init; }
    public required decimal NewUsed { get; init; }
    public required decimal NewRemaining { get; init; }
    public string? Reason { get; init; }
}
