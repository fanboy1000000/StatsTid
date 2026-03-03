namespace StatsTid.SharedKernel.Events;

public sealed class OrganizationCreated : DomainEventBase
{
    public override string EventType => "OrganizationCreated";

    public required string OrgId { get; init; }
    public required string OrgName { get; init; }
    public required string OrgType { get; init; }
    public string? ParentOrgId { get; init; }
    public required string MaterializedPath { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
