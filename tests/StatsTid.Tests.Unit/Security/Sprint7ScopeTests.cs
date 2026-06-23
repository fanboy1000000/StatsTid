using StatsTid.Auth;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// Tests for Sprint 7: OrgScopeValidator-related scope logic, RoleScope.CoversOrg
/// with Sprint 7 scenarios (multiple scopes, employee self-access), and ActorContext
/// construction patterns for scope-based access control.
///
/// <para>S93 / ADR-035 slice 2 (flat role-scope): ORG_AND_DESCENDANTS subtree inheritance
/// is dropped. Coverage is now exact Organisation-set membership; the former
/// ORG_AND_DESCENDANTS prefix-coverage cases are removed.</para>
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
    // 4. Employee self-access via ActorContext
    // ---------------------------------------------------------------

    [Fact]
    public void ActorContext_EmployeeSelfAccess_ActorIdMatchesTarget()
    {
        // Employee accessing own data — ActorId equals target employee ID
        var actor = new ActorContext(
            ActorId: "EMP001",
            ActorRole: StatsTidRoles.Employee,
            CorrelationId: Guid.NewGuid(),
            OrgId: "STY02",
            Scopes: new[] { new RoleScope(StatsTidRoles.Employee, "STY02", "ORG_ONLY") });

        var targetEmployeeId = "EMP001";

        // Self-access pattern: ActorId == targetEmployeeId
        Assert.Equal(actor.ActorId, targetEmployeeId);
        Assert.Equal(StatsTidRoles.Employee, actor.ActorRole);
    }

    [Fact]
    public void ActorContext_EmployeeCrossAccess_ActorIdDiffersFromTarget()
    {
        // Employee trying to access another employee's data
        var actor = new ActorContext(
            ActorId: "EMP001",
            ActorRole: StatsTidRoles.Employee,
            CorrelationId: Guid.NewGuid(),
            OrgId: "STY02",
            Scopes: new[] { new RoleScope(StatsTidRoles.Employee, "STY02", "ORG_ONLY") });

        var targetEmployeeId = "EMP002";

        // Cross-employee pattern: ActorId != targetEmployeeId
        Assert.NotEqual(actor.ActorId, targetEmployeeId);
    }

    // ---------------------------------------------------------------
    // 5. Multiple scopes / no scopes
    // ---------------------------------------------------------------

    [Fact]
    public void MultipleScopes_AccessGrantedIfAnyScopeCovers()
    {
        // Actor has two scopes: one that doesn't cover target, one that does
        var scopes = new[]
        {
            new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"),
        };

        var actor = new ActorContext(
            ActorId: "USR01",
            ActorRole: StatsTidRoles.LocalHR,
            CorrelationId: Guid.NewGuid(),
            OrgId: "STY02",
            Scopes: scopes);

        var targetOrgPath = "/MIN01/STY02/";

        // First scope (ORG_ONLY for STY01) doesn't cover /MIN01/STY02/
        Assert.False(scopes[0].CoversOrg(targetOrgPath, "/MIN01/STY01/"));
        // Second scope (ORG_ONLY for STY02) covers /MIN01/STY02/ by exact match
        Assert.True(scopes[1].CoversOrg(targetOrgPath, "/MIN01/STY02/"));

        // Access granted if ANY scope covers
        bool hasAccess = actor.Scopes!.Any(s =>
        {
            // Resolve scope org path based on scope type
            if (s.ScopeType == "GLOBAL") return s.CoversOrg(targetOrgPath, null);
            var scopeOrgPath = s.OrgId == "STY01" ? "/MIN01/STY01/" : "/MIN01/STY02/";
            return s.CoversOrg(targetOrgPath, scopeOrgPath);
        });

        Assert.True(hasAccess);
    }

    [Fact]
    public void NoScopes_EmptyArray_NoAccessGranted()
    {
        var actor = new ActorContext(
            ActorId: "USR01",
            ActorRole: StatsTidRoles.Employee,
            CorrelationId: Guid.NewGuid(),
            OrgId: "STY02",
            Scopes: Array.Empty<RoleScope>());

        var targetOrgPath = "/MIN01/STY02/";

        // No scopes means no scope can cover the target
        bool hasAccess = actor.Scopes!.Any(s => s.CoversOrg(targetOrgPath, "/MIN01/STY02/"));

        Assert.False(hasAccess);
    }
}
