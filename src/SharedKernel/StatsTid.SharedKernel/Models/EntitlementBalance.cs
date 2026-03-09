namespace StatsTid.SharedKernel.Models;

public sealed class EntitlementBalance
{
    public required Guid BalanceId { get; init; }
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }
    public required decimal TotalQuota { get; init; }
    public decimal Used { get; init; }
    public decimal Planned { get; init; }
    public decimal CarryoverIn { get; init; }
    public decimal Remaining => TotalQuota + CarryoverIn - Used - Planned;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
