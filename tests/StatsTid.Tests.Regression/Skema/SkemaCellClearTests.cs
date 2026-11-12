using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14840-T — the shared container, host and helpers of the cell-clearing Docker facts
/// (<see cref="SkemaCellClearTests"/>, <see cref="SkemaCellClearQuotaTests"/>, <see cref="SkemaCellClearBoundTests"/>,
/// <see cref="SkemaCellClearRaceTests"/>, <see cref="SkemaCellClearAuthorityTests"/>). One Postgres with the full
/// <c>init.sql</c>, one Backend host whose rule-engine client is the entitlement-validation stub (the convention of
/// the Skema suites: the real <c>EntitlementValidationRule</c> behind the HTTP seam) and whose clock is pinned
/// (PAT-008) at Monday 1 June 2026, on the OK26 side of the 1 April cutover. Isolation between facts is by
/// case-unique employee ids, never by cleanup. The host is booted once BEFORE any fact seeds an employee.
/// </summary>
public class SkemaCellClearFixture : IAsyncLifetime
{
    /// <summary>The pinned "today": Monday 1 June 2026. Every cell date of these facts lies after it.</summary>
    public static readonly DateOnly Clock = new(2026, 6, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    /// <summary>True (the default): the host's clock is pinned at <see cref="Clock"/>. A derived fixture overrides it for the
    /// payroll facts, whose months (September and October 2026) are past on the real clock.</summary>
    protected virtual bool PinClock => true;

    public WebApplicationFactory<Program> Host { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public DbConnectionFactory Db { get; private set; } = null!;
    internal TestFixtures.DockerHarness Harness => _harness;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        ConnectionString = _harness.ConnectionString;
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
        // No leaver-flip job: a fact that seeds a departed employee must not race the 5-minute job.
        _factory = new StatsTidWebApplicationFactory(ConnectionString, withoutLeaverJob: true);
        Host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new PaidAbsenceRuleStubFactory());
                if (PinClock)
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock));
            }));
        _ = Host.CreateClient(); // boot the startup seeders now; every fact seeds after this
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

[CollectionDefinition("SkemaCellClear")]
public sealed class SkemaCellClearCollection : ICollectionFixture<SkemaCellClearFixture> { }

/// <summary>The helpers the five cell-clearing classes share: seeding, the save request, clients and tokens, and
/// read-backs by SQL (no copy of any rule under test). Every cell date is a weekday of June 2026 (Mon 8, Tue 9, Wed
/// 10, Thu 11, Fri 12) or Wed 1 July 2026, each checked against the calendar; <c>init.sql</c> seeds no public holiday
/// on them. The employee is full time (37 h, 7,4 h planned on a weekday) unless a fact says otherwise.</summary>
public abstract class SkemaCellClearTestBase
{
    protected const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    protected const string Org = "STY01";
    protected const string LeaderOrg = "STY02";

    protected static readonly DateOnly Mon8 = new(2026, 6, 8);
    protected static readonly DateOnly Tue9 = new(2026, 6, 9);
    protected static readonly DateOnly Wed10 = new(2026, 6, 10);
    protected static readonly DateOnly Thu11 = new(2026, 6, 11);
    protected static readonly DateOnly Fri12 = new(2026, 6, 12);
    protected static readonly DateOnly Wed1July = new(2026, 7, 1);

    protected readonly SkemaCellClearFixture Fx;

    protected SkemaCellClearTestBase(SkemaCellClearFixture fx) => Fx = fx;

    protected static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    // ── Seeding ──

    /// <summary>A fresh employee of <paramref name="agreement"/> (OK26, fraction 1,000, profile from 0001-01-01).</summary>
    protected async Task<string> SeedEmployeeAsync(
        string agreement = "AC", string org = Org, DateOnly? birthDate = null, DateOnly? employmentStart = null)
    {
        var emp = "emp_s148_cl_" + Guid.NewGuid().ToString("N")[..10];
        await RegressionSeed.SeedEmployeeAsync(Fx.ConnectionString, emp, org, agreement, "OK26", ensureOrg: false);
        if (birthDate is not null || employmentStart is not null)
            await ExecAsync(
                "UPDATE users SET birth_date = COALESCE(@b, birth_date), employment_start_date = COALESCE(@s, employment_start_date) WHERE user_id = @e",
                ("b", birthDate), ("s", employmentStart), ("e", emp));
        return emp;
    }

