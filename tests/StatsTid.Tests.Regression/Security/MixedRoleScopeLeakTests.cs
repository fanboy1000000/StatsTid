using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S76 / TASK-7600 — the B1 mixed-role scope-leak hardening (CLAUDE.md priority 7, pre-launch).
///
/// <para><b>The vulnerability.</b> <see cref="OrgScopeValidator"/> grants org/employee access by
/// iterating <c>actor.Scopes</c> and admitting on the FIRST scope that <see cref="RoleScope.CoversOrg"/>s
/// the target — WITHOUT checking that scope's ROLE (FAIL-001: the validator binds to ANY covering
/// scope, not a role-matching one). So a MIXED-role actor — e.g. an admin in styrelse A who ALSO
/// holds a non-admin (Employee/Leader) scope covering styrelse B — passes an ADMIN gate for B via
/// the non-admin scope, and can read/write B's admin data.</para>
///
/// <para><b>The fix.</b> Floored overloads of the three scope-admission paths
/// (<see cref="OrgScopeValidator.ValidateOrgAccessAsync(ActorContext, string, string?, CancellationToken)"/>,
/// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync(ActorContext, string, string?, CancellationToken)"/>,
/// <see cref="OrgScopeValidator.GetAccessibleOrgsAsync(ActorContext, string?, CancellationToken)"/>):
/// each ADMIN-policy endpoint passes its policy floor (LocalAdmin for LocalAdminOrAbove, LocalHR for
/// HROrAbove); a scope below the floor never admits/contributes. The defaulted no-floor overloads
/// keep EmployeeOrAbove/LeaderOrAbove callers byte-identical.</para>
///
/// <para><b>Discrimination.</b> Every test mints a mixed-role actor whose ADMIN scope sits in a
/// DISJOINT styrelse (STY05, <c>/MIN02/STY05/</c>) while its NON-admin scope COVERS the target via
/// MIN01 (<c>/MIN01/</c>, ancestor of the STY01 target). On the PRE-FIX unfloored code the covering
/// non-admin scope admitted (the leak); each test asserts the FLOORED path now DENIES — and the
/// validator-direct tests prove the leak directly by showing the no-floor overload STILL admits the
/// same actor. Positive controls pin the no-regression: a correctly-scoped admin/HR still succeeds.</para>
///
/// <para>Fixture/JWT conventions mirror <see cref="TerminatedEmployeeAccessTests"/> and
/// <see cref="Approval.PeriodStatusAndPersonSearchReadsTests"/> (same WAF harness, token minting,
/// the seed org tree MIN01/STY01/STY05).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class MixedRoleScopeLeakTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string TargetOrg = "STY01";    // /MIN01/STY01/ — the target styrelse (org "B")
    private const string DisjointOrg = "STY05";  // /MIN02/STY05/ — disjoint admin home (org "A")
    private const string CoveringOrg = "MIN01";  // /MIN01/ — covers STY01 via ORG_AND_DESCENDANTS

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (org tree MIN01/STY01/STY05 + configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Validator-direct discrimination — the sharpest pin: the SAME mixed-role actor is
    //  ADMITTED by the no-floor overload (the pre-fix leak, exercised directly) and DENIED
    //  by the floored overload (the fix). Proves the floor is load-bearing at BOTH levels.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>LocalAdmin floor, org path: admin@STY05 + LEADER@MIN01 (covers STY01). The no-floor
    /// overload admits via the Leader scope (the leak); the LocalAdmin-floored overload denies.</summary>
    [Fact]
    public async Task ValidateOrgAccess_MixedAdminPlusLeader_NoFloorAdmits_LocalAdminFloorDenies()
    {
        var validator = MakeValidator();
        var actor = AdminAtAPlusLeaderCoveringB(StatsTidRoles.LocalAdmin, "mix_org");

        // No floor (legacy behavior) → ADMITTED via the covering Leader scope = the pre-fix leak.
        var legacy = await validator.ValidateOrgAccessAsync(actor, TargetOrg);
        Assert.True(legacy.Allowed);

        // LocalAdmin floor → the Leader scope is skipped; the admin STY05 scope does not cover
        // STY01 → DENIED.
        var floored = await validator.ValidateOrgAccessAsync(actor, TargetOrg, StatsTidRoles.LocalAdmin);
        Assert.False(floored.Allowed);
    }

    /// <summary>LocalAdmin floor, employee path: admin@STY05 + LEADER@MIN01, target employee in
    /// STY01. No-floor admits (leak); LocalAdmin-floored denies.</summary>
    [Fact]
    public async Task ValidateEmployeeAccess_MixedAdminPlusLeader_NoFloorAdmits_LocalAdminFloorDenies()
    {
        var emp = await SeedTargetEmployeeAsync();
        var validator = MakeValidator();
        var actor = AdminAtAPlusLeaderCoveringB(StatsTidRoles.LocalAdmin, "mix_emp");

        Assert.True((await validator.ValidateEmployeeAccessAsync(actor, emp)).Allowed);
        Assert.False((await validator.ValidateEmployeeAccessAsync(actor, emp, StatsTidRoles.LocalAdmin)).Allowed);
    }

    /// <summary>LocalHR floor, employee path: HR@STY05 + LEADER@MIN01, target employee in STY01.
    /// No-floor admits (leak); LocalHR-floored denies (the HR-data gate).</summary>
    [Fact]
    public async Task ValidateEmployeeAccess_MixedHrPlusLeader_NoFloorAdmits_LocalHrFloorDenies()
    {
        var emp = await SeedTargetEmployeeAsync();
        var validator = MakeValidator();
        var actor = AdminAtAPlusLeaderCoveringB(StatsTidRoles.LocalHR, "mix_emp_hr");

        Assert.True((await validator.ValidateEmployeeAccessAsync(actor, emp)).Allowed);
        Assert.False((await validator.ValidateEmployeeAccessAsync(actor, emp, StatsTidRoles.LocalHR)).Allowed);
    }

    /// <summary>The picker union: admin@STY05 + LEADER@MIN01. The no-floor union INCLUDES STY01
    /// (the Leader scope contributes the whole MIN01 subtree = the picker leak); the LocalAdmin-
    /// floored union EXCLUDES STY01 (only the STY05 subtree remains).</summary>
    [Fact]
    public async Task GetAccessibleOrgs_MixedAdminPlusLeader_NoFloorIncludesTarget_LocalAdminFloorExcludesIt()
    {
        var validator = MakeValidator();
        var actor = AdminAtAPlusLeaderCoveringB(StatsTidRoles.LocalAdmin, "mix_pick");

        var legacyUnion = await validator.GetAccessibleOrgsAsync(actor);
        Assert.NotNull(legacyUnion);
        Assert.Contains(TargetOrg, legacyUnion!); // the Leader scope leaked STY01 into the union

        var flooredUnion = await validator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalAdmin);
        Assert.NotNull(flooredUnion);
        Assert.DoesNotContain(TargetOrg, flooredUnion!); // floored: only the STY05 subtree
    }

    /// <summary>GLOBAL-branch floor on the picker: a token whose GLOBAL scope carries Role
    /// LocalLeader (a non-admin GLOBAL scope) must NOT short-circuit to the unrestricted "see
    /// everything" sentinel under a LocalAdmin floor. No-floor → null (unrestricted, the leak);
    /// floored → a bounded set that excludes the disjoint target.</summary>
    [Fact]
    public async Task GetAccessibleOrgs_GlobalScopeBelowFloor_NoFloorUnrestricted_AdminFloorBounded()
    {
        var validator = MakeValidator();
        // Primary role minted LocalLeader; the only scope is a GLOBAL one carrying Role LocalLeader.
        var actor = new ActorContext(
            "mix_glob", StatsTidRoles.LocalLeader, Guid.NewGuid(), null,
            new[] { new RoleScope(StatsTidRoles.LocalLeader, null, "GLOBAL") });

        Assert.Null(await validator.GetAccessibleOrgsAsync(actor)); // no floor → unrestricted (leak)

        var floored = await validator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalAdmin);
        Assert.NotNull(floored); // floored → NOT the unrestricted sentinel
        Assert.DoesNotContain(TargetOrg, floored!);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP endpoint discrimination — LocalAdmin-floor surfaces (the /api/admin/* writers
    //  + tree reads + picker). Each would have LEAKED on the pre-fix unfloored validator.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Roster/period-status read (LocalAdmin floor): the mixed-role JWT (admin@STY05 +
    /// leader@MIN01) is denied the STY01 tree. The endpoint policy is LocalAdminOrAbove and the
    /// JWT's primary role LocalAdmin PASSES it — the validator floor is the layer that bites.</summary>
    [Fact]
    public async Task PeriodStatusRead_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_roster"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/period-status");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Medarbejdere roster read (LocalAdmin floor): same mixed-role JWT → 403.</summary>
    [Fact]
    public async Task MedarbejdereRead_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_med"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/medarbejdere");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Reporting-line WRITE (LocalAdmin floor): assigning a manager to a STY01 employee
    /// via the mixed-role JWT → 403 (the assign gate is per-employee
    /// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/>).</summary>
    [Fact]
    public async Task ReportingLineAssign_MixedRoleJwt_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync();
        var mgr = await SeedTargetEmployeeAsync();
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_assign"));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reporting-lines")
        {
            Content = JsonContent.Create(new
            {
                employeeId = emp,
                managerId = mgr,
                relationship = "PRIMARY",
                effectiveFrom = "2026-01-01",
            }),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*"); // first assignment
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Person-search picker (LocalAdmin floor): the mixed-role JWT's accessible-org set
    /// must NOT include STY01, so a STY01 user does NOT appear in the picker results. Pre-fix the
    /// leader@MIN01 scope leaked the whole MIN01 subtree into the picker.</summary>
    [Fact]
    public async Task PersonSearchPicker_MixedRoleJwt_DoesNotReturnTargetOrgUsers()
    {
        var emp = await SeedTargetEmployeeAsync("picktarget");
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_picker"));

        var rsp = await client.GetAsync("/api/admin/users/search?q=picktarget&limit=200&offset=0");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()).ToList();
        Assert.DoesNotContain(emp, ids); // STY01 user out of the floored accessible set
    }

    /// <summary>Config absence-type visibility POST (LocalAdmin floor, S76 fix-forward B1): the
    /// mixed admin@STY05 + leader@MIN01 JWT is denied a config write in STY01. Discriminating —
    /// pre-fix the no-floor ValidateOrgAccessAsync admitted via the covering Leader scope. The
    /// LocalAdminOrAbove policy passes on the JWT's primary role; the validator floor is the gate.</summary>
    [Fact]
    public async Task AbsenceTypeVisibilityPost_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_cfgvis"));

        var rsp = await client.PostAsync(
            $"/api/config/{TargetOrg}/absence-types/visibility",
            JsonContent.Create(new { absenceType = "VACATION", isHidden = true }));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Config profile PUT (LocalAdmin floor, S76 fix-forward B1): same mixed-role JWT →
    /// 403 on a STY01 profile write. The 403 fires at the scope gate BEFORE any If-Match parse.</summary>
    [Fact]
    public async Task ConfigProfilePut_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_cfgprof"));

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/config/{TargetOrg}/profile/AC/OK24")
        {
            Content = JsonContent.Create(new { weeklyNormHours = 37.0m, effectiveFrom = "2026-01-01" }),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Project create POST (LocalAdmin floor, S76 fix-forward B1): the mixed admin@STY05
    /// + leader@MIN01 JWT is denied a project create in STY01. Discriminating — pre-fix the
    /// covering Leader scope admitted the write.</summary>
    [Fact]
    public async Task ProjectCreate_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_projcreate"));

        var rsp = await client.PostAsync(
            $"/api/projects/{TargetOrg}",
            JsonContent.Create(new { projectCode = "P76", projectName = "Leak probe", sortOrder = 1 }));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Project update PUT (LocalAdmin floor): same mixed-role JWT → 403 on a STY01
    /// project update. The scope gate fires before the update touches any row.</summary>
    [Fact]
    public async Task ProjectUpdate_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_projupd"));

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{TargetOrg}/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { projectName = "Renamed", sortOrder = 2 }),
        };
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Project delete (LocalAdmin floor): same mixed-role JWT → 403 on a STY01 project
    /// deactivate.</summary>
    [Fact]
    public async Task ProjectDelete_MixedRoleJwt_Returns403()
    {
        var client = ClientWith(MixedAdminLeaderToken(StatsTidRoles.LocalAdmin, "mix_projdel"));

        var rsp = await client.DeleteAsync($"/api/projects/{TargetOrg}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HasGlobalScope discrimination (S76 fix-forward B3) — a GLOBAL scope only admits a
    //  GlobalAdmin operation if the scope ITSELF carries GlobalAdmin role. The guarded
    //  GlobalAdmin-only surfaces are top-level org create + global role grant/revoke.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A token whose only GLOBAL scope carries a NON-GlobalAdmin role (GLOBAL+LocalLeader)
    /// is DENIED a top-level org create. Discriminating — pre-fix HasGlobalScope returned true for
    /// ANY GLOBAL scope, so the LocalLeader-GLOBAL token would have passed the GlobalAdmin gate.
    /// The LocalAdminOrAbove policy is satisfied by the JWT's LocalLeader→below... so to isolate the
    /// HasGlobalScope gate (not the endpoint policy) we mint primary role LocalAdmin while the only
    /// scope is the below-floor GLOBAL one.</summary>
    [Fact]
    public async Task TopLevelOrgCreate_GlobalScopeBelowGlobalAdmin_Returns403()
    {
        var client = ClientWith(GlobalScopeToken(StatsTidRoles.LocalLeader, StatsTidRoles.LocalAdmin, "glob_leader"));

        var rsp = await client.PostAsync("/api/admin/organizations", JsonContent.Create(new
        {
            orgId = "STYNEW_" + Guid.NewGuid().ToString("N")[..6],
            orgName = "Leak probe top-level",
            orgType = "STYRELSE",
            parentOrgId = (string?)null, // top-level → HasGlobalScope gate
            agreementCode = "AC",
            okVersion = "OK24",
        }));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Positive control: a real GlobalAdmin (GLOBAL scope carrying GlobalAdmin role) IS
    /// ADMITTED to top-level org create → 201. Proves the B3 floor does not over-restrict the
    /// legitimate GlobalAdmin whose GLOBAL scope is at GlobalAdmin role.</summary>
    [Fact]
    public async Task TopLevelOrgCreate_GenuineGlobalAdmin_Returns201()
    {
        var client = ClientWith(GlobalScopeToken(StatsTidRoles.GlobalAdmin, StatsTidRoles.GlobalAdmin, "glob_admin"));

        var rsp = await client.PostAsync("/api/admin/organizations", JsonContent.Create(new
        {
            orgId = "STYNEW_" + Guid.NewGuid().ToString("N")[..6],
            orgName = "Genuine global create",
            orgType = "STYRELSE",
            parentOrgId = (string?)null,
            agreementCode = "AC",
            okVersion = "OK24",
        }));
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
    }

    /// <summary>Validator-direct twin of the B3 gate via the picker GLOBAL short-circuit: the
    /// GLOBAL+LocalLeader actor is NOT granted the unrestricted (null) sentinel under a GlobalAdmin
    /// floor — proving the GLOBAL-scope role check is load-bearing for the GlobalAdmin tier too.</summary>
    [Fact]
    public async Task GetAccessibleOrgs_GlobalScopeBelowGlobalAdminFloor_NotUnrestricted()
    {
        var validator = MakeValidator();
        var actor = new ActorContext(
            "glob_pick", StatsTidRoles.LocalLeader, Guid.NewGuid(), null,
            new[] { new RoleScope(StatsTidRoles.LocalLeader, null, "GLOBAL") });

        Assert.Null(await validator.GetAccessibleOrgsAsync(actor)); // no floor → unrestricted (leak)
        Assert.NotNull(await validator.GetAccessibleOrgsAsync(actor, StatsTidRoles.GlobalAdmin)); // floored → bounded
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Completeness-sweep finds (S76 fix-forward) — the SAME mechanical leak class on two
    //  HROrAbove read surfaces the first pass missed: the audit list + the §24 payout-pending
    //  list both unioned org scopes via GetAccessibleOrgsAsync WITHOUT the LocalHR floor, so a
    //  mixed HR@A + Leader@B actor saw B's rows. The fix routes both through the LocalHR floor.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The HR-floor picker union (the mechanism shared by the audit-list and §24
    /// payout-pending HROrAbove reads): admin@STY05 + leader@MIN01. No-floor union INCLUDES STY01
    /// (the Leader scope leaks the whole MIN01 subtree); the LocalHR-floored union EXCLUDES it.
    /// Discriminating at the exact seam both list endpoints query.</summary>
    [Fact]
    public async Task GetAccessibleOrgs_MixedHrPlusLeader_LocalHrFloorExcludesTarget()
    {
        var validator = MakeValidator();
        var actor = AdminAtAPlusLeaderCoveringB(StatsTidRoles.LocalHR, "mix_hrpick");

        var legacyUnion = await validator.GetAccessibleOrgsAsync(actor);
        Assert.NotNull(legacyUnion);
        Assert.Contains(TargetOrg, legacyUnion!); // leak: the Leader scope unioned STY01 in

        var flooredUnion = await validator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR);
        Assert.NotNull(flooredUnion);
        Assert.DoesNotContain(TargetOrg, flooredUnion!); // floored: only the STY05 subtree
    }

    /// <summary>§24 payout-pending list (HROrAbove, LocalHR floor): the mixed HR@STY05 +
    /// leader@MIN01 JWT must NOT see a STY01 employee's settled payout row. Pre-fix the
    /// leader@MIN01 scope unioned the whole MIN01 subtree into the accessible set.
    ///
    /// <para><b>S76 fix-forward cycle 2 — discriminating seed (Codex c2 weak-test find).</b> The
    /// prior version seeded only an employee, never a qualifying settled payout ROW, so the list
    /// was empty for EVERY actor and the assertion passed even on the unfloored pre-fix code
    /// (non-discriminating). This now seeds a REAL <c>SETTLED</c> + <c>payout_days &gt; 0</c> +
    /// <c>payout_reconciled_at IS NULL</c> STY01 row — exactly the row the endpoint surfaces. On
    /// the pre-fix no-floor union the leader@MIN01 scope unions STY01 in and the row LEAKS; the
    /// LocalHR-floored union excludes STY01 so the row is gone. The positive control proves the
    /// seed produced a row the endpoint genuinely surfaces (a correctly-scoped HR@MIN01 sees it).</para></summary>
    [Fact]
    public async Task PayoutPendingList_MixedRoleJwt_DoesNotReturnTargetOrgRows()
    {
        var emp = await SeedTargetEmployeeAsync("payoutleak");
        await SeedQualifyingPayoutRowAsync(emp, year: 2024); // SETTLED + payout_days>0 + unreconciled
        var client = ClientWith(MixedHrLeaderToken("mix_payout"));

        var rsp = await client.GetAsync("/api/vacation-settlements/payout-pending");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("employeeId").GetString()).ToList();
        Assert.DoesNotContain(emp, ids); // STY01 row out of the floored accessible set (leaks pre-fix)
    }

    /// <summary>Positive control for the discriminating payout seed: a correctly-scoped HR@MIN01
    /// (which covers STY01) DOES see the seeded STY01 settled payout row — proving the seed
    /// produces a genuinely-surfaced row, so the mixed-role denial above is the floor biting, not
    /// an empty list.</summary>
    [Fact]
    public async Task PayoutPendingList_CorrectlyScopedHr_DoesReturnTargetOrgRow()
    {
        var emp = await SeedTargetEmployeeAsync("payoutok");
        await SeedQualifyingPayoutRowAsync(emp, year: 2024);
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "hr_payout_ok"));

        var rsp = await client.GetAsync("/api/vacation-settlements/payout-pending");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("employeeId").GetString()).ToList();
        Assert.Contains(emp, ids); // the genuine admin scope sees the row
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP endpoint discrimination — LocalHR-floor surfaces (employee-profile / DOB /
    //  CHILD_SICK). The mixed-role JWT here is HR@STY05 + leader@MIN01.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Employee-profile PUT (LocalHR floor): the mixed HR@STY05 + leader@MIN01 JWT →
    /// 403 on a STY01 employee. Primary role LocalHR passes the HROrAbove policy; the validator's
    /// LocalHR floor is the gate.</summary>
    [Fact]
    public async Task EmployeeProfilePut_MixedRoleJwt_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync();
        var client = ClientWith(MixedHrLeaderToken("mix_prof"));

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{emp}")
        {
            Content = JsonContent.Create(new { partTimeFraction = 0.8m }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>DOB PUT (LocalHR floor): mixed HR@STY05 + leader@MIN01 → 403 on a STY01 employee.</summary>
    [Fact]
    public async Task BirthDatePut_MixedRoleJwt_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync();
        var client = ClientWith(MixedHrLeaderToken("mix_dob"));

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{emp}/birth-date")
        {
            Content = JsonContent.Create(new { birthDate = "1990-05-01" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>CHILD_SICK entitlement-eligibility PUT (LocalHR floor): mixed HR@STY05 +
    /// leader@MIN01 → 403 on a STY01 employee.</summary>
    [Fact]
    public async Task ChildSickEligibilityPut_MixedRoleJwt_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync();
        var client = ClientWith(MixedHrLeaderToken("mix_cs"));

        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/employees/{emp}/entitlement-eligibility/CHILD_SICK")
        {
            Content = JsonContent.Create(new { eligible = true, effectiveFrom = "2026-01-01" }),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ACTIVE-target mixed-role denial (S76 fix-forward cycle 2, Codex c2 MUST-FIX) — the
    //  ValidateEmployeeAccessIncludingTerminatedAsync floored helper. Pre-fix the floor on
    //  THIS helper only bit a TERMINATED target; for an ACTIVE target it admitted via ANY
    //  covering scope, so the mixed HR@STY05 + leader@MIN01 JWT could WRITE/READ HR data on
    //  an ACTIVE STY01 employee via the Leader scope. Each target here is an ACTIVE employee.
    //  Discriminating: the scope gate fires BEFORE body/header validation, returning 403.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>§26 termination-payout-request WRITE (HROrAbove, terminated-inclusive helper,
    /// LocalHR floor): the mixed HR@STY05 + leader@MIN01 JWT → 403 on an ACTIVE STY01 employee.
    /// Pre-fix the helper admitted the ACTIVE target via the covering Leader scope (the floor was
    /// terminated-only). The scope gate is the first check in the handler, before body parse.</summary>
    [Fact]
    public async Task TerminationPayoutRequestWrite_MixedRoleJwt_ActiveTarget_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync("termpayout");
        var client = ClientWith(MixedHrLeaderToken("mix_termpayout"));

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/admin/employees/{emp}/termination-payout-request")
        {
            Content = JsonContent.Create(new
            {
                entitlementYear = 2024,
                expectedSettlementSequence = 1,
                requestDate = "2026-01-15",
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Settlement-reversal WRITE (HROrAbove, terminated-inclusive helper, LocalHR floor):
    /// the mixed HR@STY05 + leader@MIN01 JWT → 403 on an ACTIVE STY01 employee. Pre-fix the helper
    /// admitted the ACTIVE target via the Leader scope. (The self-target guard does not fire — the
    /// actor and target differ — so the scope gate is the decisive layer.)</summary>
    [Fact]
    public async Task SettlementReversalWrite_MixedRoleJwt_ActiveTarget_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync("settrev");
        var client = ClientWith(MixedHrLeaderToken("mix_settrev"));

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/admin/employees/{emp}/settlement-reversal")
        {
            Content = JsonContent.Create(new
            {
                mode = "BARE",
                entitlementYear = 2024,
                expectedSettlementSequence = 1,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Employment-end-date WRITE (HROrAbove, terminated-inclusive helper, LocalHR floor):
    /// the mixed HR@STY05 + leader@MIN01 JWT → 403 on an ACTIVE STY01 employee. Pre-fix the helper
    /// admitted the ACTIVE target via the Leader scope (the R1 lifecycle/reactivation surface). The
    /// self-target guard does not fire (actor ≠ target); the floored scope gate is the layer.</summary>
    [Fact]
    public async Task EmploymentEndDateWrite_MixedRoleJwt_ActiveTarget_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync("enddatewr");
        var client = ClientWith(MixedHrLeaderToken("mix_enddatewr"));

        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/employees/{emp}/employment-end-date")
        {
            Content = JsonContent.Create(new { employmentEndDate = "2026-03-31" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>Employment-end-date sensitive READ (HROrAbove, terminated-inclusive helper, LocalHR
    /// floor): the mixed HR@STY05 + leader@MIN01 JWT → 403 on an ACTIVE STY01 employee. The end
    /// date never appears in any Employee-facing DTO; pre-fix the helper served it via the Leader
    /// scope on an ACTIVE target.</summary>
    [Fact]
    public async Task EmploymentEndDateRead_MixedRoleJwt_ActiveTarget_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync("enddaterd");
        var client = ClientWith(MixedHrLeaderToken("mix_enddaterd"));

        var rsp = await client.GetAsync($"/api/admin/employees/{emp}/employment-end-date");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>No-regression positive control for the floored terminated-inclusive helper: a
    /// correctly-scoped HR@MIN01 (covers STY01) is NOT 403'd by the LocalHR floor on the sensitive
    /// end-date READ of an ACTIVE STY01 employee — it reads through (200 with an ETag). Proves the
    /// active-target floor does not over-restrict a genuine HR scope.</summary>
    [Fact]
    public async Task EmploymentEndDateRead_CorrectlyScopedHr_ActiveTarget_Returns200()
    {
        var emp = await SeedTargetEmployeeAsync("enddaterdok");
        var client = ClientWith(AdminToken(StatsTidRoles.LocalHR, CoveringOrg, "hr_enddate_ok"));

        var rsp = await client.GetAsync($"/api/admin/employees/{emp}/employment-end-date");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Direct §21 transfer / §24 payout disposition WRITE discrimination (S76 fix-forward
    //  cycle 2, Codex c2 note: the manual-resolve + reconcile-payout HROrAbove writes lacked
    //  direct HTTP tests). Each → 403 for the mixed HR@STY05 + leader@MIN01 JWT on a STY01 row.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>§34/§24 manual-resolve disposition WRITE (HROrAbove, terminated-inclusive helper,
    /// LocalHR floor): the mixed HR@STY05 + leader@MIN01 JWT → 403 on a STY01 employee's
    /// PENDING_REVIEW settlement. Discriminating — pre-fix the covering Leader scope admitted the
    /// disposition write via the helper.</summary>
    [Fact]
    public async Task SettlementManualResolve_MixedRoleJwt_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync("resolveleak");
        var client = ClientWith(MixedHrLeaderToken("mix_resolve"));

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{emp}/VACATION/2024/resolve")
        {
            Content = JsonContent.Create(new { disposition = "FORFEIT" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>§24 reconcile-payout marker WRITE (HROrAbove, terminated-inclusive helper, LocalHR
    /// floor): the mixed HR@STY05 + leader@MIN01 JWT → 403 on a STY01 employee's settled row.
    /// Discriminating — pre-fix the covering Leader scope admitted the reconcile write.</summary>
    [Fact]
    public async Task ReconcilePayout_MixedRoleJwt_Returns403()
    {
        var emp = await SeedTargetEmployeeAsync("reconcileleak");
        var client = ClientWith(MixedHrLeaderToken("mix_reconcile"));

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{emp}/VACATION/2024/reconcile-payout");
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  No-regression positive controls — a correctly-scoped admin/HR still succeeds (the
    //  floor must not over-restrict). A LocalAdmin@MIN01 reads the STY01 tree; an HR@MIN01
    //  validator check on a STY01 employee passes.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>No-regression: a single-scope LocalAdmin@MIN01 (covers STY01) reads the STY01
    /// period-status tree → 200 (the floor admits a genuine admin scope).</summary>
    [Fact]
    public async Task PeriodStatusRead_CorrectlyScopedAdmin_Returns200()
    {
        var client = ClientWith(AdminToken(StatsTidRoles.LocalAdmin, CoveringOrg, "admin_ok"));
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{TargetOrg}/period-status");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>No-regression at the validator: a genuine LocalHR@MIN01 scope still admits a STY01
    /// employee under the LocalHR floor (the floor passes an at-or-above-floor scope), and a genuine
    /// LocalAdmin@MIN01 still admits under the LocalAdmin floor.</summary>
    [Fact]
    public async Task FlooredValidator_CorrectlyScopedAdminAndHr_StillAdmit()
    {
        var emp = await SeedTargetEmployeeAsync();
        var validator = MakeValidator();

        var hr = new ActorContext(
            "hr_ok", StatsTidRoles.LocalHR, Guid.NewGuid(), CoveringOrg,
            new[] { new RoleScope(StatsTidRoles.LocalHR, CoveringOrg, "ORG_AND_DESCENDANTS") });
        Assert.True((await validator.ValidateEmployeeAccessAsync(hr, emp, StatsTidRoles.LocalHR)).Allowed);

        var admin = new ActorContext(
            "admin_ok2", StatsTidRoles.LocalAdmin, Guid.NewGuid(), CoveringOrg,
            new[] { new RoleScope(StatsTidRoles.LocalAdmin, CoveringOrg, "ORG_AND_DESCENDANTS") });
        Assert.True((await validator.ValidateOrgAccessAsync(admin, TargetOrg, StatsTidRoles.LocalAdmin)).Allowed);
        Assert.True((await validator.ValidateEmployeeAccessAsync(admin, emp, StatsTidRoles.LocalAdmin)).Allowed);
    }

    // ─────────────────────────────── clients / tokens / actors ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    /// <summary>The B1 escalation shape, validator twin: primary role = the admin role (highest of
    /// the scopes), anchored in the DISJOINT STY05; plus a LocalLeader scope on MIN01 that COVERS
    /// the STY01 target. Pre-fix the covering Leader scope admitted the target.</summary>
    private static ActorContext AdminAtAPlusLeaderCoveringB(string adminRole, string actorId) => new(
        actorId, adminRole, Guid.NewGuid(), DisjointOrg,
        new[]
        {
            new RoleScope(adminRole, DisjointOrg, "ORG_AND_DESCENDANTS"),
            new RoleScope(StatsTidRoles.LocalLeader, CoveringOrg, "ORG_AND_DESCENDANTS"),
        });

    /// <summary>JWT twin of <see cref="AdminAtAPlusLeaderCoveringB"/> for a LocalAdmin admin role.</summary>
    private static string MixedAdminLeaderToken(string adminRole, string actorId)
    {
        var svc = NewTokenService();
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: adminRole,
            agreementCode: "AC", orgId: DisjointOrg,
            scopes: new[]
            {
                new RoleScope(adminRole, DisjointOrg, "ORG_AND_DESCENDANTS"),
                new RoleScope(StatsTidRoles.LocalLeader, CoveringOrg, "ORG_AND_DESCENDANTS"),
            });
    }

    /// <summary>JWT: HR@STY05 (primary) + LocalLeader@MIN01 (covers STY01).</summary>
    private static string MixedHrLeaderToken(string actorId) =>
        MixedAdminLeaderToken(StatsTidRoles.LocalHR, actorId);

    /// <summary>A token whose ONLY scope is a GLOBAL scope carrying <paramref name="scopeRole"/>,
    /// with primary role <paramref name="primaryRole"/>. Used to exercise the B3 HasGlobalScope
    /// gate: a GLOBAL+LocalLeader scope must NOT pass a GlobalAdmin-only operation even when the
    /// JWT's primary role clears the endpoint's LocalAdminOrAbove policy.</summary>
    private static string GlobalScopeToken(string scopeRole, string primaryRole, string actorId)
    {
        var svc = NewTokenService();
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: primaryRole,
            agreementCode: "AC", orgId: null,
            scopes: new[] { new RoleScope(scopeRole, null, "GLOBAL") });
    }

    /// <summary>A single-scope admin token anchored at <paramref name="orgId"/>.</summary>
    private static string AdminToken(string role, string orgId, string actorId)
    {
        var svc = NewTokenService();
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(role, orgId, "ORG_AND_DESCENDANTS") });
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private OrgScopeValidator MakeValidator()
    {
        var factory = new DbConnectionFactory(_harness.ConnectionString);
        return new OrgScopeValidator(
            new OrganizationRepository(factory),
            new UserRepository(factory),
            NullLogger<OrgScopeValidator>.Instance);
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedTargetEmployeeAsync(string? prefix = null)
    {
        var employeeId = (prefix ?? "emp_s76") + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, TargetOrg, "AC", "OK24", ensureOrg: false);
        return employeeId;
    }

    /// <summary>Seeds a REAL qualifying §24 payout-pending row for <paramref name="employeeId"/>:
    /// <c>SETTLED</c> + <c>payout_days &gt; 0</c> + <c>payout_reconciled_at IS NULL</c> — the exact
    /// shape the <c>/payout-pending</c> list selects. Mirrors
    /// <see cref="TerminatedEmployeeAccessTests"/>'s settlement seed (YEAR_END trigger, minimal
    /// valid snapshot). Used to make the payout-pending mixed-role test DISCRIMINATING: pre-fix the
    /// leader@MIN01 scope unioned STY01 in and this row leaked; floored it is excluded.</summary>
    private async Task SeedQualifyingPayoutRowAsync(string employeeId, int year)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            transferAgreementDays = 0m,
            isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, 'VACATION', @y, 1, 'SETTLED', 'YEAR_END', @snapshot::jsonb, 0, 5, 0, NULL, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        await cmd.ExecuteNonQueryAsync();
    }
}
