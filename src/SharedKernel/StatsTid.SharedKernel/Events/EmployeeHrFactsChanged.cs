namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S148 C-11 (ADR-043 D4). A write to the dated <c>employee_hr_facts</c> row of a person (the
/// overtime regime and "on the flex scheme"). Carries the row's state AFTER the write. Emitted on
/// the employee's stream; rides ONE transaction with the row, its <c>employee_hr_facts_audit</c>
/// row and the outbox append (ADR-018 D3). Nothing emits it yet; its emitter is TASK-14836, and the
/// emission described is the intended one. Every nullable member is NON-required (S66 round-trip
/// lesson).
/// </summary>
public sealed class EmployeeHrFactsChanged : DomainEventBase
{
    public override string EventType => "EmployeeHrFactsChanged";

    public required string EmployeeId { get; init; }
    public required Guid FactsId { get; init; }

    /// <summary>CREATED, UPDATED or SUPERSEDED.</summary>
    public required string Action { get; init; }

    public string? OvertimeRegime { get; init; }
    public bool? OnFlexScheme { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }

    public Guid? PrecedingFactsId { get; init; }
    public long? VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
