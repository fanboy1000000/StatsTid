namespace StatsTid.SharedKernel.Events;

public sealed class OvertimeCalculated : DomainEventBase
{
    public override string EventType => "OvertimeCalculated";
    public required string EmployeeId { get; init; }
    public required string OvertimeType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Rate { get; init; }
    public required string RuleId { get; init; }
}
