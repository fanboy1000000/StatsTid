using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S78-7801 (R1/R2/R6) — concurrency hardening for the approval action endpoints
/// (approve / reject / reopen). Two invariants are proven here with DETERMINISTIC held-lock
/// interleaves (mirroring the <c>ReportingLineWriteLifecycleTests</c> side-conn + <c>pg_locks</c>
/// waiter harness), not bare <c>Task.WhenAll</c> races that can pass by running sequentially:
///
/// <list type="number">
/// <item><description><b>R2 — the status race.</b> The status transition is a CONDITIONAL
/// <c>UPDATE … WHERE status = ANY(allowed)</c> issued as the FIRST mutation in the tx. A concurrent
/// double-transition (approve-vs-approve, approve-vs-reopen) yields exactly ONE winner; the loser sees
/// 0 rows affected → a clean 409 with NO audit row, NO outbox event, and NO FallbackTraversalWarning
/// written (the reorder before the side-effects guarantees this).</description></item>
/// <item><description><b>R1 — in-lock edge-auth re-evaluation.</b> The action tx takes the
/// period-employee's <c>reporting-tree</c> advisory (drift-guarded) as its first lock-bearing
/// statement, THEN re-evaluates the designated edge. (a) MUTUAL EXCLUSION: a side connection holding the
/// SAME tree key (simulating an in-flight reporting-line/admin-vikar revoke) makes the approve BLOCK on
/// the key — proven by the <c>pg_locks</c> waiter assertion (a sequentially-run or merely-slow request
/// cannot satisfy it). (b) SERIALIZATION CATCHES THE REVOKE: a vikar revocation that commits BEFORE the
/// approve gets the lock flips the in-tx re-eval to DENY → 403 (proving the serialization observes the
/// frozen committed edge state, not merely "a lock exists").</description></item>
/// </list>
///
/// <para>
/// Topology reuses the seed orgs from <see cref="DesignatedApproverAuthorityTests"/> (S92/ADR-035
/// flatten: STY02 is an ORGANISATION under MAO MIN01). The designated approver (<c>s78_mgr</c>, STY02)
/// is the PRIMARY manager of <c>s78_emp</c> (STY02); the PRIMARY edge is the load-bearing authorizing
/// surface the in-lock re-eval guards. (Post-flatten Mgr ALSO org-scope-covers Emp, but the deep-chain
/// designated cases below resolve PURELY via the edge.)
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ApprovalConcurrencyHardeningTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // STY02 Organisation — distinct from the seed + the S74 test users.
    private const string Emp = "s78_emp";   // STY02 — the report
    private const string Mgr = "s78_mgr";   // STY02 — PRIMARY manager of Emp
    private const string Vik = "s78_vik";   // STY02 — Mgr's vikar stand-in (a Leader)
    // B1 real-revoker co-location (the admin-vikar CREATE endpoint, Endpoint 14):
    private const string Adm = "s78_adm";   // STY02 — LOCAL_ADMIN @ STY02 (the admin-on-behalf actor)
    private const string Cov = "s78_cov";   // STY02 — LOCAL_LEADER @ STY02 (covers Emp → a valid vikar for Mgr)
    // B2 deep fallback-traversal chain (seeded inline in the B2 test; listed here only so the
    // shared cleanup removes them). DEmp → DM1(inactive) → DM2(inactive) → DM3(inactive) →
    // DM4(inactive) → DM5(active): the escalation walk advances 4 levels (depth = 4 > 3) so the
    // resolved approver DM5 genuinely emits ONE FallbackTraversalWarning.
    private const string DEmp = "s78_demp";  // STY02 — the deep-chain report
    private const string DM1 = "s78_dm1";    // STY02 — inactive
    private const string DM2 = "s78_dm2";    // STY02 — inactive
    private const string DM3 = "s78_dm3";    // STY02 — inactive
    private const string DM4 = "s78_dm4";    // STY02 — inactive
    private const string DM5 = "s78_dm5";    // STY02 — ACTIVE; the resolved approver at depth 4
    private const string TreeRootSty02 = "STY02";

    private static readonly string[] AllUsers =
        { Emp, Mgr, Vik, Adm, Cov, DEmp, DM1, DM2, DM3, DM4, DM5 };

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await CleanupAsync(conn);
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await CleanupAsync(conn);
        }
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R2 — the status race: a concurrent double-transition has ONE winner + a clean 409 loser
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two simultaneous approves of the SAME SUBMITTED period: exactly ONE wins (200 → APPROVED), the
    /// other loses the conditional UPDATE (0 rows) → 409. The loser wrote NO audit row, NO outbox event,
    /// and — the load-bearing, DISCRIMINATING assertion — NO FallbackTraversalWarning (the conditional
    /// UPDATE precedes every side-effect; the warning enqueue is gated behind the 0-row short-circuit).
    /// We HOLD the tree advisory key on a side connection so both legs queue behind it (deterministic
    /// ordering), release, and let them resolve — without the held lock the two legs could run wholly
    /// sequentially.
    ///
    /// <para>B2 (Step-7a) — the warning assertion is now DISCRIMINATING. The period belongs to a DEEP
    /// fallback-traversal chain (DEmp → DM1..DM4 all INACTIVE → DM5 ACTIVE), so the in-tx resolver walks
    /// the escalation 4 levels (depth = 4 &gt; 3) and the WINNER genuinely emits exactly ONE
    /// FallbackTraversalWarning. The actor is DM5 (the resolved approver) — a DESIGNATED_MANAGER (NOT a
    /// fallback), so no 428 gate fires. We assert the WINNER writes exactly ONE warning AND the 409 LOSER
    /// writes ZERO (total = 1). With the OLD depth-0 topology NEITHER leg EVER emitted a warning, so the
    /// "loser writes zero" assertion passed TRIVIALLY (0 == 0) and could not distinguish correct ordering
    /// from a regression that lets the 0-row loser leak its side-effects.</para>
    ///
    /// <para>NOW the assertion is load-bearing: if a regression let the 0-row loser fall through the
    /// conditional-UPDATE short-circuit and COMMIT its tx (e.g. the <c>if (oldStatus is null) return
    /// Conflict</c> guard removed/weakened), the loser ALSO enqueues a FallbackTraversalWarning + an audit
    /// row + a PeriodApproved outbox event → each loser-writes-nothing count becomes 2 and this test goes
    /// RED. VERIFIED: neutering that short-circuit makes the loser commit, the warning/audit/outbox counts
    /// flip 1 → 2, and this test FAILS (it stays GREEN with the correct short-circuit). The depth-0
    /// topology could not exercise the warning path at all, so it could never have caught this.</para>
    /// </summary>
    [Fact]
    public async Task R2_ConcurrentDoubleApprove_OneWinner_OneClean409_LoserWritesNothing()
    {
        // Seed the DEEP fallback chain (depth 4 > 3 → the resolved approver DM5 emits ONE warning).
        await SeedDeepFallbackChainAsync();
        var periodId = await InsertPeriodAsync(DEmp, "STY02", "SUBMITTED");
        // DM5 is the resolved DESIGNATED_MANAGER of DEmp at depth 4 (escalation through the inactive
        // DM1..DM4) → DESIGNATED_MANAGER, no 428 (DM5 is the designated approver, so even with org-scope
        // also covering DEmp post-flatten the classification is designated, not a fallback).
        var client = LeaderClient(DM5, "STY02");

        // HOLD the STY02 tree key so both approves park on the advisory (both take it in-tx).
        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        Task<HttpResponseMessage> legA, legB;
        try
        {
            legA = client.PostAsync($"/api/approval/{periodId}/approve", null);
            legB = client.PostAsync($"/api/approval/{periodId}/approve", null);

            // Both legs must reach and WAIT on our tree advisory key (proves they take the lock in-tx).
            Assert.True(await WaitForAdvisoryLockWaiterCountAsync(TreeRootSty02, minWaiters: 2),
                "Expected both approve legs to block on the STY02 tree advisory lock.");

            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
        }
        finally
        {
            await holdConn.DisposeAsync();
        }

        var responses = await Task.WhenAll(legA, legB);
        var codes = responses.Select(r => r.StatusCode).OrderBy(c => c).ToArray();

        // Exactly one 200 (winner) and one 409 (loser of the conditional UPDATE).
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.Conflict));
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));

        // The loser wrote NOTHING (the conditional UPDATE precedes EVERY side-effect): exactly ONE
        // APPROVED audit row + ONE PeriodApproved outbox event.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM approval_audit WHERE period_id = @id AND action = 'APPROVED'",
            ("id", periodId)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PeriodApproved'",
            ("s", ApprovalStream(DEmp, new DateOnly(2026, 5, 1)))));

        // THE DISCRIMINATING ASSERTION (B2): the WINNER emits EXACTLY ONE FallbackTraversalWarning
        // (depth 4 > 3) and the LOSER emits ZERO → total == 1. The warning stream is keyed on the
        // PERIOD's employee (reporting-line-{DEmp}). If a regression hoisted the warning enqueue ABOVE
        // the conditional UPDATE, the 0-row loser would ALSO enqueue one → total == 2 → this FAILS.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'FallbackTraversalWarning'",
            ("s", $"reporting-line-{DEmp}")));
    }

    /// <summary>
    /// Seeds the B2 DEEP fallback-traversal chain so the in-tx <c>ResolveDesignatedApproverAsync</c> walks
    /// the inactive-manager escalation FOUR levels — depth = 4 (&gt; the 3-level FallbackTraversalWarning
    /// threshold), so the resolved approver (DM5) genuinely emits ONE warning on approve.
    ///
    /// <para>Chain: <c>DEmp → DM1(inactive) → DM2(inactive) → DM3(inactive) → DM4(inactive) → DM5(active)</c>.
    /// The resolver advances UP once per inactive manager (DM1..DM4 = 4 advances → depth 4), then returns
    /// DM5 (active) as <c>(DM5, "DESIGNATED_MANAGER", 4)</c>. Created inline (not in the shared seed) so the
    /// other tests keep the simple depth-0 Emp→Mgr topology; the users are in <see cref="AllUsers"/> so the
    /// shared cleanup removes them.</para>
    /// </summary>
    private async Task SeedDeepFallbackChainAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // DEmp + DM5 are ACTIVE; DM1..DM4 are INACTIVE (the escalation sources).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@demp, @demp, '$2a$11$fake', 'S78 DEmp', 's78_demp@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@dm1,  @dm1,  '$2a$11$fake', 'S78 DM1',  's78_dm1@test.dk',  'STY02', 'HK', 'OK24', FALSE),
                (@dm2,  @dm2,  '$2a$11$fake', 'S78 DM2',  's78_dm2@test.dk',  'STY02', 'HK', 'OK24', FALSE),
                (@dm3,  @dm3,  '$2a$11$fake', 'S78 DM3',  's78_dm3@test.dk',  'STY02', 'HK', 'OK24', FALSE),
                (@dm4,  @dm4,  '$2a$11$fake', 'S78 DM4',  's78_dm4@test.dk',  'STY02', 'HK', 'OK24', FALSE),
                (@dm5,  @dm5,  '$2a$11$fake', 'S78 DM5',  's78_dm5@test.dk',  'STY02', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("demp", DEmp);
            cmd.Parameters.AddWithValue("dm1", DM1);
            cmd.Parameters.AddWithValue("dm2", DM2);
            cmd.Parameters.AddWithValue("dm3", DM3);
            cmd.Parameters.AddWithValue("dm4", DM4);
            cmd.Parameters.AddWithValue("dm5", DM5);
            await cmd.ExecuteNonQueryAsync();
        }

        // DM5 must be a LeaderOrAbove for IsEffectiveDesignatedApproverAsync's explicit role gate. It
        // is the resolved designated approver of DEmp (the edge classifies the approve as designated).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@dm5,  'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@demp, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("dm5", DM5);
            cmd.Parameters.AddWithValue("demp", DEmp);
            await cmd.ExecuteNonQueryAsync();
        }

        // The PRIMARY chain (all same tree STY02). AssignAsync does not require an active manager — the
        // resolver walks past inactive managers by escalation (the endpoint-level same-tree/active checks
        // do not run on this direct repo seed, mirroring DesignatedApproverAuthorityTests).
        var repo = new ReportingLineRepository(_dbFactory);
        await repo.AssignAsync(null, MakeLine(DEmp, DM1, TreeRootSty02));
        await repo.AssignAsync(null, MakeLine(DM1, DM2, TreeRootSty02));
        await repo.AssignAsync(null, MakeLine(DM2, DM3, TreeRootSty02));
        await repo.AssignAsync(null, MakeLine(DM3, DM4, TreeRootSty02));
        await repo.AssignAsync(null, MakeLine(DM4, DM5, TreeRootSty02));
    }

    /// <summary>
    /// approve-vs-reopen race on the SAME EMPLOYEE_APPROVED period, queued behind a held tree key so BOTH
    /// verbs PARK on the advisory and provably SERIALIZE on it (both take <c>reporting-tree</c> in-tx). It
    /// is the ONE shared source state where BOTH verbs are legal — approve: {SUBMITTED, EMPLOYEE_APPROVED};
    /// leader-reopen: {EMPLOYEE_APPROVED, APPROVED} — so after release WHICHEVER acquires the lock first
    /// commits its conditional UPDATE, then the loser re-reads the locked-in status.
    ///
    /// <para>Post-S78-7801, there is NO "never both 200" invariant: the allowed-source sets OVERLAP only on
    /// EMPLOYEE_APPROVED, but they also CHAIN —</para>
    /// <list type="bullet">
    /// <item><description><b>approve wins the lock first</b> → EMPLOYEE_APPROVED→APPROVED (200). The reopen
    /// then sees APPROVED, which is in its {EMPLOYEE_APPROVED, APPROVED} source set → it LEGALLY takes
    /// APPROVED→DRAFT (200). BOTH 200 — a valid two-step sequence — and the reopen's
    /// <c>PeriodReopened.PreviousStatus</c> must be the LOCKED-IN <b>APPROVED</b> (the BLOCKER-1
    /// <c>RETURNING</c> fix), NOT the stale pre-tx read of EMPLOYEE_APPROVED.</description></item>
    /// <item><description><b>reopen wins the lock first</b> → EMPLOYEE_APPROVED→DRAFT (200), recording
    /// <c>PreviousStatus = EMPLOYEE_APPROVED</c>. The approve then sees DRAFT (outside its
    /// {SUBMITTED, EMPLOYEE_APPROVED} set) → 0 rows → a clean 409.</description></item>
    /// </list>
    ///
    /// <para>So the REAL invariant is: <c>okCount ∈ {1, 2}</c>, the non-200s are clean 409s (never 5xx), and
    /// the terminal status is APPROVED or DRAFT (DRAFT whenever reopen returned 200 — never a torn
    /// intermediate). We additionally VERIFY the BLOCKER-1 fix by reading the emitted
    /// <c>PeriodReopened</c> outbox payload's <c>previousStatus</c> and asserting it equals the LOCKED-IN
    /// prior status (APPROVED when approve also won → committed first; EMPLOYEE_APPROVED when approve 409'd
    /// → reopen won first), which the stale pre-tx read would get wrong in the both-200 branch.</para>
    /// </summary>
    [Fact]
    public async Task R2_ConcurrentApproveVsReopen_SerializeOnLock_NoTornState_ReopenRecordsLockedInPrevStatus()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "EMPLOYEE_APPROVED");
        var client = LeaderClient(Mgr, "STY02");

        // HOLD the STY02 tree key so BOTH verbs park on the advisory (both take it in-tx) → they serialize
        // on the lock; whichever acquires first arbitrates the chained transition.
        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        Task<HttpResponseMessage> approveLeg, reopenLeg;
        try
        {
            approveLeg = client.PostAsync($"/api/approval/{periodId}/approve", null);
            reopenLeg = client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "race" });

            // Both legs must reach and WAIT on our tree advisory key (proves they serialize on the lock —
            // a sequentially-run pair could not both be observed waiting).
            Assert.True(await WaitForAdvisoryLockWaiterCountAsync(TreeRootSty02, minWaiters: 2),
                "Expected both the approve and reopen legs to block on the STY02 tree advisory lock.");

            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
        }
        finally
        {
            await holdConn.DisposeAsync();
        }

        var approveRsp = await approveLeg;
        var reopenRsp = await reopenLeg;

        // Both valid outcomes accepted: okCount ∈ {1, 2}; the non-200s are CLEAN 409s (never 5xx).
        var statuses = new[] { approveRsp.StatusCode, reopenRsp.StatusCode };
        var okCount = statuses.Count(c => c == HttpStatusCode.OK);
        var conflictCount = statuses.Count(c => c == HttpStatusCode.Conflict);
        Assert.Contains(okCount, new[] { 1, 2 });
        Assert.Equal(2 - okCount, conflictCount);
        Assert.DoesNotContain(statuses, c => (int)c >= 500);

        // No torn state: terminal status ∈ {APPROVED, DRAFT}, and is DRAFT whenever reopen returned 200
        // (reopen's transition always lands on DRAFT; if it won at all, DRAFT is the terminal status).
        var finalStatus = await ReadStatusAsync(periodId);
        Assert.Contains(finalStatus, new[] { "APPROVED", "DRAFT" });
        if (reopenRsp.StatusCode == HttpStatusCode.OK)
            Assert.Equal("DRAFT", finalStatus);

        // BLOCKER-1 verification: when reopen won (200), its emitted PeriodReopened event must record the
        // LOCKED-IN prior status (the RETURNING value), NOT the stale pre-tx read.
        if (reopenRsp.StatusCode == HttpStatusCode.OK)
        {
            var recordedPrev = await ReadOutboxPayloadFieldAsync(
                ApprovalStream(Emp, new DateOnly(2026, 5, 1)), "PeriodReopened", "previousStatus");

            // approve also 200 → approve committed EMPLOYEE_APPROVED→APPROVED first, so reopen's locked-in
            // prior is APPROVED (the stale pre-tx read would wrongly record EMPLOYEE_APPROVED).
            // approve 409 → reopen won first directly from EMPLOYEE_APPROVED.
            var expectedPrev = approveRsp.StatusCode == HttpStatusCode.OK ? "APPROVED" : "EMPLOYEE_APPROVED";
            Assert.Equal(expectedPrev, recordedPrev);
        }
    }

    /// <summary>
    /// R2 on the EMPLOYEE reopen arm: two simultaneous employee-reopens of the same EMPLOYEE_APPROVED
    /// period. The EMPLOYEE arm gets the conditional UPDATE (allowed source = {EMPLOYEE_APPROVED}) but NOT
    /// the advisory lock — so it cannot use the tree-lock barrier; instead we fire both legs with a bare
    /// concurrent dispatch and assert the conditional UPDATE still yields exactly one winner + one 409.
    /// (The conditional UPDATE is row-level: even without the advisory, the second UPDATE sees DRAFT and
    /// affects 0 rows.)
    /// </summary>
    [Fact]
    public async Task R2_EmployeeReopenArm_ConditionalUpdate_OneWinner_One409()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "EMPLOYEE_APPROVED");
        var empClient = EmployeeClient(Emp, "STY02");

        var legA = empClient.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "a" });
        var legB = empClient.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "b" });
        var responses = await Task.WhenAll(legA, legB);
        var codes = responses.Select(r => r.StatusCode).ToArray();

        // Exactly one transition succeeded; the loser saw a non-EMPLOYEE_APPROVED row → 0 rows → 409.
        // (A pre-tx status re-read could ALSO surface as the 403 "Employee can only reopen
        // EMPLOYEE_APPROVED" guard if the loser reads DRAFT before its own tx; either way it never
        // double-transitions, so we assert exactly one 200 and the loser is a clean conflict/forbidden.)
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(1, codes.Count(c => c is HttpStatusCode.Conflict or HttpStatusCode.Forbidden));
        Assert.Equal("DRAFT", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// R2 on the LEADER reopen arm (the arm that DOES take the advisory): two simultaneous leader-reopens
    /// of an APPROVED period queued behind a held tree key → exactly one 200 + one 409, proving the
    /// conditional UPDATE (allowed source = {EMPLOYEE_APPROVED, APPROVED}) serializes the leader arm too.
    /// </summary>
    [Fact]
    public async Task R2_LeaderReopenArm_ConcurrentDouble_OneWinner_One409()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "APPROVED");
        var client = LeaderClient(Mgr, "STY02");

        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        Task<HttpResponseMessage> legA, legB;
        try
        {
            legA = client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "a" });
            legB = client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "b" });

            Assert.True(await WaitForAdvisoryLockWaiterCountAsync(TreeRootSty02, minWaiters: 2),
                "Expected both leader-reopen legs to block on the STY02 tree advisory lock.");

            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
        }
        finally
        {
            await holdConn.DisposeAsync();
        }

        var responses = await Task.WhenAll(legA, legB);
        var codes = responses.Select(r => r.StatusCode).ToArray();
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.Conflict));
        Assert.Equal("DRAFT", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R1 — in-lock edge-auth re-evaluation: mutual exclusion + serialization catches the revoke
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// MUTUAL EXCLUSION (the waiter assertion). A side connection HOLDS the STY02 <c>reporting-tree</c>
    /// advisory key — exactly the key a concurrent reporting-line remove / admin-vikar revoke would hold.
    /// An approve for Emp (whose tree is STY02) must BLOCK on that key: proven by the <c>pg_locks</c>
    /// waiter poll (the endpoint's backend is observed WAITING, granted=false, on our key) AND by the
    /// approve NOT completing within a short window. Releasing the key lets the approve proceed → 200.
    /// A merely-slow or sequentially-run request cannot satisfy the waiter assertion.
    /// </summary>
    [Fact]
    public async Task R1_HeldTreeLock_BlocksApprove_UntilReleased_ThenProceeds()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = LeaderClient(Mgr, "STY02");

        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        try
        {
            var approveLeg = client.PostAsync($"/api/approval/{periodId}/approve", null);

            // The endpoint's backend must be WAITING on OUR advisory key (it took it in-tx).
            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                "No backend was observed WAITING on the STY02 tree advisory lock — approve did not take it.");

            // PROOF OF BLOCKING: the approve has not completed while we hold the key.
            var finishedEarly = await Task.WhenAny(approveLeg, Task.Delay(500)) == approveLeg;
            Assert.False(finishedEarly,
                "The approve completed while the tree advisory lock was held — it did not serialize on the lock.");

            // Release → the parked approve acquires the key and proceeds.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();

            var approveRsp = await approveLeg;
            Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
            Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
        }
        finally
        {
            await holdConn.DisposeAsync();
        }
    }

    /// <summary>
    /// SERIALIZATION CATCHES A REVOKE THAT COMMITS *AFTER* THE PRE-TX CHECK (the load-bearing R1 proof —
    /// the whole point of the in-tx re-evaluation, NOT merely "a lock exists" and NOT a revoke that
    /// committed before the request). Vik is Mgr's active vikar → Vik (a Leader) is Emp's single
    /// effective approver via the vikar edge. S92 flatten: to keep the EDGE the ONLY grant (so the in-tx
    /// EDGE re-eval — gated on !orgScopeAllowed — actually runs), Vik's TOKEN is minted with a disjoint
    /// scope (STY01, NOT covering Emp's STY02), while Vik still holds the vikar edge in the DB. Org-scope
    /// therefore denies; only the (revocable) edge grants.
    ///
    /// <para>The DETERMINISTIC interleave (held-lock barrier):</para>
    /// <list type="number">
    /// <item><description>HOLD the STY02 tree advisory on a side connection.</description></item>
    /// <item><description>Fire Vik's approve. It runs its PRE-tx edge check (vikar STILL active → passes),
    /// then BLOCKS on <c>AcquireTreeLockForEmployeeAsync</c> (our held key). The <c>pg_locks</c> waiter
    /// assertion proves it reached and parked ON the lock AFTER passing the pre-tx check.</description></item>
    /// <item><description>WHILE the approve is parked, COMMIT the vikar revocation on another connection
    /// (a direct DELETE that does NOT take the advisory, so it commits freely).</description></item>
    /// <item><description>RELEASE the held lock → the approve acquires it and runs its IN-TX re-eval
    /// (ReadCommitted), which re-reads the now-revoked edge → resolver no longer returns Vik for Emp →
    /// DENY → 403.</description></item>
    /// </list>
    /// This proves the SERIALIZATION catches a revoke that committed AFTER the pre-tx check (impossible to
    /// satisfy if the endpoint only relied on the pre-tx check — that check passed). The status stays
    /// SUBMITTED and NO approval side-effects are written.
    /// </summary>
    [Fact]
    public async Task R1_RevokeCommittedWhileApproveBlocked_InTxReeval_Denies403()
    {
        await CreateVikarAsync(Mgr, Vik, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        // Vik's TOKEN scope is STY01 (disjoint from Emp's STY02) → org-scope denies, so the ONLY grant is
        // the vikar EDGE — letting the in-tx edge re-eval (gated on !orgScopeAllowed) run and catch the revoke.
        var vikClient = LeaderClient(Vik, "STY01");

        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        try
        {
            // Fire the approve: pre-tx edge check PASSES (vikar active), then it parks on our held key.
            var approveLeg = vikClient.PostAsync($"/api/approval/{periodId}/approve", null);

            // Prove it reached and BLOCKED ON the lock AFTER passing its pre-tx check (a merely-slow or
            // sequentially-run request can't satisfy this — the pre-tx check has already run).
            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                "The approve did not block on the STY02 tree advisory after its pre-tx check.");

            // WHILE parked, COMMIT the revoke (direct DELETE, no advisory) — the post-pre-tx-check race.
            await EndVikarAsync(Mgr, Vik);

            // Release → the approve acquires the key and its IN-TX re-eval re-reads the revoked edge → 403.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();

            var approveRsp = await approveLeg;
            Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
            Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));

            // No approval side-effects from the denied leg.
            Assert.Equal(0, await CountAsync(
                "SELECT COUNT(*) FROM approval_audit WHERE period_id = @id AND action = 'APPROVED'",
                ("id", periodId)));
        }
        finally
        {
            await holdConn.DisposeAsync();
        }
    }

    /// <summary>
    /// B1 (Step-7a) — the REAL-REVOKER CO-LOCATION proof. The synthetic-lock tests above prove the
    /// approve BLOCKS on a side-connection-held <c>reporting-tree</c> key (the blocking mechanic) and
    /// that the in-tx re-eval catches a direct-SQL revoke (the serialization). What they DON'T prove is
    /// that the REAL revoker ENDPOINT actually shares the SAME <c>reporting-tree</c> advisory key — the
    /// co-location the whole approve-vs-revoke serialization depends on. This test proves it END-TO-END
    /// with the real <c>POST /api/admin/reporting-lines/{managerId}/vikar</c> endpoint (Endpoint 14, an
    /// employee-current-root mutator that takes the tree advisory as its FIRST in-tx statement).
    ///
    /// <para>WHY admin-vikar CREATE is a genuine REVOKE of Mgr's authority over Emp: Mgr approves Emp's
    /// period PURELY via the PRIMARY edge. S92 flatten: Mgr's DB org (STY02) would org-cover Emp, so to
    /// keep the approve EDGE-only (so the in-tx EDGE re-eval — gated on !orgScopeAllowed — runs and the
    /// revoke-wins branch lands on 403) Mgr's approve TOKEN is minted with a disjoint scope (STY01).
    /// Planting an active vikar (<c>Cov</c>) FOR Mgr makes the resolver return <c>Cov</c> — not Mgr — as
    /// Emp's single effective approver (the R3 vikar-consult step), so Mgr is no longer the designated
    /// approver of Emp. That is exactly the edge revocation the in-tx re-eval must catch.</para>
    ///
    /// <para>The CO-LOCATION proof (≥2 distinct waiters on ONE key): HOLD <c>reporting-org-STY02</c> on a
    /// side connection, then fire BOTH the approve (for Emp, tree STY02) AND the admin-vikar CREATE (for
    /// Mgr, whose tree root is STY02) concurrently. Assert <c>minWaiters: 2</c> — both endpoints PARK on
    /// the SAME <c>reporting-org-STY02</c> advisory (a synthetic key or a non-co-located revoker could
    /// not produce two distinct waiters on this exact key).</para>
    ///
    /// <para>SERIALIZED OUTCOME after release (lock-acquisition order is non-deterministic — BOTH orders are
    /// asserted as valid serializations, never a torn state):</para>
    /// <list type="bullet">
    /// <item><description><b>revoke commits first</b> → the vikar is planted; the approve's in-tx re-eval
    /// re-reads the now-revoked edge (Cov, not Mgr, resolves) → <b>403</b>, the period stays SUBMITTED,
    /// the vikar exists.</description></item>
    /// <item><description><b>approve commits first</b> → the approve <b>200</b>s (Emp→APPROVED) BEFORE the
    /// vikar lands; the admin-vikar CREATE then proceeds and the vikar exists.</description></item>
    /// </list>
    /// Either way the vikar is committed (the revoke applies) and the period is APPROVED-iff-approve-won —
    /// the two endpoints provably serialized on the shared key.
    /// </summary>
    [Fact]
    public async Task R1_RealAdminVikarRevoke_CoLocatesOnSameTreeKey_AsApprove_SerializedOutcome()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var approveClient = LeaderClient(Mgr, "STY01");   // disjoint token scope → approves PURELY via the edge
        var adminClient = AdminClient(Adm, "STY02");      // the admin-on-behalf actor (covers STY02)

        // HOLD the STY02 tree key so BOTH the approve AND the real admin-vikar CREATE park on the SAME
        // advisory key → ≥2 distinct waiters = the co-location proof.
        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        Task<HttpResponseMessage> approveLeg, vikarCreateLeg;
        try
        {
            approveLeg = approveClient.PostAsync($"/api/approval/{periodId}/approve", null);
            // The REAL revoker: plant Cov as Mgr's vikar (Cov is LOCAL_LEADER @ STY02 → covers Mgr's
            // only report, Emp on STY02). This endpoint keys on Mgr's current tree root = STY02.
            vikarCreateLeg = adminClient.PostAsJsonAsync(
                $"/api/admin/reporting-lines/{Mgr}/vikar",
                new { vikarUserId = Cov, effectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd"), reason = "FERIE" });

            // BOTH legs must park on the SAME reporting-org-STY02 advisory key — the real-co-location
            // proof (≥2 distinct waiters). A synthetic key or a non-co-located revoke endpoint could not
            // satisfy this against THIS key.
            Assert.True(await WaitForAdvisoryLockWaiterCountAsync(TreeRootSty02, minWaiters: 2),
                "Expected BOTH the approve AND the real admin-vikar CREATE endpoint to park on the SAME reporting-org-STY02 advisory key (≥2 waiters) — proving the real revoker co-locates with the approve.");

            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
        }
        finally
        {
            await holdConn.DisposeAsync();
        }

        var approveRsp = await approveLeg;
        var vikarRsp = await vikarCreateLeg;

        // The real revoke ALWAYS commits (the admin-vikar CREATE is independent of the approve's outcome):
        // 200 + an active vikar keyed on Mgr now exists. (If the approve won the lock first, the create
        // ran right after; if the create won, it ran first — either way it succeeds.)
        Assert.Equal(HttpStatusCode.OK, vikarRsp.StatusCode);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM manager_vikar WHERE absent_approver_id = @m AND vikar_user_id = @v AND effective_to IS NULL",
            ("m", Mgr), ("v", Cov)));

        // The SERIALIZED outcome — both orderings are valid, never a torn state:
        var finalStatus = await ReadStatusAsync(periodId);
        if (approveRsp.StatusCode == HttpStatusCode.OK)
        {
            // approve won the lock first → it committed Emp→APPROVED before the vikar landed.
            Assert.Equal("APPROVED", finalStatus);
            Assert.Equal(1, await CountAsync(
                "SELECT COUNT(*) FROM approval_audit WHERE period_id = @id AND action = 'APPROVED'",
                ("id", periodId)));
        }
        else
        {
            // revoke won the lock first → the approve's in-tx re-eval re-read the revoked edge → 403,
            // NO side-effects, period stays SUBMITTED. (A clean 403, never a 5xx.)
            Assert.Equal(HttpStatusCode.Forbidden, approveRsp.StatusCode);
            Assert.Equal("SUBMITTED", finalStatus);
            Assert.Equal(0, await CountAsync(
                "SELECT COUNT(*) FROM approval_audit WHERE period_id = @id AND action = 'APPROVED'",
                ("id", periodId)));
        }
    }

    // S94 (TASK-9406): two REQUIRED-mode enforcement tests were DELETED here —
    // `R1_ReassignmentWhileApproveBlocked_InTxReclassify_FiresConfirmFallback428` (the in-tx 428
    // re-classification gate) and `R1_OrgScopeAdmitted_NotDeniedBy_InTxEdgeReeval` (the confirmFallback
    // round-trip). REQUIRED-mode enforcement is retired (ADR-035 OQ6): there is no 428 gate, no
    // `confirmFallback`, and no `explicit_fallback_confirmation` column. The org-scope arm is now the
    // HR/Admin fallback floored at LocalHR (covered by S94FlatApprovalTests in
    // DesignatedApproverAuthorityTests). The genuine S78 advisory-lock + in-tx edge re-eval coverage
    // (R1 mutual exclusion / serialization-catches-revoke / real-revoker co-location / unchanged
    // designated metadata below) is KEPT.

    /// <summary>
    /// S78 BLOCKER 2 — NO OVER-DENIAL + correct LOCKED metadata for a legitimately-unchanged designated
    /// approval. Mgr is Emp's seeded PRIMARY designated manager (same STY02 Organisation; the edge
    /// classifies the approve as DESIGNATED even though org-scope also covers). We run the approve behind
    /// a held advisory (so the in-tx re-resolution executes), with nothing changing while it is parked.
    /// The in-tx re-derivation must NOT over-deny (Mgr is still the designated manager) and MUST persist
    /// the IN-TX-resolved metadata (<c>approval_method = DESIGNATED_MANAGER</c>,
    /// <c>designated_approver_id = Mgr</c>) — proving the authoritative metadata matches the locked edge
    /// state, not a stale snapshot. (S94: the REQUIRED-mode <c>explicit_fallback_confirmation</c>
    /// assertion was removed with the column; the kept <c>approval_method</c>/<c>designated_approver_id</c>
    /// audit metadata is the surviving classification.)
    /// </summary>
    [Fact]
    public async Task R1_UnchangedDesignatedApproval_BehindHeldLock_NotOverDenied_PersistsLockedMetadata()
    {
        var periodId = await InsertPeriodAsync(Emp, "STY02", "SUBMITTED");
        var client = LeaderClient(Mgr, "STY02");

        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        try
        {
            var approveLeg = client.PostAsync($"/api/approval/{periodId}/approve", null);

            Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                "The designated-manager approve did not block on the STY02 tree advisory in-tx.");

            // Nothing changes while parked — the in-tx re-derivation must reproduce the same (designated)
            // classification and NOT over-deny.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();

            var rsp = await approveLeg;
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
            // The IN-TX-resolved metadata is persisted (matches the locked edge state).
            Assert.Equal("DESIGNATED_MANAGER", await ReadColumnAsync(periodId, "approval_method"));
            Assert.Equal(Mgr, await ReadColumnAsync(periodId, "designated_approver_id"));
        }
        finally
        {
            await holdConn.DisposeAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Held-lock + pg_locks waiter harness (mirrors ReportingLineWriteLifecycleTests:632-808)
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireTreeLockOnSideConnAsync(string treeRoot)
    {
        var conn = _dbFactory.Create();
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('reporting-org-' || @root))", conn, tx);
        cmd.Parameters.AddWithValue("root", treeRoot);
        await cmd.ExecuteScalarAsync();
        return (conn, tx);
    }

    private async Task<bool> WaitForAdvisoryLockWaiterAsync(string treeRoot, int timeoutMs = 5000)
        => await WaitForAdvisoryLockWaiterCountAsync(treeRoot, minWaiters: 1, timeoutMs);

    private async Task<bool> WaitForAdvisoryLockWaiterCountAsync(string treeRoot, int minWaiters, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(DISTINCT pl.pid)
                FROM pg_locks pl
                JOIN pg_stat_activity sa ON sa.pid = pl.pid
                WHERE pl.locktype = 'advisory'
                  AND pl.granted = FALSE
                  AND pl.pid <> pg_backend_pid()
                  AND ((pl.classid::bigint << 32) | pl.objid::bigint)
                      = hashtext('reporting-org-' || @root)::bigint
                """, conn))
            {
                cmd.Parameters.AddWithValue("root", treeRoot);
                if ((long)(await cmd.ExecuteScalarAsync())! >= minWaiters)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup / helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp, @emp, '$2a$11$fake', 'S78 Emp', 's78_emp@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@mgr, @mgr, '$2a$11$fake', 'S78 Mgr', 's78_mgr@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@vik, @vik, '$2a$11$fake', 'S78 Vik', 's78_vik@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@adm, @adm, '$2a$11$fake', 'S78 Adm', 's78_adm@test.dk', 'STY02', 'AC', 'OK24', TRUE),
                (@cov, @cov, '$2a$11$fake', 'S78 Cov', 's78_cov@test.dk', 'STY02', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@mgr, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@vik, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@adm, 'LOCAL_ADMIN',  'STY02', 'ORG_ONLY', 'TEST'),
                -- Cov is a LOCAL_LEADER scoped at STY02 (covers Emp) → a VALID vikar for Mgr:
                -- its org-scope covers Mgr's only report (Emp on STY02).
                (@cov, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@emp, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the same-Organisation, same-tree edge.
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, MakeLine(Emp, Mgr, TreeRootSty02));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("vik", Vik);
        cmd.Parameters.AddWithValue("adm", Adm);
        cmd.Parameters.AddWithValue("cov", Cov);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId, string treeRoot) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = treeRoot,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            "DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids))");
        // The B1 admin-vikar CREATE writes a ManagerVikarCreated audit_projection row (actor = the admin).
        await ExecAsync(conn, "DELETE FROM audit_projection WHERE actor_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM outbox_events WHERE stream_id LIKE 'approval-s78_%' OR stream_id LIKE 'reporting-line-s78_%'");
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            if (sql.Contains("@ids"))
                cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private Task<Guid> InsertPeriodAsync(string employeeId, string orgId, string status)
        => InsertPeriodWithRangeAsync(employeeId, orgId, status, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

    private async Task<Guid> InsertPeriodWithRangeAsync(
        string employeeId, string orgId, string status, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task CreateVikarAsync(string absentApprover, string vikarUser, DateOnly untilDate)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await new ManagerVikarRepository(_dbFactory).CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "FERIE",
            OrganisationId = TreeRootSty02,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
    }

    /// <summary>
    /// COMMITS a vikar revocation (hard-delete the active row) — the committed revoke the in-tx re-eval
    /// must observe. A direct DELETE is sufficient for the resolver miss the re-eval depends on.
    /// </summary>
    private async Task EndVikarAsync(string absentApprover, string vikarUser)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM manager_vikar WHERE absent_approver_id = @a AND vikar_user_id = @v", conn);
        cmd.Parameters.AddWithValue("a", absentApprover);
        cmd.Parameters.AddWithValue("v", vikarUser);
        await cmd.ExecuteNonQueryAsync();
    }

    // S94 (TASK-9406): InsertActingLineDirectAsync was REMOVED — it only served the deleted in-tx 428
    // re-classification test (REQUIRED-mode enforcement retired, ADR-035 OQ6).

    private async Task<int> CountAsync(string sql, params (string name, object value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<string> ReadStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Reads a single (string) column of the period row (NULL → null). The column name is a
    /// fixed test-local literal (never user input), so direct interpolation is safe here.</summary>
    private async Task<string?> ReadColumnAsync(Guid periodId, string column)
    {
        var raw = await ReadRawColumnAsync(periodId, column);
        return raw as string;
    }

    private async Task<object?> ReadRawColumnAsync(Guid periodId, string column)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT {column} FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    private static string ApprovalStream(string employeeId, DateOnly periodStart)
        => $"approval-{employeeId}-{periodStart:yyyy-MM-dd}";

    /// <summary>
    /// Reads a single top-level (string) JSON field out of the most-recent <paramref name="eventType"/>
    /// outbox row on <paramref name="streamId"/>. The event_payload is JSONB serialized by
    /// <c>EventSerializer</c> (camelCase) — so the field name is the camelCase form (e.g. "previousStatus"
    /// for <c>PeriodReopened.PreviousStatus</c>). The field/eventType are fixed test-local literals (never
    /// user input), so the <c>-&gt;&gt;</c> extraction is safe. Returns null when the field is JSON null /
    /// absent or no such row exists.
    /// </summary>
    private async Task<string?> ReadOutboxPayloadFieldAsync(string streamId, string eventType, string field)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload ->> @field
            FROM outbox_events
            WHERE stream_id = @stream AND event_type = @type
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("field", field);
        cmd.Parameters.AddWithValue("stream", streamId);
        cmd.Parameters.AddWithValue("type", eventType);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string)result;
    }

    private HttpClient LeaderClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(userId, orgId));
        return client;
    }

    private HttpClient EmployeeClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(userId, orgId));
        return client;
    }

    private HttpClient AdminClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken(userId, orgId));
        return client;
    }

    private static string MintAdminToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC", orgId: orgId, scopes: scopes);
    }

    private static string MintLeaderToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintEmployeeToken(string userId, string orgId)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