    /// <summary>An HK employee and the designated PRIMARY approver, both on STY02 (the topology of
    /// <c>TeamOverviewAggregateTests</c>); the reporting edge is effective well before the pinned clock.</summary>
    protected async Task<(string Employee, string Leader)> SeedEmployeeUnderLeaderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var emp = "emp_s148_cl_" + suffix;
        var leader = "mgr_s148_cl_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(Fx.ConnectionString, emp, LeaderOrg, "HK", "OK26", ensureOrg: false);
        await RegressionSeed.SeedEmployeeAsync(Fx.ConnectionString, leader, LeaderOrg, "HK", "OK26", ensureOrg: false);
        await ExecAsync(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@leader, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                   (@emp,    'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, ("leader", leader), ("emp", emp));
        await new ReportingLineRepository(Fx.Db).AssignAsync(null, new ReportingLineModel
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

    protected Task SetEmploymentWindowAsync(string emp, DateOnly? start, DateOnly? end) => ExecAsync(
        "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id",
        ("s", start), ("e", end), ("id", emp));

    /// <summary>A whole-month June 2026 approval period of the given status, written straight into the table.</summary>
    protected Task InsertPeriodAsync(string emp, string status, string orgId = Org, string agreement = "AC") => ExecAsync(
        """
        INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                                      agreement_code, ok_version, submitted_at, submitted_by)
        VALUES (gen_random_uuid(), @emp, @org, DATE '2026-06-01', DATE '2026-06-30', 'MONTHLY', @st, @ac, 'OK26', NOW(), @emp)
        """, ("emp", emp), ("org", orgId), ("st", status), ("ac", agreement));

    protected Task DeletePeriodsAsync(string emp) =>
        ExecAsync("DELETE FROM approval_periods WHERE employee_id = @e", ("e", emp));

    /// <summary>A TIME_POST VACATION row written by SQL: coverage for the send gate on a day, never itself cleared.</summary>
    protected Task InsertCoverageAbsenceAsync(string emp, DateOnly date, string agreement = "HK") => ExecAsync(
        """
        INSERT INTO absences_projection (event_id, employee_id, date, absence_type, hours, feriedage,
                                         agreement_code, ok_version, occurred_at, outbox_id)
        VALUES (gen_random_uuid(), @e, @d, 'VACATION', 7.4, 1.0, @ac, 'OK26', NOW(), 0)
        """, ("e", emp), ("d", date), ("ac", agreement));

    // ── The request ──

    /// <summary>One Skema save body for <paramref name="year"/>/<paramref name="month"/>: posted cells and the
    /// request's <c>cleared</c> member. Omitted arrays are not sent.</summary>
    protected static Dictionary<string, object> Save(
        (DateOnly Date, string Project, decimal Hours)[]? entries = null,
        (DateOnly Date, string Type, decimal Hours)[]? absences = null,
        (DateOnly Date, string Start, string End)[]? workTime = null,
        (DateOnly Date, string Project)[]? clearEntries = null,
        (DateOnly Date, string Type)[]? clearAbsences = null,
        DateOnly[]? clearWorkTime = null,
        int year = 2026, int month = 6)
    {
        var body = new Dictionary<string, object> { ["year"] = year, ["month"] = month };
        if (entries is not null)
            body["entries"] = entries.Select(e => new { date = D(e.Date), projectCode = e.Project, hours = e.Hours }).ToArray();
        if (absences is not null)
            body["absences"] = absences.Select(a => new { date = D(a.Date), absenceType = a.Type, hours = a.Hours }).ToArray();
        if (workTime is not null)
            body["workTime"] = workTime.Select(w => new
            {
                date = D(w.Date),
                intervals = new[] { new { start = w.Start, end = w.End } },
            }).ToArray();
        if (clearEntries is not null || clearAbsences is not null || clearWorkTime is not null)
            body["cleared"] = new
            {
                entries = clearEntries?.Select(c => new { date = D(c.Date), projectCode = c.Project }).ToArray(),
                absences = clearAbsences?.Select(c => new { date = D(c.Date), absenceType = c.Type }).ToArray(),
                workTime = clearWorkTime?.Select(d => new { date = D(d) }).ToArray(),
            };
        return body;
    }

    protected static Task<HttpResponseMessage> PostSaveAsync(HttpClient client, string emp, object body) =>
        client.PostAsJsonAsync($"/api/skema/{emp}/save", body);

    /// <summary>A setup save: asserts its own 200, so a failing fixture can never pass as a red pin.</summary>
    protected async Task SaveOkAsync(string emp, object body, string what, HttpClient? client = null)
    {
        var rsp = await PostSaveAsync(client ?? EmployeeClient(emp), emp, body);
        await AssertStatusAsync(rsp, HttpStatusCode.OK, $"setup save ({what})");
    }

    protected static async Task AssertStatusAsync(HttpResponseMessage rsp, HttpStatusCode expected, string? what = null)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected, $"{what ?? "the save"}: expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
    }

    /// <summary>The refusal's JSON body, after asserting the status.</summary>
    protected static async Task<JsonElement> RefusalAsync(HttpResponseMessage rsp, HttpStatusCode expected)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected, $"expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    // ── Clients ──

    protected HttpClient EmployeeClient(string emp, string agreement = "AC") =>
        ClientFor(emp, StatsTidRoles.Employee, Org, agreement);

    protected HttpClient ClientFor(string actor, string role, string orgId, string agreement = "AC")
    {
        var client = Fx.Host.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: actor, name: actor, role: role, agreementCode: agreement, orgId: orgId,
            scopes: new[] { new RoleScope(role, orgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var rsp = await client.GetAsync(url);
        return await RefusalAsync(rsp, HttpStatusCode.OK);
    }

    protected Task<JsonElement> MonthAsync(string emp, HttpClient? client = null, int year = 2026, int month = 6) =>
        GetJsonAsync(client ?? EmployeeClient(emp), $"/api/skema/{emp}/month?year={year}&month={month}");

    // ── Read-backs ──

    protected sealed record AbsenceRow(
        Guid EventId, DateOnly Date, string Type, decimal Hours, decimal? Feriedage, string Source, long OutboxId, long? SupersededBy);

    protected sealed record EntryRow(
        Guid EventId, DateOnly Date, string Task, decimal Hours, string Source, long OutboxId, long? SupersededBy);

    protected sealed record WorkRow(DateOnly Date, string Intervals, long OutboxId, long? ClearedBy);

    protected sealed record OutboxRow(
        long OutboxId, Guid EventId, string Type, JsonElement Payload, string? ActorId, string? ActorRole, bool Published);

    protected async Task<List<AbsenceRow>> AbsencesAsync(string emp)
    {
        var rows = new List<AbsenceRow>();
        await ReadAsync(
            """
            SELECT event_id, date, absence_type, hours, feriedage, source, outbox_id, superseded_by_outbox_id
            FROM absences_projection WHERE employee_id = @e ORDER BY outbox_id
            """, r => rows.Add(new AbsenceRow(
                r.GetGuid(0), r.GetFieldValue<DateOnly>(1), r.GetString(2), r.GetDecimal(3),
                r.IsDBNull(4) ? null : r.GetDecimal(4), r.GetString(5), r.GetInt64(6),
                r.IsDBNull(7) ? null : r.GetInt64(7))), ("e", emp));
        return rows;
    }

    protected async Task<List<EntryRow>> EntriesAsync(string emp)
    {
        var rows = new List<EntryRow>();
        await ReadAsync(
            """
            SELECT event_id, date, COALESCE(task_id, ''), hours, source, outbox_id, superseded_by_outbox_id
            FROM time_entries_projection WHERE employee_id = @e ORDER BY outbox_id
            """, r => rows.Add(new EntryRow(
                r.GetGuid(0), r.GetFieldValue<DateOnly>(1), r.GetString(2), r.GetDecimal(3), r.GetString(4),
                r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6))), ("e", emp));
        return rows;
    }

    /// <summary>Every work-time row of the employee, cleared or not; <c>Intervals</c> reads "08:00-15:24;...".</summary>
    protected async Task<List<WorkRow>> WorkAsync(string emp)
    {
        var rows = new List<WorkRow>();
        await ReadAsync(
            """
            SELECT date, COALESCE((SELECT string_agg((i->>'start') || '-' || (i->>'end'), ';' ORDER BY ord)
                                   FROM jsonb_array_elements(intervals) WITH ORDINALITY AS t(i, ord)), ''),
                   outbox_id, cleared_by_outbox_id
            FROM work_time_projection WHERE employee_id = @e ORDER BY date
            """, r => rows.Add(new WorkRow(
                r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt64(3))),
            ("e", emp));
        return rows;
    }

    /// <summary>The employee's outbox rows (all event types when <paramref name="type"/> is null), in outbox order.</summary>
    protected async Task<List<OutboxRow>> OutboxAsync(string emp, string? type = null)
    {
        var rows = new List<OutboxRow>();
        await ReadAsync(
            """
            SELECT outbox_id, event_id, event_type, event_payload::text, actor_id, actor_role, published_at IS NOT NULL
            FROM outbox_events WHERE stream_id = @s AND (@t::text IS NULL OR event_type = @t) ORDER BY outbox_id
            """, r => rows.Add(new OutboxRow(
                r.GetInt64(0), r.GetGuid(1), r.GetString(2), JsonDocument.Parse(r.GetString(3)).RootElement.Clone(),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6))),
            ("s", $"employee-{emp}"), ("t", type));
        return rows;
    }

    /// <summary>The employee's balance rows of one entitlement type as (year, used), by year.</summary>
    protected async Task<List<(int Year, decimal Used)>> BalancesAsync(string emp, string entitlementType)
    {
        var rows = new List<(int, decimal)>();
        await ReadAsync(
            "SELECT entitlement_year, used FROM entitlement_balances WHERE employee_id = @e AND entitlement_type = @t ORDER BY entitlement_year",
            r => rows.Add((r.GetInt32(0), r.GetDecimal(1))), ("e", emp), ("t", entitlementType));
        return rows;
    }

    protected async Task<decimal> UsedAsync(string emp, string entitlementType) =>
        Assert.Single(await BalancesAsync(emp, entitlementType)).Used;

    protected async Task<long> AuditCountAsync(string emp, string eventType) =>
        await ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = @t AND target_resource_id = @e", ("t", eventType), ("e", emp));

    protected async Task<long> ScalarLongAsync(string sql, params (string Name, object? Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the SQL is a compile-time test constant; values go through parameters.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected async Task ExecAsync(string sql, params (string Name, object? Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the SQL is a compile-time test constant; values go through parameters.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ReadAsync(string sql, Action<NpgsqlDataReader> each, params (string Name, object? Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the SQL is a compile-time test constant; values go through parameters.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            each(reader);
    }

    // ── The per-employee lock, for the race facts (the PaidAbsenceDayBoundRaceTests choreography) ──

    /// <summary>Opens a third connection that holds the employee's advisory lock; the caller commits the returned
    /// transaction to release it. Ordering of the racing saves is then forced by OBSERVATION (see
    /// <see cref="WaitForWaitersAsync"/>), never by a sleep.</summary>
    protected async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> HoldEmployeeLockAsync(string emp)
    {
        var conn = Fx.Db.Create();
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(conn, tx, emp);
        return (conn, tx);
    }

    /// <summary>Blocks until at least <paramref name="expected"/> backends are WAITING on the employee's advisory
    /// key, i.e. genuinely queued behind the holder (so each has passed its pre-transaction checks). Times out, and
    /// fails loud, if a request never reaches the lock.</summary>
    protected async Task WaitForWaitersAsync(string emp, int expected, int timeoutSeconds = 30)
    {
        var key = await ScalarLongAsync("SELECT hashtext('employee-' || @id)::bigint", ("id", emp));
        var u = unchecked((ulong)key);
        var classId = (long)(u >> 32);
        var objId = (long)(u & 0xFFFFFFFFUL);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var waiters = await ScalarLongAsync(
                """
                SELECT COUNT(*) FROM pg_locks
                WHERE locktype = 'advisory' AND objsubid = 1 AND classid::bigint = @c AND objid::bigint = @o AND granted = FALSE
                """, ("c", classId), ("o", objId));
            if (waiters >= expected)
                return;
            if (sw.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
                throw new TimeoutException($"Only {waiters} advisory-lock waiter(s) for {emp} after {timeoutSeconds}s; expected {expected}.");
            await Task.Delay(20);
        }
    }
}

/// <summary>
/// S148 / TASK-14840-T — the basic cell-clearing facts (owner Q2, addendum A1 AC-CL-1 … -10, -25). In plain terms:
/// an employee (or HR for them) can empty a Skema cell, and each removal is its own recorded event with an audit row;
/// the registration it removes is kept and marked as retired, never deleted; clearing a cell that holds nothing
/// records nothing; and a posted work-time day with no real work period is refused, because the only way to empty a
/// day is the explicit removal (W-a).
///
/// <para>Expected values come from the addendum (the AC table), never from what the code returns. Docker-gated:
/// <b>CI-pending</b>, never run locally. "Base" below means <c>728f4702</c>, where the <c>cleared</c> member is
/// simply ignored: no <c>UnmappedMemberHandling</c> is set, so a request that carries it answers whatever its posted
/// cells answer and removes nothing.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SkemaCellClear")]
public sealed class SkemaCellClearTests : SkemaCellClearTestBase
{
    public SkemaCellClearTests(SkemaCellClearFixture fx) : base(fx) { }

    /// <summary>
    /// AC-CL-1. <c>SICK_DAY</c> 7,4 on Wednesday 10 June saved, then cleared: 200, <c>saved</c> 0; the row's
    /// <c>superseded_by_outbox_id</c> is the clear event's outbox id; no row is inserted (the table still holds the
    /// one row); the month GET serves no absence that day.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the row is never retired and the
    /// <c>AbsenceCleared</c> lookup (<c>Assert.Single</c>) trips first ("no elements").</para>
    /// </summary>
    [Fact]
    public async Task ClearAbsence_RowRetiredByTheClear_NothingInserted_MonthGetOmitsIt()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "SICK_DAY 7,4 Wednesday");

        var rsp = await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Wed10, "SICK_DAY") }));

        var body = await RefusalAsync(rsp, HttpStatusCode.OK);
        Assert.Equal(0, body.GetProperty("saved").GetInt32());
        var clear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        var row = Assert.Single(await AbsencesAsync(emp));
        Assert.Equal(clear.OutboxId, row.SupersededBy);
        var month = await MonthAsync(emp);
        Assert.Empty(month.GetProperty("absences").EnumerateArray());
    }

    /// <summary>
    /// AC-CL-2. A project entry (PRJ-1, 3,0 h) on Wednesday 10 June cleared: <c>GET /api/time-entries/{employeeId}</c>
    /// and the month GET both omit it, and the row is retired by the clear's outbox id.
    ///
    /// <para>Kind F. Red conditions: on base the entry stays current, so <c>Assert.Single</c> on the
    /// <c>TimeEntryCleared</c> event trips first.</para>
    /// </summary>
    [Fact]
    public async Task ClearEntry_RowRetired_TimeEntriesGetAndMonthGetOmitIt()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(entries: new[] { (Wed10, "PRJ-1", 3.0m) }), "PRJ-1 3,0 Wednesday");

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearEntries: new[] { (Wed10, "PRJ-1") })), HttpStatusCode.OK);

        var clear = Assert.Single(await OutboxAsync(emp, "TimeEntryCleared"));
        Assert.Equal(clear.OutboxId, Assert.Single(await EntriesAsync(emp)).SupersededBy);
        var rows = await GetJsonAsync(EmployeeClient(emp), $"/api/time-entries/{emp}");
        Assert.Empty(rows.EnumerateArray());
        Assert.Empty((await MonthAsync(emp)).GetProperty("entries").EnumerateArray());
    }

    /// <summary>
    /// AC-CL-3. Work time 08:00-15:24 on Thursday 11 June cleared: the <c>work_time_projection</c> row is KEPT, its
    /// <c>cleared_by_outbox_id</c> and <c>outbox_id</c> both the clear's outbox id; each of six readers shows no day,
    /// one assertion per site, each against its own non-vacuous "before": the month GET's <c>workTime</c>; the team
    /// overview's worked hours (its allocation-imbalance warning, which compares worked with allocated); the allocation
    /// breakdown's <c>worked</c>; the send's allocation gate (422 <c>allocation</c> before, 200 after); the year
    /// overview's worked hours; and the strand check (HR's end-date PUT, 409 before, 200 after, on a second employee).
    ///
    /// <para>Setup: the employee is the only expected workday of the month (employment window Thursday 11 June), covered
    /// by a SQL VACATION row (coverage reads absences and entries, not work time); the leader reads need a sent month, so a
    /// SUBMITTED period is inserted for each read and deleted before the next write (the save and the send refuse a
    /// locked month).</para>
    ///
    /// <para>Kind F. Red conditions: on base the day is never marked, so <c>Assert.Single</c> on the
    /// <c>WorkTimeCleared</c> event trips first. Mutations M-19-style, one reader's filter removed, trip that reader's
    /// assertion only.</para>
    /// </summary>
    [Fact]
    public async Task ClearWorkTimeDay_RowKeptAndMarked_EveryReaderOmitsTheDay()
    {
        var (emp, leader) = await SeedEmployeeUnderLeaderAsync();
        var client = EmployeeClient(emp, "HK");
        var leaderClient = ClientFor(leader, StatsTidRoles.LocalLeader, LeaderOrg, "HK");
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "08:00", "15:24") }), "work time Thursday", client);
        var registered = Assert.Single(await WorkAsync(emp));
        await SetEmploymentWindowAsync(emp, Thu11, Thu11);
        await InsertCoverageAbsenceAsync(emp, Thu11);

        async Task<(bool Warning, decimal Worked, decimal YearWorked, int MonthDays)> ReadersAsync()
        {
            await InsertPeriodAsync(emp, "SUBMITTED", LeaderOrg, "HK");
            var overview = await GetJsonAsync(leaderClient, "/api/approval/team-overview?year=2026&month=6");
            var row = overview.GetProperty("employees").EnumerateArray()
                .Single(r => r.GetProperty("employeeId").GetString() == emp);
            var breakdown = await GetJsonAsync(leaderClient, $"/api/approval/{emp}/allocation-breakdown?year=2026&month=6");
            await DeletePeriodsAsync(emp);
            var year = await GetJsonAsync(client, $"/api/balance/{emp}/year-overview?year=2026");
            var june = year.GetProperty("months").EnumerateArray().Single(m => m.GetProperty("month").GetInt32() == 6);
            var month = await MonthAsync(emp, client);
            return (row.GetProperty("hasWarning").GetBoolean(), breakdown.GetProperty("worked").GetDecimal(),
                june.GetProperty("workedHours").GetDecimal(), month.GetProperty("workTime").GetArrayLength());
        }

        // Before: every reader shows the 7,4 h day; the send gate refuses (worked 7,4, allocated 0).
        var before = await ReadersAsync();
        Assert.Equal((true, 7.4m, 7.4m, 1), before);
        var refused = await RefusalAsync(
            await client.PostAsJsonAsync("/api/approval/send", new { employeeId = emp, year = 2026, month = 6 }),
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal("allocation", refused.GetProperty("kind").GetString());

        await AssertStatusAsync(
            await PostSaveAsync(client, emp, Save(clearWorkTime: new[] { Thu11 })), HttpStatusCode.OK);

        // The row is kept and marked: cleared_by_outbox_id and outbox_id are both the clear's outbox id.
        var clear = Assert.Single(await OutboxAsync(emp, "WorkTimeCleared"));
        var kept = Assert.Single(await WorkAsync(emp));
        Assert.Equal(clear.OutboxId, kept.ClearedBy);
        Assert.Equal(clear.OutboxId, kept.OutboxId);
        Assert.True(clear.OutboxId > registered.OutboxId);

        // After: each reader, one assertion per site.
        var after = await ReadersAsync();
        Assert.Multiple(
            () => Assert.False(after.Warning, "team overview: the cleared day still counts as worked"),
            () => Assert.Equal(0m, after.Worked), // allocation breakdown
            () => Assert.Equal(0m, after.YearWorked), // year overview
            () => Assert.Equal(0, after.MonthDays)); // month GET
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/api/approval/send", new { employeeId = emp, year = 2026, month = 6 }),
            HttpStatusCode.OK, "the send gate after the clear");

        // The strand check, on a second employee: HR's end-date PUT to Wednesday 10 June is refused while Thursday's work
        // time stands and accepted once it is cleared.
        var other = await SeedEmployeeAsync();
        await SaveOkAsync(other, Save(workTime: new[] { (Thu11, "08:00", "15:24") }), "work time Thursday (strand)");
        var hr = ClientFor("hr_s148_cl", StatsTidRoles.LocalHR, Org);
        await AssertStatusAsync(await PutEndDateAsync(hr, other, Wed10), HttpStatusCode.Conflict, "end date before a stored work-time day");
        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(other), other, Save(clearWorkTime: new[] { Thu11 })), HttpStatusCode.OK);
        await AssertStatusAsync(await PutEndDateAsync(hr, other, Wed10), HttpStatusCode.OK, "end date after the clear");
    }

    private static Task<HttpResponseMessage> PutEndDateAsync(HttpClient hr, string emp, DateOnly end)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{emp}/employment-end-date")
        {
            Content = JsonContent.Create(new { employmentEndDate = end }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\""); // a freshly seeded users row is version 1
        return hr.SendAsync(req);
    }

    /// <summary>
    /// AC-CL-4. Each kind of clear enqueues exactly one event on <c>employee-{id}</c> carrying the caller's
    /// <c>actorId</c> and <c>actorRole</c>, the cell identity, <c>clearedEventId</c> = the retired registration's event
    /// id and <c>clearedHours</c>; its outbox id is larger than the registration's. An absence that draws on no balance
    /// (<c>SICK_DAY</c>) carries no <c>clearedFeriedage</c> (event JSON omits nulls). Cells: <c>SICK_DAY</c> 7,4 Monday 8
    /// June, PRJ-1 3,0 Tuesday 9 June, work time 08:00-15:24 Wednesday 10 June.
    ///
    /// <para>Kind F. Red conditions: on base no clear event exists, so the first <c>Assert.Single</c> trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearEachKind_OneEventOnTheEmployeeStream_WithActorAndClearedEventId()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(
            absences: new[] { (Mon8, "SICK_DAY", 7.4m) },
            entries: new[] { (Tue9, "PRJ-1", 3.0m) },
            workTime: new[] { (Wed10, "08:00", "15:24") }), "the three cells");
        var absence = Assert.Single(await AbsencesAsync(emp));
        var entry = Assert.Single(await EntriesAsync(emp));
        var work = Assert.Single(await WorkAsync(emp));
        var workRegistration = Assert.Single(await OutboxAsync(emp, "WorkTimeRegistered"));

        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            clearEntries: new[] { (Tue9, "PRJ-1") },
            clearAbsences: new[] { (Mon8, "SICK_DAY") },
            clearWorkTime: new[] { Wed10 })), HttpStatusCode.OK);

        var absenceClear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        var entryClear = Assert.Single(await OutboxAsync(emp, "TimeEntryCleared"));
        var workClear = Assert.Single(await OutboxAsync(emp, "WorkTimeCleared"));
        Assert.Multiple(
            () => Assert.Equal(emp, absenceClear.ActorId),
            () => Assert.Equal(StatsTidRoles.Employee, absenceClear.ActorRole),
            () => Assert.Equal("SICK_DAY", absenceClear.Payload.GetProperty("absenceType").GetString()),
            () => Assert.Equal(D(Mon8), absenceClear.Payload.GetProperty("date").GetString()),
            () => Assert.Equal(absence.EventId, absenceClear.Payload.GetProperty("clearedEventId").GetGuid()),
            () => Assert.Equal(7.4m, absenceClear.Payload.GetProperty("clearedHours").GetDecimal()),
            () => Assert.False(absenceClear.Payload.TryGetProperty("clearedFeriedage", out _)),
            () => Assert.True(absenceClear.OutboxId > absence.OutboxId),
            () => Assert.Equal(emp, entryClear.ActorId),
            () => Assert.Equal(StatsTidRoles.Employee, entryClear.ActorRole),
            () => Assert.Equal("PRJ-1", entryClear.Payload.GetProperty("taskId").GetString()),
            () => Assert.Equal(D(Tue9), entryClear.Payload.GetProperty("date").GetString()),
            () => Assert.Equal(entry.EventId, entryClear.Payload.GetProperty("clearedEventId").GetGuid()),
            () => Assert.Equal(3.0m, entryClear.Payload.GetProperty("clearedHours").GetDecimal()),
            () => Assert.True(entryClear.OutboxId > entry.OutboxId),
            () => Assert.Equal(emp, workClear.ActorId),
            () => Assert.Equal(StatsTidRoles.Employee, workClear.ActorRole),
            () => Assert.Equal(D(Wed10), workClear.Payload.GetProperty("date").GetString()),
            () => Assert.Equal(workRegistration.EventId, workClear.Payload.GetProperty("clearedEventId").GetGuid()),
            () => Assert.Equal(7.4m, workClear.Payload.GetProperty("clearedHours").GetDecimal()),
            () => Assert.True(workClear.OutboxId > work.OutboxId));
    }

    /// <summary>
    /// AC-CL-5. Each clear writes one <c>audit_projection</c> row in the save's transaction: TENANT_TARGETED, the
    /// employee's org (STY01), resource = the employee id, and the catalog's details shape
    /// (<c>{employeeId, date, taskId | absenceType, clearedEventId, clearedHours}</c>). A save refused later in the
    /// same request leaves none: on a second employee, one request clears a sick day on Monday 8 June and posts a
    /// <c>VACATION</c> 7,4 on Wednesday 10 June, which already holds 7,4 h of work (the paid-absence bound's 422); no
    /// <c>AbsenceCleared</c> audit row exists for that employee and the sick day is still current.
    ///
    /// <para>Kind F. Red conditions: on base no audit row is written, so the per-kind row count assertion
    /// (<c>Assert.Equal(1L, ...)</c>, 0 on base) trips first.</para>
    /// </summary>
    [Fact]
    public async Task ClearEachKind_OneAuditRow_InTheSaveTransaction()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(
            absences: new[] { (Mon8, "SICK_DAY", 7.4m) },
            entries: new[] { (Tue9, "PRJ-1", 3.0m) },
            workTime: new[] { (Wed10, "08:00", "15:24") }), "the three cells");
        var absence = Assert.Single(await AbsencesAsync(emp));
        var entry = Assert.Single(await EntriesAsync(emp));
        var workRegistration = Assert.Single(await OutboxAsync(emp, "WorkTimeRegistered"));
        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            clearEntries: new[] { (Tue9, "PRJ-1") },
            clearAbsences: new[] { (Mon8, "SICK_DAY") },
            clearWorkTime: new[] { Wed10 })), HttpStatusCode.OK);

        foreach (var type in new[] { "AbsenceCleared", "TimeEntryCleared", "WorkTimeCleared" })
            Assert.Equal(1L, await AuditCountAsync(emp, type));

        async Task<(string Scope, string? OrgId, JsonElement Details)> AuditAsync(string type)
        {
            await using var conn = new NpgsqlConnection(Fx.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT visibility_scope, target_org_id, details::text FROM audit_projection WHERE event_type = @t AND target_resource_id = @e", conn);
            cmd.Parameters.AddWithValue("t", type);
            cmd.Parameters.AddWithValue("e", emp);
            await using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), JsonDocument.Parse(r.GetString(2)).RootElement.Clone());
        }

        var a = await AuditAsync("AbsenceCleared");
        var e = await AuditAsync("TimeEntryCleared");
        var w = await AuditAsync("WorkTimeCleared");
        Assert.Multiple(
            () => Assert.Equal(("TENANT_TARGETED", Org), (a.Scope, a.OrgId)),
            () => Assert.Equal(("TENANT_TARGETED", Org), (e.Scope, e.OrgId)),
            () => Assert.Equal(("TENANT_TARGETED", Org), (w.Scope, w.OrgId)),
            () => Assert.Equal(emp, a.Details.GetProperty("employeeId").GetString()),
            () => Assert.Equal(D(Mon8), a.Details.GetProperty("date").GetString()),
            () => Assert.Equal("SICK_DAY", a.Details.GetProperty("absenceType").GetString()),
            () => Assert.Equal(absence.EventId, a.Details.GetProperty("clearedEventId").GetGuid()),
            () => Assert.Equal(7.4m, a.Details.GetProperty("clearedHours").GetDecimal()),
            () => Assert.Equal("PRJ-1", e.Details.GetProperty("taskId").GetString()),
            () => Assert.Equal(entry.EventId, e.Details.GetProperty("clearedEventId").GetGuid()),
            () => Assert.Equal(3.0m, e.Details.GetProperty("clearedHours").GetDecimal()),
            () => Assert.Equal(D(Wed10), w.Details.GetProperty("date").GetString()),
            () => Assert.Equal(workRegistration.EventId, w.Details.GetProperty("clearedEventId").GetGuid()),
            () => Assert.Equal(7.4m, w.Details.GetProperty("clearedHours").GetDecimal()));

        // A save refused after its clear was planned leaves no audit row.
        var other = await SeedEmployeeAsync();
        await SaveOkAsync(other, Save(absences: new[] { (Mon8, "SICK_DAY", 7.4m) }), "sick day Monday");
        await SaveOkAsync(other, Save(workTime: new[] { (Wed10, "08:00", "15:24") }), "work Wednesday");
        var rsp = await PostSaveAsync(EmployeeClient(other), other, Save(
            absences: new[] { (Wed10, "VACATION", 7.4m) },
            clearAbsences: new[] { (Mon8, "SICK_DAY") }));
        await AssertStatusAsync(rsp, HttpStatusCode.UnprocessableEntity, "the bound refuses the vacation on a day that holds 7,4 h of work");
        Assert.Equal(0L, await AuditCountAsync(other, "AbsenceCleared"));
        Assert.Empty(await OutboxAsync(other, "AbsenceCleared"));
        Assert.Null(Assert.Single(await AbsencesAsync(other)).SupersededBy);
    }

    /// <summary>
    /// AC-CL-6. Clearing a cell with no current Skema row answers 200 and records nothing: no event, no audit row, no
    /// balance event. The employee holds one <c>VACATION</c> day (Friday 12 June); the request clears an absence
    /// (<c>VACATION</c>), an entry (PRJ-1) and a work-time day on Monday 8 June, none of which exist.
    ///
    /// <para>Kind R (a kept behaviour: the exact counts below must not move). Red conditions: none on base, where the
    /// cleared member is ignored and the counts are unchanged; it fails if a clear of an empty cell records an event, an
    /// audit row or a balance movement (a count or <c>used</c> changes).</para>
    /// </summary>
    [Fact]
    public async Task ClearEmptyCell_NoEvent_NoAuditRow_NoBalanceChange()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        var eventsBefore = (await OutboxAsync(emp)).Count;
        var balanceEventsBefore = (await OutboxAsync(emp, "EntitlementBalanceAdjusted")).Count;
        var usedBefore = await UsedAsync(emp, "VACATION");

        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            clearEntries: new[] { (Mon8, "PRJ-1") },
            clearAbsences: new[] { (Mon8, "VACATION") },
            clearWorkTime: new[] { Mon8 })), HttpStatusCode.OK);

        Assert.Equal(eventsBefore, (await OutboxAsync(emp)).Count);
        Assert.Equal(balanceEventsBefore, (await OutboxAsync(emp, "EntitlementBalanceAdjusted")).Count);
        Assert.Equal(usedBefore, await UsedAsync(emp, "VACATION"));
        foreach (var type in new[] { "AbsenceCleared", "TimeEntryCleared", "WorkTimeCleared" })
            Assert.Equal(0L, await AuditCountAsync(emp, type));
        Assert.Null(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
    }

    /// <summary>
    /// AC-CL-7 (2). With the only registrations after Tuesday 9 June cleared (an absence and an entry on Wednesday 10
    /// June, work time on Thursday 11 June), HR's end-date PUT to Tuesday 9 June answers 200, not the stranded 409. The
    /// PUT is first shown to answer 409 while they stand.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the rows stay current and the final PUT answers
    /// 409 (<c>EmploymentWindowStrandCheck</c> counts them).</para>
    /// </summary>
    [Fact]
    public async Task ClearedCells_NotCountedByTheStrandCheck_EndDatePut200()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(
            absences: new[] { (Wed10, "SICK_DAY", 4.4m) },
            entries: new[] { (Wed10, "PRJ-1", 3.0m) }), "Wednesday");
        await SaveOkAsync(emp, Save(workTime: new[] { (Thu11, "08:00", "15:24") }), "Thursday");
        var hr = ClientFor("hr_s148_cl", StatsTidRoles.LocalHR, Org);
        await AssertStatusAsync(await PutEndDateAsync(hr, emp, Tue9), HttpStatusCode.Conflict, "end date before the registrations");

        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            clearEntries: new[] { (Wed10, "PRJ-1") },
            clearAbsences: new[] { (Wed10, "SICK_DAY") },
            clearWorkTime: new[] { Thu11 })), HttpStatusCode.OK);

        await AssertStatusAsync(await PutEndDateAsync(hr, emp, Tue9), HttpStatusCode.OK, "end date after the clears");
    }

    /// <summary>
    /// AC-CL-7 (3), W-a. A posted work-time day with no positive-duration interval is refused: 400
    /// <c>skema_work_time_day_empty</c> naming its date, no row and no event. Both shapes: an empty <c>intervals</c>
    /// array, and one interval of zero length (08:00-08:00). The day is also refused when it is posted over a stored
    /// day: the stored 08:00-15:24 stands untouched.
    ///
    /// <para>Kind F. Red conditions: on base the empty day is stored (200, a row with no intervals), so the first
    /// status assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task EmptyWorkTimeDay_Returns400_NothingWritten()
    {
        var emp = await SeedEmployeeAsync();
        var client = EmployeeClient(emp);

        async Task<JsonElement> PostDayAsync(object intervals) => await RefusalAsync(
            await client.PostAsJsonAsync($"/api/skema/{emp}/save", new
            {
                year = 2026,
                month = 6,
                workTime = new[] { new { date = D(Wed10), intervals } },
            }), HttpStatusCode.BadRequest);

        var empty = await PostDayAsync(Array.Empty<object>());
        var zeroLength = await PostDayAsync(new[] { new { start = "08:00", end = "08:00" } });

        Assert.Equal("skema_work_time_day_empty", empty.GetProperty("error").GetString());
        Assert.Equal(D(Wed10), empty.GetProperty("date").GetString());
        Assert.Equal("skema_work_time_day_empty", zeroLength.GetProperty("error").GetString());
        Assert.Empty(await WorkAsync(emp));
        Assert.Empty(await OutboxAsync(emp));

        await SaveOkAsync(emp, Save(workTime: new[] { (Wed10, "08:00", "15:24") }), "work time Wednesday");
        var overStored = await PostDayAsync(Array.Empty<object>());
        Assert.Equal("skema_work_time_day_empty", overStored.GetProperty("error").GetString());
        var stored = Assert.Single(await WorkAsync(emp));
        Assert.Equal(("08:00-15:24", (long?)null), (stored.Intervals, stored.ClearedBy));
        Assert.Single(await OutboxAsync(emp, "WorkTimeRegistered"));
    }

    /// <summary>
    /// AC-CL-8. A clear in a month at <c>EMPLOYEE_APPROVED</c> or <c>APPROVED</c> answers the save's 409 and the row
    /// stays current; no clear event is written. (The pre-transaction month lock refuses any request to the month.)
    ///
    /// <para>Kind R. Red conditions: none on base; fails if a clear is allowed past the lock (200, the row retired, an
    /// <c>AbsenceCleared</c> event).</para>
    /// </summary>
    [Theory]
    [InlineData("EMPLOYEE_APPROVED")]
    [InlineData("APPROVED")]
    public async Task ClearInSentOrApprovedMonth_409_RowStaysCurrent(string status)
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "sick day Wednesday");
        await InsertPeriodAsync(emp, status);

        var rsp = await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Wed10, "SICK_DAY") }));

        await AssertStatusAsync(rsp, HttpStatusCode.Conflict);
        Assert.Null(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Empty(await OutboxAsync(emp, "AbsenceCleared"));
    }

    /// <summary>
    /// AC-CL-10 (Theory over the three cleared arrays). A June 2026 save clearing Wednesday 1 July 2026 answers 400
    /// <c>skema_cell_date_out_of_range</c> in every case: one error code for every cleared array (an entry, an absence, a
    /// work-time day), so the posted work-time sibling's <c>work_time_date_out_of_range</c> is not used. Nothing changes:
    /// the July cell, saved first through a July save, is still current and no clear event exists.
    ///
    /// <para>Kind F. Red conditions: on base the cleared member is ignored, so the save answers 200 and the status
    /// assertion trips.</para>
    /// </summary>
    [Theory]
    [InlineData("entry")]
    [InlineData("absence")]
    [InlineData("workTime")]
    public async Task ClearOutsideTheRequestedMonth_400_NothingChanges(string kind)
    {
        var emp = await SeedEmployeeAsync();
        var july = kind switch
        {
            "entry" => Save(entries: new[] { (Wed1July, "PRJ-1", 3.0m) }, month: 7),
            "absence" => Save(absences: new[] { (Wed1July, "SICK_DAY", 7.4m) }, month: 7),
            _ => Save(workTime: new[] { (Wed1July, "08:00", "15:24") }, month: 7),
        };
        await SaveOkAsync(emp, july, $"a {kind} on 1 July");
        var eventsBefore = (await OutboxAsync(emp)).Count;
        var request = kind switch
        {
            "entry" => Save(clearEntries: new[] { (Wed1July, "PRJ-1") }),
            "absence" => Save(clearAbsences: new[] { (Wed1July, "SICK_DAY") }),
            _ => Save(clearWorkTime: new[] { Wed1July }),
        };

        var body = await RefusalAsync(await PostSaveAsync(EmployeeClient(emp), emp, request), HttpStatusCode.BadRequest);

        Assert.Equal("skema_cell_date_out_of_range", body.GetProperty("error").GetString());
        Assert.Equal(eventsBefore, (await OutboxAsync(emp)).Count);
        Assert.Empty(await OutboxAsync(emp, "AbsenceCleared"));
        Assert.Empty(await OutboxAsync(emp, "TimeEntryCleared"));
        Assert.Empty(await OutboxAsync(emp, "WorkTimeCleared"));
        Assert.All(await AbsencesAsync(emp), r => Assert.Null(r.SupersededBy));
        Assert.All(await EntriesAsync(emp), r => Assert.Null(r.SupersededBy));
        Assert.All(await WorkAsync(emp), r => Assert.Null(r.ClearedBy));
    }

    /// <summary>
    /// AC-CL-25. The shapes of a request the server refuses, each with nothing written (the seeded sick day stays
    /// current and the employee's event count does not move): a cell both posted and cleared (400
    /// <c>skema_cell_cleared_and_posted</c>); cleared twice (400 <c>skema_cell_cleared_twice</c>); a cleared entry with a
    /// blank project code (400 <c>skema_entry_project_code_missing</c>); a cleared absence with an unknown code (422
    /// <c>absence_type_unknown</c>).
    ///
    /// <para>Kind F. Red conditions: on base the cleared member is ignored, so each request answers 200 and the status
    /// assertion trips.</para>
    /// </summary>
    [Theory]
    [InlineData("posted-and-cleared", 400, "skema_cell_cleared_and_posted")]
    [InlineData("cleared-twice", 400, "skema_cell_cleared_twice")]
    [InlineData("blank-project", 400, "skema_entry_project_code_missing")]
    [InlineData("unknown-absence", 422, "absence_type_unknown")]
    public async Task ClearShapeRefusals_NothingWritten(string shape, int status, string error)
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "sick day Wednesday");
        var eventsBefore = (await OutboxAsync(emp)).Count;
        var request = shape switch
        {
            "posted-and-cleared" => Save(
                absences: new[] { (Wed10, "SICK_DAY", 7.4m) }, clearAbsences: new[] { (Wed10, "SICK_DAY") }),
            "cleared-twice" => Save(clearAbsences: new[] { (Wed10, "SICK_DAY"), (Wed10, "SICK_DAY") }),
            "blank-project" => Save(clearEntries: new[] { (Wed10, "") }),
            _ => Save(clearAbsences: new[] { (Wed10, "NOT_A_CODE") }),
        };

        var body = await RefusalAsync(await PostSaveAsync(EmployeeClient(emp), emp, request), (HttpStatusCode)status);

        Assert.Equal(error, body.GetProperty("error").GetString());
        Assert.Equal(eventsBefore, (await OutboxAsync(emp)).Count);
        Assert.Null(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
    }
}
