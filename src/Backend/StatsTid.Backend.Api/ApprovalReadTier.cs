using StatsTid.Auth;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api;

/// <summary>
/// S128 / TASK-12804 (RES-002) — THE single spelling of the S124/TASK-12405 actor-TIER decision
/// behind the manager-visibility rule, lifted VERBATIM out of <c>SkemaEndpoints</c>' month GET
/// (where it lived as the inline <c>leaderTierRead</c> flag) so the sibling READ endpoints can gate
/// on the SAME tiering instead of hand-copying it — exactly the drift RES-002 warns about, and the
/// third instance of this lift pattern (<see cref="ApprovalVisibility"/> = the read-side status
/// predicate, S124; <see cref="ApprovalPeriodSaveLock"/> = the write-side lock, S128/TASK-12803).
///
/// <para><b>The tiers (S124 / TASK-12403 + TASK-12405; re-affirmed by S128 owner ruling R1):</b>
/// <list type="bullet">
///   <item><description><b>SELF</b> — an actor reading their OWN month is never month-gated
///   (the TASK-12404 self-exemption; a self-read is never an escalation).</description></item>
///   <item><description><b>HR-OR-ABOVE</b> (a covering scope at the LocalHR floor) — the CORRECTIVE
///   tier. HR/Admin may CORRECT an employee's month (TASK-12404 write floor), and you cannot
///   correct what you cannot read, so this tier is deliberately not month-gated.</description></item>
///   <item><description><b>LEADER</b> — everyone else among the already-authorized (below-HR
///   covering scope, or the designated-approver / unit-leader edge): allowed ONLY for a month the
///   employee has SENT (<see cref="ApprovalVisibility.IsSubmittedToManager"/>; null/no-row and
///   REJECTED/DRAFT are all withheld — fail-closed).</description></item>
/// </list></para>
///
/// <para><b>NOT an access-control boundary (owner ruling R5, NARROW-ONLY).</b> This member decides
/// only who among an endpoint's ALREADY-AUTHORIZED callers is exempt from the month withholding.
/// Every endpoint's existing access population is decided BEFORE this is consulted and is untouched
/// by it: the tier may only SUBTRACT (leader-tier + not-sent ⇒ 403), never admit. In particular the
/// Employee-role/self short-circuit here is NOT an admission — an Employee-role actor reading
/// someone ELSE is rejected by every caller's existing auth before the gate is reached.</para>
/// </summary>
internal static class ApprovalReadTier
{
    /// <summary>
    /// True when the actor reads <paramref name="employeeId"/>'s month as a LEADER — i.e. the
    /// month-gated tier. False for self and for HR-or-above covering scope (the exempt tiers).
    /// Callers must have ALREADY authorized the actor for this employee (R5: this member only
    /// classifies the admitted, it never admits). Same checks, same order, same
    /// <see cref="OrgScopeValidator"/> call as the original SkemaEndpoints inline flag.
    /// </summary>
    internal static async Task<bool> IsLeaderTierReadAsync(
        OrgScopeValidator scopeValidator,
        ActorContext actor,
        string employeeId,
        CancellationToken ct)
    {
        // SELF (and the Employee role, which every caller's existing auth restricts to self):
        // never month-gated. NOTE for a non-Employee actor this self short-circuit skips scope
        // resolution outright — the deliberate S124 self-read behavior documented at the
        // original SkemaEndpoints site.
        if (actor.ActorRole == StatsTidRoles.Employee || employeeId == actor.ActorId)
            return false;

        // HR-OR-ABOVE covering scope (the LocalHR role floor) → the corrective tier, exempt.
        // Anything below the floor — or an actor admitted purely via the designated-approver /
        // unit-leader edge, whose org-scope does not cover the employee at all — is LEADER tier.
        var hrFloored = await scopeValidator.ValidateEmployeeAccessAsync(
            actor, employeeId, StatsTidRoles.LocalHR, ct);
        return !hrFloored.Allowed;
    }

    /// <summary>
    /// S147 / TASK-14713 (QUAL-230 T5b; refinement BC-P15) — the TERMINATED-INCLUSIVE twin of
    /// <see cref="IsLeaderTierReadAsync"/>, used ONLY by the Skema month view (an R9c entry).
    ///
    /// <para><b>Why a second method and not a change to the first.</b> From the sixth day after a
    /// leaver's last working day the leaver job switches them off, and only HR may finish their final
    /// month (owner ruling OQ-2). HR opens that month in the Skema view. The shared method above
    /// classifies HR through the ACTIVE-only validator, so for a switched-off employee HR fell to the
    /// leader tier and was refused. Changing the shared method would also widen the three other reads
    /// that call it (the allocation breakdown, the balance summary and compliance), which are outside
    /// this ruling; so the widening lives here, for the one view that needs it.</para>
    ///
    /// <para><b>The rule.</b> Identical to <see cref="IsLeaderTierReadAsync"/> — the same self
    /// short-circuit in the same place, the same LocalHR floor — except that the HR check runs through
    /// <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(ActorContext, string, string?, CancellationToken)"/>.
    /// For an ACTIVE employee the two validators give the same answer under this floor, so the tiers
    /// are unchanged. For a switched-off employee only a scope that is itself LocalHR or above admits
    /// (a mixed-role token cannot borrow a leader scope); anyone else stays leader tier and meets the
    /// view's leader-tier access check, which still refuses a switched-off employee.</para>
    ///
    /// <para><b>The return says which tier admitted.</b> <c>LeaderTier</c> carries the shared
    /// method's meaning. <c>HrTier</c> is true only when the HR check itself admitted the actor —
    /// never for the self short-circuit — and is the one tier for which the view may resolve a
    /// switched-off employee. A switched-off person reading their own month keeps today's answer.</para>
    ///
    /// <para>Like the shared method this only CLASSIFIES; the view's own checks decide access
    /// (owner ruling R5).</para>
    /// </summary>
    internal static async Task<(bool LeaderTier, bool HrTier)> ClassifyMonthViewTierIncludingTerminatedAsync(
        OrgScopeValidator scopeValidator,
        ActorContext actor,
        string employeeId,
        CancellationToken ct)
    {
        // SELF (and the Employee role): never month-gated, never the HR tier. The same short-circuit,
        // in the same place, as IsLeaderTierReadAsync.
        if (actor.ActorRole == StatsTidRoles.Employee || employeeId == actor.ActorId)
            return (LeaderTier: false, HrTier: false);

        // HR-OR-ABOVE covering scope at the LocalHR floor, switched-off employees included.
        var hrFloored = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
            actor, employeeId, StatsTidRoles.LocalHR, ct);
        return (LeaderTier: !hrFloored.Allowed, HrTier: hrFloored.Allowed);
    }

    /// <summary>
    /// THE single construction site for the leader-tier month-withholding 403, so every gated read
    /// (the Skema month grid + the RES-002 siblings) returns byte-identically the same status code,
    /// body shape and message — the wire shape S124/TASK-12405 minted on the Skema month GET.
    /// Callers must have established leader tier + <c>!IsSubmittedToManager</c>.
    /// </summary>
    internal static IResult MonthNotSubmittedForbidden()
        => Results.Json(new
        {
            error = "Access denied",
            reason = "The month has not been submitted for approval",
        }, statusCode: 403);
}
