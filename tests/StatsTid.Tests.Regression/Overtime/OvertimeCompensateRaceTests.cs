using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Payroll;

namespace StatsTid.Tests.Regression.Overtime;

/// <summary>
/// S148 / TASK-14805-T (S0-9, QUAL-239; AC-S0-9a) — the overtime compensate race, forced deterministically against a
/// real Postgres. Docker-gated: <b>CI-pending</b> (first seen at CI-W1); never reported locally green.
///
/// <para><b>Plain language.</b> Two leaders settle the same last 6 hours at the same moment. Both are told "6 hours left"
/// by the route's check, so both go on to the balance update; the database lets only one of them take the hours. The
/// owner's promise: that one answers 200 and the other 409 "nothing was applied", and the audit trail (the outbox) holds
/// ONE "compensation applied" event, not two.</para>
///
/// <para><b>How the race is formed (no sleep decides anything).</b> The compensate handler takes no advisory lock; the
/// only place two compensations wait for each other is the row lock on the employee's <c>overtime_balances</c> row, at the
/// guarded <c>UPDATE overtime_balances</c>. So a third connection runs <c>SELECT … FOR UPDATE</c> on that row and keeps
/// its transaction open (the held-lock pattern of <c>FlexLedgerRaceTests</c>, with a row lock in place of the advisory
/// key; ruling B10). The route's pre-check is a plain read on its own connection and is not blocked by a row lock, so each
/// request passes it (6 remaining) and then waits at the update. The test waits on the CONDITION "a backend is waiting on
/// a lock while running <c>UPDATE overtime_balances</c>" (<c>pg_stat_activity</c>; the bounded
/// <c>PriorMonthExportRaceTests.WaitUntilAsync</c>), starts the second request only once the first is observed waiting,
/// and releases only when both are. A request that is never observed waiting fails the observation step, not the final
/// assertions, so the fact cannot pass vacuously.</para>
///
/// <para><b>Which branch it proves.</b> Two <c>PAYOUT</c> requests: the request queued second reads
/// <c>AdjustPaidOutAsync(...).Success</c> = false. The <c>AFSPADSERING</c> branch's own <c>Success</c> read is not run
/// here (it is the same two-line shape).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OvertimeCompensateRaceTests : IClassFixture<OvertimeSelfDecisionFixture>
{
    private const int Year = 2026;
    private const string Employee = OvertimeSelfDecisionFixture.Self;     // the balance's owner
    private const string Leader = OvertimeSelfDecisionFixture.Other;      // a leader in scope who is not the employee (SEC-056)
    private static readonly string Url = $"/api/overtime/{Employee}/compensate";
    private static readonly string Stream = $"overtime-balance-{Employee}-{Year}";

    private readonly OvertimeSelfDecisionFixture _fx;

    public OvertimeCompensateRaceTests(OvertimeSelfDecisionFixture fixture) => _fx = fixture;

    /// <summary>The held lock: a connection with an open transaction that owns the balance row's lock.</summary>
    private sealed class HeldRowLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly NpgsqlTransaction _tx;
        private bool _released;

        private HeldRowLock(NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        public static async Task<HeldRowLock> AcquireAsync(string connectionString)
        {
            var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var tx = await conn.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT balance_id FROM overtime_balances WHERE employee_id = @e AND period_year = @y FOR UPDATE", conn, tx);
            cmd.Parameters.AddWithValue("e", Employee);
            cmd.Parameters.AddWithValue("y", Year);
            Assert.NotNull(await cmd.ExecuteScalarAsync()); // the row must exist, or nothing is held
            return new HeldRowLock(conn, tx);
        }

        public async Task ReleaseAsync()
        {
            if (_released)
                return;
            _released = true;
            await _tx.CommitAsync(); // changes nothing: only the lock is released
        }

        public async ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                await _tx.RollbackAsync(); // a failed assertion must not hang teardown
            }
            await _tx.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }

    /// <summary>True when at least <paramref name="count"/> backends wait on a lock while running the guarded balance UPDATE.</summary>
    private async Task<bool> UpdateWaitersAtLeastAsync(int count)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND query LIKE '%UPDATE overtime_balances%'
            """, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= count;
    }

    private async Task AssertWaitingAsync(int waiters, Task<(HttpStatusCode Status, string Json)> request, string who)
    {
        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(
            () => UpdateWaitersAtLeastAsync(waiters), TimeSpan.FromSeconds(30), () => request.IsCompleted);
        var early = request.IsCompletedSuccessfully
            ? $" The request already answered {(int)request.Result.Status}: {request.Result.Json}"
            : string.Empty;
        Assert.True(observed,
            $"{who} was never observed waiting at the guarded balance update (waiters expected: {waiters}; request status: " +
            $"{request.Status}).{early}{PriorMonthExportRaceTests.DescribeFault(request)}");
        Assert.False(request.IsCompleted, $"{who} finished while the balance row lock was still held.");
    }

    private static async Task<(HttpStatusCode Status, string Json)> CompensateAsync(HttpClient client)
    {
        using var content = new StringContent(
            $$"""{ "periodYear": {{Year}}, "hours": 6, "compensationType": "PAYOUT" }""", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(Url, content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task SeedBalanceAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO overtime_balances (employee_id, agreement_code, period_year, accumulated, paid_out, afspadsering_used)
            VALUES (@e, 'HK', @y, 6, 0, 0)
            ON CONFLICT (employee_id, period_year) DO UPDATE SET accumulated = 6, paid_out = 0, afspadsering_used = 0
            """, conn);
        cmd.Parameters.AddWithValue("e", Employee);
        cmd.Parameters.AddWithValue("y", Year);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(decimal PaidOut, decimal AfspadseringUsed)> ReadBalanceAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT paid_out, afspadsering_used FROM overtime_balances WHERE employee_id = @e AND period_year = @y", conn);
        cmd.Parameters.AddWithValue("e", Employee);
        cmd.Parameters.AddWithValue("y", Year);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), "balance row must exist");
        return (r.GetDecimal(0), r.GetDecimal(1));
    }

    private async Task<long> CountEventsAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'OvertimeCompensationApplied' AND stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", Stream);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// <b>AC-S0-9a (kind F, Docker; REF R-9).</b> A balance with 6 hours remaining (accumulated 6, paid_out 0,
    /// afspadsering_used 0, seeded by SQL) and two concurrent PAYOUT compensations of 6, each held at the guarded update
    /// past the pre-check: exactly one answers 200 and the other 409 (as a set; which one wins is the lock queue's
    /// business), the balance moves once (paid_out 6, afspadsering_used 0), and exactly one
    /// <c>OvertimeCompensationApplied</c> sits in <c>outbox_events</c> for the stream, none for the loser.
    ///
    /// <para><b>Red conditions (argued; Docker cannot run locally).</b> Against the base <c>a03d61ae</c>, the handler
    /// discards the guarded update's result and enqueues the event regardless (<c>OvertimeEndpoints.cs</c>, the update then
    /// the enqueue), then commits and answers 200. The losing update (6 + 0 + 6 &lt;= 6 is false, so it updates nothing)
    /// therefore still enqueues, and both racers answer 200: the "one 200 and one 409" assertion fails (statuses 200, 200)
    /// and the "exactly one outbox event" assertion fails (2 rows). The balance assertion (paid_out 6) holds on the base
    /// too: it is the control that the database itself refused the second payout. It would pass vacuously only if the race
    /// never formed; then a request is not observed waiting and <c>AssertWaitingAsync</c> fails first.</para>
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task TwoConcurrentCompensationsOfSix_One200_One409_OneOutboxEvent()
    {
        await SeedBalanceAsync();
        using var clientA = _fx.LeaderClient(Leader);
        using var clientB = _fx.LeaderClient(Leader);

        (HttpStatusCode Status, string Json) first, second;
        await using (var held = await HeldRowLock.AcquireAsync(_fx.ConnectionString))
        {
            Task<(HttpStatusCode Status, string Json)>? taskA = null;
            Task<(HttpStatusCode Status, string Json)>? taskB = null;
            try
            {
                taskA = Task.Run(() => CompensateAsync(clientA));
                await AssertWaitingAsync(1, taskA, "compensation A");
                taskB = Task.Run(() => CompensateAsync(clientB));
                await AssertWaitingAsync(2, taskB, "compensation B");

                await held.ReleaseAsync();
                first = await taskA;
                second = await taskB;
            }
            finally
            {
                await held.ReleaseAsync(); // idempotent: a failed assertion above must not strand the lock
                foreach (var task in new[] { taskA, taskB })
                {
                    if (task is null)
                        continue;
                    try { await task; } catch { /* the assertions report the outcome */ }
                }
            }
        }

        var statuses = new[] { (int)first.Status, (int)second.Status }.OrderBy(s => s).ToArray();
        Assert.True(new[] { 200, 409 }.SequenceEqual(statuses),
            $"expected one 200 and one 409, got A={(int)first.Status} {first.Json} / B={(int)second.Status} {second.Json}");

        var loser = first.Status == HttpStatusCode.Conflict ? first : second;
        using var body = JsonDocument.Parse(loser.Json);
        Assert.Equal(new[] { "error" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(
            "Insufficient remaining hours: the overtime balance changed while this request was processed; nothing was applied",
            body.RootElement.GetProperty("error").GetString());

        Assert.Equal((6m, 0m), await ReadBalanceAsync());
        Assert.Equal(1L, await CountEventsAsync());
    }
}
