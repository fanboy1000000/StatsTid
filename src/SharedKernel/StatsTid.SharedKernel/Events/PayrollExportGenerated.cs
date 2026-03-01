namespace StatsTid.SharedKernel.Events;

public sealed class PayrollExportGenerated : DomainEventBase
{
    public override string EventType => "PayrollExportGenerated";

    public required string EmployeeId { get; init; }
    public required string ExportId { get; init; }
    public required string WageType { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Hours { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string OkVersion { get; init; }
}
