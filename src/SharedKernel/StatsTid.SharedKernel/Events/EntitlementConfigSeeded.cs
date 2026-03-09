namespace StatsTid.SharedKernel.Events;

public sealed class EntitlementConfigSeeded : DomainEventBase
{
    public override string EventType => "EntitlementConfigSeeded";

    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required int ConfigCount { get; init; }
}
