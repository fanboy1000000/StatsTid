namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted on the same-day live-edit path when an employee profile is updated
/// in place (no cross-day supersession). Carries the post-mutation payload
/// plus the version-before/after pair for optimistic-concurrency audit.
/// Stream: <c>employee-profile-{employeeId}</c>.
/// Per ADR-018 D6 + Sprint 31 / Phase 4d-3 Part 1.
/// </summary>
public sealed class EmployeeProfileUpdated : DomainEventBase
{
    public override string EventType => "EmployeeProfileUpdated";

    // Identity
    public required Guid ProfileId { get; init; }
    public required string EmployeeId { get; init; }

    // Payload — post-mutation state
    public required decimal PartTimeFraction { get; init; }
    public string? Position { get; init; }

    // S138 / TASK-13801 (ADR-040 D4/D8) — the dated fourth field, written on every row the
    // writer produces. Additive and optional (not `required`): pre-S138 payloads deserialize
    // with null; EventSerializer registrations are unchanged.
    public string? EmploymentCategory { get; init; }

    // Optimistic-concurrency row-version transition
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
