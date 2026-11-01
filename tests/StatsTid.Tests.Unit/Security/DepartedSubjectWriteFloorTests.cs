using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// S147 / TASK-14710-T (QUAL-230 T5a point 3; rulings R-21, R-22, R-24; owner OQ-2; AC-230-5) — the
/// self-service write floor's truth table, derived from the SPEC, not the code:
///
/// <para><b>Plain words.</b> From the day after a leaver's last working day, nobody below LocalHR may
/// write that person's record, and the leaver themself may not write their own record whatever their
/// role. HR and admins acting on SOMEONE ELSE's departed record are the routine final-month correction
/// and stay allowed. A person who has not departed is never refused here. Two refusal sentences exist,
/// one per clause (Orchestrator ruling, fix-up 1): the departed-self clause says "A departed employee
/// cannot act on their own record"; the role clause says ADR-040 D3's "Writes for a deactivated
/// employee require LocalHR or above"; where both apply (an Employee on themselves) the self sentence
/// wins.</para>
///
/// <para><b>Kind N</b> for every fact: each names <see cref="DepartedSubjectWriteFloor"/>, a type this
/// sprint creates; RED is proven by NAMED MUTATION at the wave gate in a detached worktree (see each
/// fact). M-9a drops the identity arm; M-9b drops the role arm; M-9c checks the role arm first (so an
/// Employee acting on themselves gets the role body); M-9d swaps the two bodies in
/// <c>ReasonFor</c>.</para>
/// </summary>
public class DepartedSubjectWriteFloorTests
{
    private static readonly DateOnly Today = new(2026, 3, 16);

    private static User Subject(string id, bool isActive, DateOnly? end) => new()
    {
        UserId = id, Username = id, PasswordHash = "x", DisplayName = id,
        PrimaryOrgId = "ORG1", AgreementCode = "AC", OkVersion = "OK24",
        IsActive = isActive, EmploymentEndDate = end,
    };

    private const string Mette = "mette";
    private const string Other = "someone-else";

    // ───────── (departed subject, actor below HR, not self) -> refuse (the role clause) ─────────

    /// <summary>Kind N. Red condition: M-9b (drop the role arm) makes this return null instead of
    /// "departed-subject-below-local-hr".</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee)]
    [InlineData(StatsTidRoles.LocalLeader)]
    [InlineData(null)]
    public void DepartedByEndDate_ActorBelowHr_NotSelf_RefusedByTheRoleClause(string? role)
    {
        var subject = Subject(Mette, isActive: true, end: Today.AddDays(-1)); // D+1, still active (grace)
        Assert.Equal(DepartedSubjectWriteFloor.BelowLocalHrClause,
            DepartedSubjectWriteFloor.RefusingClause(subject, Other, role, Today));
        Assert.True(DepartedSubjectWriteFloor.Refuses(subject, Other, role, Today));
    }

