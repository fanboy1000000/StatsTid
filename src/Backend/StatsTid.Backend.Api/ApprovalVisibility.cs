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
/// persistent <c>DRAFT</c> is a REOPENED month, so this is the reopen disposition too.
/// <c>REJECTED</c> is deliberately absent as well; that one reverses a prior decision, so it is
/// argued in full below rather than left to look like an oversight.</para>
///
/// <para><b>S127 / TASK-12706 — owner ruling R1 REMOVED <c>REJECTED</c> from this set.</b> S124 put it
/// here on purpose, and its reasoning is recorded in code in ApprovalEndpoints' team-overview row
/// construction (the comment block above the <c>submittedToManager</c> assignment — located by
/// symbol; the line citation this doc once carried had drifted twice):
/// <i>"REJECTED counts as submitted: the employee DID send it, the leader decided on these very
/// numbers, and hiding them afterwards would erase the basis of that decision."</i> That is sound as
/// far as it goes and R1 does not call it wrong — R1 answers it with a rule that outranks it: a
/// manager never sees a month the employee could not certify, and there is no in-progress
/// visibility. The gap in the S124 argument is that a REJECTED month does not stay frozen at "these
/// very numbers". It is editable again — only <c>EMPLOYEE_APPROVED</c> and <c>APPROVED</c> lock a
/// period — so while the employee repairs it the manager was watching its contents CHANGE: the
/// team-overview released recomputed figures and the Skema leader tier served the full day-by-day
/// grid. That live in-progress state is exactly what R1 rules out, and it is not the thing S124 was
/// protecting. What S124 actually cared about survives by other means: the team-overview withholds
/// only the month-derived figures — the ones moving under the leader — while status, submittedAt,
/// decisionAt and rejectionReason are all still served, so the leader keeps WHY they rejected. The
/// month becomes visible again the moment the employee re-sends it.</para>
///
/// <para><b>NOT an access-control boundary on its own (owner ruling R5).</b> This predicate decides
/// only WHETHER a month was sent; WHO it is withheld from is the sibling tier decision
/// (<see cref="ApprovalReadTier"/>). <b>S128 / TASK-12804 (the RES-002 slice)</b> extended
/// enforcement beyond the two display surfaces to the three year+month sibling READS —
/// <c>allocation-breakdown</c>, <c>compliance /period</c> and <c>balance /summary</c> — each gating
/// its leader-tier callers through this member (403, the Skema shape). <c>RES-002</c> still records
/// the raw-time-entry and absence siblings (and the non-month-keyed reads) as unenforced; closing
/// those remains the open follow-up. Do not cite this member as evidence that a rejected month's
/// figures are unreachable through THOSE remaining routes.</para>
/// </summary>
internal static class ApprovalVisibility
{
    /// <summary>True when the employee has sent the period to their manager — i.e. the manager may
    /// see its content. Statuses are the <c>approval_periods.status</c> CHECK set
    /// (<c>docker/postgres/init.sql:1118-1119</c>) minus <c>DRAFT</c> and <c>REJECTED</c>.</summary>
    internal static bool IsSubmittedToManager(string? status) =>
        status is "SUBMITTED" or "EMPLOYEE_APPROVED" or "APPROVED";
}
