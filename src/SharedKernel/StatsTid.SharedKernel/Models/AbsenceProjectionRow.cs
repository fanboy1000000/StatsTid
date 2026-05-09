namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Immutable row type for the <c>absences_projection</c> table (S27 Phase 4c.6,
/// init.sql:1204-1221). Mirrors the column list 1:1 so the sync-in-tx projection
/// layer (TASK-2706 Skema POST atomic path) and the read endpoints
/// (Skema month, Time absences, Balance summary, Compliance period) share the
/// same shape. Projection state is derived from <see cref="Events.AbsenceRegistered"/>
/// + <see cref="Events.DomainEventBase"/> envelope fields and the global
/// <c>outbox_events.outbox_id</c> assigned in the same transaction.
/// </summary>
public sealed class AbsenceProjectionRow
{
    public required Guid EventId { get; init; }
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required string AbsenceType { get; init; }
    public required decimal Hours { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateTime OccurredAt { get; init; }
    public string? ActorId { get; init; }
    public string? ActorRole { get; init; }
    public Guid? CorrelationId { get; init; }
    public required long OutboxId { get; init; }
}
