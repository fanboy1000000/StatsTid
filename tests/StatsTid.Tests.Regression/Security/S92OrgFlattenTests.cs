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
/// S92 / TASK-9206 + S93 / TASK-9306 — RED-on-old assertions for the ADR-035 org-model flatten
/// AND the flat role-scope mechanism (slice 2).
///
/// <para><b>S92 (slice 1).</b> The org taxonomy flattened from 4 tiers
/// (MINISTRY/STYRELSE/AFDELING/TEAM) to 2 tiers (MAO → ORGANISATION). The former AFDELING/TEAM
/// org rows are gone; their members re-point UP to the parent ORGANISATION (the intended
/// <i>coarsening</i> — Enhed holds no authority, so the smallest authority unit is the
/// Organisation).</para>
///
/// <para><b>S93 (slice 2).</b> The role-scope MECHANISM also flattened: <c>ORG_AND_DESCENDANTS</c>
/// subtree inheritance is DROPPED. <see cref="RoleScope.CoversOrg"/> is now GLOBAL + exact-equality
/// only, and <c>GetAccessibleOrgsAsync</c> returns exactly the assigned (role-floored) org set with
/// NO materialized-path descendant expansion. Coverage is the union of a user's explicit ORG_ONLY
/// rows. The post-S92 MAO-rooted HR rows were EXPANDED to explicit per-Organisation rows
/// (hr01→{STY01,STY02,STY03}; hr02→{STY04,STY05}) so coverage stays IDENTICAL — the proof obligation
/// is: no narrowing AND no widening.</para>
///
/// <para>These tests pin each direction:</para>
/// <list type="bullet">
///   <item>(a) a user under an ORGANISATION HAS that Organisation as its reporting "tree root" —
///         read DIRECTLY from <c>primary_org_id</c> (S95 / ADR-035 slice 4: the recursive tree-WALK
///         <c>ResolveTreeRootOrgIdAsync</c> is RETIRED; the former AFD01→STY02 walk collapses to a
///         direct STY02 home).</item>
///   <item>(b) an <c>ORG_ONLY</c> scope that pre-flatten keyed an afdeling, now re-pointed to the
///         parent ORGANISATION, COVERS the Organisation (the stated coarsening delta).</item>
///   <item>(c) an admin <c>ORG_ONLY</c> scope at the ORGANISATION still REACHES a moved-up report
///         that now sits directly ON that Organisation (exact membership).</item>
///   <item>(d) COVERAGE-IDENTITY (the S93 RED-on-old): hr01's MAO scope EXPANDED to explicit
///         ORG_ONLY rows {STY01,STY02,STY03} reaches EXACTLY {STY01,STY02,STY03} — no narrowing,
///         no widening (and NOT cross-MAO STY05).</item>
///   <item>(e) the <c>org_type</c> CHECK REJECTS an attempt to insert an <c>'AFDELING'</c> row.</item>
/// </list>
///
/// <para>Fixture/JWT conventions mirror <see cref="MixedRoleScopeLeakTests"/> (same WAF harness,
/// the seed org tree MIN01/STY01/STY02/STY03/STY05). The seed orgs are the post-flatten init.sql
/// ones: MIN01/MIN02 = MAO; STY01..STY05 = ORGANISATION.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S92OrgFlattenTests : IAsyncLifetime
{
    // Post-flatten init.sql seed orgs.
    private const string MaoMin01 = "MIN01";          // /MIN01/            — a MAO (former MINISTRY)
    private const string OrgSty01 = "STY01";          // /MIN01/STY01/      — an ORGANISATION under MIN01
    private const string OrgSty02 = "STY02";          // /MIN01/STY02/      — the Organisation the former AFD01/AFD02 collapse into
    private const string OrgSty03 = "STY03";          // /MIN01/STY03/      — the 3rd ORGANISATION under MIN01 (S93: hr01's expanded set)
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
    //  (a) Tree-root identity: a user on an ORGANISATION HAS that Organisation as its home.
    //      S95 / ADR-035 slice 4 — the recursive tree-WALK (ResolveTreeRootOrgIdAsync) is RETIRED.
    //      Post-S92 a user's reporting "tree root" simply IS their primary_org_id (the former walk
    //      always returned the input org at depth 1, since both MAO and ORGANISATION are terminal),
    //      so the equivalence we pin is now a DIRECT primary_org read — no walk.
    //      RED-on-old: pre-flatten a user sat on AFD01 and the walk climbed AFD01→STY02; post-flatten
    //      the user sits directly ON STY02 (an ORGANISATION), and primary_org IS the root.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UserUnderOrganisation_PrimaryOrgIsThatOrganisation()
    {
        // Seed a user directly ON the Organisation STY02.
        var emp = await SeedEmployeeAsync("s92_treeroot", OrgSty02);

        // The user's home (its "tree root" under the flat model) IS its primary_org_id — read it
        // directly, no recursive ancestor walk (the walk is gone).
        var home = await SelectPrimaryOrgAsync(emp);
        Assert.Equal(OrgSty02, home);
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
    //  (c) Preserved reach: an admin ORG_ONLY scope at the ORGANISATION still reaches a moved-up
    //      report. RED-on-old: the report used to sit on AFD01 (under the STY02 subtree);
    //      post-flatten it sits directly ON STY02 and the admin's STY02 ORG_ONLY scope contains it
    //      by exact membership (S93: no subtree branch needed — the report IS on the scoped org).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrgOnly_AtOrganisation_StillReachesMovedUpReport()
    {
        var report = await SeedEmployeeAsync("s92_movedup", OrgSty02);
        var validator = MakeValidator();

        var admin = new ActorContext(
            "s92_admin", StatsTidRoles.LocalAdmin, Guid.NewGuid(), OrgSty02,
            new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgSty02, "ORG_ONLY") });

        // Org-access to the Organisation itself, and employee-access to the moved-up report,
        // both still hold under the floored admin path.
        Assert.True((await validator.ValidateOrgAccessAsync(admin, OrgSty02, StatsTidRoles.LocalAdmin)).Allowed);
        Assert.True((await validator.ValidateEmployeeAccessAsync(admin, report, StatsTidRoles.LocalAdmin)).Allowed);

        // The reach stays BOUNDED to its tree: a DIFFERENT-MAO Organisation is NOT reached
        // (the coarsening widens within the Organisation, it does not leak across MAOs).
        Assert.False((await validator.ValidateOrgAccessAsync(admin, OrgSty05, StatsTidRoles.LocalAdmin)).Allowed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (d) COVERAGE-IDENTITY (the S93 RED-on-old): hr01's post-S92 MAO ORG_AND_DESCENDANTS scope
    //      was EXPANDED to explicit per-Organisation ORG_ONLY rows {STY01,STY02,STY03}. The
    //      accessible-org set must be EXACTLY {STY01,STY02,STY03} — no narrowing (every org under
    //      MIN01 still reached) AND no widening (no descendant expansion, no spurious extras).
    //
    //      RED-on-old: on pre-S93 code GetAccessibleOrgsAsync expanded a MAO ORG_AND_DESCENDANTS
    //      scope via the materialized-path subtree, so a single MIN01 scope returned the subtree
    //      ({MIN01, STY01, STY02, STY03}). Post-S93 it returns EXACTLY the explicit assigned set —
    //      so the test BOTH drops MIN01 (the MAO itself is no longer a member) AND keeps all three
    //      Organisations (the expansion preserved coverage). A one-row MIN01 ORG_ONLY scope would
    //      now reach EXACTLY {MIN01} (no children) — proving the expansion is load-bearing.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CoverageIdentity_Hr01ExpandedScope_ReachesExactlyTheThreeOrganisations()
    {
        var validator = MakeValidator();

        // hr01's post-S93 shape: explicit ORG_ONLY rows over the three Organisations under MIN01.
        var hr = new ActorContext(
            "s92_hr", StatsTidRoles.LocalHR, Guid.NewGuid(), OrgSty01,
            new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, OrgSty01, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgSty02, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgSty03, "ORG_ONLY"),
            });

        var accessible = await validator.GetAccessibleOrgsAsync(hr, StatsTidRoles.LocalHR);
        Assert.NotNull(accessible); // a bounded set (not the GLOBAL unrestricted sentinel)

        // EXACT membership — no narrowing AND no widening.
        var actual = new HashSet<string>(accessible!);
        Assert.Equal(new HashSet<string> { OrgSty01, OrgSty02, OrgSty03 }, actual);

        // Explicitly: every Organisation under MIN01 reached (no narrowing) …
        Assert.Contains(OrgSty01, accessible!);
        Assert.Contains(OrgSty02, accessible!);
        Assert.Contains(OrgSty03, accessible!);
        // … the MAO itself is NOT a member (no subtree key) …
        Assert.DoesNotContain(MaoMin01, accessible!);
        // … and there is NO widening to a different-MAO Organisation (bounded).
        Assert.DoesNotContain(OrgSty05, accessible!);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (d-no-widening) A SINGLE MAO-keyed ORG_ONLY scope reaches EXACTLY {MAO} — no descendant
    //      expansion. This is the sharpest no-widening pin: it proves GetAccessibleOrgsAsync no
    //      longer walks the subtree, which is WHY the seed had to expand hr01 to explicit rows.
    //      (Such a MAO-typed grant is rejected at the grant endpoint by OQ1; this exercises the
    //      validator directly on a force-constructed scope.)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoWidening_SingleMaoScope_ReachesExactlyTheMaoOnly_NoDescendants()
    {
        var validator = MakeValidator();

        var hr = new ActorContext(
            "s92_hr_mao", StatsTidRoles.LocalHR, Guid.NewGuid(), MaoMin01,
            new[] { new RoleScope(StatsTidRoles.LocalHR, MaoMin01, "ORG_ONLY") });

        var accessible = await validator.GetAccessibleOrgsAsync(hr, StatsTidRoles.LocalHR);
        Assert.NotNull(accessible);

        // Exactly the MAO — the children STY01/STY02/STY03 are NOT pulled in.
        Assert.Equal(new HashSet<string> { MaoMin01 }, new HashSet<string>(accessible!));
        Assert.DoesNotContain(OrgSty01, accessible!);
        Assert.DoesNotContain(OrgSty02, accessible!);
        Assert.DoesNotContain(OrgSty03, accessible!);
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

    private async Task<string?> SelectPrimaryOrgAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT primary_org_id FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
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
