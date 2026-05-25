namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a manager (LeaderOrAbove) delegates their approval queue to
/// another manager via the self-service <c>POST /api/reporting-lines/delegate</c>
/// endpoint. One event per batch — individual ACTING lines are persisted via
/// <see cref="ReportingLineAssigned"/> per standard outbox pattern; this event
/// captures the batch metadata for audit trail and downstream consumers.
/// </summary>
public sealed class ReportingLineSelfDelegated : DomainEventBase
{
    public override string EventType => "ReportingLineSelfDelegated";

    public required Guid BatchId { get; init; }
    public required string DelegatingManagerId { get; init; }
    public required string ActingManagerId { get; init; }
    public required int DelegatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required DateOnly EffectiveTo { get; init; }
}
