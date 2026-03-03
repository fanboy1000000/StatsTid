namespace StatsTid.SharedKernel.Events;

public sealed class UserCreated : DomainEventBase
{
    public override string EventType => "UserCreated";

    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string PrimaryOrgId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
