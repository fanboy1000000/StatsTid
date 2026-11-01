using StatsTid.Auth;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// S147 / TASK-14710 (QUAL-230 T5a point 3; rulings R-21, R-22, R-24; owner ruling OQ-2) — the
/// <b>self-service write floor</b>: one rule for every write an ordinary employee can make to their own
/// record, so that a leaver's still-valid login token cannot change anything of their own from the day
/// after their last working day, whatever their role.
///
/// <para><b>Why it exists.</b> A login token lives 480 minutes and cannot be revoked (SEC-003). Before
/// S147 the ADR-040 D3 floor refused a <i>deactivated</i> subject to an actor below LocalHR, and the
/// automatic deactivation landed on D+1, so a leaver's token stopped writing on D+1 as a side effect.
/// Under the owner's grace (OQ-2) the deactivation moves to D+6 so the leaver's MANAGER can still act
/// on the final month; the subject therefore stays active for five more days, and the D3 test alone
/// would let a token issued on D keep writing. This floor keys on the END DATE as well, so the
/// leaver's own access still ends on D+1.</para>
///
/// <para><b>The rule</b> (<see cref="RefusingClause(User, string?, string?, DateOnly)"/>): refuse when
/// the subject has departed (<see cref="DepartedSubject.IsDeparted(User, DateOnly)"/>: deactivated, or
/// the last day employed lies before <c>today</c>) <b>AND</b> either</para>
/// <list type="bullet">
///   <item><description>the actor's primary role is below LocalHR — ADR-040 D3's existing role arm,
///   now also covering the grace (log clause <see cref="BelowLocalHrClause"/>); or</description></item>
///   <item><description>the actor IS the subject (<see cref="DepartedSubject.IsSelf"/>, Ordinal) — the
///   identity arm (R-21): an HR, admin or leader who has left has no role to fall back on for their
///   OWN record (log clause <see cref="SelfClause"/>).</description></item>
/// </list>
/// <para>HR and admins acting on <i>someone else</i> who has left are untouched (the routine
/// final-month correction), and a subject who has not departed is never refused here.</para>
///
/// <para><b>Where it runs</b> — exactly the closed list of writes an Employee token can reach (R-24),
/// each after its own access check and subject read, with the handler's single Copenhagen date
/// (PAT-028): the Skema save (advisory pre-check and in-lock re-check), the time-entry POST (both),
/// the send command shared by send and employee-approve, the reopen's Employee arm, the row-preferences
/// PUT and the compensation-choice PUT. Every other write needs LeaderOrAbove or HROrAbove and reaches
/// a person's own record only through org scope, where TASK-14703's departed-self clause in
/// <c>OrgScopeValidator</c> applies. The two retired project-selection routes are gone (R-23).</para>
///
/// <para><b>The response.</b> 403 with ADR-040 D3's existing, date-free reason
/// (<see cref="DenyReason"/>), whichever arm fired — one contract across every write surface — so the
/// refusal cannot leak an employment date or be probed for one (ADR-040 D3, D7). The server log names
/// the arm that fired.</para>
/// </summary>
public static class DepartedSubjectWriteFloor
{
    /// <summary>
    /// ADR-040 D3's write-floor sentence, byte-identical to the string the registration writers and the
    /// send command carried before S147. Date-free; never extend it with a date (ADR-040 D7).
    /// </summary>
    public const string DenyReason = "Writes for a deactivated employee require LocalHR or above";

    /// <summary>The log category every refusal is written under, so the signal is greppable in one place.</summary>
    public const string LogCategory = "StatsTid.Backend.Api.Endpoints.DepartedSubjectWriteFloor";

    /// <summary>
    /// Log name of the role arm: the subject has departed and the actor's primary role is below LocalHR
    /// (ADR-040 D3, extended to the grace). Logs only; never part of a response body.
    /// </summary>
    public const string BelowLocalHrClause = "departed-subject-below-local-hr";

    /// <summary>
    /// Log name of the identity arm: the subject has departed and the actor is the subject (R-21) — the
    /// same clause name TASK-14703's validator logs (<see cref="DepartedSubject.ClauseName"/>).
    /// </summary>
    public const string SelfClause = DepartedSubject.ClauseName;

    /// <summary>
    /// The pure decision. Returns <c>null</c> when the write may proceed, else the name of the arm that
    /// refuses it (<see cref="BelowLocalHrClause"/> is checked first, then <see cref="SelfClause"/>, so an
    /// Employee acting on themselves logs the role arm — the one that has always refused them).
    /// </summary>
    /// <param name="subject">The subject's record, as the handler read it.</param>
    /// <param name="actorId">The acting user's id (token subject).</param>
    /// <param name="actorRole">The acting user's PRIMARY role, as the D3 floor has always read it.</param>
    /// <param name="today">The Copenhagen business day, read once by the handler (PAT-028).</param>
    /// <exception cref="ArgumentNullException"><paramref name="subject"/> is null.</exception>
    public static string? RefusingClause(User subject, string? actorId, string? actorRole, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (!DepartedSubject.IsDeparted(subject, today))
            return null;

        if (actorRole is null || !StatsTidRoles.IsAtLeast(actorRole, StatsTidRoles.LocalHR))
            return BelowLocalHrClause;

        if (DepartedSubject.IsSelf(actorId, subject.UserId))
            return SelfClause;

        return null;
    }

    /// <summary><see cref="RefusingClause(User, string?, string?, DateOnly)"/> for a request's actor.</summary>
    public static string? RefusingClause(User subject, ActorContext actor, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return RefusingClause(subject, actor.ActorId, actor.ActorRole, today);
    }

    /// <summary><c>true</c> iff the floor refuses the write (see <see cref="RefusingClause(User, string?, string?, DateOnly)"/>).</summary>
    public static bool Refuses(User subject, string? actorId, string? actorRole, DateOnly today) =>
        RefusingClause(subject, actorId, actorRole, today) is not null;

    /// <summary>
    /// Builds the refusal: one WARNING under <see cref="LogCategory"/> naming the arm that fired, the
    /// route, the actor and the subject; and the 403 with the date-free <see cref="DenyReason"/>. The
    /// caller has already decided to refuse and has issued no write.
    /// </summary>
    public static IResult Refusal(
        ILoggerFactory loggerFactory, ActorContext actor, string subjectId, string clause, string route)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(actor);

        loggerFactory.CreateLogger(LogCategory).LogWarning(
            "Access denied ({Clause}): Actor={ActorId} Role={ActorRole} Subject={SubjectId} Route={Route} CorrelationId={CorrelationId}",
            clause, actor.ActorId, actor.ActorRole, subjectId, route, actor.CorrelationId);

        return Results.Json(new { error = "Access denied", reason = DenyReason }, statusCode: 403);
    }
}
