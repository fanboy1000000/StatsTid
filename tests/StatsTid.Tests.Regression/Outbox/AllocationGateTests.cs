using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// Shared container + booted API for <see cref="AllocationGateTests"/>. One Postgres testcontainer
/// carrying the REAL <c>init.sql</c> and one <see cref="StatsTidWebApplicationFactory"/> for the whole
/// class; isolation between cases comes from case-unique identifiers, not from cleanup.
/// </summary>
public sealed class AllocationGateFixture : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    public DbConnectionFactory Db { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public PostgresEventStore Outbox { get; private set; } = null!;
    public TimeEntryProjectionRepository TimeEntryRepo { get; private set; } = null!;
    public WorkTimeProjectionRepository WorkTimeRepo { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        Db = new DbConnectionFactory(ConnectionString);
        Factory = new StatsTidWebApplicationFactory(ConnectionString);
        Outbox = new PostgresEventStore(Db, new OutboxServiceContext("backend-api"));
        TimeEntryRepo = new TimeEntryProjectionRepository(Db);
        WorkTimeRepo = new WorkTimeProjectionRepository(Db);
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

/// <summary>
/// S56 / TASK-5604 — the allocation-reconciliation HARD gate, <b>exercised through the production
/// endpoint</b>.
///
/// <para><b>What the gate is.</b> A month cannot be sent to the manager unless, for every day in it,
/// the hours recorded as time at work equal the hours distributed onto projects:</para>
/// <list type="bullet">
///   <item><b>worked</b> = Σ work-interval hours + <c>manual_hours</c>, from
///     <c>work_time_projection</c>. No row for the day ⇒ 0.</item>
///   <item><b>allocated</b> = Σ hours of <c>time_entries_projection</c> rows with
///     <c>activity_type = 'NORMAL'</c> AND a non-null <c>task_id</c>. Absence-type rows and ordinary
///     rows naming no project both contribute nothing — parity with the grid's "Ikke fordelt" row.</item>
/// </list>
/// <para>Both are rounded to hundredths before comparison; any day that does not match makes the send
/// return <c>422 {kind:"allocation", unbalancedDays:[…]}</c> with a per-day <c>direction</c> of
/// <c>"under"</c> (worked &gt; allocated) or <c>"over"</c>.</para>
///
/// <para><b>S127 / TASK-12705 — WHY THIS FILE WAS REWRITTEN, in full, because the previous version
/// looked like evidence and was not.</b> Until this sprint every case here called a private
/// <c>ComputeUnbalancedAsync</c> helper that RE-IMPLEMENTED the gate: the same interval summation, the
/// same NORMAL/non-null-<c>task_id</c> allowlist, the same 2-decimal rounding, the same tolerance
/// constant, the same both-directions comparison — a verbatim copy of the endpoint's inline
/// expression, described in the file header as "its executable spec". It was refinement §3.8's
/// <b>encoding 5 of five</b>, and its defect is exact and checkable: <i>all seven assertions passed
/// with the production gate deleted outright.</i> The file seeded real projection rows and then
/// asserted that the TEST's arithmetic agreed with itself. Deleting the gate from
/// <c>ApprovalEndpoints</c> would have left the suite green and shipped a month that nobody had to
/// balance.</para>
///
/// <para>So the arithmetic is gone from this file — there is no rounding here, no tolerance, no
/// allowlist and no comparison. Every case now POSTs <c>/api/approval/send</c> and reads the verdict
/// off the HTTP response. Remove the allocation arm from <c>ExecuteSendAsync</c> and the five refusal
/// cases below return 200 instead of 422 and go red immediately.</para>
///
/// <para><b>How this differs from <see cref="AllocationPredicateCharacterizationTests"/>, which sits
/// beside it.</b> That file is TASK-12700's AC-2 baseline: a value TABLE driven through all THREE
/// encodings of the predicate (the gate plus the two read surfaces) to prove the S127 consolidation
/// changed no behaviour. This file is narrower and older in intent — the GATE's own suite, about what
/// the gate refuses and what it lets through — and it keeps the cases the table does not carry: work
/// on a <b>weekend</b>, and an <b>absence registration sharing the day with real work</b>. It also
/// keeps this file's original and genuinely useful property, that the projection rows are written by
/// the PRODUCTION repositories through the real outbox rather than by hand-rolled INSERTs, so the gate
/// is pinned against the read-model shape those writers actually produce.</para>
///
/// <para><b>Reachability.</b> The gate sits BELOW the workday-coverage check, which demands a
/// registration on every expected workday. Each case therefore fills the month's other weekdays with
/// full-day absences: absences satisfy coverage and are read from a table neither the worked map nor
/// the allocated map touches, so they contribute nothing to the verdict and leave the case day as the
/// only day the gate compares.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AllocationGateTests : IClassFixture<AllocationGateFixture>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";

    // March 2026 — no Danish public holidays (ASSERTED in SeedMonthAsync, not assumed, so this stops
    // being true loudly rather than quietly if init.sql's holiday table changes).
    private static readonly DateOnly MonthStart = new(2026, 3, 1);
    private static readonly DateOnly MonthEnd = new(2026, 3, 31);

    /// <summary>2026-03-05, a Thursday — the ordinary weekday most cases vary.</summary>
    private static readonly DateOnly WeekDay = new(2026, 3, 5);

    /// <summary>2026-03-07, a Saturday — NOT an expected workday, but still gated.</summary>
    private static readonly DateOnly Saturday = new(2026, 3, 7);

    private readonly AllocationGateFixture _fx;

    public AllocationGateTests(AllocationGateFixture fx) => _fx = fx;

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  What the gate lets through
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The ordinary balanced day: 8.4 hours at work, 8.4 hours distributed onto a project.
    ///
    /// <para>A 200 here is not vacuous. The case day carries NO coverage absence, so if either seeded
    /// row had failed to land the send would have been refused before ever reaching the gate — by
    /// coverage if both failed, by the gate itself (0 vs 8.4, or 8.4 vs 0) if one did.</para>
    /// </summary>
    [Fact]
    public async Task BalancedDay_IsSent()
    {
        var emp = await SeedMonthAsync("balanced", coverageGap: WeekDay);
        await WorkedAsync(emp, WeekDay, intervals: new[] { ("08:00", "16:24") });   // 8.4h
        await AllocatedAsync(emp, WeekDay, 8.4m, "NORMAL", "PROJ-1");

        var rsp = await SendAsync(emp);
        await AssertSentAsync(rsp);
    }

    /// <summary>
    /// The tolerance's ONLY job: representation noise. 7.40 (a <c>manual_hours</c> NUMERIC) and 7.4
    /// (an hours NUMERIC of a different scale) are the same VALUE and must not block a send.
    /// </summary>
    [Fact]
    public async Task SameValueAtDifferentScale_IsSent()
    {
        var emp = await SeedMonthAsync("scale", coverageGap: WeekDay);
        await WorkedAsync(emp, WeekDay, manualHours: 7.40m);
        await AllocatedAsync(emp, WeekDay, 7.4m, "NORMAL", "PROJ-1");

        var rsp = await SendAsync(emp);
        await AssertSentAsync(rsp);
    }

    /// <summary>
    /// An absence sharing the day with real work is not something the employee has to distribute.
    /// 3 hours at work + 3 hours on a project + a 4.4-hour SICK absence on the SAME day ⇒ sendable:
    /// absences live in <c>absences_projection</c>, which the gate never reads on either side.
    ///
    /// <para>This case is the one where a silent seeding failure COULD have produced a vacuous 200 —
    /// the absence alone satisfies coverage, so a month with neither projection row would also return
    /// 200. Hence the explicit input corroboration before the send.</para>
    /// </summary>
    [Fact]
    public async Task AbsenceOnAWorkedDay_IsNeitherWorkedNorAllocated()
    {
        var emp = await SeedMonthAsync("absence", coverageGap: WeekDay);
        await WorkedAsync(emp, WeekDay, intervals: new[] { ("08:00", "11:00") });   // 3.0h
        await AllocatedAsync(emp, WeekDay, 3.0m, "NORMAL", "PROJ-1");
        await AbsenceAsync(emp, WeekDay, "SICK", 4.4m);

        await AssertInputsLandedAsync(emp, WeekDay, expectWorkTimeRow: true, expectedAllocated: 3.0m);

        var rsp = await SendAsync(emp);
        await AssertSentAsync(rsp);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  What the gate refuses
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>7.4 hours worked, 3.0 distributed — the employee owes the remaining 4.4 to a project.</summary>
    [Fact]
    public async Task WorkedMoreThanAllocated_IsRefused_Under()
    {
        var emp = await SeedMonthAsync("under", coverageGap: WeekDay);
        await WorkedAsync(emp, WeekDay, intervals: new[] { ("08:00", "15:24") });   // 7.4h
        await AllocatedAsync(emp, WeekDay, 3.0m, "NORMAL", "PROJ-1");

        var rsp = await SendAsync(emp);
        await AssertRefusedAsync(rsp, WeekDay, worked: 7.40m, allocated: 3.00m, direction: "under");
    }

    /// <summary>
    /// Project hours on a day with no recorded work time at all — worked is 0 and the day enters the
    /// comparison through the allocated side. The NORMAL entry is what satisfies coverage, so this
    /// case reaches the gate rather than being refused above it.
    /// </summary>
    [Fact]
    public async Task AllocatedWithoutAnyWorkTime_IsRefused_Over()
    {
        var emp = await SeedMonthAsync("over", coverageGap: WeekDay);
        await AllocatedAsync(emp, WeekDay, 7.4m, "NORMAL", "PROJ-1");

        var rsp = await SendAsync(emp);
        await AssertRefusedAsync(rsp, WeekDay, worked: 0.00m, allocated: 7.40m, direction: "over");
    }

    /// <summary>
    /// One hundredth of an hour short — the smallest difference that can survive the rounding, and the
    /// one that proves the tolerance is not a slack allowance.
    /// </summary>
    [Fact]
    public async Task OneOereShort_IsRefused()
    {
        var emp = await SeedMonthAsync("oere", coverageGap: WeekDay);
        await WorkedAsync(emp, WeekDay, manualHours: 7.40m);
        await AllocatedAsync(emp, WeekDay, 7.39m, "NORMAL", "PROJ-1");

        var rsp = await SendAsync(emp);
        await AssertRefusedAsync(rsp, WeekDay, worked: 7.40m, allocated: 7.39m, direction: "under");
    }

    /// <summary>
    /// An ordinary registration naming NO project is not distributed hours. It satisfies coverage —
    /// which is exactly why it is dangerous — and contributes nothing to allocated, so the day is
    /// refused as fully under-allocated.
    /// </summary>
    [Fact]
    public async Task NormalEntryWithNoProject_IsNotAllocatedHours()
    {
        var emp = await SeedMonthAsync("noproject", coverageGap: WeekDay);
        await WorkedAsync(emp, WeekDay, intervals: new[] { ("08:00", "15:24") });   // 7.4h
        await AllocatedAsync(emp, WeekDay, 7.4m, "NORMAL", taskId: null);

        var rsp = await SendAsync(emp);
        await AssertRefusedAsync(rsp, WeekDay, worked: 7.40m, allocated: 0.00m, direction: "under");
    }

    /// <summary>
    /// <b>Saturday work is gated too.</b> The coverage check only demands registrations on EXPECTED
    /// workdays, so a Saturday is never one it asks about — but the allocation gate compares every day
    /// carrying worked or allocated hours, weekend or not. Here the whole month's weekdays are covered
    /// by absences (no coverage gap at all) and the only thing in the month is unallocated Saturday
    /// work: the send is refused, and the day it names is the Saturday.
    ///
    /// <para>This is the case that separates the two checks. Were the gate ever narrowed to expected
    /// workdays, coverage would still pass, every other case in this file would still pass, and this
    /// one alone would go red.</para>
    /// </summary>
    [Fact]
    public async Task WeekendWork_IsGated_EvenThoughCoverageNeverAsksAboutIt()
    {
        Assert.Equal(DayOfWeek.Saturday, Saturday.DayOfWeek);

        var emp = await SeedMonthAsync("weekend", coverageGap: null);
        await WorkedAsync(emp, Saturday, intervals: new[] { ("10:00", "14:00") });  // 4.0h

        var rsp = await SendAsync(emp);
        await AssertRefusedAsync(rsp, Saturday, worked: 4.00m, allocated: 0.00m, direction: "under");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  The endpoint, and what its answers must look like
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The production send. The employee sends their OWN month, which is the role floor's self case
    /// (ruling R4), and the server derives the range from (year, month) — the request carries no dates.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string employeeId)
    {
        var client = ClientFor(employeeId);
        return await client.PostAsJsonAsync("/api/approval/send", new
        {
            employeeId,
            year = MonthStart.Year,
            month = MonthStart.Month,
        });
    }

    private static async Task AssertSentAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        // Surface the body: a 422 here is far more likely to be the COVERAGE arm above the gate than
        // the gate itself, and a bare status mismatch would not say which.
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal("EMPLOYEE_APPROVED",
            JsonDocument.Parse(raw).RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// Asserts the allocation refusal AND its exact contents: the shape (<c>kind:"allocation"</c>,
    /// which discriminates it from the coverage 422 that has no <c>kind</c>), the EXACT set of days
    /// reported — one, the case day — and the rounded figures and direction on it.
    ///
    /// <para>The day set matters. The month's other weekdays are filled with absences precisely so the
    /// case day is the only day the gate may name; asserting only "422" would stay green under a gate
    /// that had started refusing the filler days instead.</para>
    /// </summary>
    private static async Task AssertRefusedAsync(
        HttpResponseMessage rsp, DateOnly date, decimal worked, decimal allocated, string direction)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)rsp.StatusCode}: {raw}");

        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("allocation", body.GetProperty("kind").GetString());

        var day = Assert.Single(body.GetProperty("unbalancedDays").EnumerateArray().ToList());
        Assert.Equal(date.ToString("yyyy-MM-dd"), day.GetProperty("date").GetString());
        Assert.Equal(worked, day.GetProperty("worked").GetDecimal());
        Assert.Equal(allocated, day.GetProperty("allocated").GetDecimal());
        Assert.Equal(direction, day.GetProperty("direction").GetString());
    }

    private HttpClient ClientFor(string userId)
    {
        var client = _fx.Factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Fixture
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a case-unique employee and covers the month's weekdays with full-day absences, leaving
    /// <paramref name="coverageGap"/> (if any) uncovered for the case to fill with real registrations.
    /// Returns the employee id.
    ///
    /// <para>No <c>approval_periods</c> row is pre-created: every case goes through the send command's
    /// CREATE arm, so a refused case leaves no row behind and the cases stay independent.</para>
    /// </summary>
    private async Task<string> SeedMonthAsync(string caseId, DateOnly? coverageGap)
    {
        var emp = "t12705_" + caseId;

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // ASSERTED, not assumed: the month carries no Danish public holiday, so "expected workday"
        // is exactly "weekday" and covering every weekday covers the month.
        await using (var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= @s AND holiday_date <= @e", conn))
        {
            cmd.Parameters.AddWithValue("s", MonthStart);
            cmd.Parameters.AddWithValue("e", MonthEnd);
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@emp, @emp, '$2a$11$fake', 'T12705 Emp', @mail, @org, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("mail", emp + "@test.dk");
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@emp, 'EMPLOYEE', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        for (var d = MonthStart; d <= MonthEnd; d = d.AddDays(1))
        {
            if (d == coverageGap || d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await AbsenceAsync(emp, d, "VACATION", 7.4m);
        }

        return emp;
    }

    /// <summary>
    /// Records time at work through the PRODUCTION write path — an outbox event plus
    /// <see cref="WorkTimeProjectionRepository.UpsertAsync"/> in one transaction — so the gate reads
    /// the projection shape the real writer produces rather than a hand-rolled INSERT's guess at it.
    /// </summary>
    private async Task WorkedAsync(
        string employeeId, DateOnly date, (string Start, string End)[]? intervals = null,
        decimal manualHours = 0m)
    {
        var @event = new WorkTimeRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Intervals = (intervals ?? Array.Empty<(string, string)>())
                .Select(t => new WorkInterval { Start = t.Start, End = t.End }).ToList(),
            ManualHours = manualHours,
        };
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await _fx.Outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-{employeeId}", @event);
        await _fx.WorkTimeRepo.UpsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }

    /// <summary>Registers one time entry through the production write path (outbox + projection).</summary>
    private async Task AllocatedAsync(
        string employeeId, DateOnly date, decimal hours, string activityType, string? taskId)
    {
        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Hours = hours,
            TaskId = taskId,
            ActivityType = activityType,
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await _fx.Outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-{employeeId}", @event);
        await _fx.TimeEntryRepo.InsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }

    /// <summary>
    /// An absence row. Written directly: absences exist here only to satisfy the coverage check above
    /// the gate, and the gate never reads this table — which is itself the contract
    /// <see cref="AbsenceOnAWorkedDay_IsNeitherWorkedNorAllocated"/> asserts.
    /// </summary>
    private async Task AbsenceAsync(string employeeId, DateOnly date, string absenceType, decimal hours)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, outbox_id)
            VALUES (gen_random_uuid(), @emp, @date, @type, @hours, 1.0, 'HK', 'OK24', NOW(), @seq)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("type", absenceType);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("seq", NextOutboxId());
        await cmd.ExecuteNonQueryAsync();
    }

    private static int _absenceSeq = 900_000;
    private static long NextOutboxId() => Interlocked.Increment(ref _absenceSeq);

    /// <summary>
    /// Corroborates that the fixture produced the inputs the case intends — a guard against a green
    /// verdict that is really the accident of a row that never landed.
    ///
    /// <para>Deliberately NOT a second copy of the rule: it reads back what was written, and does no
    /// rounding, no tolerance and no comparison of the two sides against each other.</para>
    /// </summary>
    private async Task AssertInputsLandedAsync(
        string employeeId, DateOnly date, bool expectWorkTimeRow, decimal expectedAllocated)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM work_time_projection WHERE employee_id = @e AND date = @d", conn))
        {
            cmd.Parameters.AddWithValue("e", employeeId);
            cmd.Parameters.AddWithValue("d", date);
            var rows = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(expectWorkTimeRow ? 1L : 0L, rows);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(hours), 0) FROM time_entries_projection
            WHERE employee_id = @e AND date = @d AND activity_type = 'NORMAL' AND task_id IS NOT NULL
            """, conn))
        {
            cmd.Parameters.AddWithValue("e", employeeId);
            cmd.Parameters.AddWithValue("d", date);
            Assert.Equal(expectedAllocated, (decimal)(await cmd.ExecuteScalarAsync())!);
        }
    }
}
