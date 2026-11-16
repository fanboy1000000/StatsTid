namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S148 C-13 (ADR-040 D4). A user's FIRST <c>user_organisations</c> row was written: the user
/// create, or the startup backfill seeder for an existing user. Shape mirrors
/// <see cref="UserAgreementCodeSeeded"/>. Emitted on the user's stream; rides ONE transaction with
/// the row, its <c>user_organisations_audit</c> row and the outbox append (ADR-018 D3).
/// </summary>
public sealed class UserOrganisationSeeded : DomainEventBase
{
    public override string EventType => "UserOrganisationSeeded";

    public required string UserId { get; init; }
    public required Guid AssignmentId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required long RowVersion { get; init; }
}
