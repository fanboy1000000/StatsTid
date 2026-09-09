using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S140 / TASK-14004 — the two INSTITUTIONAL DEADLINES of a monthly time-registration period.
///
/// <para><b>Plain language.</b> Every month an employee registers gets two dates when the period
/// row is created: the employee should have SENT the month by month-end + 2 days, and their leader
/// should have APPROVED it by month-end + 5. Those two offsets were hard-coded in two unrelated
/// endpoint files (the period-creation read and the send flow), stored on the row, and then read by
/// nothing — so the organisation page's "efter frist" (past deadline) tile counted every pending
/// month instead of the late ones. The owner ratified +2 / +5 on 2026-09-08 as PROVISIONAL
/// institutional defaults (SYSTEM_TARGET §G lists cutoff dates as per-institution configuration
/// that does not exist yet), and S140 makes them ONE named source of truth that the two creation
/// sites and the new HR follow-up reads all consume.</para>
///
/// <para>This fact is the RED-first pin for that source of truth: it fails to COMPILE before
/// <see cref="InstitutionalDeadlines"/> exists, and it fails to PASS if either offset is changed
/// without a deliberate decision (which is the point — the values are provisional, so the change
/// must be visible). Both derivations are pinned in one fact because they are one rule: the two
/// dates a month's end implies.</para>
/// </summary>
public class InstitutionalDeadlinesTests
{
    /// <summary>
    /// Pins BOTH derivations against a real month end. February 2026 ends on the 28th, so the
    /// employee deadline is 2 March (+2, crossing the month boundary — the case a naive
    /// "day-of-month + 2" would get wrong) and the manager deadline is 5 March (+5). The named
    /// constants are pinned alongside the helpers so a caller reading
    /// <see cref="InstitutionalDeadlines.SubmitDays"/> directly gets the same answer as one
    /// calling <see cref="InstitutionalDeadlines.ForMonthEnd"/>.
    /// </summary>
    [Fact]
    public void ForMonthEnd_DerivesRatifiedSubmitAndApproveDeadlines()
    {
        var monthEnd = new DateOnly(2026, 2, 28);

        var (employeeDeadline, managerDeadline) = InstitutionalDeadlines.ForMonthEnd(monthEnd);

        // The ratified provisional defaults: month-end + 2 (employee sends) and + 5 (leader approves).
        Assert.Equal(2, InstitutionalDeadlines.SubmitDays);
        Assert.Equal(5, InstitutionalDeadlines.ApproveDays);

        Assert.Equal(new DateOnly(2026, 3, 2), employeeDeadline);
        Assert.Equal(new DateOnly(2026, 3, 5), managerDeadline);

        // The single-deadline helpers are the same rule, not a second copy of it.
        Assert.Equal(employeeDeadline, InstitutionalDeadlines.EmployeeDeadlineFor(monthEnd));
        Assert.Equal(managerDeadline, InstitutionalDeadlines.ManagerDeadlineFor(monthEnd));
    }
}
