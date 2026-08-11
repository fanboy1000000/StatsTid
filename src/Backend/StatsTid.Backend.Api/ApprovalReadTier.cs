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
