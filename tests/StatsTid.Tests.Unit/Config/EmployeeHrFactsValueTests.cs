using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Config;

/// <summary>
/// S148 / TASK-14836-T (AC-C11c, the pure halves; ADR-043 D4; ruling 19) — which overtime regimes each
/// agreement allows, and what a stored value reads as when the person's agreement later changes.
///
/// <para>
/// <b>In plain language.</b> HR records one of five regimes for a person, but each agreement only allows some
/// of them: HK overtime or exempt; PROSA overtime or a contractual (merarbejde) claim; AC (and the AC_ family)
/// a contractual claim, discretionary or none. A value outside the person's agreement is refused when HR saves
/// it. A value stored earlier that a later agreement no longer allows is not shown: it reads as "not recorded".
/// The expected sets below come from the refinement's C-11 and ADR-043 D4, not from the code.
/// </para>
///
/// <para>
/// <b>Kind N (Unit), proved by M-55 (local, at the wave gate, in no CI run).</b> <see cref="EmployeeHrFactsRules"/> is a
/// type TASK-14836 creates, so on <c>1cc0cb1d</c> these facts do not compile, which never counts as RED. M-55 makes
/// <c>AllowedOvertimeRegimes</c> return all five values for every agreement (the API kept); both facts must then fail
/// on their assertions. Expected values are literals, never the production sets.
/// </para>
/// </summary>
public sealed class EmployeeHrFactsValueTests
{
    private const string NotAllowed = "NOT_ALLOWED_FOR_AGREEMENT";

    /// <summary>
    /// <b>N (Unit; M-55).</b> A value outside the agreement's set is refused with
    /// <c>NOT_ALLOWED_FOR_AGREEMENT</c> (the route's 400 code), and every value inside the set is accepted (null).
    /// <para><b>Red conditions (M-55).</b> With the allowed sets widened to all five values, the first assertion
    /// (<c>DISCRETIONARY</c> for <c>HK</c> is <c>NOT_ALLOWED_FOR_AGREEMENT</c>) fails with null.</para>
    /// </summary>
    [Fact]
    public void OutsideAgreementSubset_400()
    {
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("DISCRETIONARY", "HK"));
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("CONTRACTUAL", "HK"));
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("NONE", "HK"));
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("EXEMPT", "PROSA"));
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("OVERTIME", "AC"));
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("EXEMPT", "AC"));
        // The AC family: a code starting AC_ is AC (TASK-14836's ruling 18).
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("OVERTIME", "AC_RESEARCH"));
        // No agreement on the day: nothing is allowed.
        Assert.Equal(NotAllowed, EmployeeHrFactsRules.OvertimeRegimeRefusal("OVERTIME", null));

        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("OVERTIME", "HK"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("EXEMPT", "HK"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("OVERTIME", "PROSA"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("CONTRACTUAL", "PROSA"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("CONTRACTUAL", "AC"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("DISCRETIONARY", "AC"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("NONE", "AC"));
        Assert.Null(EmployeeHrFactsRules.OvertimeRegimeRefusal("NONE", "AC_RESEARCH"));

        Assert.Equal(new[] { "OVERTIME", "EXEMPT" }, EmployeeHrFactsRules.AllowedOvertimeRegimes("HK"));
        Assert.Equal(new[] { "OVERTIME", "CONTRACTUAL" }, EmployeeHrFactsRules.AllowedOvertimeRegimes("PROSA"));
        Assert.Equal(new[] { "CONTRACTUAL", "DISCRETIONARY", "NONE" }, EmployeeHrFactsRules.AllowedOvertimeRegimes("AC"));
        Assert.Empty(EmployeeHrFactsRules.AllowedOvertimeRegimes(null));
    }

    /// <summary>
    /// <b>N (Unit; M-55).</b> A stored value reads as itself while the agreement allows it and as null ("not
    /// recorded") once it does not.
    /// <para><b>Red conditions (M-55).</b> With the sets widened, <c>ResolveOvertimeRegime("OVERTIME", "AC")</c>
    /// returns <c>OVERTIME</c> where null is expected, and the first null assertion fails.</para>
    /// </summary>
    [Fact]
    public void NoLongerAllowed_ReadsNotRecorded()
    {
        Assert.Equal("OVERTIME", EmployeeHrFactsRules.ResolveOvertimeRegime("OVERTIME", "HK"));
        Assert.Null(EmployeeHrFactsRules.ResolveOvertimeRegime("OVERTIME", "AC"));
        Assert.Null(EmployeeHrFactsRules.ResolveOvertimeRegime("NONE", "HK"));
        Assert.Null(EmployeeHrFactsRules.ResolveOvertimeRegime("EXEMPT", "PROSA"));
        Assert.Equal("NONE", EmployeeHrFactsRules.ResolveOvertimeRegime("NONE", "AC"));
        // Nothing recorded stays nothing recorded, and no agreement allows nothing.
        Assert.Null(EmployeeHrFactsRules.ResolveOvertimeRegime(null, "HK"));
        Assert.Null(EmployeeHrFactsRules.ResolveOvertimeRegime("OVERTIME", null));
    }
}
