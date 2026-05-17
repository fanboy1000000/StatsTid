namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S34 / TASK-3404 (ADR-023 D2 option (b)) — Phase 4e replay-data trail for
/// cross-day supersession of a user's agreement-code assignment. Emitted on
/// Case C (cross-day edit): predecessor row closed (<c>effective_to</c> set to
/// the new effective date per end-exclusive convention) and a new live history
/// row inserted in the same atomic tx.
///
/// <para>
/// Shape mirrors <see cref="EmployeeProfileSuperseded"/> (S31). One stream per
/// user lineage carries both the predecessor close and the successor insert;
/// the new row's INSERT is recorded separately as
/// <see cref="UserAgreementCodeSeeded"/> only at first-ever assignment — for
/// cross-day supersession the new row's existence is encoded by this event's
/// <c>NewAssignmentId</c> + <c>NewAgreementCode</c> + <c>NewEffectiveFrom</c>.
/// </para>
///
/// <para>
/// <b>Version semantics</b> (per S33 Step 7a P1). <c>VersionBefore</c> is the
/// predecessor's row-version at close time — UNCHANGED across the supersession
/// (the close does not bump the predecessor's version). <c>VersionAfter</c> is
/// the new live row's version = <c>predecessor.Version + 1</c>.
/// </para>
///
/// <para>
/// <b>End-exclusive interval convention.</b> <c>PredecessorEffectiveTo</c>
/// equals <c>NewEffectiveFrom</c> (the close date and the open date are the
/// same calendar day under [from, to) semantics).
/// </para>
///
/// <para>
/// <b>Emission site.</b> Wired by TASK-3407 (<c>AdminEndpoints PUT
/// /api/admin/users/{userId}</c>) when Case C applies. Same-day edits (Case B)
/// continue to emit <see cref="UserAgreementCodeChanged"/>; cross-day edits
/// emit this event instead.
/// </para>
/// </summary>
public sealed class UserAgreementCodeSuperseded : DomainEventBase
{
    public override string EventType => "UserAgreementCodeSuperseded";

    // Identity — predecessor + successor assignment rows
    public required Guid PredecessorAssignmentId { get; init; }
    public required Guid NewAssignmentId { get; init; }
    public required string UserId { get; init; }

    // Temporal validity transition (end-exclusive: PredecessorEffectiveTo == NewEffectiveFrom)
    public required DateOnly PredecessorEffectiveFrom { get; init; }
    public required DateOnly PredecessorEffectiveTo { get; init; }
    public required DateOnly NewEffectiveFrom { get; init; }

    // Agreement-code transition payload
    public required string OldAgreementCode { get; init; }
    public required string NewAgreementCode { get; init; }

    // Optimistic-concurrency row-versions (VersionBefore unchanged across supersession per S33 Step 7a P1)
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
