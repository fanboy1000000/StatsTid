namespace StatsTid.SharedKernel.Events;

public sealed class ReportingLineManagerDeactivated : DomainEventBase
{
    public override string EventType => "ReportingLineManagerDeactivated";

    public required Guid ReportingLineId { get; init; }
    public required string EmployeeId { get; init; }
    public required string ManagerId { get; init; }
    public required string OrganisationId { get; init; }
}
