using StatsTid.Auth;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// RES-003 / SEC-009 — the shared segregation-of-duties (SoD) self-check for the MANAGER-side
/// approval decisions (approve / reject / reopen-of-<c>APPROVED</c>). The rule it encodes:
/// <b>nobody performs a manager DECISION on their own period.</b>
///
/// <para><b>Why a helper and not three inline checks.</b> RES-003 is a RECURRING defect class
/// precisely because the SoD rule had no single enforcement point — it was re-stated by hand at
/// each authorization path and every new path was a fresh chance to omit it (failing OPEN). The
/// structural fix has two parts: the fail-CLOSED choke point inside
/// <see cref="StatsTid.Infrastructure.DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(string, string, System.DateOnly?, System.Threading.CancellationToken)"/>
/// (which every edge / unit-leader / vikar authority path funnels through), and THIS helper for the
/// one leg that bypasses that predicate — the org-scope / HR-Admin fallback, which is SEC-009's
/// exact path. One predicate, one place, applied at the three manager-decision endpoints.</para>
///
/// <para><b>S147 / SEC-056 (owner Q-A = (a)) — the overtime side workflow.</b> The same rule now
/// also covers the overtime decisions: approving or rejecting an overtime pre-approval, and
/// compensating (paying out / converting to time off) an overtime balance. Those rows carry an
/// employee id but are not <see cref="ApprovalPeriod"/>s, so the predicate is generalised to a bare
/// employee id (<see cref="IsSelf(ActorContext, string)"/>); the period overload delegates to it with
/// its behaviour unchanged. Creating a pre-approval for oneself stays allowed — a request decides
/// nothing, and someone else then approves it.</para>
///
/// <para><b>Identity model.</b> <see cref="ActorContext.ActorId"/>,
/// <see cref="ApprovalPeriod.EmployeeId"/> and the overtime rows' employee ids are the same
/// user/employee id space, so a self-decision is a string-equal id (Ordinal, mirroring the
/// <c>SettlementReversalService</c> precedent). The null/empty short-circuit keeps an
/// unauthenticated / id-less actor from ever matching a row that (in a corrupt row) also lacked an
/// employee id.</para>
/// </summary>
internal static class ApprovalSelfGuard
{
    /// <summary>
    /// S147 / SEC-056 — the log category the overtime decision endpoints (pre-approval approve /
    /// reject, compensate) stamp their self-denial WARNING under, so the denied-attempt signal is
    /// greppable in one place (the overtime sibling of <c>ApprovalEndpoints</c>' self-guard category).
    /// </summary>
    internal const string OvertimeLogCategory = "StatsTid.Backend.Api.Endpoints.OvertimeEndpoints.SelfGuard";

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="actor"/> is acting on THEMSELVES — i.e. the actor id
    /// is present AND string-equal (Ordinal) to <paramref name="employeeId"/>. A <c>true</c> result at
    /// a manager-decision site MUST deny (403): a self-decision violates SoD.
    /// </summary>
    public static bool IsSelf(ActorContext actor, string employeeId) =>
        !string.IsNullOrEmpty(actor.ActorId)
        && string.Equals(actor.ActorId, employeeId, StringComparison.Ordinal);

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="actor"/> is acting on their OWN period — the
    /// <see cref="IsSelf(ActorContext, string)"/> predicate applied to the period's employee id.
    /// A <c>true</c> result at a manager-decision site MUST deny (403): a self-decision violates SoD.
    /// </summary>
    public static bool IsSelf(ActorContext actor, ApprovalPeriod period) =>
        IsSelf(actor, period.EmployeeId);
}
