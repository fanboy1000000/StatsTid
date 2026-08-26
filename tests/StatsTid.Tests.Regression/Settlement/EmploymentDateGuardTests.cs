using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S136 / TASK-13604 (ADR-040 D1/D3) — the employment-date endpoint guards, end-to-end:
///
/// <list type="bullet">
///   <item><description>cross-field inverted-window 422 on BOTH PUTs (end &lt; start refused;
///     before S136 nothing anywhere checked the two dates against each other);</description></item>
///   <item><description>the D1 re-hire guard: start moved past a CLOSED spell's recorded end →
///     409, with ZERO <c>vacation_settlements</c> rows in the fixture — proving
///     settlement-INDEPENDENCE (deliberately unlike the R7a/R13 guard);</description></item>
///   <item><description>same-spell corrections still 200 — including on a DEACTIVATED leaver,
///     the terminated-inclusive positive for the S70 R9c allowlist EXTENSION (the start-date
///     PUT's lifecycle upgrade: without it the re-hire guard was unreachable);</description></item>
///   <item><description>the D3 strand guard: a window edit that would orphan existing
///     <c>time_entries_projection</c>/<c>absences_projection</c>/<c>work_time_projection</c>
///     rows (the third arm is the S136 Step-5a Reviewer-W1 fix) → 409 with the stranded-month
///     pointer list (mirrors the R7a/R13 409 contract), non-stranding edits and
///     R1(c) reactivation pass untouched;</description></item>
///   <item><description>the start-date self-target 403 for ALL actors (active AND terminated
///     self — the S70 Step-7a W1 symmetry, Reviewer-W3), nothing mutated;</description></item>
///   <item><description>the S136 lock regime: the start-date PUT parks on the ADR-032 D4
///     employee advisory lock and evaluates the strand guard IN-LOCK (mirrors the S70 R12
///     race pin's choreography).</description></item>
/// </list>
///
/// <para>Fixture/JWT conventions mirror <see cref="EmploymentEndDateLifecycleTests"/> (same WAF
/// harness, token minting, real-clock ±2-year date anchors so the Copenhagen past/future
/// classification is offset-immune). Projection rows are seeded directly (the SiblingRead
/// pattern) — the strand guard reads the projections, not the event log.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentDateGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string CoveringOrg = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private long _outboxSeq = 910_000;

    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly PastDate = TodayUtc.AddYears(-2);
    private static readonly DateOnly FutureDate = TodayUtc.AddYears(2);

    /// <summary>Mid-month anchor N months back — day 10, so AddMonths arithmetic never crosses
    /// a month boundary and <c>yyyy-MM</c> grouping is deterministic.</summary>
    private static DateOnly MonthsBack(int months)
    {
        var d = TodayUtc.AddMonths(-months);
        return new DateOnly(d.Year, d.Month, 10);
    }

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cross-field inverted-window 422 — BOTH PUTs, nothing mutated.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Start PUT: proposed start after a recorded UNPASSED (future) end → 422 (the
    /// open-spell inversion — the closed-spell case is the re-hire 409 below). Fail-closed:
    /// nothing mutated.</summary>
    [Fact]
    public async Task StartDatePut_StartAfterUnpassedEnd_Returns422_NothingMutated()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, FutureDate, ifMatch: "\"1\"")); // v2, active

        var rsp = await PutStartDateAsync(HrClient(), employeeId, FutureDate.AddDays(1), ifMatch: "\"2\"");

        Assert.Equal((HttpStatusCode)422, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("must not be after", body.GetProperty("error").GetString());

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.StartDate);
        Assert.Equal(FutureDate, tuple.EndDate);
        Assert.Equal(2L, tuple.Version);
    }

    /// <summary>End PUT: proposed end before the recorded start → 422. The proposed end is in
    /// the PAST, so is_active staying TRUE also proves the guard fired BEFORE the lifecycle
    /// writer (placement pin — no side effects on refusal).</summary>
    [Fact]
    public async Task EndDatePut_EndBeforeRecordedStart_Returns422_NothingMutated()
    {
        var employeeId = await SeedEmployeeAsync();
        var start = TodayUtc.AddYears(-1);
        await SetStartDateDirectAsync(employeeId, start); // version stays 1

        var rsp = await PutEndDateAsync(HrClient(), employeeId, start.AddDays(-1), ifMatch: "\"1\"");

        Assert.Equal((HttpStatusCode)422, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("must not be before", body.GetProperty("error").GetString());

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive); // the past-dated end never reached the R1 flip
        Assert.Equal(1L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-040 D1 — the re-hire guard (409, settlement-INDEPENDENT) and the
    // same-spell corrections that stay legal.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The D1 headline: a CLOSED spell (end date passed → the lifecycle already
    /// deactivated the leaver) and a proposed start AFTER that end → 409, with the fixture
    /// holding ZERO settlement rows — the settlement-independence proof (the R7a/R13 guard
    /// would have passed this edit). Fail-closed: nothing mutated.</summary>
    [Fact]
    public async Task StartDatePut_PastClosedSpellEnd_NoSettlementRows_Returns409Rehire()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, PastDate, ifMatch: "\"1\"")); // flip, v2

        Assert.Equal(0L, await CountSettlementRowsAsync(employeeId)); // settlement-independence evidence

        var rsp = await PutStartDateAsync(HrClient(), employeeId, PastDate.AddDays(1), ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("re-hire", body.GetProperty("error").GetString());
        Assert.Contains("ADR-040 D1", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("hint").GetString()));

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.StartDate);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.False(tuple.IsActive);
        Assert.Equal(2L, tuple.Version);
    }

    /// <summary>Same-spell correction on the SAME closed spell → 200. This is simultaneously the
    /// terminated-inclusive POSITIVE for the R9c allowlist extension: HR (a different actor)
    /// successfully edits a DEACTIVATED leaver's start date — the pre-S136 active-only rails
    /// dead-ended at 403/404 here. The start write touches no lifecycle state.</summary>
    [Fact]
    public async Task StartDatePut_SameSpellCorrection_OnDeactivatedLeaver_Returns200()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, PastDate, ifMatch: "\"1\"")); // flip, v2

        var newStart = PastDate.AddYears(-1); // ≤ recorded end — same spell
        var rsp = await PutStartDateAsync(HrClient(), employeeId, newStart, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"3\"", rsp.Headers.ETag!.Tag);

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Equal(newStart, tuple.StartDate);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.False(tuple.IsActive); // untouched — the start PUT carries no lifecycle choreography
        Assert.Equal(3L, tuple.Version);
    }

    /// <summary>Sanity on the upgraded rails: an ACTIVE employee with no end date (D2 unbounded)
    /// sets a start date exactly as before the S136 upgrade.</summary>
    [Fact]
    public async Task StartDatePut_ActiveEmployee_NoEndDate_Returns200()
    {
        var employeeId = await SeedEmployeeAsync();

        var rsp = await PutStartDateAsync(HrClient(), employeeId, TodayUtc.AddYears(-1), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Equal(TodayUtc.AddYears(-1), tuple.StartDate);
        Assert.Equal(2L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-040 D3 — the strand guard (409 + month pointers) and its pass-through.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Start PUT stranding existing data → 409 with the structured month pointer list
    /// (mirrors the R7a/R13 contract: error prose + pointer array + hint): two time entries in
    /// one month, one absence in another, proposed start after both → both months listed,
    /// ascending, with per-type counts. Fail-closed: nothing mutated.</summary>
    [Fact]
    public async Task StartDatePut_StrandingRegistrations_Returns409_WithMonthPointerList()
    {
        var employeeId = await SeedEmployeeAsync();
        var entryMonth = MonthsBack(4);
        var absenceMonth = MonthsBack(3);
        await SeedTimeEntryAsync(employeeId, entryMonth);
        await SeedTimeEntryAsync(employeeId, entryMonth.AddDays(1));
        await SeedAbsenceAsync(employeeId, absenceMonth);

        var proposedStart = MonthsBack(1);
        var rsp = await PutStartDateAsync(HrClient(), employeeId, proposedStart, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("ADR-040 D3", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("hint").GetString()));

        var months = body.GetProperty("strandedMonths").EnumerateArray().ToArray();
        Assert.Equal(2, months.Length);
        Assert.Equal(entryMonth.ToString("yyyy-MM"), months[0].GetProperty("month").GetString());
        Assert.Equal(2L, months[0].GetProperty("timeEntryCount").GetInt64());
        Assert.Equal(0L, months[0].GetProperty("absenceCount").GetInt64());
        Assert.Equal(absenceMonth.ToString("yyyy-MM"), months[1].GetProperty("month").GetString());
        Assert.Equal(0L, months[1].GetProperty("timeEntryCount").GetInt64());
        Assert.Equal(1L, months[1].GetProperty("absenceCount").GetInt64());

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.StartDate);
        Assert.Equal(1L, tuple.Version);
    }

    /// <summary>End PUT stranding an existing absence → 409 with the month pointer. The
    /// proposed end is in the PAST, so is_active staying TRUE also pins the guard's placement
    /// strictly BEFORE the lifecycle writer.</summary>
    [Fact]
    public async Task EndDatePut_StrandingAbsence_Returns409_NothingMutated()
    {
        var employeeId = await SeedEmployeeAsync();
        var absenceDate = MonthsBack(2);
        await SeedAbsenceAsync(employeeId, absenceDate);

        var rsp = await PutEndDateAsync(HrClient(), employeeId, MonthsBack(4), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("ADR-040 D3", body.GetProperty("error").GetString());
        var months = body.GetProperty("strandedMonths").EnumerateArray().ToArray();
        Assert.Single(months);
        Assert.Equal(absenceDate.ToString("yyyy-MM"), months[0].GetProperty("month").GetString());
        Assert.Equal(1L, months[0].GetProperty("absenceCount").GetInt64());

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive); // no flip — the guard ran before ApplyAsync
        Assert.Equal(1L, tuple.Version);
    }

    /// <summary>S136 Step-5a (Reviewer WARNING 1): the strand guard sees ALL THREE registration
    /// families the write side gates — a window edit stranding ONLY <c>work_time_projection</c>
    /// rows → 409 with the <c>workTimeCount</c> pointer. RED-on-old: pre-fix the guard's UNION
    /// had no work-time arm, so this exact edit returned 200 and orphaned the work-time day the
    /// Skema save could never have written (its own window gate covers the WorkTime array).</summary>
    [Fact]
    public async Task EndDatePut_StrandingOnlyWorkTime_Returns409_WithWorkTimeCount()
    {
        var employeeId = await SeedEmployeeAsync();
        var workDay = MonthsBack(2);
        await SeedWorkTimeAsync(employeeId, workDay);

        var rsp = await PutEndDateAsync(HrClient(), employeeId, MonthsBack(4), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("ADR-040 D3", body.GetProperty("error").GetString());
        var months = body.GetProperty("strandedMonths").EnumerateArray().ToArray();
        Assert.Single(months);
        Assert.Equal(workDay.ToString("yyyy-MM"), months[0].GetProperty("month").GetString());
        Assert.Equal(1L, months[0].GetProperty("workTimeCount").GetInt64());
        Assert.Equal(0L, months[0].GetProperty("timeEntryCount").GetInt64());
        Assert.Equal(0L, months[0].GetProperty("absenceCount").GetInt64());

        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive); // fail-closed, before the lifecycle writer
        Assert.Equal(1L, tuple.Version);
    }

    /// <summary>Non-stranding pass-through: data INSIDE the proposed window → 200 (the guard
    /// refuses stranding, not windows).</summary>
    [Fact]
    public async Task EndDatePut_NonStranding_BoundedWindow_Returns200()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedTimeEntryAsync(employeeId, MonthsBack(2));

        var rsp = await PutEndDateAsync(HrClient(), employeeId, FutureDate, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Equal(FutureDate, tuple.EndDate);
        Assert.Equal(2L, tuple.Version);
    }

    /// <summary>R1(c) reactivation still works through its existing rails with the new guards in
    /// play: a closed spell with IN-WINDOW registrations, end date cleared → 200, reactivated
    /// (clearing widens the window — nothing can be stranded on the cleared side, D2).</summary>
    [Fact]
    public async Task EndDateClear_Reactivation_WithInWindowRegistrations_StillWorks()
    {
        var employeeId = await SeedEmployeeAsync();
        await SetStartDateDirectAsync(employeeId, PastDate.AddYears(-2)); // version stays 1
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, PastDate, ifMatch: "\"1\"")); // flip, v2
        await SeedTimeEntryAsync(employeeId, PastDate.AddMonths(-1)); // inside [start, end]

        var rsp = await PutEndDateAsync(HrClient(), employeeId, null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive); // R1(c) provenance-guarded reactivation, untouched by S136
        Assert.Equal(3L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Self-target 403 on the start-date PUT — ALL actors, BEFORE any DB work
    // (S70 Step-7a W1 symmetry; Reviewer-W3).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>An ACTIVE in-scope HR actor PUTting its OWN start date → 403, nothing mutated
    /// (deterministic, fail-closed — actor-state-independent). The GET stays self-readable.</summary>
    [Fact]
    public async Task StartDatePut_SelfTarget_ActiveHrActor_Returns403_NothingMutated()
    {
        var hrActor = await SeedEmployeeAsync();
        var selfClient = ClientWith(HrToken(hrActor, CoveringOrg));

        var rsp = await PutStartDateAsync(selfClient, hrActor, TodayUtc.AddYears(-1), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("second administrator", body.GetProperty("reason").GetString());

        var tuple = await ReadDatesTupleAsync(hrActor);
        Assert.Null(tuple.StartDate);
        Assert.Equal(1L, tuple.Version);
    }

    /// <summary>The W1-class choreography on the START-date surface: a lifecycle-deactivated HR
    /// actor's STILL-VALID JWT rewrites its own employment-start history — the falsifiable-
    /// history vector (no reactivation lever here; the RECORD is the asset). Must 403 with
    /// nothing mutated.</summary>
    [Fact]
    public async Task StartDatePut_SelfTarget_TerminatedHrActor_Returns403_NothingMutated()
    {
        var hrActor = await SeedEmployeeAsync();
        // A SECOND administrator performs the legitimate departure (flip, v2, inactive).
        await AssertOk(PutEndDateAsync(HrClient(), hrActor, PastDate, ifMatch: "\"1\""));

        var selfClient = ClientWith(HrToken(hrActor, CoveringOrg)); // the still-valid own JWT
        var rsp = await PutStartDateAsync(selfClient, hrActor, PastDate.AddYears(-1), ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("second administrator", body.GetProperty("reason").GetString());

        var tuple = await ReadDatesTupleAsync(hrActor);
        Assert.Null(tuple.StartDate);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.False(tuple.IsActive);
        Assert.Equal(2L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // The S136 lock regime — the start-date PUT acquires the ADR-032 D4 employee
    // advisory lock FIRST and evaluates the strand guard IN-LOCK (mirrors the S70
    // R12 race pin's choreography on the end-date PUT).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>While a foreign tx holds the employee advisory lock, the start-date PUT parks
    /// BEFORE its guards. A time entry committed during that window — dated before the proposed
    /// start — MUST be seen by the in-lock strand guard → 409. Had the endpoint evaluated the
    /// guard before acquiring the lock (or never taken it), it would have seen empty projections
    /// and returned 200 with freshly-stranded data.</summary>
    [Fact]
    public async Task StartDatePut_LockHeld_StrandGuardSeesRowCommittedWhileBlocked_Yields409()
    {
        var employeeId = await SeedEmployeeAsync();

        await using var lockConn = new NpgsqlConnection(_harness.ConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await lockConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", lockConn, lockTx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", employeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        // Fire the PUT — it must park on the advisory lock (NOT complete).
        var putTask = PutStartDateAsync(HrClient(), employeeId, MonthsBack(1), ifMatch: "\"1\"");
        await Task.Delay(1500);
        Assert.False(putTask.IsCompleted,
            "start-date PUT completed while the employee advisory lock was held by another tx — " +
            "the endpoint is not acquiring the ADR-032 D4 lock (the S136 lock regime).");

        // Commit a registration OUTSIDE the proposed window while the PUT is blocked.
        await SeedTimeEntryAsync(employeeId, MonthsBack(3));

        // Release the lock — the PUT resumes and evaluates the strand guard IN-LOCK.
        await lockTx.RollbackAsync();

        var rsp = await putTask;
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var tuple = await ReadDatesTupleAsync(employeeId);
        Assert.Null(tuple.StartDate); // fail-closed
        Assert.Equal(1L, tuple.Version);
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static string StartDateUrl(string employeeId) =>
        $"/api/admin/employees/{employeeId}/employment-start-date";

    private static string EndDateUrl(string employeeId) =>
        $"/api/admin/employees/{employeeId}/employment-end-date";

    private static async Task<HttpResponseMessage> PutStartDateAsync(
        HttpClient client, string employeeId, DateOnly? startDate, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, StartDateUrl(employeeId))
        {
            Content = JsonContent.Create(new { employmentStartDate = startDate }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PutEndDateAsync(
        HttpClient client, string employeeId, DateOnly? endDate, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, EndDateUrl(employeeId))
        {
            Content = JsonContent.Create(new { employmentEndDate = endDate }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task AssertOk(Task<HttpResponseMessage> call)
    {
        var rsp = await call;
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ─────────────────────────────── clients / tokens ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient HrClient() => ClientWith(HrToken("hr_s136_edg", CoveringOrg));

    private static string HrToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ─────────────────────────────── seeding / reads ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s136_edg_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Direct start-date write for fixture arrangement (bypasses the endpoint;
    /// deliberately does NOT bump version so If-Match arithmetic stays at the seed value).</summary>
    private async Task SetStartDateDirectAsync(string employeeId, DateOnly startDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @d, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("d", startDate);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Direct <c>time_entries_projection</c> seed (the SiblingReadMonthGateTests
    /// pattern) — the strand guard reads the projection tables.</summary>
    private async Task SeedTimeEntryAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, 7.4, 'NORMAL', 'AC', 'OK24', FALSE, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Direct <c>work_time_projection</c> seed — same pattern (S136 Step-5a
    /// Reviewer-W1: the guard's third UNION arm; latest-wins PK (employee_id, date)).</summary>
    private async Task SeedWorkTimeAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO work_time_projection
                (employee_id, date, intervals, manual_hours, occurred_at, outbox_id)
            VALUES
                (@emp, @date, '[]'::jsonb, 7.4, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Direct <c>absences_projection</c> seed — same pattern.</summary>
    private async Task SeedAbsenceAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, agreement_code, ok_version,
                 occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 'AC', 'OK24', NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> CountSettlementRowsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(DateOnly? StartDate, DateOnly? EndDate, bool IsActive, long Version)>
        ReadDatesTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_start_date, employment_end_date, is_active, version
            FROM users WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }
}
