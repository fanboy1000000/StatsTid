namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted to mark a day's merarbejde hours as discretionary under the
/// ADR-024 D2 tri-state flag (the third state alongside the implicit
/// "is merarbejde" / "is not merarbejde" boolean dimension at the
/// role-config level). Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class MerarbejdeDiscretionary : DomainEventBase
{
    public override string EventType => "MerarbejdeDiscretionary";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal MerarbejdeHours { get; init; }
    public required string EmploymentCategory { get; init; }
}
