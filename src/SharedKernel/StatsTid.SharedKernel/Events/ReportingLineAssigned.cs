namespace StatsTid.SharedKernel.Events;

public sealed class ReportingLineAssigned : DomainEventBase
{
    public override string EventType => "ReportingLineAssigned";

    public required Guid ReportingLineId { get; init; }
    public required string EmployeeId { get; init; }
    public required string ManagerId { get; init; }
    public required string TreeRootOrgId { get; init; }
    public required string Relationship { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required string Source { get; init; }
    public required long RowVersion { get; init; }
}
