namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when an admin soft-deletes an employee profile (closes the live
/// row by setting effective_to). The row remains in storage for audit/replay
/// but is excluded from active lookups.
/// Stream: <c>employee-profile-{employeeId}</c>.
/// Per ADR-018 D6 + Sprint 31 / Phase 4d-3 Part 1.
/// RESERVED — registered in S31, NOT emitted until S32.
/// </summary>
public sealed class EmployeeProfileSoftDeleted : DomainEventBase
{
    public override string EventType => "EmployeeProfileSoftDeleted";

    // Identity
    public required Guid ProfileId { get; init; }
    public required string EmployeeId { get; init; }

    // Temporal validity at time of soft-delete (close date, typically today)
    public required DateOnly EffectiveTo { get; init; }

    // Post-mutation row-version. Named <c>RowVersion</c> (not <c>Version</c>)
    // to avoid shadowing <see cref="DomainEventBase.Version"/> which carries
    // the event-schema version — same disambiguation as S30
    // <see cref="EntitlementConfigSoftDeleted"/>.
    public required long RowVersion { get; init; }
}
