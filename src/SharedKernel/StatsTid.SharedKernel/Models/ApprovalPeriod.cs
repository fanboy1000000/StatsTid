namespace StatsTid.SharedKernel.Models;

public sealed class ApprovalPeriod
{
    public required Guid PeriodId { get; init; }
    public required string EmployeeId { get; init; }
    public required string OrgId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string PeriodType { get; init; }  // WEEKLY, MONTHLY
    public required string Status { get; init; }  // DRAFT, SUBMITTED, APPROVED, REJECTED
    public DateTime? SubmittedAt { get; init; }
    public string? SubmittedBy { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? RejectionReason { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
