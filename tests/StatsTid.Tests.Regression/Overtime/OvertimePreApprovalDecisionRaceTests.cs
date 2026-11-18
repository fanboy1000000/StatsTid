using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Security;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Overtime;

/// <summary>
/// S148 / TASK-14845-T fix-up 2 (AC-C13s; QUAL-257, the owner's ruling of 2026-11-18; M-48) - two leaders decide the same overtime
/// pre-approval at the same moment: exactly one decision lands.
///
/// <para>
/// <b>In plain language.</b> A pre-approval for overtime can be approved or rejected once. Before this fix, two leaders could press
/// "approve" and "reject" at the same instant: both requests had checked "still pending" before either wrote, so both answered 200
/// and two decision events were recorded for one request. Now the update itself only changes a row that is still PENDING, so the
/// second decision is told 409 "no longer pending ... nothing was applied" and records nothing.
/// </para>
///
/// <para>
/// <b>How the race is formed (no sleep decides anything).</b> Both decisions lock the employee's <c>users</c> row first (SEC-062,
/// TASK-14845, W17 and W18) and then run the guarded update. A side connection holds that users row <c>FOR UPDATE</c>. Decision A
/// (approve) is sent and the test waits on the CONDITION "one backend waits on a lock in a <c>FROM users ... FOR UPDATE</c>
/// statement"; decision B (reject) is then sent and the test waits for two such waiters (bounded
/// <c>PriorMonthExportRaceTests.WaitUntilAsync</c>). Both have by then passed the route's unlocked PENDING check. Only then is the
/// lock released: the queue lets one decision commit, and the other runs its guarded update against the committed row. A request
/// that is never observed waiting fails the observation step, after releasing the lock and awaiting both requests with a bound,
/// so no request is left in flight.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One container and one Backend host for the class (<see cref="DepartedSelfHostFixture"/>), the clock fixed at
/// <see cref="Clock"/>; a PENDING pre-approval inserted by SQL for a person in STY01; the actor a LocalHR scoped to STY01, not the
/// employee (SEC-056 would answer 403 to the employee). Docker-gated: <b>CI-pending</b>.
/// </para>
///
/// <para>
/// <b>M-48 (run at CI-M4a, alone).</b> The predicate <c>AND status = 'PENDING'</c> in
/// <c>OvertimePreApprovalRepository.BuildUpdateStatusCommand</c> deleted; the bool return and the handlers' check kept. Frozen list:
/// this one fact. With the predicate gone the loser's update changes the row too (one row), the handler sees <c>true</c>, and both
/// answer 200: <c>Assert.True(new[] {200, 409}.SequenceEqual(statuses))</c> fails first (statuses 200, 200).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OvertimePreApprovalDecisionRaceTests : IClassFixture<DepartedSelfHostFixture>, IAsyncLifetime
{
    /// <summary>The 409's sentence, as a literal (not the production constant).</summary>
    private const string NoLongerPending =
        "Pre-approval is no longer pending: another decision was recorded while this request was processed; nothing was applied";

    private readonly DepartedSelfHostFixture _fx;
    private WebApplicationFactory<Program> _host = null!;

    public OvertimePreApprovalDecisionRaceTests(DepartedSelfHostFixture fx) => _fx = fx;

    private string Cs => _fx.ConnectionString;

    public Task InitializeAsync()
    {
        _host = _fx.Factory.WithFixedToday(Clock);
        _ = _host.CreateClient(); // boot before any fact seeds (PAT-008)
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13s, QUAL-257). A PENDING pre-approval and two decisions by the same in-scope leader at once, one approve
    /// and one reject, both held at the employee's users row: the statuses are {200, 409} as a set; the 409's body holds exactly one
    /// member, <c>error</c>, with the sentence above; the row's final status is the winner's decision; <c>outbox_events</c> holds
    /// exactly one decision event (<c>OvertimePreApprovalApproved</c> or <c>OvertimePreApprovalRejected</c>) for the stream
    /// <c>overtime-preapproval-{id}</c>, and exactly one audit-projection row belongs to it.
    ///
    /// <para><b>Red conditions (argued; also run locally against a throwaway PostgreSQL 16, CI-pending).</b> On
    /// <c>0251f918</c> the update is <c>WHERE id = @id</c> and its result is not read: after the queue releases, both requests
    /// update the row and commit their own decision event. Both answer 200: the first failing assertion is
    /// <c>Assert.True(new[] {200, 409}.SequenceEqual(statuses))</c> ("expected one 200 and one 409, got ... 200 ... 200").
    /// Two events and two audit rows would follow.</para>
    ///
    /// <para><b>M-48</b> (the predicate deleted, the bool and the handlers' check kept) trips the same assertion.</para>
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task TwoConcurrentDecisions_OneLands_TheOtherIs409_OneEvent()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var id = Guid.NewGuid();
        await S120ContractAssert.ExecAsync(Cs,
            """
            INSERT INTO overtime_pre_approvals (id, employee_id, period_start, period_end, max_hours, status, reason)
            VALUES (@id, @e, DATE '2026-11-09', DATE '2026-11-15', 10, 'PENDING', 's148 decision race')
            """, ("id", id), ("e", person));
        var stream = $"overtime-preapproval-{id}";

        using var clientA = LocalHrClient(_host, "hr_s148_sty01_only", "STY01");
        using var clientB = LocalHrClient(_host, "hr_s148_sty01_only", "STY01");

        HttpResponseMessage first, second;
        await using (var side = new NpgsqlConnection(Cs))
        {
            await side.OpenAsync();
            var sideTx = await side.BeginTransactionAsync();
            await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE user_id = @u FOR UPDATE", side, sideTx))
            {
                lockCmd.Parameters.AddWithValue("u", person);
                await lockCmd.ExecuteScalarAsync();
            }

            Task<HttpResponseMessage>? taskA = null, taskB = null;
            var released = false;
            try
            {
                taskA = clientA.SendAsync(Decision("approve", id));
                await AssertWaitersAsync(1, taskA, "decision A (approve)");
                taskB = clientB.SendAsync(Decision("reject", id));
                await AssertWaitersAsync(2, taskB, "decision B (reject)");

                await sideTx.CommitAsync(); // changes nothing: only the lock is released
                released = true;
                first = await taskA;
                second = await taskB;
            }
            finally
            {
                if (!released)
                    await sideTx.RollbackAsync(); // a failed observation must not strand the lock or the requests
                foreach (var task in new[] { taskA, taskB })
                {
                    if (task is null)
                        continue;
                    try { await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))); } catch { /* the assertions report the outcome */ }
                }
                await sideTx.DisposeAsync();
            }
        }

        var firstRaw = await first.Content.ReadAsStringAsync();
        var secondRaw = await second.Content.ReadAsStringAsync();
        var statuses = new[] { (int)first.StatusCode, (int)second.StatusCode }.OrderBy(s => s).ToArray();
        Assert.True(new[] { 200, 409 }.SequenceEqual(statuses),
            $"expected one 200 and one 409, got A(approve)={(int)first.StatusCode} {firstRaw} / B(reject)={(int)second.StatusCode} {secondRaw}");

        var (winnerDecision, winnerStatus, loserRaw) = first.StatusCode == HttpStatusCode.OK
            ? ("APPROVED", "OvertimePreApprovalApproved", secondRaw)
            : ("REJECTED", "OvertimePreApprovalRejected", firstRaw);

        using (var body = JsonDocument.Parse(loserRaw))
        {
            Assert.Equal(new[] { "error" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(NoLongerPending, body.RootElement.GetProperty("error").GetString());
        }

        Assert.Equal(winnerDecision, await ScalarStringAsync(Cs, "SELECT status FROM overtime_pre_approvals WHERE id = @id", ("id", id)));
        Assert.Equal(winnerStatus, await ScalarStringAsync(Cs,
            "SELECT event_type FROM outbox_events WHERE stream_id = @s AND event_type IN ('OvertimePreApprovalApproved', 'OvertimePreApprovalRejected')",
            ("s", stream)));
        Assert.Equal(1L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type IN ('OvertimePreApprovalApproved', 'OvertimePreApprovalRejected')",
            ("s", stream)));
        Assert.Equal(1L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM audit_projection WHERE event_id IN (SELECT event_id FROM outbox_events WHERE stream_id = @s)",
            ("s", stream)));
    }

    private static HttpRequestMessage Decision(string verb, Guid id) =>
        new(HttpMethod.Put, $"/api/overtime/pre-approval/{id}/{verb}")
        {
            Content = JsonContent.Create(new { reason = "s148 decision race: " + verb }),
        };

    /// <summary>True when at least <paramref name="count"/> backends wait on a lock in a <c>FROM users ... FOR UPDATE</c> statement.</summary>
    private async Task<bool> UsersRowWaitersAtLeastAsync(int count)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND query ILIKE '%FROM users%FOR UPDATE%'
            """, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= count;
    }

    private async Task AssertWaitersAsync(int waiters, Task<HttpResponseMessage> request, string who)
    {
        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(
            () => UsersRowWaitersAtLeastAsync(waiters), TimeSpan.FromSeconds(30), () => request.IsCompleted);
        var early = request.IsCompletedSuccessfully
            ? $" The request already answered {(int)request.Result.StatusCode}: {await request.Result.Content.ReadAsStringAsync()}"
            : string.Empty;
        Assert.True(observed,
            $"{who} was never observed waiting at the employee's users row (waiters expected: {waiters}; request status: {request.Status}).{early}");
        Assert.False(request.IsCompleted, $"{who} finished while the users row lock was still held.");
    }
}
