namespace StatsTid.SharedKernel.Events;

public sealed class WageTypeMappingDeleted : DomainEventBase
{
    public override string EventType => "WageTypeMappingDeleted";
    public required string TimeType { get; init; }
    public required string OkVersion { get; init; }
    public required string AgreementCode { get; init; }
    public string Position { get; init; } = "";
}
