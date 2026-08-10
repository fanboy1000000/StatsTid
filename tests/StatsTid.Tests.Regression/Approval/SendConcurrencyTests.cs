using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S127 / TASK-12708 — one Postgres testcontainer + one booted API shared by the send-command
/// CONCURRENCY / ATOMICITY suites (<see cref="SendConcurrencyTests"/> and
/// <see cref="SendAtomicityTests"/>, joined by the <c>SendConcurrency</c> xUnit collection). Modelled
/// on <c>SendCommandMatrixFixture</c>: the schema is the REAL <c>docker/postgres/init.sql</c>, booted
/// once, so the advisory lock (<c>EmployeeConsumptionLock</c>), the unique
/// <c>(employee_id, period_start, period_end)</c> constraint and every projection table the send
/// command reads all belong to production.
///
/// <para>The API is booted here (in <see cref="InitializeAsync"/>) so its startup seeders
/// (<c>EmployeeProfileSeeder</c>, <c>UserAgreementCodeBackfillSeeder</c>) backfill every init.sql user
/// BEFORE <see cref="SendAtomicityTests"/> boots its throwing-outbox derived host — a derived boot then
/// finds nothing to backfill and never calls the throwing outbox at startup (the S63/S65 boot-order
/// lesson).</para>
/// </summary>
public sealed class SendConcurrencyFixture : IAsyncLifetime
{
    // TestFixtures is internal → a public property of the harness type would be a CS0053 leak.
    private TestFixtures.DockerHarness _harness = null!;

