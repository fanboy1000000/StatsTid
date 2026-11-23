using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Abstractions;
using static StatsTid.Tests.Regression.Concurrency.PayInputsLockTestKit;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Performance;

/// <summary>
/// S148 / TASK-14831-T (ledger row 370, ruling 14) - what the system-wide "pay inputs" lock costs, as four recorded
/// figures. Each prints its numbers through <see cref="ITestOutputHelper"/> and asserts only what is deterministic: no time
/// is asserted, because a fixed ceiling on a shared CI machine is a flaky test, not a finding. A figure that reads badly
/// becomes a performance-register or security-register row for the Orchestrator to decide.
///
/// <para>
/// <b>In plain language.</b> One lock couples every writer of a pay input and every payroll recording. (1) A big
/// configuration publish holds it while it lists thousands of months on HR's worklist: how long do recordings wait?
/// (2) An HR save of one person's profile holds it while it waits behind a Skema save of the same person: how long does an
/// unrelated person's recording wait behind that? (3) A burst of profile corrections each opens extra database connections
/// while holding the lock: what happens when the connection pool is small (SEC-066)? (4) A crowd of recordings each hold
/// one connection and their rechecks borrow up to two more: what happens to payroll's own small pool?
/// </para>
///
/// <para>
/// <b>Figure (4) predicts failures, and says so.</b> Each recording holds one pooled connection for its transaction and its
/// recheck borrows up to two more (the profile resolver keeps its connection while it reads the agreement code), three
/// connections in all (TASK-14831, W9; <c>shortcut(SEC-066)</c> in the code), so with pool size minus one recordings in
/// their turn they stall and fail after the connection timeout, writing nothing. The fact does not judge that; it records
/// it and proves the part that must hold: a failed recording left nothing behind.
/// </para>
///
/// <para>
/// <b>The held lock and the waiter query</b> come from <see cref="Concurrency.PayInputsLockTestKit"/> (the same kit the race
/// facts use). Docker-gated: CI-pending, and locally run on a throwaway cluster where noted in the report. Whether the
/// printed lines reach the trx <c>StdOut</c> is settled by the gate's trx check (A11), not by these facts.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayInputsLockTimingTests : DatedConfigDockerFixture, IDisposable
{
    private const int Persons = 300;
    private const int ExportedMonths = 4;
    private const string Prefix = "usr_s148_perf_";
    private const string Org = "STY01";

    /// <summary>How long figure (2) keeps the per-employee lock held once the export is seen waiting: a fixed hold, never an
    /// outcome-deciding sleep (the request orders are decided by the lock queue, not by this).</summary>
    private const int EmployeeLockHoldMs = 2000;

    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private readonly ITestOutputHelper _out;
    private readonly List<WebApplicationFactory<Program>> _hosts = new();
    private readonly List<StatsTidWebApplicationFactory> _factories = new();
    private readonly List<PayrollHost> _payrolls = new();

    public PayInputsLockTimingTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        foreach (var payroll in _payrolls)
            payroll.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var host in _hosts)
            host.Dispose();
        foreach (var factory in _factories)
            factory.Dispose();
    }

    /// <summary>One answered request: its status (-1 when the host threw instead of answering), what it threw, and the
    /// milliseconds from the stopwatch's start to its answer.</summary>
    private sealed record Answer(int Status, string? Threw, long Ms, long? FromSubmissionMs = null)
    {
        public bool Succeeded => Status == 200;
        public override string ToString()
        {
            var text = Threw is null ? $"{Status}@{Ms}ms" : $"threw {Threw}@{Ms}ms";
            return FromSubmissionMs is { } fromSubmission ? $"{text} ({fromSubmission}ms from submission)" : text;
        }
    }

    /// <summary>The request's answer and its time on <paramref name="clock"/>; with <paramref name="fromSubmission"/> the time
    /// on that second stopwatch is recorded too.</summary>
    private static async Task<Answer> AnswerAsync(Task<HttpResponseMessage> request, Stopwatch clock, Stopwatch? fromSubmission = null)
    {
        try
        {
            using var response = await request;
            return new Answer((int)response.StatusCode, null, clock.ElapsedMilliseconds, fromSubmission?.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new Answer(-1, ex.GetType().Name, clock.ElapsedMilliseconds, fromSubmission?.ElapsedMilliseconds);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // (1) a large publish while recordings wait
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI)</b> (the row count; the times are recorded, never asserted). 300 HK people each with January to April 2026
    /// exported (bulk SQL, the <see cref="ConfigPublishTimingTests"/> seeding), and five AC people with March 2026 APPROVED (an
    /// agreement the publish does not touch, so their exports answer 200). The test holds <c>(148, 1)</c>; the HK OK24
    /// publish dated Monday 5 January 2026 is started and seen waiting; the five exports are started and six waiters are
    /// seen; release. The publish's time from the release to its answer and each export's are recorded. Asserted: the
    /// publish answers 200 and lands exactly 1,200 EXPORTED_MONTH rows (300 people times January, February, March and April:
    /// the count <c>ConfigPublishTimingTests.LargeCentralPublish_RowCountAndHold_Recorded</c> asserts for the same seeding),
    /// and every export answers 200.
    /// <para><b>Red conditions.</b> Before TASK-14831 no export takes the lock, so only the publish ever waits and
    /// <c>AssertWaitingAsync</c> (six waiters) fails; before TASK-14823 the publish lands no row (0, not 1,200).</para>
    /// </summary>
    [Fact]
    public async Task LargePublishWhileExportsWait_Recorded()
    {
        var admin = AdminAt(Clock); // boots the Backend host before anything is seeded (PAT-008)
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        await SeedPeopleAsync();
        var acPeople = new List<string>();
        for (var i = 0; i < 5; i++)
            acPeople.Add(await SeedPayableEmployeeAsync(ConnectionString, Org, "AC", (Feb01, Feb28), (Mar01, Mar31)));
        var payroll = StartPayroll(ConnectionString);

        // Warm-up (another key, reaching only November, where nothing is exported): pays the JIT and plan-cache cost.
        var (warmId, warmVersion) = await CreateDraftAsync(admin, ConfigBody("HK", "OK26"));
        await PublishOkAsync(admin, warmId, warmVersion, "2026-11-09");
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));

        var (configId, version) = await CreateDraftAsync(admin, ConfigBody("HK", "OK24"));
        var clock = new Stopwatch();
        Answer publishAnswer;
        Answer[] exportAnswers;
        await using (var held = await HeldLock.AcquirePayInputsAsync(ConnectionString))
        {
            var publish = PublishAsync(admin, configId, version, "2026-01-05");
            await AssertWaitingAsync(ConnectionString, 1, publish, "the large publish");
            var exports = new List<Task<HttpResponseMessage>>();
            foreach (var person in acPeople)
            {
                exports.Add(payroll.ExportAsync(person, Org, Mar01, Mar31, "AC"));
                await AssertWaitingAsync(ConnectionString, 1 + exports.Count, exports[^1], $"the export of {person}");
            }

            clock.Start();
            await held.ReleaseAsync();
            var publishTimed = AnswerAsync(publish, clock);
            var exportsTimed = exports.Select(e => AnswerAsync(e, clock)).ToArray();
            publishAnswer = await publishTimed;
            exportAnswers = await Task.WhenAll(exportsTimed);
        }

        var rows = await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE kind = 'EXPORTED_MONTH'");
        _out.WriteLine(
            $"S148 PAY_INPUTS_LOCK_FIGURE_1 persons={Persons} exportedMonths={ExportedMonths} waitingExports={acPeople.Count} " +
            $"rowsLanded={rows} publishFromReleaseToAnswerMs={publishAnswer.Ms} " +
            $"exportsFromReleaseToAnswerMs=[{string.Join(", ", exportAnswers.Select(a => a.Ms))}]");

        Assert.True(publishAnswer.Succeeded, $"the large publish expected 200, got {publishAnswer}.");
        Assert.All(exportAnswers, a => Assert.True(a.Succeeded, $"an export expected 200, got {a}."));
        Assert.Equal((long)Persons * ExportedMonths, rows);
    }

    // ═════════════════════════════════════════════════════════════════════
    // (2) a profile PUT queued behind the per-employee lock, an export waiting behind it
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI)</b> (the waits are recorded, never asserted). The test holds <c>employee-{E}</c> (the lock a Skema save of E
    /// would hold) on its own connection. E's profile PUT (fraction 1.0 to 0.8, dated today) takes <c>(148, 1)</c> exclusive
    /// first and then queues for <c>employee-{E}</c>: the fact sees it HOLD <c>(148, 1)</c> granted while it waits on the
    /// other key. Another person's export is then started and seen waiting on <c>(148, 1)</c>. After a fixed hold of 2,000 ms
    /// (chosen here; it does not decide an order) the employee lock is released; the PUT proceeds and commits, and the export
    /// follows. Recorded: the export's wait from the moment it was seen waiting to its answer, and from the release to its
    /// answer. Asserted: the PUT answers 200 and the export answers 200.
    /// <para><b>Red conditions.</b> Before TASK-14831 the export takes no pay-inputs lock, so it is never observed waiting
    /// (<c>AssertWaitingAsync</c> fails) and would answer 200 while the PUT still held the key.</para>
    /// </summary>
    [Fact]
    public async Task ProfilePutQueuedBehindEmployeeLock_ExportWaits_Recorded()
    {
        var host = Factory.WithFixedToday(Clock);
        _hosts.Add(host);
        var admin = GlobalAdminClient(host); // boots the host first
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var profileOwner = await SeedPersonAsync(ConnectionString, Org);
        var payer = await SeedPayableEmployeeAsync(ConnectionString, Org, (Feb01, Feb28), (Mar01, Mar31));
        var payroll = StartPayroll(ConnectionString);
        var version = await ScalarAsync<long>("SELECT version FROM users WHERE user_id = @u", ("u", profileOwner));

        Task<HttpResponseMessage> put;
        Task<HttpResponseMessage> export;
        var sinceSeenWaiting = new Stopwatch();
        var sinceRelease = new Stopwatch();
        Answer exportAnswer;
        HttpResponseMessage putResponse;
        await using (var employeeLock = await HeldLock.AcquireEmployeeAsync(ConnectionString, profileOwner))
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{profileOwner}")
            {
                Content = JsonContent.Create(new
                {
                    effectiveFrom = "2026-11-10",
                    partTimeFraction = 0.8m,
                    position = (string?)null,
                    employmentCategory = (string?)null,
                }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            put = admin.SendAsync(request);

            // The PUT holds (148, 1) while it waits for employee-{E}: both are conditions PostgreSQL reports.
            var queued = await PriorMonthExportRaceTests.WaitUntilAsync(
                async () => (await PidsAsync(ConnectionString, HoldersSql)).Count >= 1
                    && (await PidsAsync(ConnectionString, WaitingOnSingleKeySql)).Count >= 1,
                WaitBound, () => put.IsCompleted);
            Assert.True(queued,
                $"the profile PUT was never seen holding (148, 1) while waiting for the employee lock (status: {put.Status}).");
            Assert.False(put.IsCompleted, "the profile PUT finished while the employee lock was still held.");

            export = payroll.ExportAsync(payer, Org, Mar01, Mar31);
            await AssertWaitingAsync(ConnectionString, 1, export, "the export behind the profile PUT");
            sinceSeenWaiting.Start();

            await Task.Delay(EmployeeLockHoldMs); // the fixed hold of this figure: it measures, it never orders

            sinceRelease.Start();
            await employeeLock.ReleaseAsync();
            putResponse = await put;
            exportAnswer = await AnswerAsync(export, sinceSeenWaiting);
        }
        var fromRelease = sinceRelease.ElapsedMilliseconds;

        _out.WriteLine(
            $"S148 PAY_INPUTS_LOCK_FIGURE_2 employeeLockHeldAfterExportSeenWaitingMs={EmployeeLockHoldMs} " +
            $"exportFromSeenWaitingToAnswerMs={exportAnswer.Ms} exportAnswerAfterReleaseMs<={fromRelease} putStatus={(int)putResponse.StatusCode}");

        Assert.True(putResponse.StatusCode == HttpStatusCode.OK, $"the profile PUT: {(int)putResponse.StatusCode} {await putResponse.Content.ReadAsStringAsync()}");
        Assert.True(exportAnswer.Succeeded, $"the export expected 200, got {exportAnswer}.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // (3) SEC-066: concurrent revaluing profile PUTs against a small Backend pool
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>R (CI)</b> (TASK-14821's behaviour; SEC-066). A Backend host whose connection string sets <c>Maximum Pool Size=4</c>
    /// and keeps the driver's default timeouts. Five people each have a future-dated VACATION absence (Wednesday 11 November
    /// 2026) the profile PUT revalues (fraction 1.0 to 0.5); the five PUTs are sent at once, so about as many requests wait
    /// for the lock as the pool has connections, and the one holding it needs more connections for the revaluation.
    /// Recorded: completions, failures by status (or the exception the host threw) and the longest wait. Asserted only that
    /// every request answered, and that <b>each failed PUT wrote nothing</b> (SEC-066: "nothing is written or lost"): its
    /// person still has exactly the one profile row they started with, no profile audit row, no event on their streams and
    /// no ADR-026 audit row.
    /// <para><b>Red conditions (kind R: the behaviour exists).</b> A failure that rolled back only part of its writes (a
    /// profile row, an audit row or an event committed before the failure) fails the "nothing written" assertions for that
    /// person. A request that never answered fails the answered assertion.</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentRevaluingProfilePuts_SmallPool_Recorded()
    {
        var small = new StatsTidWebApplicationFactory(ConnectionString + ";Maximum Pool Size=4");
        _factories.Add(small);
        var host = small.WithFixedToday(Clock);
        _hosts.Add(host);
        var admin = GlobalAdminClient(host); // boots the host first
        var people = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var person = await SeedPersonAsync(ConnectionString, Org);
            people.Add(person);
            await ExecAsync(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id)
                VALUES
                    (gen_random_uuid(), @e, DATE '2026-11-11', 'VACATION', 7.4, 1.0,
                     'AC', 'OK26', NOW(), 'test-seed', 'Employee', -1)
                """, ("e", person));
        }

        var versions = new Dictionary<string, long>();
        foreach (var person in people)
            versions[person] = await ScalarAsync<long>("SELECT version FROM users WHERE user_id = @u", ("u", person));

        var clock = Stopwatch.StartNew();
        var sent = people.Select(person =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{person}")
            {
                Content = JsonContent.Create(new
                {
                    effectiveFrom = "2026-11-10",
                    partTimeFraction = 0.5m,
                    position = (string?)null,
                    employmentCategory = (string?)null,
                }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{versions[person]}\"");
            return (Person: person, Answer: Task.Run(() => AnswerAsync(admin.SendAsync(request), clock)));
        }).ToList();
        var answers = await Task.WhenAll(sent.Select(s => s.Answer));

        var completed = answers.Count(a => a.Succeeded);
        var revalued = await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE event_type = 'EntitlementBalanceRevalued'");
        _out.WriteLine(
            $"S148 PAY_INPUTS_LOCK_FIGURE_3 maxPoolSize=4 puts={answers.Length} completed={completed} " +
            $"failed={answers.Length - completed} byAnswer=[{string.Join(", ", answers.Select(a => a.ToString()))}] " +
            $"longestWaitMs={answers.Max(a => a.Ms)} revaluationEventsLanded={revalued}");

        Assert.Equal(people.Count, answers.Length);
        for (var i = 0; i < people.Count; i++)
        {
            if (answers[i].Succeeded)
                continue;
            var person = people[i];
            Assert.Equal(1L, await CountAsync("SELECT COUNT(*) FROM employee_profiles WHERE employee_id = @e", ("e", person)));
            Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @e", ("e", person)));
            Assert.Equal(0L, await CountAsync(
                "SELECT COUNT(*) FROM outbox_events WHERE stream_id IN (@p, @s)",
                ("p", $"employee-profile-{person}"), ("s", $"employee-{person}")));
            Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @e", ("e", person)));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // (4) payroll's own small pool: a crowd of recordings
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>R (CI)</b> (ruling 14; SEC-066's payroll variant). A payroll host whose connection string sets
    /// <c>Maximum Pool Size=4</c>. Three employees each have February and March 2026 APPROVED; the test
    /// holds <c>(148, 1)</c>, starts their three exports one after the other, and <b>asserts</b> each is seen waiting
    /// (1, then 2, then 3 waiters); release. Recorded: completions, failures by status (or the exception the host threw) and
    /// the longest wait, each timed from submission and from the release. Asserted: the three waiters, that every request
    /// answered, and that <b>each failed export left no record, no event and no audit row</b>.
    /// <para><b>Why three, not four (the three-connection model).</b> A recording holds one pooled connection for its
    /// transaction, and its recheck borrows up to two more, because the profile resolver keeps its connection while it reads
    /// the agreement code (TASK-14831, W9; <c>shortcut(SEC-066)</c> at <c>RecheckPayInputsUnderLockAsync</c>). Its calculation,
    /// before the lock, needs two connections at once. On a pool of 4 three waiters hold three connections and the third's
    /// calculation still found its two; a fourth export cannot calculate at all (it fails before it reaches the lock), so it
    /// cannot be a waiter. Pool size minus one is the largest crowd that can be established.</para>
    /// <para><b>Failures are the predicted outcome.</b> With pool size minus one recordings in their turn, each granted
    /// recording needs a connection that the others hold; it stalls until the connection timeout and its transaction rolls
    /// back. This fact records that and does not judge it.</para>
    /// <para><b>Red conditions (kind R).</b> A failed export that had written part of its record (an export row, a
    /// <c>PayrollExportGenerated</c> event or its audit row) fails the "left nothing behind" assertions for that employee.</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentExports_SmallPayrollPool_Recorded()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var people = new List<string>();
        for (var i = 0; i < 3; i++)
            people.Add(await SeedPayableEmployeeAsync(ConnectionString, Org, (Feb01, Feb28), (Mar01, Mar31)));
        var payroll = StartPayroll(ConnectionString + ";Maximum Pool Size=4");

        var fromRelease = new Stopwatch();
        var fromSubmission = Stopwatch.StartNew();
        var requests = new List<Task<HttpResponseMessage>>();
        Answer[] answers;
        await using (var held = await HeldLock.AcquirePayInputsAsync(ConnectionString))
        {
            foreach (var person in people)
            {
                var export = payroll.ExportAsync(person, Org, Mar01, Mar31);
                requests.Add(export);
                await AssertWaitingAsync(ConnectionString, requests.Count, export, $"export {requests.Count} of the crowd");
            }

            var waiting = (await WaitingPidsAsync(ConnectionString)).Count;
            Assert.Equal(people.Count, waiting);
            fromRelease.Start();
            await held.ReleaseAsync();
            answers = await Task.WhenAll(requests.Select(r => AnswerAsync(r, fromRelease, fromSubmission)));
            _out.WriteLine($"S148 PAY_INPUTS_LOCK_FIGURE_4_WAITING seenWaitingAtRelease={waiting} of {people.Count}");
        }

        var completed = answers.Count(a => a.Succeeded);
        _out.WriteLine(
            $"S148 PAY_INPUTS_LOCK_FIGURE_4 maxPoolSize=4 exports={answers.Length} completed={completed} " +
            $"failed={answers.Length - completed} byAnswer=[{string.Join(", ", answers.Select(a => a.ToString()))}] " +
            $"longestWaitFromReleaseMs={answers.Max(a => a.Ms)} longestWaitFromSubmissionMs={answers.Max(a => a.FromSubmissionMs)}");

        Assert.Equal(people.Count, answers.Length);
        for (var i = 0; i < people.Count; i++)
        {
            if (answers[i].Succeeded)
                continue;
            var person = people[i];
            Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", person)));
            Assert.Equal(0L, await CountAsync(
                "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
                ("s", $"employee-{person}")));
            Assert.Equal(0L, await CountAsync(
                "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated' AND target_resource_id = @e",
                ("e", person)));
        }
    }

    // ═════════════════════════════════════════════════════════════════════

    private PayrollHost StartPayroll(string connectionString)
    {
        var payroll = PayrollHost.Start(connectionString);
        _payrolls.Add(payroll);
        return payroll;
    }

    /// <summary>300 HK people in STY01 (users, an open profile, the organisation row from the literal <c>'0001-01-01'</c>, the
    /// agreement-code row) and January to April 2026 exported for each, by four set-based statements: the
    /// <c>ConfigPublishTimingTests</c> seeding, the shape <c>RegressionSeed</c> writes one person at a time.</summary>
    private async Task SeedPeopleAsync()
    {
        await ExecAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version)
            SELECT @prefix || g, @prefix || g, 'dev-only', 'Perf ' || g, NULL, 'STY01', 'HK', 'OK24'
            FROM generate_series(1, @n) AS g
            """, ("prefix", Prefix), ("n", Persons));
        await ExecAsync(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
            SELECT gen_random_uuid(), u.user_id, 1.000, NULL, '0001-01-01', NULL, 1, u.employment_category
            FROM users u WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
        await ExecAsync(
            """
            INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
            SELECT gen_random_uuid(), u.user_id, 'STY01', '0001-01-01', NULL, 1
            FROM users u WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            SELECT gen_random_uuid(), u.user_id, 'HK', '0001-01-01', NULL, 1
            FROM users u WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            SELECT gen_random_uuid(), NULL, u.user_id, 2026, m, '[]'::jsonb, '[]'::jsonb, 'hash-2026-' || m
            FROM users u CROSS JOIN generate_series(1, 4) AS m
            WHERE starts_with(u.user_id, @prefix)
            """, ("prefix", Prefix));
    }
}
