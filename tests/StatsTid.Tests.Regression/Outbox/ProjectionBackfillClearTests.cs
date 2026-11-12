using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Skema;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S148 / TASK-14840-T — the projection rebuild and cell clears (addendum A1 AC-CL-22, -33, -38). In plain terms: every
/// Backend start re-reads the event log and rebuilds the projections, so a removed cell must never come back from it,
/// even when the removal was saved a moment before the restart and has not reached the event log yet (the publisher moves
/// facts from the outbox to the log after the save commits). The rule the rebuild follows: a clear retires the cell's
/// current row (an entry or absence row is marked; a work-time day is KEPT and marked), and a cell registered again after
/// its clear is current again; the work-time clear only retires a day whose registration is not newer than the clear, so
/// a replayed clear never retires a day registered after it.
///
/// <para>Each fact has its own Postgres, the real Backend host and its own publisher, which the facts stop and restart
/// through the host's service provider (the <c>StatsTidWebApplicationFactory.StopPublisherAsync</c> mechanism, on the
/// derived host that carries the save): the real stop, never a flag. A fact that needs a fact left unpublished asserts
/// <c>published_at IS NULL</c> on that outbox row BEFORE it runs the rebuild. Every expectation is asserted explicitly with
/// the outbox ids read back, never as "before equals after". The rebuild is driven directly
/// (<see cref="ProjectionBackfillService.RunAsync"/>), as the Backend startup hook does.</para>
///
/// <para>Dates: Monday 8 (absence), Tuesday 9 (entry), Wednesday 10 and Thursday 11 June 2026 (work time). Docker-gated:
/// <b>CI-pending</b>. "Base" is <c>728f4702</c>, where the <c>cleared</c> member is ignored.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectionBackfillClearTests : SkemaCellClearTestBase, IAsyncLifetime
{
    private static readonly (DateOnly, string, decimal)[] Absence = { (Mon8, "SICK_DAY", 7.4m) };
    private static readonly (DateOnly, string, decimal)[] Entry = { (Tue9, "PRJ-1", 3.0m) };

    public ProjectionBackfillClearTests() : base(new SkemaCellClearFixture()) { }

    public Task InitializeAsync() => Fx.InitializeAsync();

    public Task DisposeAsync() => Fx.DisposeAsync();

    // ── The publisher and the rebuild ──

    private OutboxPublisher Publisher() =>
        Fx.Host.Services.GetServices<IHostedService>().OfType<OutboxPublisher>().Single();

    private Task StopPublisherAsync() => Publisher().StopAsync(CancellationToken.None);

    private Task StartPublisherAsync() => Publisher().StartAsync(CancellationToken.None);

    /// <summary>Waits until every outbox row of the employee's stream is published, so the rebuild can read it all from
    /// <c>events</c>. Fails loud on a timeout.</summary>
    private async Task DrainAsync(string emp, int timeoutSeconds = 30)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (await ScalarLongAsync(
                   "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND published_at IS NULL", ("s", $"employee-{emp}")) > 0)
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
                throw new TimeoutException($"The outbox of employee-{emp} did not drain within {timeoutSeconds}s.");
            await Task.Delay(100);
        }
    }

    private Task<ProjectionBackfillResult> RebuildAsync() =>
        new ProjectionBackfillService(Fx.Db, NullLogger<ProjectionBackfillService>.Instance).RunAsync();

    private async Task WipeProjectionsAsync(string emp)
    {
        await ExecAsync("DELETE FROM absences_projection WHERE employee_id = @e", ("e", emp));
        await ExecAsync("DELETE FROM time_entries_projection WHERE employee_id = @e", ("e", emp));
        await ExecAsync("DELETE FROM work_time_projection WHERE employee_id = @e", ("e", emp));
    }

    /// <summary>The outbox rows of one type, narrowed to a date by the payload's <c>date</c> member.</summary>
    private async Task<List<OutboxRow>> OnAsync(string emp, string type, DateOnly date) =>
        (await OutboxAsync(emp, type)).Where(r => r.Payload.GetProperty("date").GetString() == D(date)).ToList();

    // ── AC-CL-22 ──

    /// <summary>
    /// AC-CL-22 (1). From EMPTY projections, with the outbox drained first so the rebuild reads every fact from
    /// <c>events</c>: an absence (Monday 8), an entry (Tuesday 9) and a work-time day (Wednesday 10) are each registered,
    /// cleared and registered again; another work-time day (Thursday 11) is registered and cleared. The rebuild gives the
    /// live current set: the first absence and entry rows retired by the clear's outbox id and a second current row each,
    /// with the re-registration's outbox id; Wednesday current (marker null, <c>outbox_id</c> = the re-registration's);
    /// Thursday kept with its marker and <c>outbox_id</c> set to the clear's outbox id.
    ///
    /// <para>Kind F. Red conditions: on base no clear event exists, so the first <c>Assert.Single</c> on a clear event
    /// trips.</para>
    /// </summary>
    [Fact]
    public async Task FromEmpty_ClearsReplayed_EqualsLive()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: Absence, entries: Entry,
            workTime: new[] { (Wed10, "08:00", "15:24"), (Thu11, "08:00", "12:00") }), "register");
        await SaveOkAsync(emp, Save(
            clearAbsences: new[] { (Mon8, "SICK_DAY") }, clearEntries: new[] { (Tue9, "PRJ-1") },
            clearWorkTime: new[] { Wed10, Thu11 }), "clear");
        await SaveOkAsync(emp, Save(absences: Absence, entries: Entry, workTime: new[] { (Wed10, "09:00", "13:00") }), "register again");
        await DrainAsync(emp);
        await WipeProjectionsAsync(emp);

        await RebuildAsync();

        var absClear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        var absRegs = await OutboxAsync(emp, "AbsenceRegistered");
        var entClear = Assert.Single(await OutboxAsync(emp, "TimeEntryCleared"));
        var entRegs = await OutboxAsync(emp, "TimeEntryRegistered");
        var wedRegs = await OnAsync(emp, "WorkTimeRegistered", Wed10);
        var thuClear = Assert.Single(await OnAsync(emp, "WorkTimeCleared", Thu11));
        Assert.Equal(2, absRegs.Count);
        Assert.Equal(2, entRegs.Count);
        Assert.Equal(2, wedRegs.Count);

        var absences = await AbsencesAsync(emp);
        Assert.Equal(
            new (Guid, long?)[] { (absRegs[0].EventId, absClear.OutboxId), (absRegs[1].EventId, null) },
            absences.Select(r => (r.EventId, r.SupersededBy)).ToArray());
        var entries = await EntriesAsync(emp);
        Assert.Equal(
            new (Guid, long?)[] { (entRegs[0].EventId, entClear.OutboxId), (entRegs[1].EventId, null) },
            entries.Select(r => (r.EventId, r.SupersededBy)).ToArray());
        Assert.Equal(
            new (DateOnly, string, long, long?)[]
            {
                (Wed10, "09:00-13:00", wedRegs[1].OutboxId, null),
                (Thu11, "08:00-12:00", thuClear.OutboxId, thuClear.OutboxId),
            },
            (await WorkAsync(emp)).Select(r => (r.Date, r.Intervals, r.OutboxId, r.ClearedBy)).ToArray());
    }

    /// <summary>The fixture of AC-CL-22 (2) and (3): an absence, an entry and two work-time days registered and then
    /// cleared (no re-registration), the outbox drained.</summary>
    private async Task<string> RegisterAndClearAllAsync()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: Absence, entries: Entry,
            workTime: new[] { (Wed10, "08:00", "15:24"), (Thu11, "08:00", "12:00") }), "register");
        await SaveOkAsync(emp, Save(
            clearAbsences: new[] { (Mon8, "SICK_DAY") }, clearEntries: new[] { (Tue9, "PRJ-1") },
            clearWorkTime: new[] { Wed10, Thu11 }), "clear");
        await DrainAsync(emp);
        return emp;
    }

    /// <summary>The state after the clears, asserted explicitly: both hours rows retired by their clear's outbox id, both
    /// work-time days kept and marked, and the month GET serving none of it.</summary>
    private async Task AssertAllClearedAsync(string emp)
    {
        var absClear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        var entClear = Assert.Single(await OutboxAsync(emp, "TimeEntryCleared"));
        Assert.Equal(absClear.OutboxId, Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Equal(entClear.OutboxId, Assert.Single(await EntriesAsync(emp)).SupersededBy);
        var work = await WorkAsync(emp);
        Assert.Equal(new[] { Wed10, Thu11 }, work.Select(r => r.Date).ToArray());
        foreach (var day in work)
        {
            var clear = Assert.Single(await OnAsync(emp, "WorkTimeCleared", day.Date));
            Assert.Equal((clear.OutboxId, clear.OutboxId), (day.ClearedBy ?? -1, day.OutboxId));
        }
        var month = await MonthAsync(emp);
        Assert.Empty(month.GetProperty("absences").EnumerateArray());
        Assert.Empty(month.GetProperty("entries").EnumerateArray());
        Assert.Empty(month.GetProperty("workTime").EnumerateArray());
    }

    /// <summary>
    /// AC-CL-22 (2). A re-run over POPULATED tables (the startup hook) brings back no cleared cell, work time included: the
    /// retired absence and entry rows stay retired, the work-time days stay kept and marked, and the month GET serves none.
    ///
    /// <para>Kind F. Red conditions: on base the clears are ignored, so the first expectation (<c>Assert.Single</c> on the
    /// clear event) trips.</para>
    /// </summary>
    [Fact]
    public async Task RerunOverPopulated_ResurrectsNoClearedCell()
    {
        var emp = await RegisterAndClearAllAsync();

        await RebuildAsync();

        await AssertAllClearedAsync(emp);
    }

    /// <summary>
    /// AC-CL-22 (3). A second re-run changes nothing: after the first re-run the explicit cleared state holds, and the
    /// second re-run leaves every row of the three projections as it was and reports no clear applied
    /// (<c>ClearedTime</c>, <c>ClearedAbsences</c>, <c>ClearedWorkTime</c> all 0).
    ///
    /// <para>Kind F. Red conditions: on base the clears are ignored, so <c>AssertAllClearedAsync</c> trips first.</para>
    /// </summary>
    [Fact]
    public async Task SecondRerun_ChangesNothing()
    {
        var emp = await RegisterAndClearAllAsync();
        await RebuildAsync();
        await AssertAllClearedAsync(emp);
        var (absences, entries, work) = (await AbsencesAsync(emp), await EntriesAsync(emp), await WorkAsync(emp));

        var second = await RebuildAsync();

        Assert.Equal((0, 0, 0), (second.ClearedTime, second.ClearedAbsences, second.ClearedWorkTime));
        Assert.Equal(absences, await AbsencesAsync(emp));
        Assert.Equal(entries, await EntriesAsync(emp));
        Assert.Equal(work, await WorkAsync(emp));
        await AssertAllClearedAsync(emp);
    }

    // ── AC-CL-33 (the publisher stopped through the host) ──

    private async Task AssertUnpublishedAsync(string emp, string type)
    {
        foreach (var row in await OutboxAsync(emp, type))
            Assert.False(row.Published, $"precondition: the {type} outbox row {row.OutboxId} must be unpublished before the rebuild");
    }

    /// <summary>
    /// AC-CL-33 (1). Work time on populated tables, the registration published and the clear not: with the publisher
    /// stopped, a work-time day (Thursday 11 June) is registered and drained, then cleared; the clear's outbox row is
    /// asserted <c>published_at IS NULL</c>; after the rebuild the day stays cleared (kept, <c>cleared_by_outbox_id</c> = the
    /// clear's outbox id) and the month GET serves no work time.
    ///
    /// <para>Kind F. Red conditions: on base no clear event exists, so <c>Assert.Single</c> on it trips.</para>
    ///
    /// <para><b>Mutation M-20 (CI-M2; argued, not run locally).</b> The work-time clear sets <c>cleared_by_outbox_id</c> but
    /// leaves the row's <c>outbox_id</c> at the registration's. Live the day is cleared and the saves answer 200 (the setup
    /// steps and the <c>published_at IS NULL</c> precondition pass). The rebuild then replays the published registration,
    /// whose id is EQUAL to the row's, so the <c>outbox_id &lt;= EXCLUDED.outbox_id</c> guard passes and the upsert sets the
    /// marker back to NULL: the day is current again. The first assertion to trip is the
    /// <c>Assert.Equal((clear id, clear id), (marker, outbox_id))</c> ("the day stays cleared after the rebuild"), which is
    /// the row's "must trip" cell. Also red under M-20 (the census names them): <c>ClearWorkTimeDay_RowKeptAndMarked_...</c>
    /// (<c>kept.OutboxId</c> assertion, after its marker assertion passes), <c>FromEmpty_ClearsReplayed_EqualsLive</c>,
    /// <c>RerunOverPopulated_...</c>, <c>SecondRerun_...</c> and <c>DrainedThenRerun_...</c>; green under M-20:
    /// <c>UnpublishedReRegistration_StaysCurrent</c> and <c>PublishedClear_UnpublishedReRegistration_StaysCurrent</c> (the
    /// re-registration's id is larger than the registration's, so the guard blocks the replay either way).</para>
    /// </summary>
    [Fact]
    public async Task UnpublishedClear_SurvivesTheRebuild()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "08:00", "12:00") }), "register");
        await DrainAsync(emp);
        await StopPublisherAsync();
        await SaveOkAsync(emp, Save(clearWorkTime: new[] { Thu11 }), "clear");
        var clear = Assert.Single(await OutboxAsync(emp, "WorkTimeCleared"));
        Assert.False(clear.Published, "precondition: the clear must be unpublished before the rebuild");

        await RebuildAsync();

        var day = Assert.Single(await WorkAsync(emp));
        Assert.Equal((clear.OutboxId, clear.OutboxId), (day.ClearedBy ?? -1, day.OutboxId));
        Assert.Empty((await MonthAsync(emp)).GetProperty("workTime").EnumerateArray());
    }

    /// <summary>
    /// AC-CL-33 (2). Register, clear, register again, the last two unpublished: after the rebuild the re-registration is
    /// current (its intervals 09:00-13:00, its <c>outbox_id</c> the re-registration's, served by the month GET). Both unpublished
    /// rows are asserted <c>published_at IS NULL</c> first.
    ///
    /// <para>Kind R (a kept behaviour: the replay of the registration must not undo the later facts). It reads only columns that exist on base (row count, <c>outbox_id</c>, the month GET), so it is green on base, where the second registration simply replaces the first; fails if the rebuild resurrects the cleared state
    /// over the re-registration (the day not served, or the registration's outbox id replaced).</para>
    /// </summary>
    [Fact]
    public async Task UnpublishedReRegistration_StaysCurrent()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "08:00", "12:00") }), "register");
        await DrainAsync(emp);
        await StopPublisherAsync();
        await SaveOkAsync(emp, Save(clearWorkTime: new[] { Thu11 }), "clear");
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "09:00", "13:00") }), "register again");
        await AssertUnpublishedAsync(emp, "WorkTimeCleared");
        var again = (await OutboxAsync(emp, "WorkTimeRegistered"))[^1];
        Assert.False(again.Published, "precondition: the re-registration must be unpublished before the rebuild");

        await RebuildAsync();

        // Base-compatible reads (no cleared_by_outbox_id): the row count, its outbox_id, and the served intervals.
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT(*) FROM work_time_projection WHERE employee_id = @e", ("e", emp)));
        Assert.Equal(again.OutboxId, await ScalarLongAsync("SELECT outbox_id FROM work_time_projection WHERE employee_id = @e", ("e", emp)));
        var served = Assert.Single((await MonthAsync(emp)).GetProperty("workTime").EnumerateArray());
        Assert.Equal(D(Thu11), served.GetProperty("date").GetString());
        var interval = Assert.Single(served.GetProperty("intervals").EnumerateArray());
        Assert.Equal(("09:00", "13:00"), (interval.GetProperty("start").GetString(), interval.GetProperty("end").GetString()));
    }

    /// <summary>
    /// AC-CL-33 (3) (Theory: an absence, an entry). On populated tables with the publisher stopped, each clear unpublished
    /// (asserted): after the rebuild the row stays retired (<c>superseded_by_outbox_id</c> = the clear's outbox id) and the
    /// month GET serves none.
    ///
    /// <para>Kind F. Red conditions: on base no clear event exists, so <c>Assert.Single</c> on it trips.</para>
    /// </summary>
    [Theory]
    [InlineData("absence")]
    [InlineData("entry")]
    public async Task UnpublishedAbsenceAndEntryClears_StayRetired(string kind)
    {
        var emp = await SeedEmployeeAsync();
        var isAbsence = kind == "absence";
        await SaveOkAsync(emp, isAbsence ? Save(absences: Absence) : Save(entries: Entry), "register");
        await DrainAsync(emp);
        await StopPublisherAsync();
        await SaveOkAsync(emp, isAbsence
            ? Save(clearAbsences: new[] { (Mon8, "SICK_DAY") })
            : Save(clearEntries: new[] { (Tue9, "PRJ-1") }), "clear");
        var clear = Assert.Single(await OutboxAsync(emp, isAbsence ? "AbsenceCleared" : "TimeEntryCleared"));
        Assert.False(clear.Published, "precondition: the clear must be unpublished before the rebuild");

        await RebuildAsync();

        var retired = isAbsence
            ? Assert.Single(await AbsencesAsync(emp)).SupersededBy
            : Assert.Single(await EntriesAsync(emp)).SupersededBy;
        Assert.Equal(clear.OutboxId, retired);
        var month = await MonthAsync(emp);
        Assert.Empty(month.GetProperty(isAbsence ? "absences" : "entries").EnumerateArray());
    }

    /// <summary>
    /// AC-CL-33 (4). The outbox then drained (every fact published) and the rebuild run again. Setup: an absence (Monday 8),
    /// an entry (Tuesday 9) and two work-time days (Wednesday 10, Thursday 11) registered and drained; the publisher
    /// stopped; all four cleared and Wednesday registered again (09:00-13:00), none of it published (asserted); a first
    /// rebuild; the publisher restarted and drained; a second rebuild. Each expectation is asserted explicitly: Thursday's
    /// day kept with <c>cleared_by_outbox_id</c> equal to its clear's outbox id and shown by no reader; Wednesday current,
    /// its marker null and its <c>outbox_id</c> the re-registration's; the absence and entry rows each with
    /// <c>superseded_by_outbox_id</c> equal to its clear's outbox id, and the month GET serving neither.
    ///
    /// <para>Kind F, by its cleared expectations. Red conditions: on base the clears are ignored, so the first
    /// <c>Assert.Single</c> on a clear event trips.</para>
    /// </summary>
    [Fact]
    public async Task DrainedThenRerun_ClearedStayCleared_ReRegisteredStaysCurrent()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: Absence, entries: Entry,
            workTime: new[] { (Wed10, "08:00", "15:24"), (Thu11, "08:00", "12:00") }), "register");
        await DrainAsync(emp);
        await StopPublisherAsync();
        await SaveOkAsync(emp, Save(
            clearAbsences: new[] { (Mon8, "SICK_DAY") }, clearEntries: new[] { (Tue9, "PRJ-1") },
            clearWorkTime: new[] { Wed10, Thu11 }), "clear");
        await SaveOkAsync(emp, Save(workTime: new[] { (Wed10, "09:00", "13:00") }), "register Wednesday again");
        foreach (var type in new[] { "AbsenceCleared", "TimeEntryCleared", "WorkTimeCleared" })
            await AssertUnpublishedAsync(emp, type);
        var again = (await OnAsync(emp, "WorkTimeRegistered", Wed10))[^1];
        Assert.False(again.Published, "precondition: the re-registration must be unpublished before the rebuild");

        await RebuildAsync();
        await StartPublisherAsync();
        await DrainAsync(emp);
        await RebuildAsync();

        var absClear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        var entClear = Assert.Single(await OutboxAsync(emp, "TimeEntryCleared"));
        var thuClear = Assert.Single(await OnAsync(emp, "WorkTimeCleared", Thu11));
        Assert.Equal(absClear.OutboxId, Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Equal(entClear.OutboxId, Assert.Single(await EntriesAsync(emp)).SupersededBy);
        Assert.Equal(
            new (DateOnly, string, long, long?)[]
            {
                (Wed10, "09:00-13:00", again.OutboxId, null),
                (Thu11, "08:00-12:00", thuClear.OutboxId, thuClear.OutboxId),
            },
            (await WorkAsync(emp)).Select(r => (r.Date, r.Intervals, r.OutboxId, r.ClearedBy)).ToArray());
        var month = await MonthAsync(emp);
        Assert.Empty(month.GetProperty("absences").EnumerateArray());
        Assert.Empty(month.GetProperty("entries").EnumerateArray());
        var served = Assert.Single(month.GetProperty("workTime").EnumerateArray());
        Assert.Equal(D(Wed10), served.GetProperty("date").GetString());
    }

    // ── AC-CL-38 ──

    /// <summary>
    /// AC-CL-38 (cycle-2 ruling R6). A replayed clear does not retire a newer registration. Work time 08:00-15:24 on
    /// Thursday 11 June is registered and cleared, both published; the publisher is then stopped and the day registered
    /// again (09:00-13:00), its outbox row asserted <c>published_at IS NULL</c>. The rebuild replays the published
    /// registration and the published clear over a row whose registration is newer than the clear: the day is current
    /// (<c>cleared_by_outbox_id</c> null) with <c>outbox_id</c> equal to the re-registration's, and the month GET serves it.
    ///
    /// <para>Kind R in intent (a kept behaviour). On base it is red even earlier than the rebuild: the LINQ <c>.Single()</c> on the nonexistent <c>WorkTimeCleared</c> outbox row throws an <c>InvalidOperationException</c>, and the marker column read would error; it is "red with the column present and the clear ignored". Its red evidence for the guard is the
    /// mutation <b>M-21</b> (CI-M2; argued, not run locally): the replayed work-time clear's <c>outbox_id &lt;= @clear</c>
    /// guard removed from <c>ClearDaySql</c>. The saves answer 200 and the preconditions hold (the clear is published, the
    /// re-registration is not). The rebuild replays the published clear over the row of the NEWER, unpublished
    /// re-registration: without the guard it marks the row cleared and rewinds its <c>outbox_id</c> to the clear's, so the
    /// first assertion to trip is the <c>Assert.Equal</c> on <c>(intervals, outbox_id, marker)</c>, the row's "must trip"
    /// cell (the served-day assertion would trip next). M-18, M-19 and M-20 leave this fact green (under M-20 the guard
    /// still blocks the replayed clear, because the re-registration's id is larger).</para>
    /// </summary>
    [Fact]
    public async Task PublishedClear_UnpublishedReRegistration_StaysCurrent()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "08:00", "15:24") }), "register");
        await SaveOkAsync(emp, Save(clearWorkTime: new[] { Thu11 }), "clear");
        await DrainAsync(emp);
        await StopPublisherAsync();
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "09:00", "13:00") }), "register again");
        var again = (await OutboxAsync(emp, "WorkTimeRegistered"))[^1];
        Assert.False(again.Published, "precondition: the re-registration must be unpublished before the rebuild");
        Assert.True((await OutboxAsync(emp, "WorkTimeCleared")).Single().Published, "precondition: the clear is published");

        await RebuildAsync();

        var day = Assert.Single(await WorkAsync(emp));
        Assert.Equal(("09:00-13:00", again.OutboxId, (long?)null), (day.Intervals, day.OutboxId, day.ClearedBy));
        var served = Assert.Single((await MonthAsync(emp)).GetProperty("workTime").EnumerateArray());
        Assert.Equal(D(Thu11), served.GetProperty("date").GetString());
    }
}
