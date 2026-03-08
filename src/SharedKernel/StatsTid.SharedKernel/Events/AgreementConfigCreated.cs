namespace StatsTid.SharedKernel.Events;

public sealed class AgreementConfigCreated : DomainEventBase
{
    public override string EventType => "AgreementConfigCreated";

    public required Guid ConfigId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
