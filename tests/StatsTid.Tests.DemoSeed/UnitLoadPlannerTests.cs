using StatsTid.Tools.DemoSeed.Loading;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tests.DemoSeed;

/// <summary>
/// S114 / TASK-11400 — the loader's PURE stage/probe logic: canonical stage order, parent-first
/// create ordering, forest-probe matching by (parent-chain, name), and probe-first homing
/// (skip-if-already-homed — a re-run must plan ZERO writes).
/// </summary>
public sealed class UnitLoadPlannerTests
{
    // A small 3-level plan: direktion (m1) → omrade (m2) → kontor (m3), plus a sibling omrade (m4).
    private static DemoUnitPlan SamplePlan() => new()
    {
        OrganisationId = "STYX1",
        ManagersPerDepth = new List<int> { 1, 2, 1, 0, 0 }, // (shape irrelevant to the planner)
        Units = new List<DemoUnit>
        {
            // Deliberately listed CHILD-before-parent to prove the planner orders, not the input.
            new() { UnitKey = "m3", ParentUnitKey = "m2", Type = "kontor", Name = "IT-kontoret", Depth = 2,
                    LeaderUserId = "m3", MemberUserIds = new List<string> { "m3", "e5" } },
            new() { UnitKey = "m2", ParentUnitKey = "m1", Type = "omrade", Name = "Driftsomraadet", Depth = 1,
                    LeaderUserId = "m2", MemberUserIds = new List<string> { "m2", "e3", "e4" } },
            new() { UnitKey = "m4", ParentUnitKey = "m1", Type = "omrade", Name = "Juraomraadet", Depth = 1,
                    LeaderUserId = null, MemberUserIds = new List<string> { "m4" } }, // deliberately leaderless
            new() { UnitKey = "m1", ParentUnitKey = null, Type = "direktion", Name = "Direktionen", Depth = 0,
                    LeaderUserId = "m1", MemberUserIds = new List<string> { "m1", "e1", "e2" } },
        },
        LeaderlessUnitKeys = new List<string> { "m4" },
    };

    [Fact]
    public void CanonicalStageOrder_IsUnitsThenHomingThenLeaders()
    {
        // Units must exist before homing (the PUT validates the target unit); homing must FULLY
        // precede leader appointment (POST …/leaders 422s a non-member AND a later re-home would
        // silently strip the designation again — D3). Pinned so a refactor cannot reorder it.
        Assert.Equal(
            new[] { "create-units-parent-first", "home-members-probe-first", "appoint-leaders-last" },
            UnitLoadPlanner.CanonicalStageOrder);
    }

    [Fact]
    public void PlanUnitCreates_OrdersParentFirst()
    {
        var actions = UnitLoadPlanner.PlanUnitCreates(SamplePlan(), Array.Empty<UnitLoadPlanner.ExistingUnit>());
        var keys = actions.Select(a => a.Unit.UnitKey).ToList();
        Assert.Equal(new[] { "m1", "m2", "m4", "m3" }, keys); // depth, then key — parent always first
        Assert.All(actions, a => Assert.False(a.AlreadyExists)); // empty forest ⇒ everything is a create
    }

    [Fact]
    public void PlanUnitCreates_MatchesExisting_ByParentChainAndName_CaseInsensitive()
    {
        var rootId = Guid.NewGuid();
        var omradeId = Guid.NewGuid();
        var foreignParent = Guid.NewGuid(); // an unrelated subtree carrying a colliding name
        var existing = new[]
        {
            new UnitLoadPlanner.ExistingUnit(rootId, null, "direktion", "DIREKTIONEN"), // case-insensitive match
            new UnitLoadPlanner.ExistingUnit(omradeId, rootId, "omrade", "Driftsomraadet"),
            // Same NAME as the m3 plan unit but under the WRONG parent — must NOT match (the
            // parent-chain half of the match key).
            new UnitLoadPlanner.ExistingUnit(Guid.NewGuid(), foreignParent, "kontor", "IT-kontoret"),
        };

        var actions = UnitLoadPlanner.PlanUnitCreates(SamplePlan(), existing).ToDictionary(a => a.Unit.UnitKey);

        Assert.True(actions["m1"].AlreadyExists);
        Assert.Equal(rootId, actions["m1"].ExistingUnitId);
        Assert.True(actions["m2"].AlreadyExists);
        Assert.Equal(omradeId, actions["m2"].ExistingUnitId);
        Assert.False(actions["m3"].AlreadyExists); // its parent matched, but no same-parent name match
        Assert.False(actions["m4"].AlreadyExists);
    }

