using System.Text.Json;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S114 / TASK-11400 — invariants over the derived unit spine (ADR-038): depth histogram 0–4
/// exactly, all 5 types per org, single-unit-membership partition, leader-is-member,
/// PARTIAL-RANK validity, per-parent name uniqueness, the deliberate-messiness ledger
/// (exact + disjoint + non-manager movers), derivation determinism, and the generation-time
/// depth-assertion RED case.
/// </summary>
public sealed class UnitDerivationTests
{
    private static readonly DateOnly Ref = new(2026, 6, 15);

    private static DemoDataset Gen(string scale) => new DemoGenerator(scale, 42, Ref).Generate();

    private static readonly string[] TypeByDepth = { "direktion", "omrade", "kontor", "team", "enhed" };

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryOverrideOrg_HasManagerDepths0Through4_Exactly(string scale)
    {
        var ds = Gen(scale);
        Assert.NotNull(ds.Manifest.UnitPlans);
        Assert.Equal(ds.Manifest.Trees.Count, ds.Manifest.UnitPlans!.Count); // every shipped tree is overridden
        foreach (var plan in ds.Manifest.UnitPlans)
        {
            Assert.Equal(5, plan.ManagersPerDepth.Count);
            Assert.All(plan.ManagersPerDepth, c => Assert.True(c >= 1, $"{plan.OrganisationId}: empty depth layer"));
            Assert.Equal(4, plan.Units.Max(u => u.Depth)); // capped at 4 — never an enhed under an enhed
        }
    }

