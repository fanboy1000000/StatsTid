namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Immutable row type for the <c>time_entries_projection</c> table (S27 Phase 4c.6,
/// init.sql:1181-1202). Mirrors the column list 1:1 so the sync-in-tx projection
/// layer (TASK-2706 Skema POST atomic path) and the read endpoints
/// (Skema month, Time entries, Balance summary, Compliance period) share the
/// same shape. Projection state is derived from <see cref="Events.TimeEntryRegistered"/>
/// + <see cref="Events.DomainEventBase"/> envelope fields and the global
/// <c>outbox_events.outbox_id</c> assigned in the same transaction.
/// </summary>
public sealed class TimeEntryProjectionRow
{
    public required Guid EventId { get; init; }
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Hours { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public string? TaskId { get; init; }
    public string? ActivityType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public bool VoluntaryUnsocialHours { get; init; }
    public required DateTime OccurredAt { get; init; }
    public string? ActorId { get; init; }
    public string? ActorRole { get; init; }
    public Guid? CorrelationId { get; init; }
    public required long OutboxId { get; init; }

    /// <summary>
    /// S145 / A0 — <c>source</c> column: <see cref="HoursSource.Skema"/> for a Skema save (latest-wins
    /// per cell) or <see cref="HoursSource.TimePost"/> for an additive <c>POST /api/time-entries</c>
    /// row. Defaults to <see cref="HoursSource.TimePost"/>, the truthful class of every row that
    /// predates the column (and the column's own DEFAULT), so a row built without it stays additive.
    /// </summary>
    public string Source { get; init; } = HoursSource.TimePost;

    /// <summary>
    /// S145 / A0 — <c>superseded_by_outbox_id</c> column: the <c>outbox_id</c> of the Skema re-save
    /// that replaced this row, or null while it is the current value of its cell. A superseded row
    /// is retained for replay and audit; readers that total hours count current rows only.
    /// </summary>
    public long? SupersededByOutboxId { get; init; }
}
