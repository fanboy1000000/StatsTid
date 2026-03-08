namespace StatsTid.SharedKernel.Events;

public sealed class AgreementConfigUpdated : DomainEventBase
{
    public override string EventType => "AgreementConfigUpdated";

    public required Guid ConfigId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
