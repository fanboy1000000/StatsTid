namespace StatsTid.SharedKernel.Events;

public sealed class WageTypeMappingUpdated : DomainEventBase
{
    public override string EventType => "WageTypeMappingUpdated";
    public required string TimeType { get; init; }
    public required string WageType { get; init; }
    public required string OkVersion { get; init; }
    public required string AgreementCode { get; init; }
    public string Position { get; init; } = "";
}
