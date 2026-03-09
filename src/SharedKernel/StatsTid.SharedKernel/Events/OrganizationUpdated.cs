namespace StatsTid.SharedKernel.Events;

public sealed class OrganizationUpdated : DomainEventBase
{
    public override string EventType => "OrganizationUpdated";

    public required string OrgId { get; init; }
    public string? OrgName { get; init; }
    public string? AgreementCode { get; init; }
    public string? OkVersion { get; init; }
}
