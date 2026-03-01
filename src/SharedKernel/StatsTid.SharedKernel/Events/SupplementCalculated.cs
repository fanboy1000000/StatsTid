namespace StatsTid.SharedKernel.Events;

public sealed class SupplementCalculated : DomainEventBase
{
    public override string EventType => "SupplementCalculated";
    public required string EmployeeId { get; init; }
    public required string SupplementType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Rate { get; init; }
    public required string RuleId { get; init; }
}
