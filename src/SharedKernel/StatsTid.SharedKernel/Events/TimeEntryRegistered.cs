namespace StatsTid.SharedKernel.Events;

public sealed class TimeEntryRegistered : DomainEventBase
{
    public override string EventType => "TimeEntryRegistered";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Hours { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public string? TaskId { get; init; }
    public string? ActivityType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public bool VoluntaryUnsocialHours { get; init; }

    /// <summary>
    /// Which kind of write produced this entry (S145 / A0, QUAL-192): <c>SKEMA</c> for a Skema
    /// save, which REPLACES the cell's earlier row; null means <c>TIME_POST</c>, an additive
    /// registration (<c>POST /api/time-entries</c>). Nullable and NOT required, so every
    /// pre-S145 serialized event keeps deserializing to null, which is exactly <c>TIME_POST</c>:
    /// events stay append-only and the old ones stay additive. Resolve with
    /// <see cref="Models.HoursSource.Resolve(string?)"/>; the projection stores the resolved value.
    /// </summary>
    public string? Source { get; init; }
}
