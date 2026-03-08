namespace StatsTid.SharedKernel.Events;

public sealed class PositionOverrideActivated : DomainEventBase
{
    public override string EventType => "PositionOverrideActivated";
    public required Guid OverrideId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string PositionCode { get; init; }
}
