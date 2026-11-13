using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Approval;
using Xunit;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S136 / TASK-13603 — the employment-window registration gate (ADR-040 D1–D3) on the registration
/// writer <c>POST /api/skema/{id}/save</c> (S148 R-20: the second writer, <c>POST /api/time-entries</c>,
/// is retired; its boundary-matrix and windowless facts are pinned by the Skema facts below).
///
/// <para><b>What is pinned here:</b></para>
/// <list type="bullet">
///   <item><b>The D1 boundary semantics</b>: <c>date == employment_start_date</c> and
///   <c>date == employment_end_date</c> are ACCEPTED (end inclusive — the last day employed,
///   the ADR-033/S70-R1 convention); day-before-start and day-after-end are the 422.</item>
///   <item><b>The D2 NULL rule</b>: a NULL date is unbounded on ITS side — NULL start admits
///   any past date, NULL end any future date, and a fully windowless employee (every existing
///   fixture) is byte-unaffected.</item>
///   <item><b>The D3 date-free body</b>: the 422 carries EXACTLY
///   <c>{error, kind, message}</c> — no date-valued field, no start/end echo, and ONE uniform
///   body for before-start and after-end (pinned byte for byte by
///   <c>SkemaSave_BeforeStartAndAfterEnd_RefusalBodiesAreByteIdentical</c>), so employment dates
///   (HR-scoped, <c>User.cs:38-62</c>) cannot be binary-searched by probing registration dates. The
///   body comes from ONE construction site (<c>EmploymentWindowGate</c>).</item>
///   <item><b>All THREE skema arrays</b> are gated — Entries, Absences, AND WorkTime (the
///   first date gate Entries/Absences have ever had) — and a single offending date rejects the
///   WHOLE save atomically (zero rows in any projection).</item>
/// </list>
///
/// <para>The SEC-046 deactivation half (terminated self / HR-corrects-a-leaver) lives in
/// <see cref="TerminatedEmployeeAccessTests"/>; the window-edit-vs-registration race pin lives
/// in <c>EmploymentWindowRaceTests</c> (the SendConcurrency collection, which owns the pg_locks
/// contention barrier). Fixture discipline: case-unique employee identity, production write
/// paths, observations with no copy of the rule under test.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class EmploymentWindowRegistrationGateTests : SendCommandMatrixTestBase, IDisposable
{
    public EmploymentWindowRegistrationGateTests(SendCommandMatrixFixture fx) : base(fx) { }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? _pinnedHost;

    public void Dispose() => _pinnedHost?.Dispose();

    /// <summary>S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first.
    /// Every fact that gives the employee an END date runs on a host whose clock is the window's last day
    /// (<see cref="WindowEnd"/>, 2026-03-20): the person has not departed yet (the last day is still employed), so the
    /// employment-window gate is what answers, exactly as these facts have always pinned. Without the pin, the real
    /// clock is long past the end date and an Employee on their own record is refused 403 by the floor first. The
    /// actor stays an Employee; no assertion changed.</summary>
    private HttpClient PinnedEmployeeClient(string employeeId, string orgId = Org)
    {
        _pinnedHost ??= Fx.Factory.WithFixedToday(WindowEnd);
        var client = _pinnedHost.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid", Audience = "statstid", SigningKey = DevFallbackSigningKey, ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: orgId, scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // 2026-03: 03-01 is a Sunday. WindowStart 03-05 (Thu) / WindowEnd 03-20 (Fri) are weekdays,
    // as are the probes 03-04 (Wed), 03-06 (Fri), 03-09 (Mon), 03-10 (Tue) and 03-23 (Mon) —
    // weekday dates keep the skema absence day-norm guard (7.4h cap) out of the picture, so the
    // only gate under test is the window.
    private static readonly DateOnly WindowStart = new(2026, 3, 5);
    private static readonly DateOnly WindowEnd = new(2026, 3, 20);
    private static readonly DateOnly DayBeforeStart = new(2026, 3, 4);
    private static readonly DateOnly DayAfterEnd = new(2026, 3, 21);

    private static string UniqueEmp(string tag) => $"s136wg_{tag}_{Guid.NewGuid():N}"[..18];

    /// <summary>An entries-only Skema save of one project entry for the date's month, self-registered
    /// (S148 R-20: replaces the retired time-entry POST helper).</summary>
    private static Task<HttpResponseMessage> PostSkemaEntryAsync(
        HttpClient client, string employeeId, DateOnly date, decimal hours = 7.4m)
        => client.PostAsJsonAsync($"/api/skema/{employeeId}/save", new
        {
            year = date.Year,
            month = date.Month,
            entries = new[] { new { date, projectCode = "PROJ-S136-NUL", hours } },
        });

    // ── Direct window seeding (the gate keys on the users date columns however written;
    //    the employment-date PUTs have their own suites — TASK-13604) ─────────────────────────

    private async Task SetEmploymentWindowAsync(string employeeId, DateOnly? start, DateOnly? end)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET employment_start_date = @s, employment_end_date = @e, updated_at = NOW()
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("s", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ── Zero-trace counters (TOTAL per employee — the S127 F6 unfiltered-count discipline) ────

    private Task<long> CountTimeEntriesTotalAsync(string emp)
        => ScalarLongAsync("SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", emp));

    private Task<long> CountAbsencesTotalAsync(string emp)
        => ScalarLongAsync("SELECT COUNT(*) FROM absences_projection WHERE employee_id=@e", ("e", emp));

    private Task<long> CountWorkTimeTotalAsync(string emp)
        => ScalarLongAsync("SELECT COUNT(*) FROM work_time_projection WHERE employee_id=@e", ("e", emp));

    private Task<long> CountEmployeeStreamOutboxTotalAsync(string emp)
        => ScalarLongAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s", ("s", $"employee-{emp}"));

    private async Task AssertNothingWrittenAsync(string emp)
    {
        Assert.Equal(0L, await CountTimeEntriesTotalAsync(emp));
        Assert.Equal(0L, await CountAbsencesTotalAsync(emp));
        Assert.Equal(0L, await CountWorkTimeTotalAsync(emp));
        Assert.Equal(0L, await CountEmployeeStreamOutboxTotalAsync(emp));
    }

    // ── The shared 422 body assertion — THE date-free shape pin (ADR-040 D3) ───────────────────

    /// <summary>Matches an ISO (yyyy-MM-dd) or Danish (dd-MM-yyyy) date anywhere in a value —
    /// the two formats every sibling 422 in this codebase echoes dates in.</summary>
    private static readonly Regex AnyDateValue =
        new(@"\d{4}-\d{2}-\d{2}|\d{2}-\d{2}-\d{4}", RegexOptions.CultureInvariant);

    /// <summary>Asserts the response is the shared outside-employment-period 422 and returns the
    /// RAW body (for cross-writer/cross-case byte-identity assertions). The property-name SET is
    /// pinned exactly — {error, kind, message} and NOTHING else — so a future "helpful"
    /// <c>date</c>/<c>start</c>/<c>end</c> echo field fails here, and every VALUE is scanned for
    /// date-shaped content so a date smuggled into the message fails too.</summary>
    private static async Task<string> AssertWindow422DateFreeAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)rsp.StatusCode}: {raw}");

        var root = JsonDocument.Parse(raw).RootElement;
        var propertyNames = root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "error", "kind", "message" }, propertyNames);
        Assert.Equal("outside_employment_period", root.GetProperty("error").GetString());
        Assert.Equal("employment-window", root.GetProperty("kind").GetString());

        Assert.False(AnyDateValue.IsMatch(raw),
            $"the outside-employment-period 422 must be DATE-FREE (ADR-040 D3) but the body contains a date: {raw}");
        return raw;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // POST /api/skema/{id}/save — the D2 NULL sides (S148 R-20: re-pointed from the retired time-entry route).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>D2, NULL start: employed "since the beginning of time" — a date far before the
    /// end is accepted; the end still binds (day after ⇒ 422).</summary>
    [Fact]
    public async Task SkemaSave_NullStart_UnboundedPast_EndStillBinds()
    {
        var emp = UniqueEmp("nst");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, start: null, end: WindowEnd);
        var client = PinnedEmployeeClient(emp); // S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first

        var pastRsp = await PostSkemaEntryAsync(client, emp, new DateOnly(2026, 3, 2)); // no start to violate
        Assert.Equal(HttpStatusCode.OK, pastRsp.StatusCode); // S148 R-20: was 201 (the retired time-entry route); the Skema save answers 200

        await AssertWindow422DateFreeAsync(await PostSkemaEntryAsync(client, emp, DayAfterEnd));
        Assert.Equal(1L, await CountTimeEntriesTotalAsync(emp));
    }

    /// <summary>D2, NULL end: open-ended employment — a date after the start is accepted with no
    /// upper bound; the start still binds (day before ⇒ 422).</summary>
    [Fact]
    public async Task SkemaSave_NullEnd_OpenEnded_StartStillBinds()
    {
        var emp = UniqueEmp("nen");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, start: WindowStart, end: null);
        var client = EmployeeClient(emp);

        await AssertWindow422DateFreeAsync(await PostSkemaEntryAsync(client, emp, DayBeforeStart));

        var futureRsp = await PostSkemaEntryAsync(client, emp, new DateOnly(2026, 3, 25)); // no end to violate
        Assert.Equal(HttpStatusCode.OK, futureRsp.StatusCode); // S148 R-20: was 201 (the retired time-entry route); the Skema save answers 200
        Assert.Equal(1L, await CountTimeEntriesTotalAsync(emp));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // POST /api/skema/{id}/save — ALL THREE arrays gated; one offending date rejects the whole
    // save atomically; boundary days accepted through the same gate.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Entries array: an out-of-window entry date (in an otherwise legal save) ⇒ the
    /// date-free 422 and ZERO rows across ALL projections + the outbox — the whole save is
    /// atomic, and its body is the shared date-free 422 (ONE construction
    /// site). Entries never had ANY date gate before S136.</summary>
    [Fact]
    public async Task SkemaSave_EntryBeforeStart_Whole422_NothingWritten()
    {
        var emp = UniqueEmp("ent");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, WindowStart, WindowEnd);

        var rsp = await PinnedEmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new // S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first
        {
            year = 2026,
            month = 3,
            entries = new[]
            {
                new { date = new DateOnly(2026, 3, 10), projectCode = "PROJ-S136-OK", hours = 3.0m },
                new { date = DayBeforeStart, projectCode = "PROJ-S136-BAD", hours = 4.4m },
            },
        });

        await AssertWindow422DateFreeAsync(rsp);
        await AssertNothingWrittenAsync(emp);
    }

    /// <summary>Kind R (regression pin, holds today; S148 Step 5a W-1, replacing the retired time-entry
    /// boundary matrix's uniformity assertion). For the same windowed employee, a save dated the day BEFORE
    /// the start and a save dated the day AFTER the end both answer the date-free 422, and the two bodies are
    /// equal byte for byte: the refusal must not reveal which side of the window a date falls on (ADR-040 D3,
    /// SEC-046). <b>Red condition:</b> a direction-dependent message (or any direction-dependent member) in
    /// <c>EmploymentWindowGate.OutsideEmploymentPeriod()</c> makes <c>Assert.Equal(beforeBody, afterBody)</c> fail.</summary>
    [Fact]
    public async Task SkemaSave_BeforeStartAndAfterEnd_RefusalBodiesAreByteIdentical()
    {
        var emp = UniqueEmp("uni");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, WindowStart, WindowEnd);
        var client = PinnedEmployeeClient(emp); // S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first

        var beforeBody = await AssertWindow422DateFreeAsync(await PostSkemaEntryAsync(client, emp, DayBeforeStart));
        var afterBody = await AssertWindow422DateFreeAsync(await PostSkemaEntryAsync(client, emp, DayAfterEnd));

        Assert.Equal(beforeBody, afterBody);
    }

    /// <summary>Absences array: an absence dated after the employment end ⇒ 422, nothing
    /// written. SICK_DAY (non-entitlement) on a weekday keeps every sibling absence guard
    /// (norm cap, eligibility, quota) inert — the window gate is the only gate firing.
    /// Absences never had ANY date gate before S136.</summary>
    [Fact]
    public async Task SkemaSave_AbsenceAfterEnd_Whole422_NothingWritten()
    {
        var emp = UniqueEmp("abs");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, WindowStart, WindowEnd);

        var rsp = await PinnedEmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new // S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first
        {
            year = 2026,
            month = 3,
            absences = new[]
            {
                new { date = new DateOnly(2026, 3, 23), absenceType = "SICK_DAY", hours = 7.4m }, // Monday, after end
            },
        });

        await AssertWindow422DateFreeAsync(rsp);
        await AssertNothingWrittenAsync(emp);
    }

    /// <summary>WorkTime array: an out-of-window (but in-month, so past the S56 month-bounds
    /// check) work-time day ⇒ 422, nothing written — the window gate sits BEHIND the existing
    /// month-bound and catches what it cannot.</summary>
    [Fact]
    public async Task SkemaSave_WorkTimeBeforeStart_Whole422_NothingWritten()
    {
        var emp = UniqueEmp("wkt");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, WindowStart, WindowEnd);

        var rsp = await PinnedEmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new // S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first
        {
            year = 2026,
            month = 3,
            workTime = new[]
            {
                new { date = DayBeforeStart, intervals = new[] { new { start = "08:00", end = "16:00" } } },
            },
        });

        await AssertWindow422DateFreeAsync(rsp);
        await AssertNothingWrittenAsync(emp);
    }

    /// <summary>The positive matrix in one save: all three arrays with in-window dates — the
    /// entry ON the start day and the work time ON the end day (both boundaries through the
    /// skema writer), an absence in between ⇒ 200 {saved:3} with one row in each projection.</summary>
    [Fact]
    public async Task SkemaSave_AllThreeArraysInWindow_IncludingBothBoundaries_Saves()
    {
        var emp = UniqueEmp("pos");
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, WindowStart, WindowEnd);

        var rsp = await PinnedEmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new // S147 OQ-2: today pinned inside the window — the departed-self floor would otherwise answer first
        {
            year = 2026,
            month = 3,
            entries = new[] { new { date = WindowStart, projectCode = "PROJ-S136-POS", hours = 7.4m } },
            absences = new[] { new { date = new DateOnly(2026, 3, 6), absenceType = "SICK_DAY", hours = 7.4m } },
            workTime = new[] { new { date = WindowEnd, intervals = new[] { new { start = "08:00", end = "15:24" } } } },
        });

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal(3, JsonDocument.Parse(raw).RootElement.GetProperty("saved").GetInt32());

        Assert.Equal(1L, await CountTimeEntriesTotalAsync(emp));
        Assert.Equal(1L, await CountAbsencesTotalAsync(emp));
        Assert.Equal(1L, await CountWorkTimeTotalAsync(emp));
    }

    /// <summary>D2 on the skema writer: a windowless employee's three-array save is untouched
    /// by the gate (the no-backfill guarantee on the second writer).</summary>
    [Fact]
    public async Task SkemaSave_WindowlessEmployee_Unaffected()
    {
        var emp = UniqueEmp("wnl");
        await SeedEmployeeAsync(emp);

        var rsp = await EmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new
        {
            year = 2026,
            month = 3,
            entries = new[] { new { date = new DateOnly(2026, 3, 10), projectCode = "PROJ-S136-WNL", hours = 7.4m } },
            absences = new[] { new { date = new DateOnly(2026, 3, 6), absenceType = "SICK_DAY", hours = 7.4m } },
            workTime = new[] { new { date = new DateOnly(2026, 3, 9), intervals = new[] { new { start = "08:00", end = "15:24" } } } },
        });

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal(3, JsonDocument.Parse(raw).RootElement.GetProperty("saved").GetInt32());
    }
}
