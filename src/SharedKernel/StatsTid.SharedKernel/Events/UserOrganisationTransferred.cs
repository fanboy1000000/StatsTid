namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S148 C-13 (ADR-040 D4). A user's organisation changed. One event for both write shapes:
/// close-and-open (a date after the live row's start: the live row is closed at the date and a new
/// row opened, <see cref="InPlace"/> false) and same-day replacement (a date equal to the live
/// row's start: the row's organisation is replaced in place, <see cref="InPlace"/> true,
/// <see cref="PredecessorAssignmentId"/> null). Emitted on the user's stream; rides ONE transaction
/// with the row, its audit row and the outbox append (ADR-018 D3). Its one emitter is the user edit's
/// transfer branch (S148 TASK-14820).
///
/// <para><b>Version meaning.</b> <see cref="VersionBefore"/> is the version of the row live before
/// the write; <see cref="VersionAfter"/> is the version of the row live after it: predecessor + 1
/// for close-and-open (the <see cref="UserAgreementCodeSuperseded"/> rule), the bumped version in
/// place.</para>
///
/// <para>The nullable <see cref="PredecessorAssignmentId"/> is NON-required (S66 round-trip lesson).</para>
/// </summary>
public sealed class UserOrganisationTransferred : DomainEventBase
{
    public override string EventType => "UserOrganisationTransferred";

    public required string UserId { get; init; }

    /// <summary>The row that now holds the new organisation.</summary>
    public required Guid AssignmentId { get; init; }

    /// <summary>The closed row; null when the row was updated in place.</summary>
    public Guid? PredecessorAssignmentId { get; init; }

    public required string OldOrgId { get; init; }
    public required string NewOrgId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required bool InPlace { get; init; }
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
