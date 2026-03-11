namespace StatsTid.SharedKernel.Events;

public sealed class CompensatoryRestGranted : DomainEventBase
{
    public override string EventType => "CompensatoryRestGranted";

    public required string EmployeeId { get; init; }
    public required DateOnly SourceDate { get; init; }
    public DateOnly? CompensatoryDate { get; init; }
    public required decimal Hours { get; init; }
}
