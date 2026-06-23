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
/// S93 / TASK-9306 — flat role-scope RED-on-old assertions (ADR-035 slice 2).
///
/// <para><b>The change under test.</b> <c>ORG_AND_DESCENDANTS</c> subtree inheritance is dropped.
/// Coverage is now exact Organisation-set membership: the union of a user's explicit ORG_ONLY rows,
/// with NO materialized-path descendant expansion. The <c>scope_type</c> CHECK collapsed to
/// <c>('GLOBAL','ORG_ONLY')</c>, and a non-GLOBAL grant's org_id must be an ORGANISATION (a MAO is
/// rejected — OQ1; that gate is pinned at the grant endpoint in
/// <see cref="StatsTid.Tests.Regression.Admin.RoleAssignmentGrantRevokeEndpointTests"/>).</para>
///
/// <para>The post-S92 MAO-rooted seed HR rows were EXPANDED to explicit per-Organisation rows so
/// coverage stays IDENTICAL (hr01→{STY01,STY02,STY03}; hr02→{STY04,STY05}). The proof obligation is:
/// NO narrowing AND NO widening. These tests pin:</para>
/// <list type="bullet">
///   <item>(a) the <c>scope_type</c> CHECK REJECTS an <c>ORG_AND_DESCENDANTS</c> role_assignments row.</item>
///   <item>(c) a multi-row {STY01,STY02} ORG_ONLY assignment covers BOTH, and an Organisation-X
///         ORG_ONLY scope does NOT cover a different Organisation Y.</item>
///   <item>(d) coverage-identity: hr01-expanded reaches exactly {STY01,STY02,STY03}, hr02-expanded
///         exactly {STY04,STY05}.</item>
///   <item>(e) no-widening: hr01 does NOT reach cross-MAO STY04/STY05.</item>
///   <item>(f) <c>GetAccessibleOrgsAsync</c> returns the EXACT assigned set (no descendants).</item>
/// </list>
///
/// <para>Org tree (post-S92 init.sql): MIN01 (MAO) ⊃ {STY01,STY02,STY03}; MIN02 (MAO) ⊃ {STY04,STY05}.
/// Fixture/JWT conventions mirror <see cref="MixedRoleScopeLeakTests"/> / <see cref="S92OrgFlattenTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S93FlatRoleScopeTests : IAsyncLifetime
{
    // Post-S92 init.sql seed orgs.
    private const string MaoMin01 = "MIN01";   // MAO
    private const string OrgSty01 = "STY01";   // ORGANISATION under MIN01
    private const string OrgSty02 = "STY02";   // ORGANISATION under MIN01
    private const string OrgSty03 = "STY03";   // ORGANISATION under MIN01
    private const string MaoMin02 = "MIN02";   // MAO (different authority)
    private const string OrgSty04 = "STY04";   // ORGANISATION under MIN02
    private const string OrgSty05 = "STY05";   // ORGANISATION under MIN02

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
    //  (a) The scope_type CHECK rejects an ORG_AND_DESCENDANTS row.
    //      RED-on-old: pre-S93 the CHECK was IN ('GLOBAL','ORG_ONLY','ORG_AND_DESCENDANTS') and
    //      this INSERT succeeded. Post-S93 the CHECK is IN ('GLOBAL','ORG_ONLY') → check_violation.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScopeTypeCheck_RejectsOrgAndDescendantsRow()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES ('s93_check', 'LOCAL_HR', 'STY01', 'ORG_AND_DESCENDANTS', 'TEST')
            """, conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23514", ex.SqlState); // check_violation — scope_type IN ('GLOBAL','ORG_ONLY')
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (c) A multi-row {STY01,STY02} ORG_ONLY assignment covers BOTH; an Organisation-X ORG_ONLY
    //      scope does NOT cover a different Organisation Y (exact membership, no subtree).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultiRowOrgOnly_CoversEveryAssignedOrganisation_AndNoOther()
    {
        var validator = MakeValidator();

        var hr = new ActorContext(
            "s93_multi", StatsTidRoles.LocalHR, Guid.NewGuid(), OrgSty01,
            new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, OrgSty01, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgSty02, "ORG_ONLY"),
            });

        // Both assigned Organisations are covered.
        Assert.True((await validator.ValidateOrgAccessAsync(hr, OrgSty01, StatsTidRoles.LocalHR)).Allowed);
        Assert.True((await validator.ValidateOrgAccessAsync(hr, OrgSty02, StatsTidRoles.LocalHR)).Allowed);

        // A non-assigned sibling Organisation under the SAME MAO is NOT covered (no subtree, no
        // sibling spill).
        Assert.False((await validator.ValidateOrgAccessAsync(hr, OrgSty03, StatsTidRoles.LocalHR)).Allowed);
        // The parent MAO is NOT covered.
        Assert.False((await validator.ValidateOrgAccessAsync(hr, MaoMin01, StatsTidRoles.LocalHR)).Allowed);
        // A cross-MAO Organisation is NOT covered.
        Assert.False((await validator.ValidateOrgAccessAsync(hr, OrgSty05, StatsTidRoles.LocalHR)).Allowed);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (d)+(e)+(f) Coverage-identity + no-widening for hr01 and hr02 (the two expanded seed rows).
    //      hr01-expanded reaches EXACTLY {STY01,STY02,STY03}; hr02-expanded EXACTLY {STY04,STY05}.
    //      Neither widens across the MAO boundary, and GetAccessibleOrgsAsync returns the exact
    //      assigned set (no descendant expansion).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CoverageIdentity_Hr01Expanded_ReachesExactlySty010203_NotCrossMao()
    {
        var validator = MakeValidator();

        // hr01's post-S93 explicit shape.
        var hr01 = new ActorContext(
            "s93_hr01", StatsTidRoles.LocalHR, Guid.NewGuid(), OrgSty01,
            new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, OrgSty01, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgSty02, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgSty03, "ORG_ONLY"),
            });

        var accessible = await validator.GetAccessibleOrgsAsync(hr01, StatsTidRoles.LocalHR);
        Assert.NotNull(accessible);

        // (f) EXACT assigned set — no descendants, no extras.
        Assert.Equal(
            new HashSet<string> { OrgSty01, OrgSty02, OrgSty03 },
            new HashSet<string>(accessible!));

        // (e) no-widening: NOT the cross-MAO MIN02 Organisations.
        Assert.DoesNotContain(OrgSty04, accessible!);
        Assert.DoesNotContain(OrgSty05, accessible!);
        // And not the parent MAO itself.
        Assert.DoesNotContain(MaoMin01, accessible!);
    }

    [Fact]
    public async Task CoverageIdentity_Hr02Expanded_ReachesExactlySty0405_NotCrossMao()
    {
        var validator = MakeValidator();

        // hr02's post-S93 explicit shape: {STY04, STY05} under MIN02.
        var hr02 = new ActorContext(
            "s93_hr02", StatsTidRoles.LocalHR, Guid.NewGuid(), OrgSty04,
            new[]
            {
                new RoleScope(StatsTidRoles.LocalHR, OrgSty04, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgSty05, "ORG_ONLY"),
            });

        var accessible = await validator.GetAccessibleOrgsAsync(hr02, StatsTidRoles.LocalHR);
        Assert.NotNull(accessible);

        // (d)+(f) EXACT assigned set.
        Assert.Equal(
            new HashSet<string> { OrgSty04, OrgSty05 },
            new HashSet<string>(accessible!));

        // (e) no-widening: NOT the cross-MAO MIN01 Organisations.
        Assert.DoesNotContain(OrgSty01, accessible!);
        Assert.DoesNotContain(OrgSty02, accessible!);
        Assert.DoesNotContain(OrgSty03, accessible!);
        Assert.DoesNotContain(MaoMin02, accessible!);
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
}
