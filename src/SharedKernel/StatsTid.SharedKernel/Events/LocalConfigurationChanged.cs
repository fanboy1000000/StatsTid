namespace StatsTid.SharedKernel.Events;

public sealed class LocalConfigurationChanged : DomainEventBase
{
    public override string EventType => "LocalConfigurationChanged";

    public required Guid ConfigId { get; init; }
    public required string OrgId { get; init; }
    public required string ConfigArea { get; init; }
    public required string ConfigKey { get; init; }
    public required string ConfigValue { get; init; }
    public string? PreviousValue { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
