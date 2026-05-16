namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a fresh employee profile row is INSERTED into
/// <c>employee_profiles</c>. Covers both the live "first profile for
/// employee" path and the S31 backfill path (<c>EffectiveFrom = 0001-01-01</c>).
/// Stream: <c>employee-profile-{employeeId}</c> — one stream per employee
/// lineage; cross-day supersession events flow into the same stream.
/// Per ADR-018 D6 + Sprint 31 / Phase 4d-3 Part 1.
/// </summary>
public sealed class EmployeeProfileCreated : DomainEventBase
{
    public override string EventType => "EmployeeProfileCreated";

    // Identity
    public required Guid ProfileId { get; init; }
    public required string EmployeeId { get; init; }

    // Payload — full new-row fields needed by downstream consumers + replay
    public required decimal WeeklyNormHours { get; init; }
    public required decimal PartTimeFraction { get; init; }
    public string? Position { get; init; }

    // Temporal validity — always today's date in S31 live path;
    // '0001-01-01' for backfill rows.
    public required DateOnly EffectiveFrom { get; init; }
}
