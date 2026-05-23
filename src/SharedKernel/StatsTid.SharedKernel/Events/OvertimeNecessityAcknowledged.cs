namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a manager records post-hoc necessity acknowledgement for overtime
/// that was incurred without a pre-approval. Records the reason + the set of time
/// entries the acknowledgement covers, so subsequent payroll runs can route these
/// hours through the compensation pipeline rather than rejecting them.
/// ADR-024 D7 post-hoc necessity-ack workflow. Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class OvertimeNecessityAcknowledged : DomainEventBase
{
    public override string EventType => "OvertimeNecessityAcknowledged";

    public required Guid PreApprovalId { get; init; }
    public required string NecessityReason { get; init; }

    // Non-null by construction (initialized to empty array). Not `required`
    // because the EventSerializer round-trip test fills only string properties
    // and JsonIgnoreCondition.WhenWritingNull would otherwise omit a null
    // collection, breaking deserialization under `required`. Callers MUST
    // populate this list via init — an empty list is semantically wrong
    // (the event records WHICH entries are acknowledged).
    public IReadOnlyList<Guid> AcknowledgedForEntries { get; init; } = Array.Empty<Guid>();
}
