namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S70 / ADR-033 slice 3a (SPRINT-70 R10). Emitted from the admin employment-end-date
/// set/clear endpoint whenever <c>users.employment_end_date</c> mutates — set, clear, or
/// correction are all THIS one event, distinguished by the <see cref="OldEndDate"/> /
/// <see cref="NewEndDate"/> pair (set: null→date; clear: date→null; correction: date→date).
///
/// Carries the <c>is_active</c> transition alongside (R1 lifecycle: a passed end date
/// deactivates same-tx; a clear reactivates ONLY when the deactivation was end-date-provenance)
/// plus the optimistic-concurrency row-version pair (ADR-018 D7 — the mutation bumps
/// <c>users.version</c> so a held ETag cannot survive the transition).
///
/// Stream: <c>employee-{EmployeeId}</c>. Rides ONE atomic tx with the user mutation, the
/// outbox append and the ADR-026 audit projection (R10). Actor = the admin caller (base
/// <see cref="DomainEventBase.ActorId"/>/<see cref="DomainEventBase.ActorRole"/>).
///
/// ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson: the nullable <see cref="OldEndDate"/> /
/// <see cref="NewEndDate"/> are NON-required (a null + WhenWritingNull omission must not break
/// the <c>required</c>-member deserialization check).
/// </summary>
public sealed class EmployeeEmploymentEndDateSet : DomainEventBase
{
    public override string EventType => "EmployeeEmploymentEndDateSet";

    public required string EmployeeId { get; init; }

    /// <summary>The previous <c>employment_end_date</c> (null = none was set).</summary>
    public DateOnly? OldEndDate { get; init; }

    /// <summary>The new <c>employment_end_date</c> (null = cleared).</summary>
    public DateOnly? NewEndDate { get; init; }

    // is_active transition (R1 — same-tx deactivation on a passed end date / provenance-guarded reactivation on clear)
    public required bool OldIsActive { get; init; }
    public required bool NewIsActive { get; init; }

    // Optimistic-concurrency row-version transition (ADR-018 D7 / ADR-019)
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
