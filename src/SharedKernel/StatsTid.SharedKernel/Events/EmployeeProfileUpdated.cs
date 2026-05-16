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
    public required decimal WeeklyNormHours { get; init; }
    public required decimal PartTimeFraction { get; init; }
    public string? Position { get; init; }

    // Optimistic-concurrency row-version transition
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
