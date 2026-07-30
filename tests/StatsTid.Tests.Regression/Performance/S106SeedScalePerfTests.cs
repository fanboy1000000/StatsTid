using System.Diagnostics;
using System.Text;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Abstractions;

namespace StatsTid.Tests.Regression.Performance;

/// <summary>
/// SPRINT-106 / TASK-10605 (Enhedsspor Phase 3a) — the load-bearing PERF seed-scale regression for the
/// three new Phase-3a reads, asserting each is BOUNDED at Demoministeriet scale and that NONE degrades
/// into a per-unit / per-person / total-org-size scan.
///
/// <para><b>Scale.</b> A self-contained bulk seed mirroring the DemoSeed <c>full</c> "Demoministeriet"
/// shape: ONE MAO over FIVE Organisations sized 2000 / 600 / 250 / 250 / 250 = <b>3350 active users</b>,
/// each Organisation carrying a typed unit tree of <b>depth 5</b> (direktion → omrade → kontor → team →
/// enhed; 15 units/org = 75 units), with ~80% of users homed in a leaf unit and ~20% homed directly at
/// the Organisation. The seed is bulk SQL (<c>generate_series</c>) — fast + deterministic — so the test
/// measures the reads at realistic volume WITHOUT the slow API-driven DemoSeed loader.</para>
///
/// <para><b>Query-count hook.</b> Npgsql 8 emits one <c>System.Diagnostics.Activity</c> per executed
/// command under the <c>"Npgsql"</c> ActivitySource. <see cref="DbCommandCounter"/> registers an
/// <see cref="ActivityListener"/> filtered to THIS container's port (robust against parallel-test
/// pollution) and counts/records the exact SQL statements issued by a measured read — the clean
/// "command interceptor" the task calls for. Each measurement also takes a generous wall-clock ceiling.</para>
///
/// <para><b>What is asserted (the bounded-round-trips property).</b>
/// <list type="bullet">
///   <item>FOREST — the unified read issues a CONSTANT 4 set-based commands (org list + unit list + 2
///     GROUP BY counts) + an in-memory roll-up, INDEPENDENT of the 3350-user scale (never per-unit /
///     per-person).</item>
///   <item>ROSTER — one Organisation's roster is ONE <c>materialized_path</c>-scoped load (its row count
///     == that Organisation's users, NOT the 3350 global total) on a small, bounded number of
///     round-trips.</item>
///   <item>SEARCH — scope-bounded: a single-Organisation actor's results are confined to that
///     Organisation (no cross-scope / global scan), on a CONSTANT 4 commands.</item>
///   <item>TILE-COUNT — <see cref="ApprovalPeriodRepository.GetPeriodStatusProjectionForTreeAsync"/>
///     carries a PRE-EXISTING per-pending-employee N+1 (S105 / the plan's documented shape). This pins
///     that it scales with the PENDING set, NOT total org size: a 2000-user Organisation with ZERO
///     pending periods issues exactly ONE command; the per-pending cost is a small constant multiplier.</item>
/// </list>
/// If any read were to scale with total org size, the constant-command or wall-clock assertions fail
/// LOUDLY — that is the point.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S106SeedScalePerfTests : IClassFixture<S106SeedScalePerfFixture>
{
    private readonly S106SeedScalePerfFixture _fx;
    private readonly ITestOutputHelper _out;

    public S106SeedScalePerfTests(S106SeedScalePerfFixture fx, ITestOutputHelper outputHelper)
    {
        _fx = fx;
        _out = outputHelper;
    }

    // Generous wall-clock ceilings (the seed-scale reads run in well under these locally; the budget
    // catches a degradation into a per-row scan, not micro-timing).
    private const int ForestBudgetMs = 5000;
    private const int RosterBudgetMs = 5000;
    private const int SearchBudgetMs = 5000;
    private const int TileBudgetMs = 8000;

    // ════════════════════════════════════════════════════════════════════════
    //  FOREST — constant 4 set-based commands + in-memory roll-up (scale-invariant)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The forest read's repository sequence (the EXACT reads
    /// <c>GET /api/admin/units/forest</c> performs) issues a CONSTANT 4 commands regardless of the
    /// 3350-user scale: <c>organizations</c> list + <c>units</c> list + 2 <c>GROUP BY</c> member counts.
    /// Visibility admission (<c>GetAccessibleOrgsAsync</c>) is an in-memory scope projection — ZERO DB
    /// round-trips — and the depth-≤5 roll-up is in memory (units ≪ people, no recursive CTE). RED if a
    /// per-unit or per-person query slips into the read path.</summary>
    [Fact]
    public async Task Forest_IssuesConstantFourSetBasedReads_AtSeedScale()
    {
        var orgRepo = new OrganizationRepository(_fx.Factory);
        var unitRepo = new UnitRepository(_fx.Factory);

        using var counter = new DbCommandCounter(_fx.Port);
        var sw = Stopwatch.StartNew();

        // The exact four set-based reads the forest endpoint performs (GetAccessibleOrgsAsync issues no
        // query — it is a synchronous scope projection — so it is excluded by construction).
        var orgs = await orgRepo.GetAllAsync();
        var units = await unitRepo.ListAllActiveAsync();
        var byUnit = await unitRepo.GetActiveMemberCountByUnitAsync();
        var byOrgHomed = await unitRepo.GetActiveOrgHomedCountByOrgAsync();

        sw.Stop();
        var count = counter.Count;

        _out.WriteLine($"FOREST: {count} commands, {sw.ElapsedMilliseconds} ms; orgs={orgs.Count} units={units.Count} unitsWithMembers={byUnit.Count} orgsWithHomed={byOrgHomed.Count}");
        counter.DumpTo(_out);

        // The load-bearing assertion: a CONSTANT 4 commands, NOT a function of 3350 users / 75 units.
        Assert.Equal(4, count);
        Assert.True(sw.ElapsedMilliseconds < ForestBudgetMs, $"Forest reads took {sw.ElapsedMilliseconds} ms (budget {ForestBudgetMs} ms).");

        // Sanity that the scale is genuinely present (else the constant-count claim is vacuous).
        Assert.True(units.Count >= 75, $"Expected ≥75 seeded units, saw {units.Count}.");
        Assert.True(byUnit.Values.Sum() + byOrgHomed.Values.Sum() >= 3000, "Expected ≥3000 active members across the forest.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ROSTER — ONE materialized_path-scoped load, bounded round-trips
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>One Organisation's roster is a single <c>materialized_path</c>-scoped load: its returned
    /// row count equals THAT Organisation's active users (≈2000), NOT the 3350 global total — i.e. no
    /// cross-scope / global scan. The round-trip count is a small bounded constant (the one scoped roster
    /// query + the reused status projection + a single batched name-resolution) — NOT per-person. With no
    /// pending periods in this Organisation, the per-pending N+1 contributes nothing.</summary>
    [Fact]
    public async Task Roster_IsOneScopedLoad_NotGlobalScan_AtSeedScale()
    {
        var repo = NewApprovalRepo();

        var expectedOrgUsers = await _fx.CountActiveUsersInOrgAsync(S106SeedScalePerfFixture.Org1);
        var globalUsers = await _fx.CountAllActiveUsersAsync();

        using var counter = new DbCommandCounter(_fx.Port);
        var sw = Stopwatch.StartNew();
        var roster = await repo.GetMedarbejderRosterForTreeAsync(S106SeedScalePerfFixture.Org1Path);
        sw.Stop();
        var count = counter.Count;

        _out.WriteLine($"ROSTER (Org1): {count} commands, {sw.ElapsedMilliseconds} ms; rows={roster.Employees.Count} expectedOrgUsers={expectedOrgUsers} globalUsers={globalUsers}");
        counter.DumpTo(_out);

        // SCOPE: the roster loaded exactly Org1's users — provably NOT the global 3350 scan.
        Assert.Equal(expectedOrgUsers, roster.Employees.Count);
        Assert.True(roster.Employees.Count < globalUsers, "The roster must be Organisation-scoped, not global.");
        Assert.DoesNotContain(roster.Employees, e => e.EmployeeId.StartsWith("perf_o2_", StringComparison.Ordinal));

        // BOUNDED round-trips: a small constant (≤5), NOT a per-person count over ≈2000 rows.
        Assert.True(count <= 5, $"Roster issued {count} commands for {roster.Employees.Count} people — expected a bounded constant (≤5).");
        Assert.True(sw.ElapsedMilliseconds < RosterBudgetMs, $"Roster took {sw.ElapsedMilliseconds} ms (budget {RosterBudgetMs} ms).");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SEARCH — scope-bounded, constant 4 commands
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The search read's repository sequence (the EXACT reads <c>GET /api/admin/search</c>
    /// performs) is scope-bounded + a CONSTANT 4 commands: a single-Organisation actor searching the
    /// term "Perf" (which matches users/units across ALL five Organisations) gets back ONLY that
    /// Organisation's units + people — the D5 boundary holds (no cross-scope / global scan).</summary>
    [Fact]
    public async Task Search_IsScopeBounded_ConstantReads_AtSeedScale()
    {
        var unitRepo = new UnitRepository(_fx.Factory);
        var approvalRepo = NewApprovalRepo();
        var orgRepo = new OrganizationRepository(_fx.Factory);

        // A scoped HR whose accessible-org set is EXACTLY Org1 (the LocalHR-floored admission).
        var accessible = new[] { S106SeedScalePerfFixture.Org1 };
        const string term = "Perf"; // matches seeded names in every Organisation — scope must discriminate.

        using var counter = new DbCommandCounter(_fx.Port);
        var sw = Stopwatch.StartNew();

        var (unitHits, _) = await unitRepo.SearchUnitsAsync(term, accessible, 200, 0);
        var (peopleHits, _) = await approvalRepo.SearchPeopleForOverlayAsync(term, accessible, 200, 0);
        _ = await orgRepo.GetAllAsync();          // the in-memory path-build org map
        _ = await unitRepo.ListAllActiveAsync();  // the in-memory path-build unit map

        sw.Stop();
        var count = counter.Count;

        _out.WriteLine($"SEARCH (scoped to Org1, term '{term}'): {count} commands, {sw.ElapsedMilliseconds} ms; unitHits={unitHits.Count} peopleHits={peopleHits.Count}");
        counter.DumpTo(_out);

        // CONSTANT 4 commands (2 scoped searches + 2 in-memory-map reads), scale-invariant.
        Assert.Equal(4, count);
        Assert.True(sw.ElapsedMilliseconds < SearchBudgetMs, $"Search took {sw.ElapsedMilliseconds} ms (budget {SearchBudgetMs} ms).");

        // SCOPE (D5): every hit is within the single accessible Organisation — no sibling leak.
        Assert.NotEmpty(peopleHits);
        Assert.All(peopleHits, p => Assert.Equal(S106SeedScalePerfFixture.Org1, p.PrimaryOrgId));
        Assert.All(unitHits, u => Assert.Equal(S106SeedScalePerfFixture.Org1, u.OrganisationId));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TILE-COUNT — bounded by the PENDING set, not org size (the N+1 characterization)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The tile-count projection's command count is independent of BOTH org size AND the
    /// pending count.
    ///
    /// <para>Org-size independence (unchanged since S106): a 2000-user Organisation and a 250-user one,
    /// each with ZERO pending periods, both issue exactly ONE command — the phase-(1) status scan.</para>
    ///
    /// <para><b>Pending-count independence is NEW (S125 / TASK-12501).</b> This test previously asserted
    /// that the count grows monotonically with K, characterising a per-pending-employee N+1 of ~27
    /// statements that S106 accepted on the premise that cost tracked the pending set rather than org
    /// size. At month-end those converge, and the measured cost was 27,001 commands / 13.8s at K=1000.
    /// The projection now prefetches the resolver's inputs and the authorizer's facts in set-based
    /// reads, so K=10 and K=20 issue the SAME number of commands. See the inline note on the
    /// assertions for the superseded expectation.</para></summary>
    [Fact]
    public async Task TileCount_ScalesWithPendingSet_NotOrgSize_AtSeedScale()
    {
        var repo = NewApprovalRepo();
        await _fx.ClearPendingScenarioAsync(); // Org3 starts clean (idempotent)

        var org1Users = await _fx.CountActiveUsersInOrgAsync(S106SeedScalePerfFixture.Org1);
        var org3Users = await _fx.CountActiveUsersInOrgAsync(S106SeedScalePerfFixture.Org3);

        // ── Org-size INDEPENDENCE: 0 pending over BOTH a 2000-user and a 250-user Organisation → each
        //    issues exactly ONE command. The 8× size swing does not change the count: no per-user work. ──
        int big0, small0;
        using (var counter = new DbCommandCounter(_fx.Port))
        {
            var proj = await repo.GetPeriodStatusProjectionForTreeAsync(S106SeedScalePerfFixture.Org1Path);
            big0 = counter.Count;
            Assert.Equal(org1Users, proj.Employees.Count); // genuinely the big org
        }
        using (var counter = new DbCommandCounter(_fx.Port))
        {
            var proj = await repo.GetPeriodStatusProjectionForTreeAsync(S106SeedScalePerfFixture.Org3Path);
            small0 = counter.Count;
            Assert.Empty(proj.PendingCountByManager);
        }
        _out.WriteLine($"TILE pending=0: Org1({org1Users}u)={big0} cmd, Org3({org3Users}u)={small0} cmd");
        Assert.Equal(1, big0);   // 2000 users → 1 command
        Assert.Equal(1, small0); // 250 users → 1 command (org-size independent)

        // ── K=10 pending (each: edge manager + a 2-leader unit → 3 candidate approvers). ──
        await _fx.AddPendingScenarioAsync(10);
        int count10;
        using (var counter = new DbCommandCounter(_fx.Port))
        {
            var proj = await repo.GetPeriodStatusProjectionForTreeAsync(S106SeedScalePerfFixture.Org3Path);
            count10 = counter.Count;
            _out.WriteLine($"TILE (Org3, pending=10): {count10} commands; tiles={proj.PendingCountByManager.Count}");
            // The pending employees tally to their edge manager + both unit leaders (the S106 enumeration).
            Assert.True(proj.PendingCountByManager.Count >= 1, "Expected populated tiles for the pending set.");
        }

        // ── K=20 pending → command count grows ~linearly in K, bounded by the pending set. ──
        await _fx.AddPendingScenarioAsync(20);
        int count20;
        var sw = Stopwatch.StartNew();
        using (var counter = new DbCommandCounter(_fx.Port))
        {
            var proj = await repo.GetPeriodStatusProjectionForTreeAsync(S106SeedScalePerfFixture.Org3Path);
            count20 = counter.Count;
            sw.Stop();
            _out.WriteLine($"TILE (Org3, pending=20): {count20} commands, {sw.ElapsedMilliseconds} ms");
        }

        var perPending10 = (count10 - 1) / 10.0;
        var perPending20 = (count20 - 1) / 20.0;
        _out.WriteLine($"TILE per-pending multiplier: ~{perPending10:0.0} (K=10), ~{perPending20:0.0} (K=20); org size {org3Users} is irrelevant to the slope.");

        // ── S125 / TASK-12501: THIS ASSERTION WAS INVERTED, and that inversion is the deliverable ──
        //
        // SUPERSEDED EXPECTATION (S106 → S125): this test used to assert
        //     Assert.True(count20 > count10 && count10 > small0)
        // i.e. that command count grows STRICTLY MONOTONICALLY with the pending count. That was a
        // faithful characterisation of a pre-existing per-pending-employee N+1 (~27 statements each),
        // deliberately accepted at S106 because the cost tracked the PENDING set rather than org size.
        //
        // The premise failed at month-end: pending → org size, and the two converge. Measured at
        // K=1000 the projection issued 27,001 commands and took 13.8 SECONDS. The old assertion could
        // never catch that — it tops out at K=20 under an 8s budget and asserts the very property
        // (linear growth in K) that makes K=1,925 catastrophic. It proved the defect.
        //
        // The projection is now FLAT in K: the resolver's inputs and the authorizer's facts are
        // prefetched in set-based reads, so the per-employee round-trips are gone. What remains is a
        // small constant — phase (1) + the prefetch builds + the batched candidate enumeration.
        //
        // Flatness is asserted DIRECTLY rather than via a shrinking multiplier, because "smaller" and
        // "independent of K" are different claims and only the second one survives month-end.
        Assert.Equal(count10, count20);
        Assert.True(count20 <= 12, $"Expected a small constant command count independent of K, saw {count20} at K=20.");
        // Zero pending still costs exactly the phase-(1) scan — the prefetch must not be built for an
        // empty pending set (it was, briefly, and this guard caught it: Expected 1, Actual 4).
        Assert.Equal(1, small0);
        // Per-pending multiplier now trends to ZERO as K grows — the signature of O(1), where the old
        // shape held it at a constant ~27.
        Assert.True(perPending20 < perPending10, $"Expected the per-pending multiplier to FALL with K (O(1)); saw {perPending10:0.0} → {perPending20:0.0}.");
        Assert.True(sw.ElapsedMilliseconds < TileBudgetMs, $"Tile-count took {sw.ElapsedMilliseconds} ms (budget {TileBudgetMs} ms).");

        await _fx.ClearPendingScenarioAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  S125 / TASK-12501 — the F1 CHARACTERISATION BASELINE
    //
    //  F1 rewrites how many round-trips evaluate the approval-authority rule inside
    //  GetPeriodStatusProjectionForTreeAsync. That loop COMPUTES WHO MAY APPROVE, so "faster" and
    //  "changed who can approve what" are indistinguishable without a baseline. These two tests ARE
    //  that baseline: they must produce byte-identical output before and after the optimisation.
    //
    //  Captured AFTER FAIL-004 / TASK-12502 landed, deliberately — a baseline taken before that fix
    //  would have encoded self-approval as the reference and made the fix read as an F1 regression.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Baseline 1 — the happy path at K=10: the EXACT <c>pendingCountByManager</c> map, the employee
    /// status histogram, and the documented ordering contract.
    ///
    /// <para>Pins the map EXACTLY (keys AND values) because that is the output the authorization loop
    /// produces and therefore the only place a round-trip rewrite can silently change authority. The
    /// per-employee statuses are pinned as a histogram rather than 263 golden rows: they come from
    /// step (1), a single set-based query this task does not touch, so an exact-row golden there would
    /// be churn without added protection. Total count + histogram + ordering is the proportionate
    /// pin.</para>
    ///
    /// <para><b>The multi-tally property</b> (invariant 6): each of these 10 pending employees tallies
    /// to THREE managers (edge manager + both unit leaders), so Σ = 30 for THIS fixture. A rewrite that
    /// "fixed" the multi-tally into count-once would drop Σ to 10 and fail here.</para>
    ///
    /// <para><b>Σ = 30 is a characterisation of this fixture, NOT a general lower bound</b> (external
    /// review BLOCKER, 2026-07-30 — an earlier revision of this comment wrongly stated the invariant as
    /// "Σ tiles ≥ pending"). There is NO such guarantee: a pending employee with no resolvable edge and
    /// no unit leaders — or whose every candidate fails the role/same-Org floors — contributes ZERO
    /// tiles (`ApprovalPeriodRepository.cs:640` only adds a candidate when the edge is non-null, and
    /// `:651` skips unauthorized ones). `PeriodStatusAndPersonSearchReadsTests
    /// .PerManagerPendingCount_RoleRevokedResolvedApprover_IsNotTallied_TileMatchesEmptyDashboard`
    /// exhibits exactly that. The real invariant is one-directional: an employee MAY contribute to
    /// several tiles, and nothing may impose count-once.</para>
    /// </summary>
    [Fact]
    public async Task F1Characterisation_HappyPath_K10_ProjectionIsExactlyReproducible()
    {
        var repo = NewApprovalRepo();
        await _fx.AddPendingScenarioAsync(10);
        try
        {
            var proj = await repo.GetPeriodStatusProjectionForTreeAsync(S106SeedScalePerfFixture.Org3Path);

            // ── The EXACT tile map: 3 tiles, each counting all 10 pending employees. ──
            var map = proj.PendingCountByManager
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}");
            Assert.Equal(
                $"{S106SeedScalePerfFixture.Org3EdgeManager}=10;" +
                $"{S106SeedScalePerfFixture.Org3Leader1}=10;" +
                $"{S106SeedScalePerfFixture.Org3Leader2}=10",
                string.Join(";", map));

            // ── The multi-tally property: a pending employee counts toward MULTIPLE managers,
            //    deliberately. 30 characterises THIS fixture (10 × 3 candidates); it is NOT a general
            //    Σ ≥ pending bound — see the summary. ──
            Assert.Equal(30, proj.PendingCountByManager.Values.Sum());

            // ── The status histogram over the whole Organisation. ──
            var histogram = proj.Employees
                .GroupBy(e => e.Status)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}={g.Count()}");
            _out.WriteLine($"CHARACTERISATION K=10: employees={proj.Employees.Count}, {string.Join(",", histogram)}");
            Assert.Equal(10, proj.Employees.Count(e => e.Status == "SUBMITTED"));
            Assert.All(proj.Employees, e => Assert.Contains(e.Status, new[] { "OPEN", "SUBMITTED", "APPROVED" }));
            // Exactly the 10 scenario users are the SUBMITTED ones — not "10 of someone".
            Assert.Equal(
                Enumerable.Range(1, 10).Select(i => $"{S106SeedScalePerfFixture.PendingPrefix}{i}")
                    .OrderBy(x => x, StringComparer.Ordinal),
                proj.Employees.Where(e => e.Status == "SUBMITTED").Select(e => e.EmployeeId)
                    .OrderBy(x => x, StringComparer.Ordinal));

            // ── The documented ordering contract: ORDER BY display_name, user_id. ──
            var ordered = proj.Employees
                .OrderBy(e => e.DisplayName, StringComparer.Ordinal)
                .ThenBy(e => e.EmployeeId, StringComparer.Ordinal)
                .Select(e => e.EmployeeId);
            Assert.Equal(ordered, proj.Employees.Select(e => e.EmployeeId));
        }
        finally
        {
            await _fx.ClearPendingScenarioAsync();
        }
    }

    /// <summary>
    /// Baseline 2 — the SHAPE MATRIX, which is where the authorization invariants actually live. Four
    /// pending employees with structurally different candidate sets, and the exact map they produce:
    ///
    /// <list type="table">
    /// <item><term>x1 — leaf unit + edge</term><description>EdgeManager, Leader1, Leader2, VikarOfLeader1</description></item>
    /// <item><term>x2 — NULL unit + edge</term><description>EdgeManager ONLY (invariant 3)</description></item>
    /// <item><term>x3 — orphan in leaf unit</term><description>the unit leaders + vikar; no edge resolves</description></item>
    /// <item><term>x4 — edge to a ROLE-REVOKED manager</term><description>the unit leaders + vikar; the
    /// revoked manager is resolved but REJECTED by the role floor (invariant 9) and must be ABSENT
    /// from the map entirely</description></item>
    /// <item><term>x5 — edge to an INACTIVE manager</term><description>the escalation walk runs, finds
    /// the inactive manager has no PRIMARY of their own, and yields NO edge; the unit leaders alone
    /// tally it, and the inactive manager must be absent from the map</description></item>
    /// </list>
    ///
    /// <para>Expected map: EdgeManager=2 (x1, x2); Leader1=6, Leader2=6 and VikarOfLeader1=6 (the six
    /// leaf-unit shapes — x2 is NULL-unit); StandIn=1 (x6's edge resolves THROUGH its manager to that
    /// manager's active stand-in — the R3 vikar branch); and NO key for the role-revoked manager, the
    /// inactive manager, the cross-Organisation stand-in, or the manager whose vikar is cross-Org.</para>
    ///
    /// <para>This is the test that makes a prefetch-based rewrite honest: answering the four
    /// authorization primitives from in-memory maps means reimplementing their semantics, and every
    /// one of those semantics is exercised here — the NULL-unit exclusion, the absent-edge case, the
    /// role floor, and the vikar coverage window.</para>
    /// </summary>
    [Fact]
    public async Task F1Characterisation_ShapeMatrix_CandidateSetsAndFloors_AreExactlyReproducible()
    {
        var repo = NewApprovalRepo();
        await _fx.ClearPendingScenarioAsync(); // isolate: the map below is PURELY the shape matrix
        await _fx.AddShapeMatrixAsync();
        try
        {
            var proj = await repo.GetPeriodStatusProjectionForTreeAsync(S106SeedScalePerfFixture.Org3Path);

            var map = proj.PendingCountByManager
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}")
                .ToList();
            _out.WriteLine($"CHARACTERISATION shapes: {string.Join(";", map)}");

            Assert.Equal(
                $"{S106SeedScalePerfFixture.Org3EdgeManager}=2;" +          // x1, x2
                $"{S106SeedScalePerfFixture.Org3Leader1}=6;" +               // the six leaf-unit shapes
                $"{S106SeedScalePerfFixture.Org3Leader2}=6;" +
                $"{S106SeedScalePerfFixture.ShapeStandIn}=1;" +              // x6's edge → the STAND-IN
                $"{S106SeedScalePerfFixture.ShapeVikarOfLeader1}=6",
                string.Join(";", map));

            // Invariant 9, stated as its own assertion so a regression names itself: an active,
            // resolvable manager WITHOUT LeaderOrAbove grants nothing.
            Assert.DoesNotContain(S106SeedScalePerfFixture.ShapeRoleRevokedMgr, proj.PendingCountByManager.Keys);

            // All four shapes are SUBMITTED — i.e. the map above is not small because rows went missing
            // from step (1).
            var submitted = proj.Employees.Where(e => e.Status == "SUBMITTED").Select(e => e.EmployeeId).ToList();
            Assert.Equal(7, submitted.Count);
            Assert.Contains(S106SeedScalePerfFixture.ShapeOrphan, submitted);
            Assert.Contains(S106SeedScalePerfFixture.ShapeNullUnit, submitted);
            // The escalation shape: its manager is INACTIVE, so no edge resolves and only the unit
            // leaders tally it — which is why l1/l2/xv are 4 while the edge manager stays at 2. This
            // shape is also what makes the differential test discriminating: an is_active divergence
            // in the prefetched source shows up here and nowhere else.
            Assert.Contains(S106SeedScalePerfFixture.ShapeEscalates, submitted);
            Assert.DoesNotContain(S106SeedScalePerfFixture.ShapeInactiveMgr, proj.PendingCountByManager.Keys);

            // ── THE CROSS-ORGANISATION PIN (S125 close, external-lens BLOCKER) ───────────────────
            // x7's manager holds a vikar homed in a DIFFERENT Organisation. Live SQL resolves the
            // edge TO that vikar and then DENIES on same-Organisation, so NEITHER the cross-Org
            // stand-in NOR x7's own manager may appear. The bug this pins was the opposite: an
            // Organisation-scoped prefetch read the out-of-scope vikar as "inactive", SKIPPED it, and
            // fell through to the in-Org manager — who then passed same-Organisation and was ADMITTED.
            // Being more permissive than SQL is the direction that matters.
            Assert.DoesNotContain(S106SeedScalePerfFixture.ShapeCrossOrgStandIn, proj.PendingCountByManager.Keys);
            Assert.DoesNotContain(S106SeedScalePerfFixture.ShapeCrossVikaredMgr, proj.PendingCountByManager.Keys);
            // And the in-Organisation vikar case DOES resolve to the stand-in — the same branch,
            // working, so the assertion above is a same-Org bound and not the vikar path being dead.
            Assert.Contains(S106SeedScalePerfFixture.ShapeStandIn, proj.PendingCountByManager.Keys);
        }
        finally
        {
            await _fx.ClearShapeMatrixAsync();
        }
    }

    /// <summary>
    /// <b>S125 / TASK-12501 step 3b — THE DIFFERENTIAL TEST. This is the test that makes the prefetch
    /// defensible.</b>
    ///
    /// <para>Step 3b introduced a SECOND way to answer the resolver's four data questions
    /// (<see cref="PrefetchedReportingLineDataSource"/> alongside
    /// <see cref="SqlReportingLineDataSource"/>). The resolution ALGORITHM is shared — the R3
    /// precedence, the FAIL-004 self-exclusion invariant and the depth ceiling exist once — but two
    /// data sources is still two chances to diverge: an inclusive-vs-exclusive date bound, a missing
    /// row read as permission, a mapper that drops a column.</para>
    ///
    /// <para><b>It compares VERDICTS PAIR-BY-PAIR, not totals.</b> Asserting that the two produce the
    /// same tile map would be much weaker — totals can agree by luck while individual resolutions are
    /// wrong in offsetting directions. Here every user in the Organisation is resolved through BOTH
    /// sources and the full <c>(ManagerId, ApprovalMethod, Depth)</c> triple must match, so a single
    /// employee resolving differently fails the test and names itself.</para>
    ///
    /// <para><b>Depth is compared deliberately</b>, not just the resolved manager. Depth drives the
    /// existing <c>FallbackTraversalWarning</c> (fires above 3), so a source that reached the same
    /// answer by a different route — e.g. skipping an inactive-manager hop because it never saw the
    /// inactive flag — would be a real behavioural difference even though the approver matched.</para>
    ///
    /// <para>Run over the pending scenario AND the shape matrix together, so the comparison covers an
    /// employee with an ordinary edge, one whose manager is INACTIVE (the escalation walk), one whose
    /// manager holds an active VIKAR (the R3 precedence branch), one whose manager's vikar is homed in
    /// a DIFFERENT Organisation, an ORPHAN with no line at all, and a NULL-unit member.</para>
    ///
    /// <para><b>Those branches are ASSERTED to have executed, not assumed.</b> An earlier revision of
    /// this comment claimed R3-vikar coverage the fixture could not deliver — its only vikar covered a
    /// leader nobody reported to — so the branch never ran and "0 divergences" was partly vacuous. The
    /// external review lens caught that at the S125 close, and it had in fact hidden a real BLOCKER.</para>
    /// </summary>
    [Fact]
    public async Task F1Differential_PrefetchedSource_MatchesSqlSource_ForEveryUser_TripleByTriple()
    {
        await _fx.AddPendingScenarioAsync(10);
        await _fx.AddShapeMatrixAsync();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var vikarRepo = new ManagerVikarRepository(_fx.Factory);
            var repo = new ReportingLineRepository(_fx.Factory, vikarRepo);

            await using var conn = _fx.Factory.Create();
            await conn.OpenAsync();
            // The same isolation the projection uses, so both sources observe one identical snapshot —
            // otherwise a concurrent change could make them differ for reasons that are not a defect.
            await using var tx = await conn.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead);

            var sqlSource = new SqlReportingLineDataSource(conn, tx, vikarRepo);
            var prefetched = await PrefetchedReportingLineDataSource.BuildAsync(
                conn, tx, S106SeedScalePerfFixture.Org3Path + "%", today, sqlSource, default);

            var userIds = new List<string>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT u.user_id FROM users u
                JOIN organizations o ON o.org_id = u.primary_org_id
                WHERE o.materialized_path LIKE @p
                ORDER BY u.user_id
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("p", S106SeedScalePerfFixture.Org3Path + "%");
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    userIds.Add(reader.GetString(0));
            }

            Assert.True(userIds.Count > 250, $"Expected the seeded Organisation, saw {userIds.Count} users.");

            var divergences = new List<string>();
            var nonTrivial = 0;
            foreach (var userId in userIds)
            {
                var viaSql = await repo.ResolveDesignatedApproverAsync(sqlSource, userId, today);
                var viaPrefetch = await repo.ResolveDesignatedApproverAsync(prefetched, userId, today);
                if (viaSql != viaPrefetch)
                    divergences.Add($"{userId}: sql={viaSql} prefetched={viaPrefetch}");
                if (viaSql.ManagerId is not null)
                    nonTrivial++;
            }

            _out.WriteLine($"DIFFERENTIAL: {userIds.Count} users compared, {nonTrivial} resolving to a manager, {divergences.Count} divergences");

            // Non-vacuity: if nothing resolved, the comparison would be 250 identical (null, null, 0)s
            // and would pass while proving nothing.
            Assert.True(nonTrivial >= 10, $"Only {nonTrivial} users resolved to a manager — the comparison would be near-vacuous.");
            Assert.Empty(divergences);

            // ── BRANCH-COVERAGE PROOF (S125 close, external-lens WARNING) ────────────────────────
            // An earlier revision of this test's summary CLAIMED to cover the R3 vikar branch while
            // the fixture made it unreachable — its only vikar covered a leader nobody reported to.
            // "No divergences" over branches that never execute proves nothing, so the branches are
            // now asserted to have actually run.
            var underVikar = await repo.ResolveDesignatedApproverAsync(
                sqlSource, S106SeedScalePerfFixture.ShapeUnderVikar, today);
            Assert.Equal(S106SeedScalePerfFixture.ShapeStandIn, underVikar.ManagerId);
            Assert.Equal("ACTING_MANAGER", underVikar.ApprovalMethod);   // the R3 vikar branch RAN

            var escalated = await repo.ResolveDesignatedApproverAsync(
                sqlSource, S106SeedScalePerfFixture.ShapeEscalates, today);
            Assert.Null(escalated.ManagerId);                            // the escalation branch RAN
            Assert.Equal(1, escalated.Depth);

            // THE CROSS-ORGANISATION CASE (external-lens BLOCKER). Live SQL resolves to the cross-Org
            // stand-in; an Organisation-scoped prefetch that answered "inactive" from a map MISS would
            // skip it and fall through to the in-Org PRIMARY — admitting an approver SQL admits nobody
            // for. Asserted on BOTH sources, so the divergence cannot hide.
            var crossSql = await repo.ResolveDesignatedApproverAsync(
                sqlSource, S106SeedScalePerfFixture.ShapeUnderCrossVikar, today);
            var crossPre = await repo.ResolveDesignatedApproverAsync(
                prefetched, S106SeedScalePerfFixture.ShapeUnderCrossVikar, today);
            Assert.Equal(S106SeedScalePerfFixture.ShapeCrossOrgStandIn, crossSql.ManagerId);
            Assert.Equal(crossSql, crossPre);

            await tx.RollbackAsync();
        }
        finally
        {
            await _fx.ClearShapeMatrixAsync();
            await _fx.ClearPendingScenarioAsync();
        }
    }

    /// <summary>
    /// <b>S125 / TASK-12501 step 3c — the COMBINED differential test: the final authority VERDICT,
    /// prefetched vs live SQL, for every (candidate, employee) pair.</b>
    ///
    /// <para>Step 3c moved three more lookups behind a prefetch — the role floor, the home-Organisation
    /// lookup and the unit-leader classification. The resolver differential test above covers the edge
    /// leg only; this one compares what actually gates approval:
    /// <c>IsEffectiveApproverOrUnitLeaderAsync</c>, the predicate the approve/reject/reopen endpoints
    /// use. If any of the three prefetched facts disagrees with SQL for any pair, the verdict differs
    /// and this fails.</para>
    ///
    /// <para><b>Why pairs and not the tile map.</b> Comparing two projections' maps would let
    /// compensating errors cancel — one employee wrongly admitted and another wrongly denied leaves
    /// the same totals. Comparing the boolean verdict for the full cross-product of candidates ×
    /// employees cannot cancel: any single disagreement is reported with both sides named.</para>
    ///
    /// <para>The candidate set here is deliberately wider than the projection's own (every unit leader,
    /// vikar, edge manager and role-revoked manager in the fixture, against every pending employee),
    /// so the comparison covers pairs the projection would never construct — including ones that must
    /// be DENIED. A test that only compares pairs expected to pass would miss a prefetch that is too
    /// permissive, which is the dangerous direction.</para>
    /// </summary>
    [Fact]
    public async Task F1Differential_PrefetchedFacts_MatchSqlFacts_ForEveryCandidateEmployeePair()
    {
        await _fx.AddPendingScenarioAsync(10);
        await _fx.AddShapeMatrixAsync();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var vikarRepo = new ManagerVikarRepository(_fx.Factory);
            var reportingRepo = new ReportingLineRepository(_fx.Factory, vikarRepo);
            var authorizer = new DesignatedApproverAuthorizer(_fx.Factory, reportingRepo);

            await using var conn = _fx.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead);

            var pathParam = S106SeedScalePerfFixture.Org3Path + "%";
            var sqlLines = reportingRepo.CreateSqlDataSource(conn, tx);
            var lines = await PrefetchedReportingLineDataSource.BuildAsync(conn, tx, pathParam, today, sqlLines, default);
            var facts = await PrefetchedAuthorityFacts.BuildAsync(conn, tx, pathParam, today, default);

            // Candidates: every actor in the fixture that could plausibly hold authority, INCLUDING
            // ones that must be denied (the role-revoked manager, the inactive manager).
            var candidates = new[]
            {
                S106SeedScalePerfFixture.Org3EdgeManager,
                S106SeedScalePerfFixture.Org3Leader1,
                S106SeedScalePerfFixture.Org3Leader2,
                S106SeedScalePerfFixture.ShapeVikarOfLeader1,
                S106SeedScalePerfFixture.ShapeRoleRevokedMgr,
                S106SeedScalePerfFixture.ShapeInactiveMgr,
            };
            var employees = new List<string>
            {
                S106SeedScalePerfFixture.ShapeInUnit,
                S106SeedScalePerfFixture.ShapeNullUnit,
                S106SeedScalePerfFixture.ShapeOrphan,
                S106SeedScalePerfFixture.ShapeRevokedEdge,
                S106SeedScalePerfFixture.ShapeEscalates,
            };
            employees.AddRange(Enumerable.Range(1, 10)
                .Select(i => $"{S106SeedScalePerfFixture.PendingPrefix}{i}"));
            // Self-pairs included deliberately: segregation of duties must hold identically in both.
            employees.AddRange(candidates);

            var divergences = new List<string>();
            var admitted = 0;
            foreach (var candidate in candidates)
            {
                foreach (var employee in employees)
                {
                    var viaSql = await authorizer.IsEffectiveApproverOrUnitLeaderAsync(
                        conn, tx, ctx: null, source: null, facts: null, candidate, employee, today, default);
                    var viaPrefetch = await authorizer.IsEffectiveApproverOrUnitLeaderAsync(
                        conn, tx, ctx: null, lines, facts, candidate, employee, today, default);

                    if (viaSql != viaPrefetch)
                        divergences.Add($"({candidate} -> {employee}): sql={viaSql} prefetched={viaPrefetch}");
                    if (viaSql)
                        admitted++;
                }
            }

            var pairs = candidates.Length * employees.Count;
            _out.WriteLine($"DIFFERENTIAL-COMBINED: {pairs} pairs compared, {admitted} admitted by SQL, {divergences.Count} divergences");

            // Non-vacuity in BOTH directions: a comparison where everything is denied would pass while
            // proving nothing, and so would one where everything is admitted.
            Assert.True(admitted >= 5, $"Only {admitted} pairs admitted — the comparison is near-vacuous.");
            Assert.True(admitted < pairs, "Every pair was admitted — the comparison cannot detect an over-permissive prefetch.");
            Assert.Empty(divergences);

            await tx.RollbackAsync();
        }
        finally
        {
            await _fx.ClearShapeMatrixAsync();
            await _fx.ClearPendingScenarioAsync();
        }
    }

    // ── Helpers ──

    private ApprovalPeriodRepository NewApprovalRepo()
    {
        var reportingRepo = new ReportingLineRepository(_fx.Factory);
        var authorizer = new DesignatedApproverAuthorizer(_fx.Factory, reportingRepo);
        return new ApprovalPeriodRepository(_fx.Factory, authorizer, reportingRepo);
    }
}

