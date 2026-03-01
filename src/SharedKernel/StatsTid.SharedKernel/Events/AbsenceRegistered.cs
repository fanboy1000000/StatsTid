namespace StatsTid.SharedKernel.Events;

public sealed class AbsenceRegistered : DomainEventBase
{
    public override string EventType => "AbsenceRegistered";
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required string AbsenceType { get; init; }
    public required decimal Hours { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
