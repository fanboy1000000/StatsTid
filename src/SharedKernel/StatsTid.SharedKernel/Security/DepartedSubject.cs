using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Security;

/// <summary>
/// S147 / TASK-14703 (QUAL-230 T5a; rulings R-30, R-31, P-1) — the departed-self rule: <b>a person who
/// has left cannot change their own record, whatever their role.</b> The owner's OQ-2: a leaver's own
/// access ends the day after their last working day. (A manager keeps reaching a leaver for a short
/// grace after that day, and HR keeps acting on leavers; neither is this rule's business — it refuses
/// only the leaver acting on <i>themselves</i>.)
///
/// <para><b>The rule (<see cref="IsDeparted(User, DateOnly)"/>).</b> A subject is departed when their
/// record is deactivated (<c>is_active = FALSE</c>) OR their recorded last day of employment lies
/// before <c>today</c>, the Copenhagen business day the caller read once
/// (<c>CopenhagenBusinessDate.Today</c>, PAT-028). A NULL end date is open-ended employment
/// (ADR-040 D2) and never makes anyone departed on its own.</para>
///
/// <para><b>Why <c>&lt;</c> and not <c>&lt;=</c>.</b> <c>employment_end_date</c> is the LAST DAY
/// EMPLOYED, inclusive (ADR-033 as implemented by S70 R1; ADR-040 D1). On that day the person still
/// works and may still register, send and correct their own month; they are departed only from the
/// next day on. Comparing with <c>&lt;=</c> would cut a leaver off on their own last working day.</para>
///
/// <para><b>The identity arm is the caller's.</b> This predicate looks only at the SUBJECT's state;
/// it never asks who is acting. It refuses an actor only where the caller has first established
/// that the actor IS the subject (<see cref="IsSelf"/>: an Ordinal id match; a null or empty actor id
/// never matches). It has no role arm on purpose: during the grace a LocalLeader must still reach a
/// leaver who is someone else (OQ-2; AC-230-1), and HR must still act on leavers. Where the rule
/// runs:</para>
/// <list type="bullet">
///   <item><description>inside <c>OrgScopeValidator</c>'s two employee overloads, right after the
///   target read (R-30), so every route that reaches an employee through them refuses a departed
///   self with no handler change;</description></item>
///   <item><description>inline at the thirteen write routes that reach an employee another way, and as
///   one derived check in the manager-removal route (R-31, P-1; the S147 census, F4). Each answers
///   <c>403 { error = "Access denied", reason = <see cref="DenyReason"/> }</c> BEFORE any write and logs
///   a WARNING naming <see cref="ClauseName"/> and the route.</description></item>
/// </list>
///
/// <para><b>The deny-only terminated-inclusive reads (R9c inventory addendum).</b> Where an inline
/// route already reads its subject active-only, it reuses that read (a deactivated self already
/// answers 404 there). Where it reads no subject, it reads the actor's own record ONLY when the
/// identity matches, with the terminated-inclusive read (<c>UserRepository.GetByIdIncludingTerminatedAsync</c>),
/// so a deactivated self is refused too. These reads are <b>deny-only</b>: their result can only
/// refuse the request, never admit anyone or widen what the route may touch, and a subject that
/// cannot be read at all is not refused here (the route's own outcome applies). The sites:</para>
/// <list type="bullet">
///   <item><description><c>POST /api/admin/roles/revoke</c> — the assignment's user, read before the
///   transaction;</description></item>
///   <item><description><c>POST /api/admin/reporting-lines/import</c> — the actor, when any row names
///   them as the employee;</description></item>
///   <item><description><c>POST /api/admin/reporting-lines/{employeeId}/remove</c> — the actor, in the
///   transaction (the <c>(conn, tx)</c> overload), when they are an incoming report of the removed
///   person;</description></item>
///   <item><description><c>POST</c> and <c>DELETE /api/reporting-lines/delegate</c> — the
///   actor;</description></item>
///   <item><description><c>DELETE /api/admin/reporting-lines/{managerId}/vikar</c> — the actor, when
///   they are the manager;</description></item>
///   <item><description><c>POST</c> and <c>DELETE /api/admin/units/{id}/leaders…</c> — the actor, when
///   they are the designee;</description></item>
///   <item><description><c>DELETE /api/admin/units/{id}</c> — one in-transaction <c>SELECT</c> of the
///   actor's own membership and state (no <c>is_active</c> filter, because the delete re-homes inactive
///   members too), through <see cref="IsDeparted(bool, DateOnly?, DateOnly)"/>;</description></item>
///   <item><description><c>POST /api/payroll/recalculate</c> (Payroll host) — the actor, when the
///   correction names them.</description></item>
/// </list>
///
/// <para><b>Date-free by construction (ADR-040 D3, D7).</b> <see cref="DenyReason"/> names no date,
/// so the refusal cannot leak an employment date or be probed for one.</para>
/// </summary>
public static class DepartedSubject
{
    /// <summary>
    /// The date-free refusal sentence every departed-self 403 carries, in the validator's
    /// <c>reason</c> and in each inline route's body. Never extend it with a date (ADR-040 D7).
    /// </summary>
    public const string DenyReason = "A departed employee cannot act on their own record";

    /// <summary>
    /// The clause's name in server logs ("departed-self"), so a refusal is traceable to this rule.
    /// Logs only; never part of a response body.
    /// </summary>
    public const string ClauseName = "departed-self";

    /// <summary>
    /// The identity arm the callers apply first: <c>true</c> iff <paramref name="actorId"/> is present
    /// and string-equal (Ordinal) to <paramref name="subjectId"/>. A null or empty actor id never
    /// matches, so an id-less token is never treated as anybody's self.
    /// </summary>
    public static bool IsSelf(string? actorId, string? subjectId) =>
        !string.IsNullOrEmpty(actorId)
        && string.Equals(actorId, subjectId, StringComparison.Ordinal);

    /// <summary>
    /// <c>true</c> iff <paramref name="subject"/> is deactivated, or their recorded last day of
    /// employment lies before <paramref name="today"/> (the last day itself is still employed). See the
    /// type remarks for the rule, the <c>&lt;</c> and the caller's identity arm.
    /// </summary>
    /// <param name="subject">The subject's record, as the caller read it.</param>
    /// <param name="today">The Copenhagen business day, read once by the caller (PAT-028).</param>
    /// <exception cref="ArgumentNullException"><paramref name="subject"/> is null.</exception>
    public static bool IsDeparted(User subject, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return IsDeparted(subject.IsActive, subject.EmploymentEndDate, today);
    }

    /// <summary>
    /// The same rule over the two columns it reads, for a caller that selected them directly rather
    /// than a whole <see cref="User"/> (the unit delete's one in-transaction <c>SELECT</c>).
    /// <see cref="IsDeparted(User, DateOnly)"/> delegates here, so the rule has one text.
    /// </summary>
    /// <param name="isActive">The subject's <c>users.is_active</c>.</param>
    /// <param name="employmentEndDate">The subject's <c>users.employment_end_date</c> (last day employed; NULL = open-ended).</param>
    /// <param name="today">The Copenhagen business day, read once by the caller (PAT-028).</param>
    public static bool IsDeparted(bool isActive, DateOnly? employmentEndDate, DateOnly today) =>
        !isActive || (employmentEndDate is { } d && d < today);
}