    /// <summary>Kind N. The last day is still employed (boundary): the same actor is allowed on D and
    /// refused on D+1. Red condition: M-9b, or a departed test that counts the last day.</summary>
    [Fact]
    public void LastDayEmployed_Allowed_DayAfter_Refused()
    {
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject(Mette, true, Today), Other, StatsTidRoles.Employee, Today));
        Assert.Equal(DepartedSubjectWriteFloor.BelowLocalHrClause,
            DepartedSubjectWriteFloor.RefusingClause(
                Subject(Mette, true, Today.AddDays(-1)), Other, StatsTidRoles.Employee, Today));
    }

    // ───────── (departed, HR, not self) -> allow ─────────

    /// <summary>Kind N. HR and admins on someone else's departed record: the routine final-month
    /// correction. Red condition: a mutation that refuses regardless of role (M-9e: always refuse when
    /// departed) makes these return a clause.</summary>
    [Theory]
    [InlineData(StatsTidRoles.LocalHR)]
    [InlineData(StatsTidRoles.LocalAdmin)]
    [InlineData(StatsTidRoles.GlobalAdmin)]
    public void Departed_ActorAtLeastHr_NotSelf_Allowed(string role)
    {
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject(Mette, true, Today.AddDays(-1)), Other, role, Today));
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject(Mette, false, Today.AddDays(-9)), Other, role, Today));
    }

    // ───────── (departed, HR, self) -> refuse (M-9: the identity arm) ─────────

    /// <summary>Kind N. An HR, admin or leader who has left has no role to fall back on for their OWN
    /// record. Red condition: M-9a (drop the identity arm) makes these return null — the ONLY layer
    /// for the LocalHR self-only routes (row-preferences, compensation-choice).</summary>
    [Theory]
    [InlineData(StatsTidRoles.LocalHR)]
    [InlineData(StatsTidRoles.LocalAdmin)]
    [InlineData(StatsTidRoles.GlobalAdmin)]
    public void Departed_ActorAtLeastHr_IsSelf_RefusedByTheSelfClause(string role)
    {
        Assert.Equal(DepartedSubjectWriteFloor.SelfClause,
            DepartedSubjectWriteFloor.RefusingClause(
                Subject(Mette, true, Today.AddDays(-1)), Mette, role, Today));
        Assert.Equal(DepartedSubject.ClauseName, DepartedSubjectWriteFloor.SelfClause);
    }

    /// <summary>Kind N. Where both arms hold (an Employee acting on themselves) the self clause wins
    /// (Orchestrator ruling, fix-up 1). Red condition: M-9c (role arm checked first) returns
    /// "departed-subject-below-local-hr" here.</summary>
    [Fact]
    public void Departed_EmployeeActingOnSelf_BothArmsHold_SelfClauseWins()
    {
        Assert.Equal(DepartedSubjectWriteFloor.SelfClause,
            DepartedSubjectWriteFloor.RefusingClause(
                Subject(Mette, true, Today.AddDays(-1)), Mette, StatsTidRoles.Employee, Today));
        Assert.Equal(DepartedSubjectWriteFloor.SelfClause,
            DepartedSubjectWriteFloor.RefusingClause(
                Subject(Mette, false, null), Mette, StatsTidRoles.Employee, Today));
    }

    // ───────── (not departed, any) -> allow ─────────

    /// <summary>Kind N. Red condition: a mutation that ignores departed-ness (M-9f: refuse whenever the
    /// actor is self or below HR) refuses an ordinary active employee.</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee, Mette)]
    [InlineData(StatsTidRoles.Employee, Other)]
    [InlineData(StatsTidRoles.LocalLeader, Other)]
    [InlineData(StatsTidRoles.LocalHR, Mette)]
    [InlineData(StatsTidRoles.GlobalAdmin, Other)]
    public void NotDeparted_AnyActor_Allowed(string role, string actorId)
    {
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject(Mette, true, null), actorId, role, Today));
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject(Mette, true, Today.AddDays(30)), actorId, role, Today));
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject(Mette, true, Today), actorId, role, Today));
    }

    // ───────── (deactivated, no end date, below HR) -> refuse (ADR-040 D3 kept) ─────────

    /// <summary>Kind N. A manually deactivated subject (no end date) is departed: D3's original floor
    /// is kept. Red condition: M-9g (the departed test keys on the end date only) returns null.</summary>
    [Theory]
    [InlineData(StatsTidRoles.Employee)]
    [InlineData(StatsTidRoles.LocalLeader)]
    public void DeactivatedNoEndDate_ActorBelowHr_Refused(string role)
    {
        Assert.Equal(DepartedSubjectWriteFloor.BelowLocalHrClause,
            DepartedSubjectWriteFloor.RefusingClause(Subject(Mette, false, null), Other, role, Today));
    }

    /// <summary>Kind N. Identity is compared exactly (Ordinal): a different id that differs only by case
    /// is NOT the subject, so HR on a "same-looking" other id is allowed. Red condition: a mutation
    /// making IsSelf case-insensitive returns the self clause.</summary>
    [Fact]
    public void SelfIsOrdinal_CaseDifferentIdIsAnotherPerson()
    {
        Assert.Null(DepartedSubjectWriteFloor.RefusingClause(
            Subject("Mette", true, Today.AddDays(-1)), "mette", StatsTidRoles.LocalHR, Today));
    }

    // ───────── the refusal body follows the clause that fired ─────────

    /// <summary>Kind N. The two sentences, byte for byte, and date-free (ADR-040 D7). Red condition:
    /// M-9d (swap the bodies in ReasonFor) fails both Equal assertions.</summary>
    [Fact]
    public void ReasonFor_EachClauseHasItsOwnDateFreeSentence()
    {
        Assert.Equal("A departed employee cannot act on their own record",
            DepartedSubjectWriteFloor.ReasonFor(DepartedSubjectWriteFloor.SelfClause));
        Assert.Equal("Writes for a deactivated employee require LocalHR or above",
            DepartedSubjectWriteFloor.ReasonFor(DepartedSubjectWriteFloor.BelowLocalHrClause));
        Assert.Equal(DepartedSubject.DenyReason,
            DepartedSubjectWriteFloor.ReasonFor(DepartedSubjectWriteFloor.SelfClause));
        Assert.Equal(DepartedSubjectWriteFloor.DenyReason,
            DepartedSubjectWriteFloor.ReasonFor(DepartedSubjectWriteFloor.BelowLocalHrClause));
        Assert.DoesNotMatch(@"\d{4}", DepartedSubject.DenyReason);
        Assert.DoesNotMatch(@"\d{4}", DepartedSubjectWriteFloor.DenyReason);
    }

    /// <summary>Kind N. The response itself: 403, <c>{ error = "Access denied", reason = ... }</c>, the
    /// body of the clause passed in. Red condition: M-9d, or a Refusal that returns a different
    /// status/shape.</summary>
    [Theory]
    [InlineData(DepartedSubject.ClauseName, "A departed employee cannot act on their own record")]
    [InlineData(DepartedSubjectWriteFloor.BelowLocalHrClause, "Writes for a deactivated employee require LocalHR or above")]
    public void Refusal_Is403_WithTheClausesBody(string clause, string expectedReason)
    {
        var actor = new ActorContext(Mette, StatsTidRoles.Employee, Guid.NewGuid());
        var result = DepartedSubjectWriteFloor.Refusal(
            NullLoggerFactory.Instance, actor, Mette, clause, "PUT /x");

        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        Assert.Equal("Access denied", value.GetType().GetProperty("error")!.GetValue(value));
        Assert.Equal(expectedReason, value.GetType().GetProperty("reason")!.GetValue(value));
    }

    /// <summary>Kind N. A caller bug (an unknown clause) throws rather than answering with a guess.
    /// Red condition: a ReasonFor that falls through to a default string.</summary>
    [Fact]
    public void ReasonFor_UnknownClause_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepartedSubjectWriteFloor.ReasonFor("nonsense"));
    }
}
