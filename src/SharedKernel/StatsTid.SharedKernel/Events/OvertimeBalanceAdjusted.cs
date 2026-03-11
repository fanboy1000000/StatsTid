namespace StatsTid.SharedKernel.Events;

public sealed class OvertimeBalanceAdjusted : DomainEventBase
{
    public override string EventType => "OvertimeBalanceAdjusted";
    public required string EmployeeId { get; init; }
    public required string AdjustmentType { get; init; } // ACCUMULATED, PAID_OUT, AFSPADSERING
    public required decimal DeltaHours { get; init; }
    public required decimal NewAccumulated { get; init; }
    public required decimal NewPaidOut { get; init; }
    public required decimal NewAfspadseringUsed { get; init; }
    public required decimal NewRemaining { get; init; }
    public required int PeriodYear { get; init; }
    public string? Reason { get; init; }
}
