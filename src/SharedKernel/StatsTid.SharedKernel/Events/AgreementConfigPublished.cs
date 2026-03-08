namespace StatsTid.SharedKernel.Events;

public sealed class AgreementConfigPublished : DomainEventBase
{
    public override string EventType => "AgreementConfigPublished";

    public required Guid ConfigId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public Guid? ArchivedConfigId { get; init; }
}
