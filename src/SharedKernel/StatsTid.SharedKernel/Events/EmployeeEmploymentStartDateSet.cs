namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S148 P-28 / NR-18. The event of the admin employment-start-date set/clear endpoint: one per
/// mutation of <c>users.employment_start_date</c>. The end-date event's shape
/// (<see cref="EmployeeEmploymentEndDateSet"/>) without the <c>is_active</c> pair, which the
/// start-date route never changes. Set, clear and correction are this one event, told apart by the
/// <see cref="OldStartDate"/> / <see cref="NewStartDate"/> pair.
///
/// Stream: <c>employee-{EmployeeId}</c>. Rides ONE transaction with the user mutation, the outbox
/// append and the ADR-026 audit projection (ADR-018 D3). <see cref="VersionBefore"/> /
/// <see cref="VersionAfter"/> are the <c>users</c> row version (ADR-018 D7).
///
/// ROUND-TRIPPABILITY (S66 lesson): the nullable dates are NON-required.
/// </summary>
public sealed class EmployeeEmploymentStartDateSet : DomainEventBase
{
    public override string EventType => "EmployeeEmploymentStartDateSet";

    public required string EmployeeId { get; init; }

    /// <summary>The previous <c>employment_start_date</c> (null = none was set).</summary>
    public DateOnly? OldStartDate { get; init; }

    /// <summary>The new <c>employment_start_date</c> (null = unknown or cleared).</summary>
    public DateOnly? NewStartDate { get; init; }

    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