    public StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    public DbConnectionFactory Db { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public ApprovalPeriodRepository ApprovalRepo { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        Db = new DbConnectionFactory(ConnectionString);
        Factory = new StatsTidWebApplicationFactory(ConnectionString);
        _ = Factory.CreateClient(); // boot seeders (idempotent; backfills every existing init.sql user)
        ApprovalRepo = new ApprovalPeriodRepository(Db);
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

[CollectionDefinition("SendConcurrency")]
public sealed class SendConcurrencyCollection : ICollectionFixture<SendConcurrencyFixture> { }

/// <summary>
/// Shared seeding / driving / observation helpers for the send-command concurrency + atomicity
/// classes. Two disciplines it enforces, because this is the sprint's recurring failure class
/// (a test that asserts on something that LOOKS like evidence and is not):
/// <list type="bullet">
///   <item><c>Task.WhenAll</c> is NOT proof of overlap. Real contention is forced with a THIRD
///   connection that holds the advisory lock and a poll of <c>pg_locks</c> that blocks until the
///   racing requests are genuinely QUEUED behind it as waiters (<see cref="WaitForWaitersAsync"/>).
///   The barrier TIMES OUT (fails loud) if a request never reaches the lock.</item>
///   <item>Every observation reads state back with NO copy of the rule under test.</item>
/// </list>
/// Isolation between cases is by CASE-UNIQUE employee identity; the collection runs its classes
/// sequentially against the one container, so <c>pg_locks</c> reflects only this suite's activity.
/// </summary>
public abstract class SendConcurrencyTestBase
{
    protected const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>STY02 (agreement HK) — seeded by init.sql before any WAF seeder runs.</summary>
    protected const string Org = "STY02";

    protected const int MarchYear = 2026;
    protected const int MarchMonth = 3;
    protected static readonly DateOnly MarchStart = new(2026, 3, 1);
    protected static readonly DateOnly MarchEnd = new(2026, 3, 31);

    // Monotonic outbox_id for hand-written absence rows (NOT-NULL BIGINT). Range chosen clear of the
    // other suites (SendCommandMatrix uses 71_271_000; AllocationGate 900_000; S116 51_160_000).
    private static long _absenceSeq = 72_708_000;
    private static long NextAbsenceOutboxId() => Interlocked.Increment(ref _absenceSeq);

    protected readonly SendConcurrencyFixture Fx;

    protected SendConcurrencyTestBase(SendConcurrencyFixture fx) => Fx = fx;

    protected static string UniqueEmp(string tag) => $"conc_{tag}_{Guid.NewGuid():N}"[..18];

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds users + user_agreement_codes + employee_profiles for a fresh employee.
    /// <c>ensureOrg:false</c> — STY02 exists in init.sql.</summary>
    protected Task SeedEmployeeAsync(string employeeId, string orgId = Org, string agreementCode = "HK")
        => RegressionSeed.SeedEmployeeAsync(
            Fx.ConnectionString, employeeId, orgId, agreementCode, "OK24", ensureOrg: false);

    /// <summary>Covers every expected March weekday (March 2026 carries no Danish public holiday —
    /// asserted) with a full-day VACATION absence, so a send passes the workday-coverage check.
    /// Absences live in a table NEITHER side of the allocation gate reads, so a covered month is
    /// vacuously allocation-balanced unless work/allocation rows are added.</summary>
    protected async Task CoverMarchWithAbsencesAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();

        await using (var holidayCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= @s AND holiday_date <= @e", conn))
        {
            holidayCmd.Parameters.AddWithValue("s", MarchStart);
            holidayCmd.Parameters.AddWithValue("e", MarchEnd);
            Assert.Equal(0L, (long)(await holidayCmd.ExecuteScalarAsync())!);
        }

        for (var d = MarchStart; d <= MarchEnd; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @seq)
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("seq", NextAbsenceOutboxId());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Directly seeds an <c>approval_periods</c> row (the by-id adapter's source).</summary>
    protected async Task<Guid> SeedApprovalRowAsync(
        string employeeId, string status, DateOnly start, DateOnly end, string orgId = Org)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (employee_id, org_id, period_start, period_end,
                                          period_type, status, agreement_code, ok_version)
            VALUES (@e, @org, @s, @en, 'MONTHLY', @st, 'HK', 'OK24')
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("en", end);
        cmd.Parameters.AddWithValue("st", status);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    // ── Driving the real routes ───────────────────────────────────────────────────────────────────

    protected static Task<HttpResponseMessage> PostSendAsync(
        HttpClient client, string employeeId, int year = MarchYear, int month = MarchMonth)
        => client.PostAsJsonAsync("/api/approval/send", new { employeeId, year, month });

    protected static Task<HttpResponseMessage> PostEmployeeApproveAsync(HttpClient client, Guid periodId)
        => client.PostAsync($"/api/approval/{periodId}/employee-approve", content: null);

    protected static Task<HttpResponseMessage> PostTimeEntryAsync(
        HttpClient client, string employeeId, DateOnly date, decimal hours,
        string activityType = "NORMAL", string? taskId = null, string agreementCode = "HK")
        => client.PostAsJsonAsync("/api/time-entries",
            new { employeeId, date, hours, taskId, activityType, agreementCode });

    protected static Task<HttpResponseMessage> PostSkemaWorkTimeSaveAsync(
        HttpClient client, string employeeId, DateOnly date, string start, string end,
        int year = MarchYear, int month = MarchMonth)
        => client.PostAsJsonAsync($"/api/skema/{employeeId}/save",
            new { year, month, workTime = new[] { new { date, intervals = new[] { new { start, end } }, manualHours = 0m } } });

    // ── Clients / tokens ──────────────────────────────────────────────────────────────────────────

    protected HttpClient EmployeeClient(string employeeId, string orgId = Org)
        => ClientFor(Fx.Factory, employeeId, StatsTidRoles.Employee, orgId,
            new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY"));

    protected static HttpClient ClientFor(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        string actorId, string role, string orgId, params RoleScope[] scopes)
    {
        var client = factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── The advisory-lock contention barrier (pg_locks) ───────────────────────────────────────────

    /// <summary>The (classid, objid) split of the send command's advisory key —
    /// <c>pg_advisory_xact_lock(hashtext('employee-' || id)::bigint)</c>. Postgres stores the bigint
    /// key as classid = high 32 bits, objid = low 32 bits, objsubid = 1. Computed via the SAME
    /// <c>hashtext</c> the production helper uses, so the poll cannot drift from the key.</summary>
    protected async Task<(long ClassId, long ObjId)> AdvisoryKeyPartsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT hashtext('employee-' || @id)::bigint", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        var key = (long)(await cmd.ExecuteScalarAsync())!;
        var u = unchecked((ulong)key);
        return ((long)(u >> 32), (long)(u & 0xFFFFFFFFUL));
    }

    /// <summary>Rows in <c>pg_locks</c> on our specific advisory key, filtered by grant state. A
    /// <c>granted=false</c> row is a backend BLOCKED on the lock; a <c>granted=true</c> row is the
    /// holder.</summary>
    protected async Task<int> CountAdvisoryLocksAsync(long classId, long objId, bool granted)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_locks
            WHERE locktype = 'advisory' AND objsubid = 1
              AND classid::bigint = @classid AND objid::bigint = @objid
              AND granted = @granted
            """, conn);
        cmd.Parameters.AddWithValue("classid", classId);
        cmd.Parameters.AddWithValue("objid", objId);
        cmd.Parameters.AddWithValue("granted", granted);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Blocks until at least <paramref name="expected"/> backends are WAITING on our advisory
    /// key — i.e. genuinely queued behind the holder. Times out (fails loud) if the racing requests
    /// never reach the lock, so a mis-wired test can never hang or silently pass.</summary>
    protected async Task WaitForWaitersAsync(long classId, long objId, int expected, int timeoutSeconds = 30)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var waiters = await CountAdvisoryLocksAsync(classId, objId, granted: false);
            if (waiters >= expected)
                return;
            if (sw.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
                throw new TimeoutException(
                    $"Only {waiters} advisory-lock waiter(s) on ({classId},{objId}) after {timeoutSeconds}s; " +
                    $"expected {expected}. The racing request(s) never queued on the lock.");
            await Task.Delay(20);
        }
    }

    // ── Reading state back (no copy of any rule under test) ───────────────────────────────────────

    protected async Task<(string Status, DateTime? EmployeeApprovedAt)?> ReadRowAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, employee_approved_at FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetFieldValue<DateTime>(1));
    }

    protected async Task<Guid?> FindPeriodIdAsync(string employeeId, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT period_id FROM approval_periods WHERE employee_id=@e AND period_start=@s AND period_end=@en",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("en", end);
        return await cmd.ExecuteScalarAsync() is Guid g ? g : null;
    }

    protected static string SendStreamId(string employeeId) => $"approval-{employeeId}-{MarchStart:yyyy-MM-dd}";

    protected Task<long> CountApprovalRowsAsync(string employeeId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM approval_periods WHERE employee_id=@e AND period_start=@s AND period_end=@en",
            ("e", employeeId), ("s", MarchStart), ("en", MarchEnd));

    protected Task<long> CountApprovalAuditAsync(Guid periodId, string action)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM approval_audit WHERE period_id=@id AND action=@a",
            ("id", periodId), ("a", action));

    // S127 Step-7a F7 — approval_audit.period_id carries NO foreign key (init.sql:900-908). An audit row
    // written on a SELF-MANAGED connection would COMMIT independently and survive the send's transaction
    // rollback as an orphan. The create-arm rollback test cannot key on its period id (minted in-tx and
    // rolled back), so it keys on the case-unique SELF-send actor: zero audit rows for this actor is the
    // only observation that catches an orphaned audit write.
    protected Task<long> CountApprovalAuditByActorAsync(string actorId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM approval_audit WHERE actor_id=@a", ("a", actorId));

    protected Task<long> CountOutboxAsync(string streamId, string eventType)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s AND event_type=@t",
            ("s", streamId), ("t", eventType));

    protected Task<long> CountEventsAsync(string streamId)
        => ScalarLongAsync("SELECT COUNT(*) FROM events WHERE stream_id=@s", ("s", streamId));

    protected Task<long> CountAuditProjectionByPeriodAsync(Guid periodId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type='PeriodEmployeeApproved' AND target_resource_id=@id",
            ("id", periodId.ToString()));

    protected Task<long> CountAuditProjectionByEmployeeAsync(string employeeId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type='PeriodEmployeeApproved' AND details->>'employeeId'=@e",
            ("e", employeeId));

    protected Task<long> CountWorkTimeRowsAsync(string employeeId, DateOnly date)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM work_time_projection WHERE employee_id=@e AND date=@d",
            ("e", employeeId), ("d", date));

    protected Task<long> CountTimeEntryRowsAsync(string employeeId, DateOnly date)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e AND date=@d",
            ("e", employeeId), ("d", date));

    protected async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    protected static async Task<string> BodyAsync(HttpResponseMessage rsp) => await rsp.Content.ReadAsStringAsync();
}

/// <summary>
/// S127 / TASK-12708 — the send-command concurrency invariants (refinement AC-7). Every test FORCES
/// real overlap or the isolation failure rather than observing it: (a)/(d)/(e) queue the racing
/// request(s) behind a third connection that holds the advisory lock and wait on <c>pg_locks</c> until
/// they are provably contending.
///
/// <para><b>The load-bearing isolation level (READ COMMITTED) has TWO complementary coverages
/// (S127 Step-7a F5):</b>
/// <list type="bullet">
///   <item><b>(a) is the PRODUCTION pin.</b> <see cref="AC7a_TwoConcurrentFirstSends_ExactlyOneWins_OtherCleanlyConflicts"/>
///   drives the real <c>/send</c> command at ITS production isolation. Flip that isolation to REPEATABLE
///   READ and (a) goes RED — the RR loser's frozen snapshot misses the winner's committed row, so it
///   takes the create arm and either 500s on a <c>40001</c> serialization failure (codes become
///   <c>[200,500]</c>) or returns the create-arm "concurrently" 409, NEITHER of which carries the
///   source-state gate's <c>EMPLOYEE_APPROVED</c> 409 body. Verified RED-under-RR at Step-7a.</item>
///   <item><b>The AC7b_* tests are the MECHANISM demonstration, NOT the pin.</b> They compose the
///   command's PRODUCTION primitives at a CHOSEN isolation (an argument they pass) to show DIRECTLY why
///   READ COMMITTED is required — RC re-read sees the row, RR misses it. Because they pass their own
///   isolation they stay green regardless of production's line 1718; that honest limit is why (a) carries
///   the pin. Their names therefore describe the PostgreSQL behaviour they demonstrate, not a production
///   guarantee (PAT-015 records the mechanism).</item>
/// </list></para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendConcurrency")]
public sealed class SendConcurrencyTests : SendConcurrencyTestBase
{
    public SendConcurrencyTests(SendConcurrencyFixture fx) : base(fx) { }

    // ── AC-7(a) — two concurrent FIRST sends of the same month ────────────────────────────────────
    //
    // FAILS IF: both sends create a row (duplicate), or both succeed (double transition / duplicate
    // audit+outbox), or the loser returns something other than a clean 409 naming the sent status.
    // Overlap is not assumed: a THIRD connection holds the lock, both sends queue behind it as
    // waiters, and the barrier only releases once TWO waiters are provably present.
    [Fact]
    public async Task AC7a_TwoConcurrentFirstSends_ExactlyOneWins_OtherCleanlyConflicts()
    {
        var emp = UniqueEmp("7a");
        await SeedEmployeeAsync(emp);
        await CoverMarchWithAbsencesAsync(emp);
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        // Third connection: acquire and HOLD the per-employee advisory lock so both sends must queue.
        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);

        // Fire both sends (separate clients). Each opens its tx and blocks at the lock's first statement.
        var clientA = EmployeeClient(emp);
        var clientB = EmployeeClient(emp);
        var sendA = Task.Run(() => PostSendAsync(clientA, emp));
        var sendB = Task.Run(() => PostSendAsync(clientB, emp));

        // Prove REAL overlap: block until BOTH sends are queued as waiters on OUR key.
        await WaitForWaitersAsync(classId, objId, expected: 2);
        Assert.Equal(1, await CountAdvisoryLocksAsync(classId, objId, granted: true)); // the blocker holds it

        // Release: one send acquires, creates+transitions+commits; the other re-reads the committed row.
        await blockerTx.CommitAsync();

        var responses = await Task.WhenAll(sendA, sendB);
        var codes = responses.Select(r => (int)r.StatusCode).OrderBy(c => c).ToArray();
        var raw = string.Join(" | ", await Task.WhenAll(responses.Select(BodyAsync)));

        // Exactly one 200 and one 409.
        Assert.True(codes is [200, 409], $"expected exactly one 200 and one 409, got [{string.Join(",", codes)}]: {raw}");

        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal("EMPLOYEE_APPROVED",
            JsonDocument.Parse(await BodyAsync(winner)).RootElement.GetProperty("status").GetString());

        // The loser's 409 must NAME the sent status — proving it re-read the winner's committed row
        // inside the lock (the source-state gate), not a generic/duplicate-key 409.
        //
        // S127 Step-7a F5 — this body assertion together with the [200,409] codes assertion above is the
        // ROUTE-LEVEL pin on production's READ COMMITTED isolation. Under a regression to REPEATABLE READ
        // the loser cannot see the winner's committed row, so it takes the create arm and either 500s on a
        // 40001 serialization failure (the codes assertion fails: [200,500]) or returns the create-arm
        // "concurrently" 409 (this body assertion fails: no EMPLOYEE_APPROVED). Verified RED-under-RR at
        // Step-7a — so unlike the AC7b_* mechanism demos, THIS test fails if line 1718 is flipped.
        var loserBody = await BodyAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Contains("EMPLOYEE_APPROVED", loserBody);

        // Exactly one row, one SUBMITTED audit, one outbox event, one audit_projection row — no dupes.
        var periodId = await FindPeriodIdAsync(emp, MarchStart, MarchEnd);
        Assert.NotNull(periodId);
        Assert.Equal(1L, await CountApprovalRowsAsync(emp));
        Assert.Equal(1L, await CountApprovalAuditAsync(periodId!.Value, "SUBMITTED"));
        Assert.Equal(1L, await CountOutboxAsync(SendStreamId(emp), "PeriodEmployeeApproved"));
        Assert.Equal(1L, await CountAuditProjectionByPeriodAsync(periodId.Value));
    }

    // ── AC-7(b) — the isolation MECHANISM, demonstrated directly on the production primitives ──────
    //
    // S127 Step-7a F5 — these two tests are NOT the production-isolation pin (AC7a is — it drives the
    // real /send at its production isolation and goes RED if that is flipped to REPEATABLE READ, verified
    // at Step-7a). They are the MECHANISM demonstration: they compose the command's REAL production
    // primitives (EmployeeConsumptionLock.AcquireAsync, TryCreateIfAbsentAsync,
    // GetByEmployeeAndPeriodAsync(conn,tx)) in the command's exact order and vary ONLY the loser tx's
    // isolation level — an argument they PASS, never the level production actually uses — to show
    // DIRECTLY why READ COMMITTED is required:
    //   • READ COMMITTED  → the post-lock re-read SEES the winner's committed row (production's choice).
    //   • REPEATABLE READ → the snapshot is frozen before the lock is granted, so the SAME re-read
    //                       MISSES the row — the collide-on-create defect PAT-015 describes.
    // Because they pass their own isolation they stay green regardless of production's line 1718 — the
    // honest limit of a primitive-composition test, and exactly why the production pin lives in AC7a.
    [Fact]
    public async Task AC7b_ReadCommitted_LoserPostLockReadSeesWinnerRow()
    {
        var read = await RunLoserPostLockReadAsync(IsolationLevel.ReadCommitted);
        Assert.NotNull(read); // under READ COMMITTED the post-lock re-read observes the winner's INSERT
    }

    [Fact]
    public async Task AC7b_RepeatableRead_LoserMissesWinnerRow_TheDefectReadCommittedAvoids()
    {
        var read = await RunLoserPostLockReadAsync(IsolationLevel.RepeatableRead);
        Assert.Null(read); // under REPEATABLE READ the frozen snapshot misses it — the defect RC avoids
    }

    /// <summary>
    /// Runs the command's critical-section protocol at the loser's isolation level: a winner holds the
    /// lock and INSERTs (uncommitted); the loser begins its tx at <paramref name="loserIsolation"/> and
    /// blocks on the SAME advisory lock; once the loser is provably waiting, the winner COMMITs (row +
    /// lock release); the loser then acquires and issues its FIRST post-lock existence read. Returns
    /// that read's result.
    ///
    /// <para>S127 Step-7a F5 — a MECHANISM demonstration on the production primitives at the CALLER's
    /// chosen isolation; it does NOT exercise production's pinned isolation (that pin is AC7a).</para>
    /// </summary>
    private async Task<ApprovalPeriod?> RunLoserPostLockReadAsync(IsolationLevel loserIsolation)
    {
        var emp = UniqueEmp("7b");
        await SeedEmployeeAsync(emp);
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        // Winner: hold the lock, INSERT the row via the REAL primitive, do NOT commit yet.
        await using var winnerConn = Fx.Db.Create();
        await winnerConn.OpenAsync();
        await using var winnerTx = await winnerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(winnerConn, winnerTx, emp);
        var created = await Fx.ApprovalRepo.TryCreateIfAbsentAsync(winnerConn, winnerTx, new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = emp,
            OrgId = Org,
            PeriodStart = MarchStart,
            PeriodEnd = MarchEnd,
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24",
        });
        Assert.NotNull(created); // the winner genuinely inserted (uncommitted)

        // Loser: begin its tx (at the isolation under test), then block on the SAME lock on a task —
        // its snapshot is established when the advisory-lock SELECT starts, BEFORE the winner commits.
        ApprovalPeriod? postLockRead = null;
        await using var loserConn = Fx.Db.Create();
        await loserConn.OpenAsync();
        await using var loserTx = await loserConn.BeginTransactionAsync(loserIsolation);
        var loserTask = Task.Run(async () =>
        {
            await EmployeeConsumptionLock.AcquireAsync(loserConn, loserTx, emp); // BLOCKS until winner commits
            postLockRead = await Fx.ApprovalRepo.GetByEmployeeAndPeriodAsync(
                loserConn, loserTx, emp, MarchStart, MarchEnd); // the FIRST post-lock read
        });

        await WaitForWaitersAsync(classId, objId, expected: 1); // loser is genuinely blocked on the lock
        await winnerTx.CommitAsync();                            // commit the winner's row + release the lock
        await loserTask;                                         // loser acquires + does its post-lock read

        await loserTx.RollbackAsync(); // read-only; leave no trace
        return postLockRead;
    }

    // ── AC-7(d) — send vs Skema save ──────────────────────────────────────────────────────────────
    //
    // The invariant is "no save commits into a month that is already sent" — NOT "never both commit".
    // Construction: the blocker forces BOTH the real /send and the real Skema save to queue, with the
    // send enqueued FIRST (Postgres grants an exclusive advisory lock to waiters in FIFO order), so the
    // send commits EMPLOYEE_APPROVED WHILE the save is still blocked on the lock. The save then acquires,
    // re-reads the status inside the lock, and must refuse.
    //
    // FAILS IF: the save commits its work-time row into the now-sent month (its in-lock re-read missed
    // the send), i.e. a work_time_projection row exists for the saved day.
    [Fact]
    public async Task AC7d_SendVsSkemaSave_SaveDoesNotCommitIntoAlreadySentMonth()
    {
        var emp = UniqueEmp("7d");
        await SeedEmployeeAsync(emp);
        await CoverMarchWithAbsencesAsync(emp);
        var saveDay = new DateOnly(2026, 3, 10); // a March weekday
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);

        // Send enqueues FIRST → it is at the head of the lock's wait queue.
        var sendClient = EmployeeClient(emp);
        var send = Task.Run(() => PostSendAsync(sendClient, emp));
        await WaitForWaitersAsync(classId, objId, expected: 1);

        // Save enqueues SECOND → behind the send.
        var saveClient = EmployeeClient(emp);
        var save = Task.Run(() => PostSkemaWorkTimeSaveAsync(saveClient, emp, saveDay, "09:00", "16:24"));
        await WaitForWaitersAsync(classId, objId, expected: 2);

        await blockerTx.CommitAsync(); // send acquires first (FIFO), commits EMPLOYEE_APPROVED, then the save runs

        var sendRsp = await send;
        var saveRsp = await save;

        // Send won.
        Assert.Equal(HttpStatusCode.OK, sendRsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED",
            JsonDocument.Parse(await BodyAsync(sendRsp)).RootElement.GetProperty("status").GetString());

        // Save refused, naming the sent status (its in-lock re-read saw EMPLOYEE_APPROVED).
        Assert.Equal(HttpStatusCode.Conflict, saveRsp.StatusCode);
        Assert.Contains("EMPLOYEE_APPROVED", await BodyAsync(saveRsp));

        // THE invariant: no save committed into the sent month.
        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, saveDay));

        var periodId = await FindPeriodIdAsync(emp, MarchStart, MarchEnd);
        Assert.NotNull(periodId);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId!.Value))!.Value.Status);
    }

    // ── AC-7(e) — send vs POST /api/time-entries ──────────────────────────────────────────────────
    //
    // A holder simulates the send's critical section (it holds the SAME advisory key the send holds).
    // FAILS IF: the time-entry request does NOT enrol in the lock — then it would not block, so no
    // waiter would ever appear (WaitForWaitersAsync would time out) AND a time_entries_projection row
    // would be observable WHILE the lock is held (the mid-window assertion below would see 1, not 0).
    [Fact]
    public async Task AC7e_SendVsTimeEntry_TimeEntryWaitsOnLock_NoRowCommitsInsideTheWindow()
    {
        var emp = UniqueEmp("7e");
        await SeedEmployeeAsync(emp);
        var entryDay = new DateOnly(2026, 3, 12);
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        // Hold the send's advisory lock (the "send window").
        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);

        // Fire the real time-entry POST — it must WAIT on the advisory lock.
        var teClient = EmployeeClient(emp);
        var timeEntry = Task.Run(() => PostTimeEntryAsync(teClient, emp, entryDay, 7.4m));

        // Prove it WAITS: it appears as a waiter on OUR key.
        await WaitForWaitersAsync(classId, objId, expected: 1);

        // Inside the send's window (lock still held): NO projection row has committed.
        Assert.Equal(0L, await CountTimeEntryRowsAsync(emp, entryDay));

        // Release the window → the time entry proceeds and commits exactly one row.
        await blockerTx.CommitAsync();

        var teRsp = await timeEntry;
        Assert.Equal(HttpStatusCode.Created, teRsp.StatusCode);
        Assert.Equal(1L, await CountTimeEntryRowsAsync(emp, entryDay));
    }
}
