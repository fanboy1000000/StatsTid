namespace StatsTid.SharedKernel.Events;

public sealed class OvertimeCompensationApplied : DomainEventBase
{
    public override string EventType => "OvertimeCompensationApplied";
    public required string EmployeeId { get; init; }
    public required string CompensationType { get; init; } // AFSPADSERING or UDBETALING
    public required string OvertimeType { get; init; } // OVERTIME_50, OVERTIME_100, MERARBEJDE
    public required decimal Hours { get; init; }
    public required decimal ConvertedHours { get; init; } // For afspadsering: hours * rate
    public required int PeriodYear { get; init; }
}
