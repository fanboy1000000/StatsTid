using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S127 / TASK-12701a — AC-14a for the GENERATED world: every organisation the demo seed creates
/// carries an active project catalogue.
///
/// <para>Why this is a real invariant and not decoration: projects are strictly org-scoped
/// (<c>ProjectRepository.GetByOrgAsync</c> filters on <c>org_id</c> AND <c>is_active</c>), so an
/// employee homed in a project-less org sees an EMPTY project list — not a filtered one. Under the
/// S127 submit-time allocation gate that employee can never send a month. Measured against the live
/// demo database before this task: 13 organisations, 2 with any projects, 41% of active users
/// stranded.</para>
/// </summary>
public sealed class ProjectSeedingTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    private static DemoDataset Gen(string scale) => new DemoGenerator(scale, 42, Ref).Generate();

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryOrg_HasAtLeastOneProject(string scale)
    {
        var ds = Gen(scale);
        var byOrg = ds.Projects.GroupBy(p => p.OrgId).ToDictionary(g => g.Key, g => g.Count());

        foreach (var org in ds.Orgs)
            Assert.True(byOrg.TryGetValue(org.OrgId, out var count) && count > 0,
                $"org {org.OrgId} ({org.OrgType}) has no projects — its employees could never allocate their hours");
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryActiveUsersOwnOrg_HasProjects_IncludingTheMaoWhereDemoAdminHomes(string scale)
    {
        var ds = Gen(scale);
        var orgsWithProjects = ds.Projects.Select(p => p.OrgId).ToHashSet(StringComparer.Ordinal);

        foreach (var user in ds.Users.Where(u => u.IsActive))
            Assert.Contains(user.PrimaryOrgId, orgsWithProjects);

        // demo_admin is NOT in ds.Users — the emitter writes it separately, homed on the MAO root
        // (SqlEmitter's minId = Orgs[0]). It is an active user like any other, so the MAO needs
        // projects too; this is the case a "join users to orgs" reading of AC-14a would miss.
        var mao = ds.Orgs.Single(o => o.OrgType == "MAO");
        Assert.Contains(mao.OrgId, orgsWithProjects);
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void ProjectCodes_AreUniqueWithinAnOrg(string scale)
    {
        // The projects table carries UNIQUE (org_id, project_code); a duplicate would be silently
        // dropped by the emitter's ON CONFLICT DO NOTHING, shrinking a catalogue without a word.
        var ds = Gen(scale);
        foreach (var group in ds.Projects.GroupBy(p => p.OrgId))
        {
            var codes = group.Select(p => p.ProjectCode).ToList();
            Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryProject_NamesAnOrgTheSeedActuallyCreates(string scale)
    {
        // A project row pointing at a missing org would violate the projects.org_id FK and abort the
        // whole seed file at container boot.
        var ds = Gen(scale);
        var orgIds = ds.Orgs.Select(o => o.OrgId).ToHashSet(StringComparer.Ordinal);
        foreach (var p in ds.Projects)
            Assert.Contains(p.OrgId, orgIds);
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EmittedSql_CarriesEveryProjectRow(string scale)
    {
        var ds = Gen(scale);
        var sql = SqlEmitter.Emit(ds);

        Assert.Contains("INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by) VALUES", sql);

        // Every generated project reaches the artifact — an emitter that silently dropped rows
        // (or emitted only the first org's) would still satisfy a mere "contains INSERT" check.
        foreach (var p in ds.Projects)
            Assert.Contains($"('{p.OrgId}', '{p.ProjectCode}', '{p.ProjectName}', {p.SortOrder}, 'DEMO_SEED')", sql);

        var emittedRows = sql
            .Split('\n')
            .Count(l => l.TrimStart().StartsWith("('", StringComparison.Ordinal)
                        && l.Contains("'DEMO_SEED')", StringComparison.Ordinal)
                        && l.Contains(", 'Daglig drift', ", StringComparison.Ordinal));
        Assert.Equal(ds.Orgs.Count, emittedRows); // exactly one 'Daglig drift' row per org
    }

    [Fact]
    public void Generator_FailsLoudly_WhenAnOrgHasNoProjects()
    {
        // The RED case, driven through the REAL assertion. The generator can never produce this
        // state today, so the only way to prove the guard bites is to hand it a thinned catalogue.
        // Without this the guard could be deleted and every other test here would stay green.
        var ds = Gen("smoke");
        var orphanOrg = ds.Orgs.Last().OrgId;
        var thinned = ds.Projects.Where(p => p.OrgId != orphanOrg).ToList();

        var ex = Assert.Throws<InvalidOperationException>(
            () => DemoGenerator.AssertEveryOrgHasProjects(ds.Orgs, thinned));
        Assert.Contains(orphanOrg, ex.Message, StringComparison.Ordinal);

        // …and the intact catalogue passes, so the guard is not simply always-throwing.
        DemoGenerator.AssertEveryOrgHasProjects(ds.Orgs, ds.Projects);
    }

    [Fact]
    public void Catalogue_HasAtLeastTwoProjects_SoASplitDayCannotBookTheSameCodeTwice()
    {
        // AllocatedMonthBuilder splits every third day across two codes; with a one-project
        // catalogue it would have to fall back to a whole-day booking. Pinning the floor here keeps
        // the split path reachable in the generated world.
        Assert.True(ProjectCatalog.PerOrg.Length >= 2);
        Assert.Equal(ProjectCatalog.PerOrg.Length,
            ProjectCatalog.PerOrg.Select(t => t.Code).Distinct(StringComparer.Ordinal).Count());
    }
}
