namespace StatsTid.SharedKernel.Events;

public sealed class PeriodCalculationCompleted : DomainEventBase
{
    public override string EventType => "PeriodCalculationCompleted";

    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required int RuleCount { get; init; }
    public required int ExportLineCount { get; init; }
    public required decimal TotalHours { get; init; }
}
