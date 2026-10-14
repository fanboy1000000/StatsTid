namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14512 (rulings R5(b), R16, R21 as amended by R26) — payroll's one question about the
/// approval workflow: "what is the approval status of exactly this pay period?". The calculation
/// service asks it about the PREVIOUS month (M−1) before it calculates a month whose opening
/// Monday–Sunday week holds EMPLOYED days of M−1.
///
/// <para>
/// <b>Why a month waits for the previous one (plain language).</b> Since S145 a week that crosses a
/// month boundary is settled by the month holding its Sunday (owner ruling Q3 = F1). October's first
/// week (Monday 28 September – Sunday 4 October) therefore counts September's last days toward its
/// overtime. If September's hours could still change, October would be paid on hours nobody has
/// signed off, so October is calculated only once September is APPROVED.
/// </para>
///
/// <para>
/// <b>Why an interface and not the repository (R26).</b> The production answer comes from
/// <c>approval_periods</c> through <see cref="StatsTid.Infrastructure.ApprovalPeriodRepository"/>, a sealed
/// concrete class over a database connection. Many calculation fixtures run with no database, so the
/// calculation service depends on this small payroll-local seam instead: production registers
/// <see cref="ApprovalPeriodPriorPeriodReader"/>, and a fixture that is not about the check injects a
/// fake answering <c>APPROVED</c>, saying so in one comment. The dependency is optional in the
/// constructor but never silent: a calculation that needs the answer and has no reader throws,
/// naming the missing registration (R21).
/// </para>
/// </summary>
public interface IPriorPeriodApprovalReader
{
    /// <summary>
    /// The <c>approval_periods.status</c> of exactly the period
    /// (<paramref name="employeeId"/>, <paramref name="periodStart"/>, <paramref name="periodEnd"/>) —
    /// for example <c>APPROVED</c>, <c>EMPLOYEE_APPROVED</c> or <c>DRAFT</c> — or <c>null</c> when no such
    /// row exists. A month exported through the raw routes has no row (they write no approval period,
    /// R16), so it answers <c>null</c>, which is not approved.
    /// </summary>
    Task<string?> GetApprovalStatusAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}
