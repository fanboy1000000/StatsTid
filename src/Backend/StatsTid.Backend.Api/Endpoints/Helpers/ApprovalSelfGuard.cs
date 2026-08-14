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
/// <para><b>Identity model.</b> <see cref="ActorContext.ActorId"/> and
/// <see cref="ApprovalPeriod.EmployeeId"/> are the same user/employee id space, so a self-decision
/// is a string-equal id (Ordinal, mirroring the <c>SettlementReversalService</c> precedent). The
/// null/empty short-circuit keeps an unauthenticated / id-less actor from ever matching a period
/// that (in a corrupt row) also lacked an employee id.</para>
/// </summary>
internal static class ApprovalSelfGuard
{
    /// <summary>
    /// Returns <c>true</c> iff <paramref name="actor"/> is acting on their OWN period — i.e. the
    /// actor id is present AND string-equal (Ordinal) to the period's employee id. A <c>true</c>
    /// result at a manager-decision site MUST deny (403): a self-decision violates SoD.
    /// </summary>
    public static bool IsSelf(ActorContext actor, ApprovalPeriod period) =>
        !string.IsNullOrEmpty(actor.ActorId)
        && string.Equals(actor.ActorId, period.EmployeeId, StringComparison.Ordinal);
}
