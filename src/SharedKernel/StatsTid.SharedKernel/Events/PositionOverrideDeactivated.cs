namespace StatsTid.SharedKernel.Events;

public sealed class PositionOverrideDeactivated : DomainEventBase
{
    public override string EventType => "PositionOverrideDeactivated";
    public required Guid OverrideId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string PositionCode { get; init; }
}
