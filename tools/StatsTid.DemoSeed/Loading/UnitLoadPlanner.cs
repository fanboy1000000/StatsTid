using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Loading;

/// <summary>
/// S114 / TASK-11400 — the PURE planning logic behind the loader's three unit stages, split out
/// so the stage ordering + probe-first idempotency logic is unit-testable without HTTP.
///
/// Canonical stage order (API-invariant-driven, verified in review):
///   (a) create units PARENT-FIRST via forest-probe-then-create (match existing by org +
///       parent-chain + name; create only the missing; capture server GUIDs; NEVER delete);
///   (b) home ALL members PROBE-FIRST (skip when already homed correctly; a PUT uses the
///       FRESHLY-FETCHED user ETag — never a blanket If-Match "1", which would 412-storm a re-run);
///   (c) appoint leaders LAST (POST …/leaders 422s a NON-member, and a re-home SILENTLY strips
///       existing leaderships [D3] — so homing MUST fully precede any appointment).
/// </summary>
public static class UnitLoadPlanner
{
    /// <summary>The canonical unit-stage order — logged by the loader, pinned by a test.</summary>
    public static readonly IReadOnlyList<string> CanonicalStageOrder =
        new[] { "create-units-parent-first", "home-members-probe-first", "appoint-leaders-last" };

    /// <summary>One EXISTING unit as flattened from the live forest read (server GUIDs).</summary>
    public sealed record ExistingUnit(Guid UnitId, Guid? ParentUnitId, string Type, string Name);

    /// <summary>One planned create (parent GUID resolved lazily — the parent may itself be a
    /// planned create earlier in the same ordered list).</summary>
    public sealed record UnitAction(DemoUnit Unit, bool AlreadyExists, Guid? ExistingUnitId);

    /// <summary>
    /// Orders the plan's units PARENT-FIRST (depth, then unit key — both deterministic) and marks
    /// each as existing (matched by parent-chain + case-insensitive name against the live forest —
    /// the DB active-name uniqueness is lower(name)-scoped) or to-create. Matching walks top-down
    /// so a child can only match under its MATCHED parent (the parent-chain part); an
    /// owner-renamed unit simply never matches and is left alone (the loader never deletes).
    /// </summary>
    public static List<UnitAction> PlanUnitCreates(DemoUnitPlan plan, IReadOnlyList<ExistingUnit> existing)
    {
        var ordered = plan.Units
            .OrderBy(u => u.Depth)
            .ThenBy(u => u.UnitKey, StringComparer.Ordinal)
            .ToList();

        // children-of-parent index over the existing forest (null parent = top-level).
        var existingByParent = existing
            .GroupBy(e => e.ParentUnitId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchedIdByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var actions = new List<UnitAction>();
        foreach (var unit in ordered)
        {
            Guid? matchedParentId = null;
            bool parentIsMatched;
            if (unit.ParentUnitKey is null)
            {
                parentIsMatched = true; // top-level: match among the existing NULL-parent units
            }
            else if (matchedIdByKey.TryGetValue(unit.ParentUnitKey, out var pid))
            {
                matchedParentId = pid;
                parentIsMatched = true;
            }
            else
            {
                parentIsMatched = false; // parent is itself a pending create ⇒ child cannot pre-exist
            }

            ExistingUnit? match = null;
            if (parentIsMatched
                && existingByParent.TryGetValue(matchedParentId ?? Guid.Empty, out var siblings))
            {
                match = siblings.FirstOrDefault(s =>
                    string.Equals(s.Name, unit.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (match is not null)
                matchedIdByKey[unit.UnitKey] = match.UnitId;

            actions.Add(new UnitAction(unit, match is not null, match?.UnitId));
        }
        return actions;
    }

    /// <summary>
    /// Computes the probe-first homing actions for one org: (userId, targetUnitKey) for every
    /// planned member whose CURRENT unit (from the roster probe; null = homed at the Organisation,
    /// absent = unknown) differs from the planned one. Already-correctly-homed members are
    /// SKIPPED — the re-run makes zero writes (no 412s, no D3 leadership strips).
    /// </summary>
    public static List<(string UserId, string UnitKey)> PlanHomingActions(
        DemoUnitPlan plan,
        IReadOnlyDictionary<string, Guid> unitIdByKey,
        IReadOnlyDictionary<string, Guid?> currentUnitByUser)
    {
        var actions = new List<(string, string)>();
        foreach (var unit in plan.Units)
        {
            if (!unitIdByKey.TryGetValue(unit.UnitKey, out var targetId))
                continue; // unresolved unit (creation failed) — surfaced by the loader as a warning
            foreach (var member in unit.MemberUserIds)
            {
                var alreadyCorrect = currentUnitByUser.TryGetValue(member, out var current)
                                     && current == targetId;
                if (!alreadyCorrect)
                    actions.Add((member, unit.UnitKey));
            }
        }
        return actions;
    }

    /// <summary>
    /// The leader appointments — every unit WITH a planned leader (the deliberately-leaderless
    /// ones are the ledger's business, not an omission). MUST run after homing completes.
    /// </summary>
    public static List<(string UnitKey, string LeaderUserId)> PlanLeaderAppointments(DemoUnitPlan plan)
        => plan.Units
            .Where(u => u.LeaderUserId is not null)
            .OrderBy(u => u.Depth).ThenBy(u => u.UnitKey, StringComparer.Ordinal)
            .Select(u => (u.UnitKey, u.LeaderUserId!))
            .ToList();
}