    [Fact]
    public void PlanUnitCreates_ChildOfAPendingCreate_IsNeverMatched()
    {
        // The plan's m2 (parent of m3) does not exist ⇒ m3 cannot pre-exist under it, even if some
        // unit elsewhere carries m3's name at top level.
        var existing = new[]
        {
            new UnitLoadPlanner.ExistingUnit(Guid.NewGuid(), null, "kontor", "IT-kontoret"),
        };
        var actions = UnitLoadPlanner.PlanUnitCreates(SamplePlan(), existing).ToDictionary(a => a.Unit.UnitKey);
        Assert.False(actions["m3"].AlreadyExists);
    }

    [Fact]
    public void PlanHomingActions_SkipsAlreadyHomed_AndReRunPlansZeroWrites()
    {
        var plan = SamplePlan();
        var unitIdByKey = plan.Units.ToDictionary(u => u.UnitKey, _ => Guid.NewGuid(), StringComparer.Ordinal);

        // First run: nobody is homed (roster says org-homed/null for all).
        var nobody = plan.Units.SelectMany(u => u.MemberUserIds)
            .ToDictionary(id => id, _ => (Guid?)null, StringComparer.Ordinal);
        var first = UnitLoadPlanner.PlanHomingActions(plan, unitIdByKey, nobody);
        Assert.Equal(plan.Units.Sum(u => u.MemberUserIds.Count), first.Count); // everyone gets homed

        // Re-run: the roster now reports everyone in their planned unit ⇒ ZERO planned writes
        // (no PUTs at all ⇒ no 412 storm, no D3 leadership strips).
        var homed = plan.Units
            .SelectMany(u => u.MemberUserIds.Select(id => (id, unit: unitIdByKey[u.UnitKey])))
            .ToDictionary(x => x.id, x => (Guid?)x.unit, StringComparer.Ordinal);
        Assert.Empty(UnitLoadPlanner.PlanHomingActions(plan, unitIdByKey, homed));
    }

    [Fact]
    public void PlanHomingActions_TargetsTheWrongOrUnknownlyHomed()
    {
        var plan = SamplePlan();
        var unitIdByKey = plan.Units.ToDictionary(u => u.UnitKey, _ => Guid.NewGuid(), StringComparer.Ordinal);

        var current = plan.Units
            .SelectMany(u => u.MemberUserIds.Select(id => (id, unit: unitIdByKey[u.UnitKey])))
            .ToDictionary(x => x.id, x => (Guid?)x.unit, StringComparer.Ordinal);
        current["e3"] = unitIdByKey["m1"]; // homed in the WRONG unit
        current.Remove("e5");              // unknown to the roster ⇒ treated as needing homing

        var actions = UnitLoadPlanner.PlanHomingActions(plan, unitIdByKey, current);
        Assert.Equal(2, actions.Count);
        Assert.Contains(("e3", "m2"), actions);
        Assert.Contains(("e5", "m3"), actions);
    }

    [Fact]
    public void PlanLeaderAppointments_ExcludesTheDeliberatelyLeaderless()
    {
        var appointments = UnitLoadPlanner.PlanLeaderAppointments(SamplePlan());
        Assert.Equal(new[] { ("m1", "m1"), ("m2", "m2"), ("m3", "m3") }, appointments);
        Assert.DoesNotContain(appointments, a => a.UnitKey == "m4"); // the ledger's business, not an omission
    }
}
