namespace StatsTid.SharedKernel.Events;

/// <summary>
/// A Skema project cell (one employee, one day, one task) emptied by a Skema save (owner Q2; addendum A1
/// D-3). The cell's current Skema row in <c>time_entries_projection</c> is kept and marked
/// <c>superseded_by_outbox_id</c> = this event's outbox id; nothing is inserted. Rides
/// <c>employee-{EmployeeId}</c>, the stream of the registration it retires, so per-stream order puts it after
/// that registration (ADR-018 D5). A registration is never written with 0 hours: a removal is this event.
/// </summary>
public sealed class TimeEntryCleared : DomainEventBase
{
    public override string EventType => "TimeEntryCleared";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>The cell's project code (the retired row's <c>task_id</c>).</summary>
    public required string TaskId { get; init; }

    /// <summary>The <c>TimeEntryRegistered</c> whose row this clear retired.</summary>
    public required Guid ClearedEventId { get; init; }

    /// <summary>The retired row's hours.</summary>
    public required decimal ClearedHours { get; init; }
}
