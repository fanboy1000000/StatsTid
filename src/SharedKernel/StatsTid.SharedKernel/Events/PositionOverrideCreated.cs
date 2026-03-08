namespace StatsTid.SharedKernel.Events;

public sealed class PositionOverrideCreated : DomainEventBase
{
    public override string EventType => "PositionOverrideCreated";
    public required Guid OverrideId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string PositionCode { get; init; }
}