    [Fact]
    public void ShippedScales_DepthHistograms_AreThePinnedLayerings()
    {
        // DELIBERATE pins of the shipped output (manager counts = round(activeN × 0.14) for full;
        // the smoke ManagerCountOverride 6). A layering change is a decision, not drift.
        var smoke = Gen("smoke").Manifest.UnitPlans!;
        Assert.Equal(new[] { 1, 2, 1, 1, 1 }, smoke.Single().ManagersPerDepth);

        var full = Gen("full").Manifest.UnitPlans!.ToDictionary(p => p.OrganisationId, p => p.ManagersPerDepth);
        Assert.Equal(new[] { 1, 4, 16, 64, 185 }, full["STYX1"]); // span 4, 270 active managers
        Assert.Equal(new[] { 1, 3, 9, 27, 41 }, full["STYX2"]);  // span 3, 81
        Assert.Equal(new[] { 1, 2, 4, 8, 19 }, full["STYX3"]);   // span 2, 34
        Assert.Equal(new[] { 1, 2, 4, 8, 19 }, full["STYX4"]);   // span 2, 34
        Assert.Equal(new[] { 1, 2, 4, 8, 18 }, full["STYX5"]);   // span 2, 33
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryOrg_HasAllFiveUnitTypes(string scale)
    {
        var ds = Gen(scale);
        foreach (var plan in ds.Manifest.UnitPlans!)
        {
            var types = plan.Units.Select(u => u.Type).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(new HashSet<string>(TypeByDepth), types);
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void UnitMembership_IsASingleUnitPartition_OfActiveOrgUsers(string scale)
    {
        var ds = Gen(scale);
        foreach (var plan in ds.Manifest.UnitPlans!)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in plan.Units)
                foreach (var member in unit.MemberUserIds)
                    Assert.True(seen.Add(member), $"{plan.OrganisationId}: {member} homed in more than one unit");

            var active = ds.Users
                .Where(u => u.OrganisationId == plan.OrganisationId && u.IsActive)
                .Select(u => u.UserId)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(active, seen); // totality: every active person homed, nobody extra (no leavers)
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void EveryLeader_IsAMemberOfTheirUnit_AndEveryAnchorIsHomedInOwnUnit(string scale)
    {
        var ds = Gen(scale);
        foreach (var plan in ds.Manifest.UnitPlans!)
            foreach (var unit in plan.Units)
            {
                if (unit.LeaderUserId is not null)
                {
                    Assert.Equal(unit.UnitKey, unit.LeaderUserId); // the anchor manager leads U(m)
                    Assert.Contains(unit.LeaderUserId, unit.MemberUserIds);
                }
                Assert.Contains(unit.UnitKey, unit.MemberUserIds); // leaderless units keep their anchor member
            }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void PartialRank_IsValid_ChildIsExactlyOneRankDeeper_AndTypeMatchesDepth(string scale)
    {
        var ds = Gen(scale);
        foreach (var plan in ds.Manifest.UnitPlans!)
        {
            var byKey = plan.Units.ToDictionary(u => u.UnitKey, StringComparer.Ordinal);
            foreach (var unit in plan.Units)
            {
                Assert.Equal(TypeByDepth[unit.Depth], unit.Type);
                if (unit.ParentUnitKey is null)
                    Assert.Equal(0, unit.Depth); // exactly the direktion root
                else
                    Assert.Equal(byKey[unit.ParentUnitKey].Depth + 1, unit.Depth);
            }
            Assert.Single(plan.Units.Where(u => u.ParentUnitKey is null));
        }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void UnitNames_AreUniquePerParent_CaseInsensitive(string scale)
    {
        var ds = Gen(scale);
        foreach (var plan in ds.Manifest.UnitPlans!)
            foreach (var siblings in plan.Units.GroupBy(u => u.ParentUnitKey ?? ""))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in siblings)
                    Assert.True(names.Add(u.Name),
                        $"{plan.OrganisationId}: duplicate sibling name '{u.Name}' (the API 409s it)");
            }
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("full")]
    public void MessyLedger_IsExact_Disjoint_AndMoversAreNonManagers(string scale)
    {
        var ds = Gen(scale);
        var managers = ds.Users.Where(u => u.IsManager).Select(u => u.UserId).ToHashSet(StringComparer.Ordinal);

        foreach (var plan in ds.Manifest.UnitPlans!)
        {
            var byKey = plan.Units.ToDictionary(u => u.UnitKey, StringComparer.Ordinal);

            // Leaderless: the ledger is EXACTLY the set of units without a leader.
            var actualLeaderless = plan.Units.Where(u => u.LeaderUserId is null).Select(u => u.UnitKey)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(actualLeaderless, plan.LeaderlessUnitKeys.ToHashSet(StringComparer.Ordinal));
            Assert.True(plan.LeaderlessUnitKeys.Count is >= 1 and <= 2,
                $"{plan.OrganisationId}: expected ~2 deliberate leaderless units, ledger has {plan.LeaderlessUnitKeys.Count}");

            // Sideways: movers are NON-manager leaves (the D3 decapitation hard rule), homed in
            // their TARGET unit, and the target keeps a leader (the amber "Ret" flow target).
            Assert.True(plan.SidewaysCases.Count >= 1, $"{plan.OrganisationId}: no sideways case");
            foreach (var c in plan.SidewaysCases)
            {
                Assert.DoesNotContain(c.UserId, managers);
                Assert.Contains(c.UserId, byKey[c.ToUnitKey].MemberUserIds);
                Assert.DoesNotContain(c.UserId, byKey[c.FromUnitKey].MemberUserIds);
                Assert.NotNull(byKey[c.ToUnitKey].LeaderUserId);
            }

            // Disjointness: each messy unit hosts exactly ONE kind of messiness.
            var messy = new List<string>();
            messy.AddRange(plan.LeaderlessUnitKeys);
            messy.AddRange(plan.SidewaysCases.Select(c => c.FromUnitKey));
            messy.AddRange(plan.SidewaysCases.Select(c => c.ToUnitKey));
            Assert.Equal(messy.Count, messy.Distinct(StringComparer.Ordinal).Count());

            // Full scale honors the refinement's ~3-5 sideways band.
            if (scale == "full")
                Assert.InRange(plan.SidewaysCases.Count, 3, 5);
        }
    }

    [Fact]
    public void UnitDerivation_SameConfig_IsDeterministic()
    {
        string Serialize() => JsonSerializer.Serialize(
            Gen("full").Manifest, DemoManifestJsonContext.Default.DemoManifest);
        Assert.Equal(Serialize(), Serialize()); // incl. the unitPlans section, byte-for-byte
    }

    [Fact]
    public void DepthUnreachableConfig_FailsGeneration_NeverTheLoad()
    {
        // RED case: a tree whose manager count (round(20 × 0.14) = 3 < 5) can NEVER populate
        // manager depths 0–4 — generation itself must throw, loudly.
        var config = new ScaleConfig
        {
            Name = "red",
            MinistryId = "MINX",
            MinistryName = "Demoministeriet",
            TargetSpan = 4,
            ActivityFraction = 0,
            PartTimeFraction = 0,
            MessyCaseCount = 0,
            Trees = new[]
            {
                new TreeProfile
                {
                    OrganisationId = "STYX9",
                    OrgName = "For lille styrelse",
                    TargetUsers = 20,
                    AgreementMix = (100, 0, 0),
                    RootAgreement = "AC",
                    UnitSpanOverride = 2, // demands the 5-layer spine it cannot fill
                },
            },
        };
        var ex = Assert.Throws<InvalidOperationException>(() => new DemoGenerator(config, 42, Ref).Generate());
        Assert.Contains("S114 unit-spine", ex.Message, StringComparison.Ordinal);
    }
}
