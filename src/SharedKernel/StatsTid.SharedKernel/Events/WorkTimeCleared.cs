namespace StatsTid.SharedKernel.Events;

/// <summary>
/// A self-recorded work-time day emptied by a Skema save (owner Q2; addendum A1 D-3, D-4). The day's
/// <c>work_time_projection</c> row is kept and marked: <c>cleared_by_outbox_id</c> and <c>outbox_id</c> both
/// become this event's outbox id, so an older <c>WorkTimeRegistered</c> replayed by the startup rebuild cannot
/// pass the row's latest-wins guard and bring the day back. A posted day with no work period is refused; this
/// event is the only way a day is emptied. Rides <c>employee-{EmployeeId}</c> (ADR-018 D5).
/// </summary>
public sealed class WorkTimeCleared : DomainEventBase
{
    public override string EventType => "WorkTimeCleared";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>The latest <c>WorkTimeRegistered</c> of the day: the event whose outbox id the row held when the
    /// clear read it.</summary>
    public required Guid ClearedEventId { get; init; }

    /// <summary>The cleared day's interval hours.</summary>
    public required decimal ClearedHours { get; init; }
}
