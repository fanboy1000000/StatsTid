using StatsTid.Infrastructure;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14512 (R26) — the production <see cref="IPriorPeriodApprovalReader"/>: a thin adapter
/// over <see cref="ApprovalPeriodRepository.GetByEmployeeAndPeriodAsync(string, DateOnly, DateOnly, CancellationToken)"/>,
/// the repository the Payroll host already registers for <c>/calculate-and-export</c>'s own approval
/// guard. It reads exactly one period by its natural key and returns its status, or <c>null</c> when
/// there is no row. It adds no rule of its own: what counts as approved (exactly <c>APPROVED</c>;
/// <c>EMPLOYEE_APPROVED</c> and a missing row do not) is decided by the calculation service, in one
/// place.
///
/// <para>
/// It is a plain read outside any transaction. The export path re-reads the previous month under a row
/// lock inside its own transaction (TASK-14512 Do 7, <c>PayrollExportService</c>), which is what makes
/// the decision safe against a concurrent reopen; this read is the early, cheap refusal.
/// </para>
/// </summary>
public sealed class ApprovalPeriodPriorPeriodReader : IPriorPeriodApprovalReader
{
    private readonly ApprovalPeriodRepository _approvalPeriods;

    public ApprovalPeriodPriorPeriodReader(ApprovalPeriodRepository approvalPeriods)
    {
        ArgumentNullException.ThrowIfNull(approvalPeriods);
        _approvalPeriods = approvalPeriods;
    }

    /// <inheritdoc />
    public async Task<string?> GetApprovalStatusAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeId);

        var period = await _approvalPeriods.GetByEmployeeAndPeriodAsync(employeeId, periodStart, periodEnd, ct);
        return period?.Status;
    }
}
