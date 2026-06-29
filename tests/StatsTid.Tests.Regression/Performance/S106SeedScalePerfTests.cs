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

    /// <summary>The tile-count projection's command count scales with the PENDING set, NOT total org
    /// size. The headline (org-size INDEPENDENCE): a 2000-user Organisation (Org1) AND a 250-user one
    /// (Org3), each with ZERO pending periods, BOTH issue exactly ONE command (the per-employee status
    /// scan) — an 8× org-size swing leaves the count at 1, so there is NO per-user fan-out. Then K=10 and
    /// K=20 pending employees (each with an edge manager + a unit holding two leaders = 3 candidate
    /// approvers) are added to Org3: the command count grows ~LINEARLY in K by a small per-pending
    /// multiplier (the pre-existing per-pending-employee N+1, S105 / TASK-10604) — bounded by the pending
    /// set, never by org size.</summary>
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
        _out.WriteLine($"TILE per-pending multiplier: ~{perPending10:0.0} (K=10), ~{perPending20:0.0} (K=20); the N+1 is bounded by the PENDING set (org size {org3Users} is irrelevant to the slope).");

        // (1) Growth is driven SOLELY by the PENDING set: strictly monotonic in K (0 < 10 < 20).
        Assert.True(count20 > count10 && count10 > small0, $"Expected monotonic growth with pending count (0→{small0}, 10→{count10}, 20→{count20}).");
        // (2) LINEAR in K (not quadratic / org-coupled): the per-pending multiplier is the SAME small
        //     constant at K=10 and K=20 (within tolerance) — the candidate fan-out (edge + 2 leaders) ×
        //     a few authorization probes, independent of the ~250 users.
        Assert.True(Math.Abs(perPending20 - perPending10) <= 3, $"Per-pending multiplier drifted ({perPending10:0.0} → {perPending20:0.0}) — growth is not linear in the pending set.");
        Assert.True(perPending20 < 40, $"Per-pending command multiplier {perPending20:0.0} is unexpectedly large.");
        Assert.True(sw.ElapsedMilliseconds < TileBudgetMs, $"Tile-count took {sw.ElapsedMilliseconds} ms (budget {TileBudgetMs} ms).");

        await _fx.ClearPendingScenarioAsync();
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
