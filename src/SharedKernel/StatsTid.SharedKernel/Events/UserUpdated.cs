namespace StatsTid.SharedKernel.Events;

public sealed class UserUpdated : DomainEventBase
{
    public override string EventType => "UserUpdated";

    public required string UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? PrimaryOrgId { get; init; }
    public string? AgreementCode { get; init; }
}
