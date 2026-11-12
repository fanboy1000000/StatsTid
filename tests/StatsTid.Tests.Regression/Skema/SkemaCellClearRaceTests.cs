using System.Net;
using System.Net.Http.Json;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14840-T — cell clears racing other saves for one employee (addendum A1 AC-CL-9, -23, -34, -35). In plain
/// terms: every decision a clear takes (which row it retires, what it gives back, whether the month is still open) is
/// taken inside the per-employee lock, so the order in which racing requests reach the lock decides the outcome, and
/// each outcome must be right: a clear that loses to a send is refused, two clears of one cell give the day back once, a
/// clear queued behind a re-save retires the re-saved row and gives back ITS value, and a re-save queued behind a clear
/// leaves its own row current.
///
/// <para><b>Ordering is forced by observation, never by a sleep</b> (PAT-015): a third connection holds the employee's
/// advisory lock; the racing requests are fired one at a time, and a poll of <c>pg_locks</c> blocks until each is observed
/// QUEUED behind the holder (so each has already passed its pre-transaction checks against the unchanged state); only
/// then does the holder commit, and Postgres grants the lock in queue order. The poll times out, and fails loud, if a
/// request never reaches the lock. Each fact then asserts what the loser SAW after the lock, not merely its status
/// (PAT-015): the in-lock read, which sees the winner's commit only because the save pins ReadCommitted.</para>
///
/// <para>Docker-gated: <b>CI-pending</b>. "Base" is <c>728f4702</c>, where the <c>cleared</c> member is ignored.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SkemaCellClear")]
public sealed class SkemaCellClearRaceTests : SkemaCellClearTestBase
{
    public SkemaCellClearRaceTests(SkemaCellClearFixture fx) : base(fx) { }

    /// <summary>Fires <paramref name="first"/>, waits until it is observed queued on the lock, fires
    /// <paramref name="second"/>, waits until both are queued, asserts nothing was written meanwhile
    /// (<paramref name="quiet"/>), then releases the lock. The grant order is first, then second.</summary>
    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RaceAsync(
        string emp, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second, Func<Task> quiet)
    {
        var (conn, tx) = await HoldEmployeeLockAsync(emp);
        try
        {
            var taskFirst = Task.Run(first);
            await WaitForWaitersAsync(emp, 1);
            var taskSecond = Task.Run(second);
            await WaitForWaitersAsync(emp, 2);
            await quiet();
            await tx.CommitAsync();
            return (await taskFirst, await taskSecond);
        }
        finally
        {
            await tx.DisposeAsync();
            await conn.DisposeAsync();
        }
    }

    private async Task<int> BalanceEventCountAsync(string emp) => (await OutboxAsync(emp, "EntitlementBalanceAdjusted")).Count;

