namespace StatsTid.SharedKernel.Models;

public sealed class OvertimeBalance
{
    public required Guid BalanceId { get; init; }
    public required string EmployeeId { get; init; }
    public required string AgreementCode { get; init; }
    public required int PeriodYear { get; init; }
    public decimal Accumulated { get; init; }
    public decimal PaidOut { get; init; }
    public decimal AfspadseringUsed { get; init; }
    public decimal Remaining => Accumulated - PaidOut - AfspadseringUsed;
    public string CompensationModel { get; init; } = "UDBETALING";
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