/// <summary>
/// Counts (and records) the SQL commands a measured block issues against ONE Postgres container, via the
/// Npgsql 8 <c>"Npgsql"</c> command <see cref="ActivitySource"/>. Filtered to <paramref name="port"/>
/// (the testcontainer's unique port, carried on the activity's <c>db.connection_string</c> tag) so a
/// concurrently-running Docker test class against a DIFFERENT container never pollutes the count.
/// </summary>
internal sealed class DbCommandCounter : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly string _portToken;
    private int _count;
    private readonly List<string> _statements = new();

    public DbCommandCounter(int port)
    {
        _portToken = $"Port={port}";
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private void OnStopped(Activity a)
    {
        var cs = a.GetTagItem("db.connection_string") as string ?? string.Empty;
        if (!cs.Contains(_portToken, StringComparison.Ordinal))
            return; // a different container — ignore.
        Interlocked.Increment(ref _count);
        var stmt = a.GetTagItem("db.statement") as string;
        lock (_statements)
            _statements.Add(stmt ?? "<no-statement>");
    }

    public int Count => Volatile.Read(ref _count);

    /// <summary>Logs the first lines of each captured statement (for the perf report / debugging).</summary>
    public void DumpTo(ITestOutputHelper outputHelper)
    {
        string[] snapshot;
        lock (_statements)
            snapshot = _statements.ToArray();
        var sb = new StringBuilder();
        for (var i = 0; i < snapshot.Length; i++)
        {
            var first = snapshot[i].Split('\n')[0].Trim();
            if (first.Length > 110) first = first[..110] + " …";
            sb.Append("  [").Append(i + 1).Append("] ").Append(first).Append('\n');
        }
        if (sb.Length > 0)
            outputHelper.WriteLine(sb.ToString().TrimEnd());
    }

    public void Dispose() => _listener.Dispose();
}