    /// <summary>
    /// AC-CL-9. The approval lock, in the lock. The employee's only expected workday is Wednesday 10 June (window), held
    /// by a sick day. A send and then a clear of that sick day are both queued on the lock; the send commits first
    /// (<c>EMPLOYEE_APPROVED</c>), and the clear, which passed its pre-transaction month check against a still-open
    /// month, is refused 409 by the in-lock re-read: the row stays current and no clear event exists.
    ///
    /// <para>Kind R. Red conditions: none on base; fails if the clear skips the in-lock approval re-read (200, the row
    /// retired in a sent month), which the final assertions catch.</para>
    /// </summary>
    [Fact]
    public async Task ClearQueuedBehindASend_409_RowStaysCurrent()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "sick day Wednesday");
        await SetEmploymentWindowAsync(emp, Wed10, Wed10);
        var client = EmployeeClient(emp);

        var (send, clear) = await RaceAsync(emp,
            () => client.PostAsJsonAsync("/api/approval/send", new { employeeId = emp, year = 2026, month = 6 }),
            () => PostSaveAsync(client, emp, Save(clearAbsences: new[] { (Wed10, "SICK_DAY") })),
            async () => Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT(*) FROM approval_periods WHERE employee_id = @e", ("e", emp))));

        await AssertStatusAsync(send, HttpStatusCode.OK, "the send");
        await AssertStatusAsync(clear, HttpStatusCode.Conflict, "the clear queued behind the send");
        Assert.Null(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Empty(await OutboxAsync(emp, "AbsenceCleared"));
    }

    /// <summary>
    /// AC-CL-23. Two concurrent clears of one <c>VACATION</c> cell (Friday 12 June), the lock held until both are queued:
    /// both answer 200; exactly one <c>AbsenceCleared</c>; <c>used</c> drops 1,0 once (1,0 to 0,0, not to -1,0); exactly
    /// one give-back balance event follows the booking's. The second clear, queued behind the first, finds no current row
    /// in the lock and does nothing.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so no <c>AbsenceCleared</c> exists and the
    /// <c>Assert.Single</c> on it trips.</para>
    /// </summary>
    [Fact]
    public async Task TwoConcurrentClears_SameVacationCell_GiveTheDayBackOnce()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        var balanceEventsBefore = await BalanceEventCountAsync(emp);
        var client = EmployeeClient(emp);

        var (a, b) = await RaceAsync(emp,
            () => PostSaveAsync(client, emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })),
            () => PostSaveAsync(client, emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })),
            async () => Assert.Empty(await OutboxAsync(emp, "AbsenceCleared")));

        await AssertStatusAsync(a, HttpStatusCode.OK, "the first clear");
        await AssertStatusAsync(b, HttpStatusCode.OK, "the second clear");
        var clear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        Assert.Equal(clear.OutboxId, Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Equal(0.0m, await UsedAsync(emp, "VACATION"));
        Assert.Equal(balanceEventsBefore + 1, await BalanceEventCountAsync(emp));
    }

    /// <summary>
    /// AC-CL-34. A clear queued behind a re-save of the same cell. <c>VACATION</c> 7,4 (a 1,0 day) on Friday 12 June; the
    /// lock held; a re-save to 3,7 (a 0,5 day) is queued first, then a clear; both answer 200. The <c>AbsenceCleared</c>
    /// names the RE-SAVE's event as <c>clearedEventId</c> (not the original registration, which the clear's pre-lock
    /// view saw) and carries <c>clearedFeriedage</c> 0,5; <c>used</c> ends at 0 (1,0, minus 0,5 for the re-save, minus
    /// 0,5 for the clear); the re-saved row is the one the clear retired.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so no <c>AbsenceCleared</c> exists and
    /// <c>Assert.Single</c> trips.</para>
    ///
    /// <para><b>Mutation M-18 (CI-M2; argued, not run locally).</b> The clear takes its <c>ClearedFeriedage</c> and
    /// <c>ClearedEventId</c> (and the balance's replaced sum) from the pre-transaction read, while whether a current row
    /// exists is still decided in the lock. The re-save is queued first, so the pre-lock view the clear saw is the original
    /// 1,0 day: the event then carries <c>clearedFeriedage</c> 1,0 and the original's event id, and <c>used</c> ends at -0,5
    /// (1,0 - 0,5 - 1,0). The "must trip" assertions (the row's cell: <c>clearedFeriedage</c> 0,5 and <c>used</c> 0) are
    /// placed BEFORE the event-id and row-link assertions below, so the first failure is <c>clearedFeriedage</c> (1,0 where
    /// 0,5 is expected), not a setup or save step (both saves answer 200 under M-18: a give-back is never refused).</para>
    /// </summary>
    [Fact]
    public async Task ClearQueuedBehindAResave_GivesBackTheInLockRow()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        var client = EmployeeClient(emp);

        var (resave, clear) = await RaceAsync(emp,
            () => PostSaveAsync(client, emp, Save(absences: new[] { (Fri12, "VACATION", 3.7m) })),
            () => PostSaveAsync(client, emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })),
            async () => Assert.Empty(await OutboxAsync(emp, "AbsenceCleared")));

        await AssertStatusAsync(resave, HttpStatusCode.OK, "the re-save");
        await AssertStatusAsync(clear, HttpStatusCode.OK, "the clear queued behind it");
        var registered = await OutboxAsync(emp, "AbsenceRegistered");
        Assert.Equal(2, registered.Count);
        var cleared = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        // The "must trip" assertions of M-18 first: what the clear gave back, then where `used` ended.
        Assert.Equal(0.5m, cleared.Payload.GetProperty("clearedFeriedage").GetDecimal());
        Assert.Equal(0.0m, await UsedAsync(emp, "VACATION"));
        Assert.Equal(registered[1].EventId, cleared.Payload.GetProperty("clearedEventId").GetGuid());
        var rows = await AbsencesAsync(emp);
        Assert.Equal(2, rows.Count);
        Assert.Equal(registered[1].OutboxId, rows.Single(r => r.EventId == registered[0].EventId).SupersededBy);
        Assert.Equal(cleared.OutboxId, rows.Single(r => r.EventId == registered[1].EventId).SupersededBy);
    }

    /// <summary>
    /// Rollback after the clear was written (Fable 5a W-1). An HK employee (employed since before 1 September 2025,
    /// <c>VACATION</c> cap 25,0) has <c>VACATION</c> on Friday 12 June and <c>used</c> 24,0. With the lock held, one request
    /// is queued that clears Friday and posts <c>VACATION</c> on Wednesday 10 and Thursday 11 June (a net of +1,0, so the
    /// pre-transaction quota check, seeing 24,0, passes). While it is queued, <c>used</c> is set to 25,0 by SQL, as a
    /// concurrent booking would; the lock is released. Inside the lock the clear is written first and the quota guard then
    /// refuses the +1,0: 422, and the WHOLE save rolls back: no <c>AbsenceCleared</c> outbox row, no audit row, Friday's row
    /// still current, no row on Wednesday or Thursday, <c>used</c> still 25,0.
    ///
    /// <para>Kind R (the atomicity of a save that fails after its clear ran). On base the clear is ignored and the request's
    /// two posted days are refused earlier or in the lock, so it is green there too; the evidence it can fail is the
    /// mutation <b>"the clear's audit row or event is written outside the save's transaction"</b> (argued, CI-M2 candidate):
    /// the quota breach then rolls back the projection writes but not the separately committed event or audit row, so the
    /// <c>AbsenceCleared</c> and audit-count assertions (the first after the 422) trip. If the event is written on the
    /// transaction but the row mark is not, the "still current" assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearThenInLockQuotaRefusal_RollsBackTheClear()
    {
        var emp = await SeedEmployeeAsync("HK", employmentStart: new DateOnly(2024, 1, 1));
        var client = EmployeeClient(emp, "HK");
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday", client);
        await ExecAsync("UPDATE entitlement_balances SET used = 24.0 WHERE employee_id = @e AND entitlement_type = 'VACATION'", ("e", emp));
        var (conn, tx) = await HoldEmployeeLockAsync(emp);
        HttpResponseMessage rsp;
        try
        {
            var request = Task.Run(() => PostSaveAsync(client, emp, Save(
                absences: new[] { (Wed10, "VACATION", 7.4m), (Thu11, "VACATION", 7.4m) },
                clearAbsences: new[] { (Fri12, "VACATION") })));
            await WaitForWaitersAsync(emp, 1);
            await ExecAsync("UPDATE entitlement_balances SET used = 25.0 WHERE employee_id = @e AND entitlement_type = 'VACATION'", ("e", emp));
            await tx.CommitAsync();
            rsp = await request;
        }
        finally
        {
            await tx.DisposeAsync();
            await conn.DisposeAsync();
        }

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"expected the in-lock quota 422, got {(int)rsp.StatusCode}: {raw}");
        Assert.Empty(await OutboxAsync(emp, "AbsenceCleared"));
        Assert.Equal(0L, await AuditCountAsync(emp, "AbsenceCleared"));
        var rows = await AbsencesAsync(emp);
        Assert.Null(Assert.Single(rows).SupersededBy);
        Assert.Equal(Fri12, rows[0].Date);
        Assert.Equal(25.0m, await UsedAsync(emp, "VACATION"));
    }

    /// <summary>
    /// AC-CL-35. A re-save queued behind a clear of the same cell. The same fixture, the order reversed: the clear is
    /// queued first, then the re-save to 3,7; both answer 200. One current row exists (3,7 h; 0,5 day); <c>used</c> ends
    /// at 0,5 (1,0 given back, 0,5 booked); the <c>AbsenceCleared</c> names the ORIGINAL registration.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so no <c>AbsenceCleared</c> exists and
    /// <c>Assert.Single</c> trips.</para>
    /// </summary>
    [Fact]
    public async Task ResaveQueuedBehindAClear_SaveRowIsCurrent()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        var original = Assert.Single(await AbsencesAsync(emp));
        var client = EmployeeClient(emp);

        var (clear, resave) = await RaceAsync(emp,
            () => PostSaveAsync(client, emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })),
            () => PostSaveAsync(client, emp, Save(absences: new[] { (Fri12, "VACATION", 3.7m) })),
            async () => Assert.Empty(await OutboxAsync(emp, "AbsenceCleared")));

        await AssertStatusAsync(clear, HttpStatusCode.OK, "the clear");
        await AssertStatusAsync(resave, HttpStatusCode.OK, "the re-save queued behind it");
        var cleared = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        Assert.Equal(original.EventId, cleared.Payload.GetProperty("clearedEventId").GetGuid());
        var current = Assert.Single((await AbsencesAsync(emp)).Where(r => r.SupersededBy is null));
        Assert.Equal((3.7m, (decimal?)0.5m), (current.Hours, current.Feriedage));
        Assert.NotEqual(original.EventId, current.EventId);
        Assert.Equal(0.5m, await UsedAsync(emp, "VACATION"));
    }
}
