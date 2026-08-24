using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// Unit tests for the REAL <see cref="RoleScope.CoversOrg"/> per-scope coverage decision — the pure,
/// unit-testable core the <c>OrgScopeValidator</c> loop aggregates.
///
/// <para>S93 / ADR-035 slice 2 (flat role-scope): ORG_AND_DESCENDANTS subtree inheritance
/// is dropped. Coverage is now exact Organisation-set membership; the former
/// ORG_AND_DESCENDANTS prefix-coverage cases are removed.</para>
///
/// <para>S133 / TASK-13306 (QUAL-095): three legacy "tests" here proved nothing about the system —
/// two constructed an <c>ActorContext</c> and asserted its own literal <c>ActorId</c> equals/differs
/// from a literal target (a POCO literal echo), and one asserted <c>Array.Empty&lt;RoleScope&gt;().Any(...)</c>
/// is false (a LINQ tautology). A fourth re-implemented the validator's "any covering scope admits"
/// loop inside the test body. They were deleted / rewired to drive <see cref="RoleScope.CoversOrg"/>
/// directly. The actual actor-level admission logic (ownership short-circuit, no-scopes deny,
/// any-covering-scope aggregation, mixed-role floor) lives in the DB-backed <c>OrgScopeValidator</c>
/// and is exercised by the Docker-gated Security regression suite (e.g. <c>MixedRoleScopeLeakTests</c>,
/// <c>S93FlatRoleScopeTests</c>, <c>TerminatedEmployeeAccessTests</c>), not by a unit test.</para>
/// </summary>
public class Sprint7ScopeTests
{
    // ---------------------------------------------------------------
    // 1. GLOBAL scope tests
    // ---------------------------------------------------------------

    [Fact]
    public void GlobalScope_CoversAnyOrgPath_IncludingDeeplyNested()
    {
        var scope = new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL");

        // Organisation under a MAO
        Assert.True(scope.CoversOrg("/MIN01/STY02/", null));
        // Root-level MAO
        Assert.True(scope.CoversOrg("/MIN01/", null));
        // Sibling subtree
        Assert.True(scope.CoversOrg("/MIN02/STY01/", null));
        // Null target path — GLOBAL still returns true
        Assert.True(scope.CoversOrg(null, null));
    }

    // ---------------------------------------------------------------
    // 2. ORG_ONLY scope tests — exact Organisation-set membership
    //    (S93: ORG_AND_DESCENDANTS subtree coverage is gone; the former
    //    prefix-coverage cases are deleted — coverage is exact-equality only.)
    // ---------------------------------------------------------------

    [Fact]
    public void OrgOnly_CoversExactMatchOnly()
    {
        var scope = new RoleScope(StatsTidRoles.Employee, "STY02", "ORG_ONLY");
        var scopeOrgPath = "/MIN01/STY02/";

        // Exact match succeeds
        Assert.True(scope.CoversOrg("/MIN01/STY02/", scopeOrgPath));
    }

    [Fact]
    public void OrgOnly_DoesNotCoverChildOrgs()
    {
        var scope = new RoleScope(StatsTidRoles.Employee, "STY02", "ORG_ONLY");
        var scopeOrgPath = "/MIN01/STY02/";

        // A deeper sub-path under the Organisation
        Assert.False(scope.CoversOrg("/MIN01/STY02/UNIT01/", scopeOrgPath));
        // Parent org (the MAO)
        Assert.False(scope.CoversOrg("/MIN01/", scopeOrgPath));
        // Sibling Organisation
        Assert.False(scope.CoversOrg("/MIN01/STY01/", scopeOrgPath));
    }

    [Fact]
    public void OrgOnly_DoesNotCoverParentMaoOrRoot()
    {
        // S93: an ORG_ONLY scope keyed on an Organisation never reaches its parent MAO
        // (no subtree branch survives — a MAO is a strictly different org_id).
        var scope = new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY");
        var scopeOrgPath = "/MIN01/STY02/";

        // Parent MAO path /MIN01/ is not an exact match
        Assert.False(scope.CoversOrg("/MIN01/", scopeOrgPath));
        // Root path does not match
        Assert.False(scope.CoversOrg("/", scopeOrgPath));
    }

    [Fact]
    public void OrgOnly_DoesNotCoverSiblingOrganisation()
    {
        // S93: even within the same MAO, an ORG_ONLY scope never reaches a sibling Organisation.
        var scope = new RoleScope(StatsTidRoles.LocalLeader, "STY02", "ORG_ONLY");
        var scopeOrgPath = "/MIN01/STY02/";

        // Sibling Organisation under the same MAO
        Assert.False(scope.CoversOrg("/MIN01/STY01/", scopeOrgPath));
        // Different MAO entirely
        Assert.False(scope.CoversOrg("/MIN02/STY02/", scopeOrgPath));
    }

    [Fact]
    public void StaleRemovedScopeType_IsDefaultDenied_NotExactMatched()
    {
        // S93 Step-7a hardening (ADR-035 slice 2): a stale pre-S93 JWT carrying the REMOVED
        // ORG_AND_DESCENDANTS type must NOT fall through to exact-match its root (which would let an
        // old MAO-rooted token pass the org-structure gates, bypassing the OQ1 grant-time MAO guard
        // for the token lifetime). CoversOrg DEFAULT-DENIES any non-GLOBAL type that is not ORG_ONLY.
        var stale = new RoleScope(StatsTidRoles.LocalAdmin, "MIN01", "ORG_AND_DESCENDANTS");
        // Pre-fix this exact-matched its own root /MIN01/ (the bypass); now denied.
        Assert.False(stale.CoversOrg("/MIN01/", "/MIN01/"));
        // And, as before the fix, it never reached descendants.
        Assert.False(stale.CoversOrg("/MIN01/STY02/", "/MIN01/"));
        // An unknown/garbage type is likewise denied (defense-in-depth).
        Assert.False(new RoleScope(StatsTidRoles.LocalAdmin, "STY02", "BOGUS").CoversOrg("/MIN01/STY02/", "/MIN01/STY02/"));
    }

    // ---------------------------------------------------------------
    // 4. Multi-scope coverage via the REAL RoleScope.CoversOrg
    // ---------------------------------------------------------------

    [Fact]
    public void MultipleScopes_OnlyTheCoveringScopeAdmits_ViaRealCoversOrg()
    {
        // A two-scope actor: an ORG_ONLY scope on STY01 (does NOT cover the STY02 target) and an
        // ORG_ONLY scope on STY02 (covers it by exact Organisation-set membership). Drives the REAL
        // RoleScope.CoversOrg for each scope — the per-scope decision the OrgScopeValidator loop
        // aggregates. RED if CoversOrg's exact-match semantics regress in either direction.
        var scopes = new[]
        {
            new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"),
        };

        var targetOrgPath = "/MIN01/STY02/";

        // The STY01 scope does not cover the STY02 target...
        Assert.False(scopes[0].CoversOrg(targetOrgPath, "/MIN01/STY01/"));
        // ...the STY02 scope does, by exact match (S93 flat role-scope).
        Assert.True(scopes[1].CoversOrg(targetOrgPath, "/MIN01/STY02/"));
    }
}
