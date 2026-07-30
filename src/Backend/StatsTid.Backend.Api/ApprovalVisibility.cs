namespace StatsTid.Backend.Api;

/// <summary>
/// S124 / TASK-12405 — the ONE definition of "the employee has sent this month to their manager".
///
/// <para>Two surfaces depend on this predicate and they MUST agree, or the manager-visibility rule
/// re-opens a back door in either direction:
/// <list type="bullet">
///   <item><description>the team-overview row WITHHOLDS its month-derived figures unless the period
///   was sent (<c>ApprovalEndpoints</c>, TASK-12402);</description></item>
///   <item><description>the leader tier of <c>GET /api/skema/{employeeId}/month</c> refuses the full
///   day-by-day grid unless the period was sent (<c>SkemaEndpoints</c>, TASK-12405).</description></item>
/// </list>
/// A row whose figures are withheld must not have its grid readable, and vice versa. Step-7a's
/// internal lens flagged that these had been TWO hand-copied literal lists bound only by a comment
/// claiming they could not drift — exactly the drift RES-002 says to avoid ("lift that predicate
/// into a shared gate rather than re-deriving it per endpoint"). This is that lift.</para>
///
/// <para>FAIL-CLOSED by construction: a null status (no period exists, so nothing was ever sent) and
/// any future status not named here are both NOT sent. <c>DRAFT</c> is deliberately absent — and
/// since the create path transitions to <c>SUBMITTED</c> inside the same transaction, every
/// persistent <c>DRAFT</c> is a REOPENED month, so this is the reopen disposition too.</para>
/// </summary>
internal static class ApprovalVisibility
{
    /// <summary>True when the employee has sent the period to their manager — i.e. the manager may
    /// see its content. Statuses are the <c>approval_periods.status</c> CHECK set (init.sql:1103)
    /// minus <c>DRAFT</c>.</summary>
    internal static bool IsSubmittedToManager(string? status) =>
        status is "SUBMITTED" or "EMPLOYEE_APPROVED" or "APPROVED" or "REJECTED";
}
