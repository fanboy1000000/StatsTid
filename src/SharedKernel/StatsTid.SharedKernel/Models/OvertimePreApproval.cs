namespace StatsTid.SharedKernel.Models;

public sealed class OvertimePreApproval
{
    public required Guid Id { get; init; }
    public required string EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal MaxHours { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public required string Status { get; init; } // PENDING, APPROVED, REJECTED
    public string? Reason { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
