namespace StatsTid.SharedKernel.Events;

public sealed class ReportingLineSuperseded : DomainEventBase
{
    public override string EventType => "ReportingLineSuperseded";

    public required Guid ReportingLineId { get; init; }
    public required string EmployeeId { get; init; }
    public required string PreviousManagerId { get; init; }
    public string? NewManagerId { get; init; }
    public required string TreeRootOrgId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required DateOnly EffectiveTo { get; init; }
    public required long RowVersion { get; init; }
}
