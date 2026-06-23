using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S84 / TASK-8401 — structural-realism + isolation invariants on the generated manifest/dataset:
/// one root per tree, no cycles, span in band, 12–18% managers, id-disjoint from the baseline,
/// agreement mix within tolerance.
///
/// <para>S92 / ADR-035 flatten: the demo org tree is now 2 LEVELS — MAO (depth 0, root) →
/// ORGANISATION (depth 1, the smallest authority unit). The former AFDELING/TEAM leaf orgs are
/// gone (their unit names became display-only enhed labels), so the org-depth invariant asserts
/// exactly 2 levels, and the baseline-disjointness set no longer carries AFD0x org ids.</para>
/// </summary>
public sealed class GeneratorStructuralTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    private static DemoDataset Gen(string scale) => new DemoGenerator(scale, 42, Ref).Generate();

    // S92 / ADR-035 flatten: the init.sql baseline orgs are now MAO (MIN0x) + ORGANISATION (STY0x)
    // only — the 5 former AFDELING rows (AFD0x) were removed.
    private static readonly string[] BaselineOrgIds =
        { "MIN01", "MIN02", "STY01", "STY02", "STY03", "STY04", "STY05" };
    private static readonly string[] BaselineUserIds =
        { "admin01", "admin02", "ladm01", "ladm02", "hr01", "hr02", "mgr01", "mgr02", "mgr03",
          "emp001", "emp002", "emp003", "emp004", "emp005", "emp006", "emp007", "emp008", "emp009", "emp010" };

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EachTree_HasExactlyOneReportingRoot(string scale)
    {
        var ds = Gen(scale);
        foreach (var tree in ds.Manifest.Trees)
        {
            var edges = ds.Manifest.ReportingEdges.Where(e => e.OrganisationId == tree.OrganisationId).ToList();
            var employees = edges.Select(e => e.EmployeeId).ToHashSet();
            // A root = a manager that never appears as an employee in this tree.
            var roots = edges.Select(e => e.ManagerId).Where(m => !employees.Contains(m)).ToHashSet();
            Assert.True(roots.Count == 1, $"tree {tree.OrganisationId} has {roots.Count} roots: {string.Join(",", roots)}");
            Assert.Equal(tree.RootEmployeeId, roots.Single());
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void ReportingEdges_HaveNoCycles(string scale)
    {
        var ds = Gen(scale);
        // Build employee→manager map; walk up from every employee; any revisit ⇒ cycle.
        var parent = ds.Manifest.ReportingEdges.ToDictionary(e => e.EmployeeId, e => e.ManagerId);
        foreach (var start in parent.Keys)
        {
            var seen = new HashSet<string> { start };
            var cur = start;
            while (parent.TryGetValue(cur, out var next))
            {
                Assert.True(seen.Add(next), $"cycle detected starting at {start} (revisited {next})");
                cur = next;
                if (seen.Count > 100_000) break; // safety
            }
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void OrgHierarchy_IsExactlyTwoLevels(string scale)
    {
        var ds = Gen(scale);
        // S92 / ADR-035 flatten: the org tree is 2 levels — MAO (depth 0, root) → ORGANISATION
        // (depth 1, the smallest authority unit). No AFDELING/TEAM leaf orgs remain.
        var maxOrgDepth = ds.Orgs.Max(o => o.Depth); // 0=MAO, 1=ORGANISATION
        Assert.Equal(1, maxOrgDepth);
        Assert.Contains(ds.Orgs, o => o.Depth == 0); // the MAO root exists
        Assert.All(ds.Orgs, o => Assert.InRange(o.Depth, 0, 1));
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void ManagerRatio_IsWithin10To20Percent(string scale)
    {
        var ds = Gen(scale);
        foreach (var tree in ds.Manifest.Trees)
        {
            var ratio = (double)tree.ManagerCount / tree.UserCount;
            Assert.InRange(ratio, 0.08, 0.25); // a band around the 12–18% target (small trees vary more)
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void Spans_AreInReasonableBand(string scale)
    {
        var ds = Gen(scale);
        // Compute direct-report counts per manager.
        var span = ds.Manifest.ReportingEdges
            .GroupBy(e => e.ManagerId)
            .Select(g => g.Count())
            .ToList();
        Assert.NotEmpty(span);
        var avg = span.Average();
        Assert.InRange(avg, 2.0, 12.0); // ~TargetSpan (4 smoke / 7 full) with rounding tolerance
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void DemoIds_AreDisjointFromBaseline(string scale)
    {
        var ds = Gen(scale);
        foreach (var o in ds.Orgs)
            Assert.DoesNotContain(o.OrgId, BaselineOrgIds);
        foreach (var u in ds.Users)
            Assert.DoesNotContain(u.UserId, BaselineUserIds);
        Assert.DoesNotContain(ds.Manifest.AdminUserId, BaselineUserIds);
    }

    [Fact]
    public void Full_AgreementMix_BigTree_WithinTolerance()
    {
        var ds = Gen("full");
        var bigTreeUsers = ds.Users.Where(u => u.OrganisationId == "STYX1").ToList();
        Assert.NotEmpty(bigTreeUsers);
        double ac = bigTreeUsers.Count(u => u.AgreementCode == "AC") / (double)bigTreeUsers.Count;
        double hk = bigTreeUsers.Count(u => u.AgreementCode == "HK") / (double)bigTreeUsers.Count;
        double prosa = bigTreeUsers.Count(u => u.AgreementCode == "PROSA") / (double)bigTreeUsers.Count;
        // Target 55/35/10 ± 8pp (large sample, low variance).
        Assert.InRange(ac, 0.47, 0.63);
        Assert.InRange(hk, 0.27, 0.43);
        Assert.InRange(prosa, 0.04, 0.18);
    }

    [Fact]
    public void Full_ProducesFiveTrees_AndApproximateTotalHeadcount()
    {
        var ds = Gen("full");
        Assert.Equal(5, ds.Manifest.Trees.Count);
        // ~3,350 target ± a small generation tolerance.
        Assert.InRange(ds.Users.Count, 3200, 3500);
    }

    [Fact]
    public void Smoke_IsSmall()
    {
        var ds = Gen("smoke");
        Assert.Single(ds.Manifest.Trees);
        Assert.InRange(ds.Users.Count, 20, 60);
        Assert.InRange(ds.Orgs.Count, 2, 12); // S92 flatten: MAO + one ORGANISATION per tree
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryUser_HasBulkEmployeeRole(string scale)
    {
        var ds = Gen(scale);
        var roleUsers = ds.EmployeeRoles.Select(r => r.UserId).ToHashSet();
        foreach (var u in ds.Users)
            Assert.Contains(u.UserId, roleUsers);
        Assert.All(ds.EmployeeRoles, r => Assert.Equal("EMPLOYEE", r.RoleId));
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void PrivilegedRoles_CoverEveryActiveManager_PlusOneHrPerTree(string scale)
    {
        var ds = Gen(scale);
        var activeManagers = ds.Users.Where(u => u.IsManager && u.IsActive).Select(u => u.UserId).ToHashSet();
        var leaderRoleUsers = ds.PrivilegedRoles.Where(r => r.RoleId == "LOCAL_LEADER").Select(r => r.UserId).ToHashSet();
        foreach (var m in activeManagers)
            Assert.Contains(m, leaderRoleUsers);

        var hrCount = ds.PrivilegedRoles.Count(r => r.RoleId == "LOCAL_HR");
        Assert.Equal(ds.Manifest.Trees.Count, hrCount); // one LOCAL_HR per tree root

        // No privileged role targets an inactive (leaver) user.
        var inactiveIds = ds.Users.Where(u => !u.IsActive).Select(u => u.UserId).ToHashSet();
        Assert.All(ds.PrivilegedRoles, r => Assert.DoesNotContain(r.UserId, inactiveIds));
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void ActivityAndProfiles_OnlyTargetActiveUsers(string scale)
    {
        var ds = Gen(scale);
        var activeIds = ds.Users.Where(u => u.IsActive).Select(u => u.UserId).ToHashSet();
        foreach (var a in ds.Manifest.Activity)
            Assert.Contains(a.EmployeeId, activeIds);
        foreach (var p in ds.Manifest.ProfileEdits)
            Assert.Contains(p.EmployeeId, activeIds);
    }

    [Fact]
    public void Full_HasLeavers_And60PlusUsers()
    {
        var ds = Gen("full");
        Assert.Contains(ds.Users, u => u.EmploymentEndDate is not null && !u.IsActive);
        var sixtyPlus = ds.Users.Count(u =>
        {
            var dob = DateOnly.Parse(u.BirthDate);
            var age = Ref.Year - dob.Year - (Ref.DayOfYear < dob.DayOfYear ? 1 : 0);
            return age >= 60;
        });
        Assert.True(sixtyPlus > 0, "expected some 60+ users for senior-day surfaces");
    }

    [Fact]
    public void MessyCases_CoverAllScriptedKinds()
    {
        var ds = Gen("full");
        var kinds = ds.Manifest.MessyCases.Select(m => m.Kind).ToHashSet();
        Assert.Contains("OK_TRANSITION", kinds);
        Assert.Contains("AGREEMENT_CHANGE", kinds);
        Assert.Contains("CROSS_STYRELSE_TRANSFER", kinds);
        Assert.Contains("ODD_PART_TIME", kinds);
        Assert.InRange(ds.Manifest.MessyCases.Count, 20, 30);
    }
}
