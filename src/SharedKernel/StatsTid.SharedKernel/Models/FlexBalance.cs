namespace StatsTid.SharedKernel.Models;

public sealed class FlexBalance
{
    public required decimal BalanceHours { get; init; }
    public required decimal CarryoverFromPrevious { get; init; }
    public required decimal EarnedThisPeriod { get; init; }
    public required decimal UsedThisPeriod { get; init; }
    public required decimal MaxBalance { get; init; }
}
