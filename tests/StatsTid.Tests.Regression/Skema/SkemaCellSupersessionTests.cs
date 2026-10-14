using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S145 / TASK-14502 — the Skema A0 endpoint pins, part 1 of 2: <b>a Skema save REPLACES the cell</b>
/// (QUAL-192, QUAL-194). The quota half of A0 (QUAL-193) lives in <see cref="SkemaQuotaResaveTests"/>.
///
/// <para><b>Plain language.</b> In Skema an employee types a number into a grid cell and can correct it
/// later. Today a correction does not replace the stored number: the server keeps the old row and adds a
/// new one, and every server-side total (the send check, the team overview, the allocation breakdown, the
/// balance summary, the strand check) adds both. An employee who corrects 7.4 h to 8.0 h is therefore
/// counted as 15.4 h, while both grids, which draw only the latest value, show 8.0. The ruled meaning of a
/// Skema save is "this is now the cell's value" (refinement A0, constraint 6). These pins define that
/// meaning for every surface an employee or HR sees. They speak HTTP and SQL only, so they compile against
/// today's code, and they fail today for the right reason: a server total shows 15.4 where the spec says 8.0.
/// The additive <c>POST /api/time-entries</c> registrations are a DIFFERENT meaning (several rows for one
/// day and task are real, and all count); two guards below keep that untouched.</para>
///
/// <para><b>Expected values come from the spec</b> (SYSTEM_TARGET.md section A; refinement A0 and criteria
/// <c>:557-580</c>; ADR-028 D1/D4), never from what the code returns today. Where a fact "fails today", that
/// IS the finding.</para>
///
/// <para><b>Fixture rules (plan, "Fixture conditions the table relies on").</b>
/// C-S1: every <c>Resave_Entry_*</c> fact (and S7, S11-S13) saves and re-saves ONE entry cell and no absence
/// cell; <c>Resave_Absence_*</c> saves and re-saves ONE absence cell and no entry cell.
/// C-S2: S7 counts the cell's current rows by SQL, not through a GET.
/// C-S3: S11 and S13 use Skema saves only, no <c>POST /api/time-entries</c>.
/// C-S4: S9 asserts the reason, <c>error == "absence_full_day_only"</c>, so a quota 422 cannot stand in.
/// Criterion (i): 8.0 h of work time is recorded for the day first (the send gate and the allocation
/// readers compare it with the allocated hours). Absences sit on a WEEKDAY, nudged by
/// <see cref="NextWeekday"/> (a zero-norm day is rejected by the product, S138/QUAL-153). Every setup save
/// asserts its own 200 before the save under test (R62); every fixture that Skema-saves an
/// entitlement-typed absence runs on a host whose rule-engine client is the entitlement-validation stub
/// (the handler delegates to <see cref="EntitlementValidationRule.Evaluate"/>), because without it the
/// save fails closed. The clock is pinned (PAT-008) at <see cref="F"/>, a Monday on the OK24 side.</para>
///
/// <para><b>Docker-gated, CI-verified, never locally green.</b> Docker does not run on the owner's machine.
/// Each fact's "Red conditions" argue its RED: what happens today and which assertion trips, and which
/// production mutation (its MD-id from the plan's partition table, R59) an evidence run applies to prove
/// the pin can fail on the finished product.</para>
///
/// <para><b>Cut order (R30).</b> The three reader-matrix facts (S11-S13) are the first to go if the sprint
/// has to shed scope; they are grouped under their own heading at the end of the class so removing them
/// leaves the rest untouched.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaCellSupersessionTests : IClassFixture<SkemaCellSupersessionTests.Harness>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>The pinned "today" (PAT-008): Monday 2025-03-03, on the OK24 side of the 2026-04-01
    /// OK24 to OK26 cutover. Only the leader reads (team overview, allocation breakdown) consult "today",
    /// to decide which reporting edges are in force.</summary>
    private static readonly DateOnly F = new(2025, 3, 3);

    /// <summary>The cell's day: Wednesday 2025-03-05, nudged to a weekday and carrying no public holiday.</summary>
    private static readonly DateOnly Day = NextWeekday(new DateOnly(2025, 3, 5));

    private static readonly DateOnly MonthStart = new(Day.Year, Day.Month, 1);
    private static readonly DateOnly MonthEnd = new(Day.Year, Day.Month, DateTime.DaysInMonth(Day.Year, Day.Month));

    private const string Project = "PROJ-S145-A";
    private const string DefaultOrg = "STY01";
    private const string LeaderOrg = "STY02";

    private readonly Harness _fx;

    public SkemaCellSupersessionTests(Harness fx) => _fx = fx;

    // ════════════════════════════════════════════════════════════════════════
    // A0 — a re-save replaces the cell (criterion "a re-save replaces the cell")
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S1.</b> Record 8.0 h of work time, save the Skema cell at 7.4 h, then save the same cell at 8.0 h.
    /// The Skema month GET returns ONE entry for the cell, and it is 8.0.
    ///
    /// <para><b>Red conditions.</b> Today the save appends a second <c>time_entries_projection</c> row and
    /// the month GET lists both, so <c>Assert.Equal(1, entries.Length)</c> trips with 2 (7.4 and 8.0).
    /// Production mutations that turn the finished product red here: <b>MD-01</b> (the save leaves
    /// <c>Source</c> null, so both rows are TIME_POST and both stay current: two entries) and <b>MD-03</b>
    /// (the repository's default read drops its current-row predicate: two entries). Both are listed against
    /// this fact in the partition table.</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_SkemaGetReturnsOneEntry_8()
    {
        var emp = await SeedEmployeeAsync();
        await SaveEntryCellTwiceAsync(emp);

        var rsp = await EmployeeClient(emp).GetAsync($"/api/skema/{emp}/month?year={Day.Year}&month={Day.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var entries = body.GetProperty("entries").EnumerateArray().ToArray();

        Assert.True(entries.Length == 1,
            "A0: a Skema re-save replaces the cell, so the month GET must return ONE entry; got " +
            $"{entries.Length}: [{string.Join(", ", entries.Select(e => e.GetProperty("hours").GetDecimal()))}].");
        Assert.Equal(8.0m, entries[0].GetProperty("hours").GetDecimal());
        Assert.Equal(Project, entries[0].GetProperty("projectCode").GetString());
        Assert.Equal(Day.ToString("yyyy-MM-dd"), entries[0].GetProperty("date").GetString());
    }

    /// <summary>
    /// <b>S2.</b> Same fixture as S1. <c>GET /api/time-entries/{employeeId}</c> returns ONE row, 8.0 h.
    ///
    /// <para><b>Red conditions.</b> Today the endpoint serves every projection row, so
    /// <c>Assert.Equal(1, rows.Length)</c> trips with 2. Listed under <b>MD-01</b> (both rows stay current)
    /// and <b>MD-03</b> (the read's current-row predicate removed; this endpoint shares the repository's
    /// <c>GetByEmployeeAsync</c>, <c>TimeEndpoints.cs:366</c>).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_TimeEntriesGetReturnsOneRow()
    {
        var emp = await SeedEmployeeAsync();
        await SaveEntryCellTwiceAsync(emp);

        var rsp = await EmployeeClient(emp).GetAsync($"/api/time-entries/{emp}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = (await rsp.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();

        Assert.True(rows.Length == 1,
            $"A0: GET /api/time-entries must return ONE row for a re-saved Skema cell; got {rows.Length}.");
        Assert.Equal(8.0m, rows[0].GetProperty("hours").GetDecimal());
    }

    /// <summary>
    /// <b>S3.</b> The send gate is balanced at 8.0: recorded work time 8.0 h equals the allocated 8.0 h of
    /// the (replaced) cell, so the employee's send is accepted.
    ///
    /// <para><b>Fixture note.</b> The send also demands that every EXPECTED workday of the month is covered.
    /// The employment window is set to exactly the cell's day (the SetEmploymentWindowAsync precedent of the
    /// send-command matrix), so the day is the month's only expected workday and the pin isolates the
    /// allocation reconciliation. The window is written after the saves, straight onto the users row.</para>
    ///
    /// <para><b>Red conditions.</b> Today the gate sums both rows: allocated 15.4 against worked 8.0, so the
    /// send answers 422 <c>kind: "allocation"</c> and <c>Assert.True(status == 200)</c> trips (the failure
    /// message echoes the body, whose <c>unbalancedDays</c> carries worked 8.0 and allocated 15.4).
    /// Listed under <b>MD-01</b> and <b>MD-03</b> (the gate reads through the repository default read).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_SendGateBalancedAt8()
    {
        var emp = await SeedEmployeeAsync();
        await SaveEntryCellTwiceAsync(emp);
        await SetEmploymentWindowAsync(emp, Day, Day);

        var rsp = await EmployeeClient(emp).PostAsJsonAsync(
            "/api/approval/send", new { employeeId = emp, year = Day.Year, month = Day.Month });
        var raw = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            "A0: after a re-save the send gate must see worked 8.0 = allocated 8.0 and accept the send; " +
            $"got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal("EMPLOYEE_APPROVED", JsonDocument.Parse(raw).RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// <b>S4.</b> The leader's team overview shows 8.0 h registered for the employee (the row's
    /// <c>normRegistered</c>), not 15.4.
    ///
    /// <para><b>Fixture note.</b> The overview withholds <c>normRegistered</c> until the employee has sent
    /// the month (S124/TASK-12402), so a SUBMITTED period row is seeded by SQL after the two saves. The
    /// leader is the employee's designated PRIMARY approver (the TeamOverviewAggregateTests topology).</para>
    ///
    /// <para><b>Red conditions.</b> Today the overview sums every projection row for the month
    /// (<c>ApprovalEndpoints.cs:883-899</c>): <c>Assert.Equal(8.0m, normRegistered)</c> trips with 15.4.
    /// Listed under <b>MD-01</b> and <b>MD-04</b> (the team-overview SQL loses its current-row predicate; the
    /// SQL is raw, so a repository mutation such as MD-03 does not reach it).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_TeamOverviewShows8()
    {
        var (emp, leader) = await SeedEmployeeUnderLeaderAsync();
        await SaveEntryCellTwiceAsync(emp);
        await InsertSubmittedPeriodAsync(emp, LeaderOrg);

        var rows = await GetTeamOverviewAsync(leader);
        var row = rows.Single(r => r.GetProperty("employeeId").GetString() == emp);

        Assert.Equal(JsonValueKind.Number, row.GetProperty("normRegistered").ValueKind);
        Assert.Equal(8.0m, row.GetProperty("normRegistered").GetDecimal());
    }

    /// <summary>
    /// <b>S5 (guard: passes today).</b> Events stay append-only: after the save and the re-save, the canonical
    /// <c>events</c> table holds exactly TWO <c>TimeEntryRegistered</c> events for the employee. A re-save
    /// emits a new event; it never rewrites or removes the first (constraint 1 of A0, Auditability).
    ///
    /// <para>The in-transaction outbox is counted first (it is written synchronously by the save); the test
    /// then waits for the publisher to drain it and counts the canonical table.</para>
    ///
    /// <para><b>Red conditions.</b> Passes today and must keep passing. The mutation that turns it red is
    /// <b>MD-12</b>: the entry replacement path updates a cell with a current SKEMA row in place and emits
    /// no second <c>TimeEntryRegistered</c>; the outbox count assertion (<c>Assert.Equal(2, ...)</c>) trips
    /// with 1. (The same mutation also turns the parity pin PAR red; PAR belongs to TASK-14501.)</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_EventsTableHoldsTwoEvents()
    {
        var emp = await SeedEmployeeAsync();
        await SaveEntryCellTwiceAsync(emp);

        var outboxCount = await OutboxCountAsync(emp, "TimeEntryRegistered");
        Assert.Equal(2L, outboxCount);

        await WaitForOutboxDrainedAsync(emp, "TimeEntryRegistered", expected: outboxCount, timeoutMs: 30_000);
        Assert.Equal(2L, await EventsCountAsync(emp, "TimeEntryRegistered"));
    }

    /// <summary>
    /// <b>S6.</b> Absence flavour of the same rule: save a VACATION day at 7.4 h, then re-save it at 3.7 h.
    /// The Skema month GET and <c>GET /api/absences/{employeeId}</c> each return ONE row, at the latest
    /// value (3.7 h; recorded feriedage 0.5 on the Skema surface).
    ///
    /// <para><b>Red conditions.</b> Today the second save appends a second <c>absences_projection</c> row
    /// and both reads list both: <c>Assert.Equal(1, absences.Length)</c> trips with 2. Listed under
    /// <b>MD-02</b> (the absence repository's default read drops its current-row predicate). MD-01 does not
    /// apply: this fact has no entry cell (C-S1).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Absence_SkemaGetAndAbsencesGetReturnOneRow()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Payload(Day, absences: new[] { (Day, "VACATION", 7.4m) }), "VACATION 7.4 h");
        await SaveOkAsync(emp, Payload(Day, absences: new[] { (Day, "VACATION", 3.7m) }), "VACATION re-save 3.7 h");

        var skema = await (await EmployeeClient(emp).GetAsync(
            $"/api/skema/{emp}/month?year={Day.Year}&month={Day.Month}")).Content.ReadFromJsonAsync<JsonElement>();
        var skemaAbsences = skema.GetProperty("absences").EnumerateArray().ToArray();
        Assert.True(skemaAbsences.Length == 1,
            $"A0: the Skema month GET must return ONE absence row for a re-saved cell; got {skemaAbsences.Length}.");
        Assert.Equal(3.7m, skemaAbsences[0].GetProperty("hours").GetDecimal());
        Assert.Equal(0.5m, skemaAbsences[0].GetProperty("feriedage").GetDecimal());

        var plain = await (await EmployeeClient(emp).GetAsync($"/api/absences/{emp}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var plainRows = plain.EnumerateArray().ToArray();
        Assert.True(plainRows.Length == 1,
            $"A0: GET /api/absences must return ONE row for a re-saved Skema cell; got {plainRows.Length}.");
        Assert.Equal(3.7m, plainRows[0].GetProperty("hours").GetDecimal());
    }

    /// <summary>
    /// <b>S7.</b> A retried save adds nothing (QUAL-194): the IDENTICAL payload posted twice, as a client
    /// does after a lost response, leaves exactly ONE current row for the cell.
    ///
    /// <para>The count is by SQL on <c>time_entries_projection</c> with
    /// <c>superseded_by_outbox_id IS NULL</c> (C-S2), not through a GET, so it cannot be satisfied by a read
    /// that merely hides the duplicate. The column is TASK-14500's.</para>
    ///
    /// <para><b>Red conditions.</b> Today nothing marks a row superseded, so both rows are current:
    /// <c>Assert.Equal(1, current)</c> trips with 2. Listed under <b>MD-01</b> (both rows are TIME_POST, so
    /// the mark never fires). MD-03 does not reach it, because the assertion is raw SQL.</para>
    /// </summary>
    [Fact]
    public async Task Retry_IdenticalPayload_LeavesOneCurrentRow()
    {
        var emp = await SeedEmployeeAsync();
        var payload = Payload(Day, entries: new[] { (Day, Project, 7.4m) });
        await SaveOkAsync(emp, payload, "first post");
        await SaveOkAsync(emp, payload, "retried identical post");

        var current = await CurrentEntryRowCountAsync(emp, Day, Project);

        Assert.True(current == 1L,
            $"A0 (QUAL-194): a retried identical save must leave ONE current row for the cell; got {current}.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // A0 — zero or negative hours are refused (R24)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S8 (four rows).</b> A Skema save carrying a cell at 0 h or -1 h, as an entry or as an absence,
    /// answers 400 and writes nothing: no new event, no new projection row, no balance movement. The stored
    /// cell stays as it was. (Clearing a cell is out of S145, R24; a 0-hour current row would be counted by
    /// the strand check, returned by the GETs and emit a 0-hour NORMAL_HOURS line.)
    ///
    /// <para>Each row first saves a valid cell at 7.4 h (asserted 200) and snapshots the employee's outbox
    /// events, the projection's rows and, for the absence rows, the VACATION balance. The refused save
    /// targets the same cell.</para>
    ///
    /// <para><b>Red conditions.</b> Today nothing validates the hours of a Skema cell (only
    /// <c>RequestValidator.cs:17</c> does, on the additive route), so each row's save succeeds with 200 and
    /// <c>Assert.Equal(HttpStatusCode.BadRequest, ...)</c> trips. Listed under <b>MD-10</b> (the pre-lock
    /// validation loses its hours-at-or-below-zero 400), which turns all four rows red the same way.</para>
    /// </summary>
    [Theory]
    [InlineData("entry", 0)]
    [InlineData("entry", -1)]
    [InlineData("absence", 0)]
    [InlineData("absence", -1)]
    public async Task Resave_ZeroOrNegativeHours_Returns400_WritesNothing(string kind, int hours)
    {
        var emp = await SeedEmployeeAsync();
        var isEntry = kind == "entry";

        await SaveOkAsync(emp,
            isEntry
                ? Payload(Day, entries: new[] { (Day, Project, 7.4m) })
                : Payload(Day, absences: new[] { (Day, "VACATION", 7.4m) }),
            $"baseline {kind} 7.4 h");

        async Task<long> ProjectionRowsAsync() => isEntry ? await EntryRowCountAsync(emp) : await AbsenceRowCountAsync(emp);

        var eventsBefore = await OutboxCountAsync(emp);
        var rowsBefore = await ProjectionRowsAsync();
        var usedBefore = isEntry ? 0m : await BalanceUsedAsync(emp, "VACATION", VacationYear);

        var rsp = await PostSaveAsync(emp,
            isEntry
                ? Payload(Day, entries: new[] { (Day, Project, (decimal)hours) })
                : Payload(Day, absences: new[] { (Day, "VACATION", (decimal)hours) }));

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Equal(eventsBefore, await OutboxCountAsync(emp));
        Assert.Equal(rowsBefore, await ProjectionRowsAsync());
        if (!isEntry)
            Assert.Equal(usedBefore, await BalanceUsedAsync(emp, "VACATION", VacationYear));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Guards — what must NOT change
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S9 (guard: passes today).</b> A full-day-only absence cell (CARE_DAY) saved at the full day (7.4 h
    /// for a full-timer) and re-saved at a positive PARTIAL number of hours (3.7 h) keeps today's 422, and the
    /// reason is the full-day rule: <c>error == "absence_full_day_only"</c> with <c>requiredHours</c> 7.4.
    /// Zero and negative hours get the 400 first (S8); every other positive number that is not the full day
    /// is refused here. Nothing is written: the stored cell is still the full day and the CARE_DAY balance
    /// is still 1.0.
    ///
    /// <para>The reason is asserted, not just the status (C-S4, N3): a quota 422 carries the different
    /// <c>"Entitlement quota exceeded"</c> and must not be able to stand in for it.</para>
    ///
    /// <para><b>Red conditions.</b> Passes today and must keep passing. <b>MD-14</b> (both full-day-only
    /// checks skipped when the cell already has a current row) lets the 3.7 h re-save through: the status
    /// assertion (<c>Assert.Equal(UnprocessableEntity, ...)</c>) trips with 200. The plan lists the
    /// fixture condition C-S4 against MD-14 and MD-06: the reason assertion is what stops a quota 422
    /// (MD-06's territory) from standing in for the full-day refusal when MD-14 is in the same run.</para>
    /// </summary>
    [Fact]
    public async Task Resave_FullDayOnlyAbsence_PositivePartialHours_Keeps422()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Payload(Day, absences: new[] { (Day, "CARE_DAY", 7.4m) }), "CARE_DAY full day 7.4 h");

        var rsp = await PostSaveAsync(emp, Payload(Day, absences: new[] { (Day, "CARE_DAY", 3.7m) }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", body.GetProperty("error").GetString());
        Assert.Equal("CARE_DAY", body.GetProperty("absenceType").GetString());
        Assert.Equal(7.4m, body.GetProperty("requiredHours").GetDecimal());

        Assert.Equal(1L, await AbsenceRowCountAsync(emp, Day));
        Assert.Equal(1.0m, await BalanceUsedAsync(emp, "CARE_DAY", Day.Year));
    }

    /// <summary>
    /// <b>S10 (guard: passes today).</b> The additive registrations are untouched: two
    /// <c>POST /api/time-entries</c> for the same day and task (3.4 h and 4.0 h) leave TWO rows, and both
    /// count: the list returns two rows and the balance summary's actual hours are 7.4 (3.4 + 4.0). Only a
    /// Skema save replaces a cell; a TIME_POST registration never does.
    ///
    /// <para><b>Red conditions.</b> Passes today and must keep passing. <b>MD-13</b> (both repositories'
    /// insert marks for every source, dropping the SKEMA-only restriction) makes the second registration
    /// supersede the first: the list shrinks to one row and <c>Assert.Equal(2, rows.Length)</c> trips.</para>
    /// </summary>
    [Fact]
    public async Task TimePost_TwoRegistrationsSameDayAndTask_BothCounted()
    {
        var emp = await SeedEmployeeAsync();
        var client = EmployeeClient(emp);

        foreach (var hours in new[] { 3.4m, 4.0m })
        {
            var rsp = await client.PostAsJsonAsync("/api/time-entries", new
            {
                employeeId = emp,
                date = Day.ToString("yyyy-MM-dd"),
                hours,
                taskId = Project,
                activityType = "NORMAL",
                agreementCode = "AC",
            });
            Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        }

        var list = (await (await client.GetAsync($"/api/time-entries/{emp}"))
            .Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(2, list.Length);
        Assert.Equal(new[] { 3.4m, 4.0m }, list.Select(r => r.GetProperty("hours").GetDecimal()).OrderBy(h => h).ToArray());

        var summary = await (await client.GetAsync(
            $"/api/balance/{emp}/summary?year={Day.Year}&month={Day.Month}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7.4m, summary.GetProperty("normHoursActual").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R30 — the reader matrix: every server reader counts a re-saved cell once
    // (cut first if the sprint sheds scope; see the class remarks)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S11 (R30).</b> The allocation breakdown counts the re-saved cell once: for the employee's month it
    /// reports allocated 8.0 h (one project bar of 8.0) against worked 8.0 h, and no imbalance.
    ///
    /// <para><b>Fixture note.</b> The breakdown is a leader read, gated on a sent month (S128/TASK-12804), so
    /// a SUBMITTED period is seeded after the Skema-only saves (C-S3), and the actor is the designated
    /// approver.</para>
    ///
    /// <para><b>Red conditions.</b> Today the breakdown's raw SQL sums both rows
    /// (<c>ApprovalEndpoints.cs:1291-1344</c>): <c>Assert.Equal(8.0m, allocated)</c> trips with 15.4 (and the
    /// imbalance flag would be true). Listed under <b>MD-01</b> and <b>MD-17</b> (the breakdown SQL loses its
    /// current-row predicate; raw SQL, so MD-03 does not reach it).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_AllocationBreakdownCountsOnce()
    {
        var (emp, leader) = await SeedEmployeeUnderLeaderAsync();
        await SaveEntryCellTwiceAsync(emp);
        await InsertSubmittedPeriodAsync(emp, LeaderOrg);

        var rsp = await LeaderClient(leader).GetAsync(
            $"/api/approval/{emp}/allocation-breakdown?year={Day.Year}&month={Day.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var b = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(8.0m, b.GetProperty("allocated").GetDecimal());
        var bars = b.GetProperty("allocations").EnumerateArray().ToArray();
        Assert.Single(bars);
        Assert.Equal(Project, bars[0].GetProperty("taskId").GetString());
        Assert.Equal(8.0m, bars[0].GetProperty("hours").GetDecimal());
        Assert.Equal(8.0m, b.GetProperty("worked").GetDecimal());
        Assert.False(b.GetProperty("hasAllocationImbalance").GetBoolean());
    }

    /// <summary>
    /// <b>S12 (R30).</b> The employee's balance summary counts the re-saved cell once: the month's
    /// <c>normHoursActual</c> is 8.0, not 15.4. (The same read's distinct vacation-date count,
    /// <c>BalanceEndpoints.cs:185-189</c>, is not doubled today and is not part of this pin.)
    ///
    /// <para><b>Red conditions.</b> Today the summary sums every row (<c>BalanceEndpoints.cs:177-179</c>):
    /// <c>Assert.Equal(8.0m, normHoursActual)</c> trips with 15.4. Listed under <b>MD-01</b> and
    /// <b>MD-03</b> (the summary reads through the repository default read).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Entry_BalanceSummaryCountsOnce()
    {
        var emp = await SeedEmployeeAsync();
        await SaveEntryCellTwiceAsync(emp);

        var rsp = await EmployeeClient(emp).GetAsync(
            $"/api/balance/{emp}/summary?year={Day.Year}&month={Day.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var summary = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(8.0m, summary.GetProperty("normHoursActual").GetDecimal());
    }

    /// <summary>
    /// <b>S13 (R30).</b> The employment-window strand check counts the re-saved cell once. HR proposes an
    /// employment end date the day BEFORE the cell; the edit is refused 409 (the registered cell would be
    /// stranded outside the window) and the stranded-month pointer reports ONE time-entry row for the month,
    /// not two. (The pointer is what HR reads to decide which registrations to correct; a doubled count
    /// would send them chasing a row that is not there.)
    ///
    /// <para><b>Red conditions.</b> Today the strand query counts every projection row
    /// (<c>EmploymentWindowStrandCheck.cs:64-70</c>): <c>Assert.Equal(1L, timeEntryCount)</c> trips with 2.
    /// Listed under <b>MD-01</b> and <b>MD-16</b> (the strand SQL loses its current-row predicate; raw SQL,
    /// so MD-03 does not reach it; TRM does not either, its superseded row is before the end date).</para>
    /// </summary>
    [Fact]
    public async Task Resave_StrandCheckCountsCellOnce()
    {
        var emp = await SeedEmployeeAsync();
        await SaveEntryCellTwiceAsync(emp, recordWorkTime: false);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{emp}/employment-end-date")
        {
            Content = JsonContent.Create(new { employmentEndDate = Day.AddDays(-1) }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\""); // a freshly seeded users row is version 1
        var rsp = await HrClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var months = body.GetProperty("strandedMonths").EnumerateArray().ToArray();
        Assert.Single(months);
        Assert.Equal(Day.ToString("yyyy-MM"), months[0].GetProperty("month").GetString());
        Assert.Equal(1L, months[0].GetProperty("timeEntryCount").GetInt64());
        Assert.Equal(0L, months[0].GetProperty("absenceCount").GetInt64());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fixture composition
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The criterion-(i) fixture: record 8.0 h of work time for the day, save the Skema entry
    /// cell at 7.4 h, then save the SAME cell at 8.0 h. Each save asserts its own 200.</summary>
    private async Task SaveEntryCellTwiceAsync(string emp, bool recordWorkTime = true)
    {
        if (recordWorkTime)
            await SaveOkAsync(emp, Payload(Day, workTime: new[] { (Day, 8.0m) }), "work time 8.0 h");
        await SaveOkAsync(emp, Payload(Day, entries: new[] { (Day, Project, 7.4m) }), "entry 7.4 h");
        await SaveOkAsync(emp, Payload(Day, entries: new[] { (Day, Project, 8.0m) }), "entry re-save 8.0 h");
    }

    // ════════════════════════════════════════════════════════════════════════
    // HTTP helpers
    // ════════════════════════════════════════════════════════════════════════

    private static object Payload(
        DateOnly monthOf,
        (DateOnly Date, string Project, decimal Hours)[]? entries = null,
        (DateOnly Date, string Type, decimal Hours)[]? absences = null,
        (DateOnly Date, decimal Manual)[]? workTime = null) => new
    {
        year = monthOf.Year,
        month = monthOf.Month,
        entries = entries?.Select(e => new { date = e.Date.ToString("yyyy-MM-dd"), projectCode = e.Project, hours = e.Hours }).ToArray(),
        absences = absences?.Select(a => new { date = a.Date.ToString("yyyy-MM-dd"), absenceType = a.Type, hours = a.Hours }).ToArray(),
        workTime = workTime?.Select(w => new { date = w.Date.ToString("yyyy-MM-dd"), intervals = Array.Empty<object>(), manualHours = w.Manual }).ToArray(),
    };

    private Task<HttpResponseMessage> PostSaveAsync(string emp, object payload)
        => EmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", payload);

    /// <summary>A setup save: asserts the 200 (R62), so a failing fixture can never pass as a red pin.</summary>
    private async Task SaveOkAsync(string emp, object payload, string what)
    {
        var rsp = await PostSaveAsync(emp, payload);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"setup save ({what}) must answer 200 before the save under test; got {(int)rsp.StatusCode}: {raw}");
    }

    private HttpClient EmployeeClient(string emp) => ClientWith(MintToken(emp, StatsTidRoles.Employee, DefaultOrgOf(emp)));

    private HttpClient LeaderClient(string leader) => ClientWith(MintToken(leader, StatsTidRoles.LocalLeader, LeaderOrg));

    private HttpClient HrClient() => ClientWith(MintToken("hr_s145_cell", StatsTidRoles.LocalHR, DefaultOrg));

    private HttpClient ClientWith(string bearer)
    {
        var client = _fx.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private async Task<List<JsonElement>> GetTeamOverviewAsync(string leader)
    {
        var rsp = await LeaderClient(leader).GetAsync(
            $"/api/approval/team-overview?year={Day.Year}&month={Day.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("employees").EnumerateArray().ToList();
    }

    // Employee org per seeded id (the leader fixture's employees sit on STY02, the rest on STY01).
    private readonly Dictionary<string, string> _orgOf = new(StringComparer.Ordinal);
    private string DefaultOrgOf(string emp) => _orgOf.TryGetValue(emp, out var org) ? org : DefaultOrg;

    private string MintToken(string actorId, string role, string orgId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(role, orgId, "ORG_ONLY") });
    }

    // ════════════════════════════════════════════════════════════════════════
    // Seeding and SQL reads
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A fresh full-time AC/OK24 employee on STY01 (users + dated agreement + open dated profile).
    /// Seeded AFTER the host's first boot (see <see cref="Harness"/>), so no startup seeder erases it.</summary>
    private async Task<string> SeedEmployeeAsync()
    {
        var emp = "emp_s145_cell_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, emp, DefaultOrg, "AC", "OK24", ensureOrg: false);
        _orgOf[emp] = DefaultOrg;
        return emp;
    }

    /// <summary>An HK employee and their designated PRIMARY approver, both on STY02 (the topology of
    /// <c>TeamOverviewAggregateTests</c>: STY02 is an ORGANISATION under MAO MIN01). The reporting edge is
    /// effective well before the pinned clock <see cref="F"/>.</summary>
    private async Task<(string Employee, string Leader)> SeedEmployeeUnderLeaderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var emp = "emp_s145_cell_" + suffix;
        var leader = "mgr_s145_cell_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, emp, LeaderOrg, "HK", "OK24", ensureOrg: false);
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, leader, LeaderOrg, "HK", "OK24", ensureOrg: false);
        _orgOf[emp] = LeaderOrg;

        await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
                VALUES (@leader, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                       (@emp,    'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("leader", leader);
            cmd.Parameters.AddWithValue("emp", emp);
            await cmd.ExecuteNonQueryAsync();
        }

        await new ReportingLineRepository(_fx.Db).AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = emp,
            ManagerId = leader,
            OrganisationId = LeaderOrg,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2025, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
        return (emp, leader);
    }

    /// <summary>The send-command-matrix precedent: write the employment window straight onto the users
    /// row (NULL = unbounded, end date inclusive). Fixture STATE, not the HR endpoint under test.</summary>
    private async Task SetEmploymentWindowAsync(string emp, DateOnly? start, DateOnly? end)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("s", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", emp);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync()); // the window premise is vacuous without the row
    }

    /// <summary>A SUBMITTED whole-month period for the cell's month, so the leader reads are released
    /// (a manager sees nothing of a month the employee has not sent).</summary>
    private async Task InsertSubmittedPeriodAsync(string emp, string orgId)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'SUBMITTED', 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("emp", emp);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("start", MonthStart);
        cmd.Parameters.AddWithValue("end", MonthEnd);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Reads. Every statement is a literal passed straight to the command (never a built string): a
    //    non-constant command text trips CA2100, and this suite must not move the CA2100 ratchet. ──

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>Events the save wrote synchronously, in the in-transaction outbox, for the employee's
    /// stream; all types when <paramref name="eventType"/> is null.</summary>
    private async Task<long> OutboxCountAsync(string emp, string? eventType = null)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND (@t::text IS NULL OR event_type = @t)", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{emp}");
        cmd.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Text) { Value = (object?)eventType ?? DBNull.Value });
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> PublishedOutboxCountAsync(string emp, string eventType)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t AND published_at IS NOT NULL", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{emp}");
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The canonical <c>events</c> table (filled by the publisher after the save committed).</summary>
    private async Task<long> EventsCountAsync(string emp, string eventType)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{emp}");
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The cell's CURRENT rows (C-S2): rows not superseded by a later save. The column is
    /// TASK-14500's.</summary>
    private async Task<long> CurrentEntryRowCountAsync(string emp, DateOnly date, string task)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM time_entries_projection
            WHERE employee_id = @e AND date = @d AND task_id = @t AND superseded_by_outbox_id IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("t", task);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>All projection rows for the employee, superseded or not (a refused save must add none).</summary>
    private async Task<long> EntryRowCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", emp);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> AbsenceRowCountAsync(string emp, DateOnly? date = null)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e AND (@d::date IS NULL OR date = @d)", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = (object?)date ?? DBNull.Value });
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<decimal> BalanceUsedAsync(string emp, string entitlementType, int entitlementYear)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", entitlementYear);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"expected an entitlement_balances row for {entitlementType}/{entitlementYear}.");
        return Convert.ToDecimal(result);
    }

    /// <summary>The VACATION ferieår of the cell's day (reset month 9: March 2025 is ferieår 2024).</summary>
    private static int VacationYear => Day.Month >= 9 ? Day.Year : Day.Year - 1;

    private async Task WaitForOutboxDrainedAsync(string emp, string eventType, long expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await PublishedOutboxCountAsync(emp, eventType) >= expected)
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"OutboxPublisher did not drain {expected} {eventType} rows on employee-{emp} within {timeoutMs} ms.");
    }

    /// <summary>The weekday nudge: a Saturday or Sunday moves to the Monday after. A zero-norm day is
    /// rejected by the product for entitlement absences and skipped by revaluation, so a fixture date
    /// that lands on one would prove nothing (S138, QUAL-153).</summary>
    private static DateOnly NextWeekday(DateOnly d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(1);
        return d;
    }

    // ════════════════════════════════════════════════════════════════════════
    // The shared container + host (one per class; cases isolate by their own employee ids)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One Postgres testcontainer and one booted API for the class. Isolation between facts is by
    /// case-unique employee ids, never by cleanup (the <c>SendCommandMatrixFixture</c> precedent). The host
    /// carries the entitlement-validation stub (R62) and the pinned clock (PAT-008) in ONE
    /// <c>ConfigureTestServices</c>, and is booted once BEFORE any fixture is seeded: a derived host's first
    /// <c>CreateClient()</c> re-runs the startup seeders against the same database.
    /// </summary>
    public sealed class Harness : IAsyncLifetime
    {
        private TestFixtures.DockerHarness _harness = null!;
        private StatsTidWebApplicationFactory _factory = null!;

        public WebApplicationFactory<Program> Host { get; private set; } = null!;
        public string ConnectionString { get; private set; } = null!;
        public DbConnectionFactory Db { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _harness = await TestFixtures.DockerHarness.StartAsync();
            ConnectionString = _harness.ConnectionString;
            await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
            _factory = new StatsTidWebApplicationFactory(ConnectionString);
            Host = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(F));
                }));
            _ = Host.CreateClient(); // boot the startup seeders now; every case seeds after this
            Db = new DbConnectionFactory(ConnectionString);
        }

        public async Task DisposeAsync()
        {
            Host?.Dispose();
            _factory?.Dispose();
            if (_harness is not null)
                await _harness.DisposeAsync();
        }
    }

    // ── Rule-engine stub: drives the REAL EntitlementValidationRule over the HTTP seam ──

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        // The endpoints resolve the NAMED RuleEngine client and use RELATIVE request URIs; this stub
        // replaces the whole factory, so it supplies the BaseAddress the production registration sets.
        public HttpClient CreateClient(string name) => new(new EntitlementValidationStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }
}
