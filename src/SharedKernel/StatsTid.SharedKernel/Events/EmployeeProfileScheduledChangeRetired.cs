namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S141 / TASK-14104 (ADR-040 Increment 4, owner ruling OQ-5 (a)) — emitted when a profile change
/// that was already SCHEDULED for a future date is retired as a side effect of deleting the
/// employee's profile.
///
/// <para>
/// <b>Why this event exists, in plain language.</b> Once HR can date a change ahead, one person can
/// schedule a promotion for 1 November and a different person can delete the profile on 14
/// September. The owner ruled that the delete takes both — and attached a condition: a row that was
/// audited into existence must not vanish unrecorded. The existing
/// <see cref="EmployeeProfileSoftDeleted"/> event describes exactly one row and one date, so it
/// cannot carry that. This event is the other half: one per retired row, saying what it was going to
/// bring, when, and which delete destroyed it.
/// </para>
///
/// <para>
/// <b>What "retired" means physically.</b> The row is closed ZERO-WIDTH
/// (<c>effective_to := effective_from</c>), not hard-deleted — no timeline table in this system has
/// ever hard-deleted a row, and a row that stays on the timeline can be explained rather than merely
/// missed. So the row still exists; it simply covers no day. <see cref="PreviousEffectiveTo"/> is the
/// end it had BEFORE that (<c>null</c> = it was the open row).
/// </para>
///
/// <para>
/// Stream: <c>employee-profile-{employeeId}</c>, the same stream as the soft-delete it follows, so
/// per-stream ordering (ADR-018 D6) reads as cause then consequence.
/// </para>
/// </summary>
public sealed class EmployeeProfileScheduledChangeRetired : DomainEventBase
{
    public override string EventType => "EmployeeProfileScheduledChangeRetired";

    // Identity — the scheduled row that was retired.
    public required Guid ProfileId { get; init; }
    public required string EmployeeId { get; init; }

    /// <summary>The day the scheduled change was going to take effect. It is strictly after the
    /// delete's close date by construction, which is what made it a SCHEDULED change rather than
    /// history.</summary>
    public required DateOnly EffectiveFrom { get; init; }

    /// <summary>The end (exclusive) the row had before retirement; <c>null</c> = it was the open
    /// row, i.e. the change was going to run indefinitely.</summary>
    public DateOnly? PreviousEffectiveTo { get; init; }

    // The values the change was going to bring — the substance of what was destroyed.
    public required decimal PartTimeFraction { get; init; }
    public string? Position { get; init; }
    public string? EmploymentCategory { get; init; }

    /// <summary>The retired row's own <c>version</c> column, unchanged by the retirement. Named
    /// <c>RowVersion</c> — NOT <c>Version</c> — to avoid shadowing
    /// <see cref="DomainEventBase.Version"/>, which carries the event-schema version. Same
    /// disambiguation as <see cref="EmployeeProfileSoftDeleted"/>.</summary>
    public required long RowVersion { get; init; }

    /// <summary>The profile row the DELETE closed — the causal link back to the action that caused
    /// this retirement. Without it a replay sees a row stop existing with nothing to attribute it
    /// to, which is precisely the hole the owner's audit condition exists to close.</summary>
    public required Guid RetiredWithProfileId { get; init; }

    /// <summary>The delete's close date: the ONE "today" of that request, so this event and the
    /// <see cref="EmployeeProfileSoftDeleted"/> beside it can never name different days.</summary>
    public required DateOnly RetiredOn { get; init; }
}
