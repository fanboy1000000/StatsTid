namespace StatsTid.SharedKernel.Events;

public sealed class TimeEntryRegistered : DomainEventBase
{
    public override string EventType => "TimeEntryRegistered";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Hours { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public string? TaskId { get; init; }
    public string? ActivityType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public bool VoluntaryUnsocialHours { get; init; }
}
