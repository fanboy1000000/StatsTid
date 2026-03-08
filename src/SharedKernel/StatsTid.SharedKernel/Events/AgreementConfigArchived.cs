namespace StatsTid.SharedKernel.Events;

public sealed class AgreementConfigArchived : DomainEventBase
{
    public override string EventType => "AgreementConfigArchived";

    public required Guid ConfigId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
