namespace StatsTid.SharedKernel.Events;

public sealed class AgreementConfigCloned : DomainEventBase
{
    public override string EventType => "AgreementConfigCloned";

    public required Guid ConfigId { get; init; }
    public required Guid SourceConfigId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
