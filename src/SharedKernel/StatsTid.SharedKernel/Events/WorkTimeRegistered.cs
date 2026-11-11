using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Full self-recorded work-time state for one (employee, date): the list of work
/// intervals. The clock periods are a day's only work-time record, so every worked
/// hour has clock times (ADR-043 D10, D-2.4; ADR-028 D1). Re-saving a day emits a NEW
/// superseding event; latest-wins is resolved by the downstream projection (not defined here).
/// </summary>
public sealed class WorkTimeRegistered : DomainEventBase
{
    public override string EventType => "WorkTimeRegistered";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<WorkInterval> Intervals { get; init; }
}
