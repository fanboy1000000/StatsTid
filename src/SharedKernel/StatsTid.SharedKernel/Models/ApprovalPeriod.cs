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
    public DateTime? EmployeeApprovedAt { get; init; }
    public string? EmployeeApprovedBy { get; init; }
    public DateOnly? EmployeeDeadline { get; init; }
    public DateOnly? ManagerDeadline { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    /// <summary>
    /// Sprint 49 (ADR-027 D5): who SHOULD have approved per reporting-line resolution.
    /// NULL for pre-reporting-line periods (approval_method = PRE_REPORTING_LINE).
    /// </summary>
    public string? DesignatedApproverId { get; init; }
    /// <summary>
    /// Sprint 49 (ADR-027 D5): how the approver was determined. One of DESIGNATED_MANAGER,
    /// ORG_SCOPE_FALLBACK, ACTING_MANAGER, PRE_REPORTING_LINE, or — S105 / ADR-038 D4 — UNIT_LEADER
    /// (a designated leader of the employee's own unit approved) / UNIT_LEADER_VIKAR (an active vikar
    /// of such a unit-leader approved). Mirrors the approval_periods.approval_method CHECK in init.sql.
    /// </summary>
    public string? ApprovalMethod { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
