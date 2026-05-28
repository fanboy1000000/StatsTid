using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Full self-recorded work-time state for one (employee, date): the list of work
/// intervals plus a manual daily-hours scalar. Re-saving a day emits a NEW superseding
/// event; latest-wins is resolved by the downstream projection (not defined here).
/// </summary>
public sealed class WorkTimeRegistered : DomainEventBase
{
    public override string EventType => "WorkTimeRegistered";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<WorkInterval> Intervals { get; init; }
    public required decimal ManualHours { get; init; }
}
