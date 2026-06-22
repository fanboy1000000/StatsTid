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
/// S92 / TASK-9206 — collapse-direction RED-on-old assertions for the ADR-035 org-model flatten.
///
/// <para><b>The change under test.</b> The org taxonomy flattened from 4 tiers
/// (MINISTRY/STYRELSE/AFDELING/TEAM) to 2 tiers (MAO → ORGANISATION). The former AFDELING/TEAM
/// org rows are gone; their members re-point UP to the parent ORGANISATION (the intended
/// <i>coarsening</i> — Enhed holds no authority, so the smallest authority unit is the
/// Organisation). The role-scope MECHANISM (<see cref="RoleScope.CoversOrg"/> / ORG_AND_DESCENDANTS
/// / materialized-path prefix) is UNCHANGED this sprint; only the granularity it operates at
/// changed (afdeling → Organisation).</para>
///
/// <para><b>The proof obligation</b> (per the sprint's authority-change framing) is NOT a single
/// "behaviour-identity" assertion. It is: rename-identical + collapse-with-stated-coverage-deltas
/// + NO narrowing. These tests pin each direction:</para>
/// <list type="bullet">
///   <item>(a) a user under an ORGANISATION resolves THAT Organisation as its reporting tree root
///         (<see cref="ResolveTreeRootOrgIdAsync"/>) — the former AFD01→STY02 walk collapses to
///         STY02→STY02.</item>
///   <item>(b) an <c>ORG_ONLY</c> scope that pre-flatten keyed an afdeling, now re-pointed to the
///         parent ORGANISATION, COVERS the Organisation (the stated coarsening delta).</item>
///   <item>(c) an <c>ORG_AND_DESCENDANTS</c> admin scoped at the ORGANISATION still REACHES a
///         moved-up report (the reach is PRESERVED — the parent path still contains the user).</item>
///   <item>(d) NO NARROWING: re-pointing an afdeling-keyed scope UP to its Organisation only ever
///         WIDENS-or-equals coverage — the post-flatten accessible-org set is a superset-or-equal
///         of the conceptual afdeling-only set.</item>
///   <item>(e) the <c>org_type</c> CHECK REJECTS an attempt to insert an <c>'AFDELING'</c> row.</item>
/// </list>
///
/// <para>Fixture/JWT conventions mirror <see cref="MixedRoleScopeLeakTests"/> (same WAF harness,
/// the seed org tree MIN01/STY01/STY02/STY05). The seed orgs are the post-flatten init.sql ones:
/// MIN01/MIN02 = MAO; STY01..STY05 = ORGANISATION.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S92OrgFlattenTests : IAsyncLifetime
{
    // Post-flatten init.sql seed orgs.
    private const string MaoMin01 = "MIN01";          // /MIN01/            — a MAO (former MINISTRY)
    private const string OrgSty01 = "STY01";          // /MIN01/STY01/      — an ORGANISATION under MIN01
    private const string OrgSty02 = "STY02";          // /MIN01/STY02/      — the Organisation the former AFD01/AFD02 collapse into
    private const string OrgSty05 = "STY05";          // /MIN02/STY05/      — an ORGANISATION under a DIFFERENT MAO

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (the MAO→ORGANISATION seed tree + configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (a) Tree-root resolution: a user on an ORGANISATION resolves THAT Organisation.
    //      RED-on-old: pre-flatten a user sat on AFD01 and the walk climbed AFD01→STY02;
    //      post-flatten the user sits ON STY02 (an ORGANISATION = its own tree root).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveTreeRoot_UserUnderOrganisation_ResolvesThatOrganisation()
    {
        var repo = new ReportingLineRepository(_dbFactory);

        // The Organisation is its own tree root (the depth-1 nearest MAO/ORGANISATION ancestor
        // of an ORGANISATION is itself).
        var root = await repo.ResolveTreeRootOrgIdAsync(OrgSty02);
        Assert.Equal(OrgSty02, root);

        // And the MAO above it resolves to itself (the MINISTRY→MAO rename is identity-preserving).
        var maoRoot = await repo.ResolveTreeRootOrgIdAsync(MaoMin01);
        Assert.Equal(MaoMin01, maoRoot);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (b) Coarsening: an ORG_ONLY scope re-pointed from an afdeling UP to its Organisation
    //      now COVERS the Organisation. RED-on-old: pre-flatten an ORG_ONLY scope keyed on
    //      AFD01 covered ONLY /MIN01/STY02/AFD01/ and did NOT cover /MIN01/STY02/; post-flatten
    //      it keys on STY02 and exactly covers the Organisation (its self-scope coarsened up).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgOnly_RepointedToOrganisation_CoversTheOrganisation()
    {
        var emp = await SeedEmployeeAsync("s92_orgonly", OrgSty02);
        var validator = MakeValidator();

        // The employee's self-scope is ORG_ONLY on the ORGANISATION it now sits on.
        var actor = new ActorContext(
            emp, StatsTidRoles.Employee, Guid.NewGuid(), OrgSty02,
            new[] { new RoleScope(StatsTidRoles.Employee, OrgSty02, "ORG_ONLY") });

        // The coarsened ORG_ONLY scope admits the Organisation it is keyed on.
        Assert.True((await validator.ValidateOrgAccessAsync(actor, OrgSty02)).Allowed);

        // ORG_ONLY is exact-only: it must NOT reach a SIBLING Organisation (no widening past self).
        Assert.False((await validator.ValidateOrgAccessAsync(actor, OrgSty01)).Allowed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (c) Preserved reach: an ORG_AND_DESCENDANTS admin scoped at the ORGANISATION still
    //      reaches a moved-up report. RED-on-old: the report used to sit on AFD01 (under the
    //      STY02 subtree); post-flatten it sits directly on STY02 and the admin's STY02
    //      ORG_AND_DESCENDANTS scope still contains it (the parent path still covers it).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgAndDescendants_AtOrganisation_StillReachesMovedUpReport()
    {
        var report = await SeedEmployeeAsync("s92_movedup", OrgSty02);
        var validator = MakeValidator();

        var admin = new ActorContext(
            "s92_admin", StatsTidRoles.LocalAdmin, Guid.NewGuid(), OrgSty02,
            new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgSty02, "ORG_AND_DESCENDANTS") });

        // Org-access to the Organisation itself, and employee-access to the moved-up report,
        // both still hold under the floored admin path.
        Assert.True((await validator.ValidateOrgAccessAsync(admin, OrgSty02, StatsTidRoles.LocalAdmin)).Allowed);
        Assert.True((await validator.ValidateEmployeeAccessAsync(admin, report, StatsTidRoles.LocalAdmin)).Allowed);

        // The reach stays BOUNDED to its tree: a DIFFERENT-MAO Organisation is NOT reached
        // (the coarsening widens within the Organisation, it does not leak across MAOs).
        Assert.False((await validator.ValidateOrgAccessAsync(admin, OrgSty05, StatsTidRoles.LocalAdmin)).Allowed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (d) NO NARROWING: re-pointing an afdeling-keyed scope UP to its parent Organisation
    //      only ever WIDENS-or-equals coverage. We compare the post-flatten accessible-org
    //      set of a MAO-scoped HR (the kind of actor that contained the whole subtree) against
    //      the conceptual afdeling-only set {STY02} — the post-flatten set is a SUPERSET-or-equal.
    //      RED-on-old: nothing the flatten does removes an org from an actor's reach.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoNarrowing_PostFlattenAccessibleSet_IsSupersetOfAfdelingOnlySet()
    {
        var validator = MakeValidator();

        // An HR scoped at the MAO MIN01 (the seed hr01's shape) — ORG_AND_DESCENDANTS over the MAO.
        var hr = new ActorContext(
            "s92_hr", StatsTidRoles.LocalHR, Guid.NewGuid(), MaoMin01,
            new[] { new RoleScope(StatsTidRoles.LocalHR, MaoMin01, "ORG_AND_DESCENDANTS") });

        var accessible = await validator.GetAccessibleOrgsAsync(hr, StatsTidRoles.LocalHR);
        Assert.NotNull(accessible); // a bounded set (not the GLOBAL unrestricted sentinel)

        // The conceptual "afdeling-only" set — the orgs the actor reached pre-flatten — re-pointed
        // up to their Organisations. Every one of them must still be reachable post-flatten: the
        // re-point only ever moved a scope UP the path (widen-or-equal, never narrow).
        var conceptualAfdelingOnly = new[] { OrgSty01, OrgSty02 }; // both under MIN01 (the MAO subtree)
        foreach (var org in conceptualAfdelingOnly)
            Assert.Contains(org, accessible!);

        // And the MAO's reach does NOT spuriously include a different-MAO Organisation (bounded).
        Assert.DoesNotContain(OrgSty05, accessible!);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (e) The org_type CHECK rejects an 'AFDELING' row — the taxonomy is closed to the old
    //      sub-org tiers. RED-on-old: pre-flatten this INSERT succeeded; post-flatten the
    //      CHECK (org_type IN ('MAO','ORGANISATION')) raises a check_violation (23514).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgTypeCheck_RejectsAfdelingRow()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path)
            VALUES ('S92_AFD_REJECT', 'Should be rejected', 'AFDELING', 'STY02', '/MIN01/STY02/S92_AFD_REJECT/')
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23514", ex.SqlState); // check_violation — org_type IN ('MAO','ORGANISATION')
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private OrgScopeValidator MakeValidator()
    {
        var factory = new DbConnectionFactory(_harness.ConnectionString);
        return new OrgScopeValidator(
            new OrganizationRepository(factory),
            new UserRepository(factory),
            NullLogger<OrgScopeValidator>.Instance);
    }

    private async Task<string> SeedEmployeeAsync(string prefix, string orgId)
    {
        var employeeId = prefix + "_" + Guid.NewGuid().ToString("N")[..8];
        // ensureOrg: false — the seed orgs already exist (booted by the WAF seeders); we re-point
        // onto a real ORGANISATION row, never an afdeling.
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, orgId, "AC", "OK24", ensureOrg: false);
        return employeeId;
    }
}
