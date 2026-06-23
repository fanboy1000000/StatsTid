namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S97 / ADR-035 — emitted when a user's structured Enhed tag-set is overwritten. Carries
/// the FULL tag-id set for the user (idempotent overwrite — NOT a delta). An EMPTY
/// <see cref="EnhedIds"/> clears the user's tags (the transfer-clears-tags path: a
/// primary_org change CLEARS the rows in the same tx, since an Enhed tag never crosses
/// Organisations — Enhed is throwaway metadata per ADR-035). Projection: delete-all-then-
/// insert the set for that user in one tx (latest-wins, non-temporal). Stream:
/// <c>user-{userId}</c>.
/// </summary>
public sealed class UserEnhederChanged : DomainEventBase
{
    public override string EventType => "UserEnhederChanged";

    public required string UserId { get; init; }

    /// <summary>The FULL active Enhed-id set the user is tagged with after this change
    /// (an empty array clears all tags). Idempotent overwrite — replay re-applies the
    /// same set.</summary>
    public required IReadOnlyList<Guid> EnhedIds { get; init; }
}
