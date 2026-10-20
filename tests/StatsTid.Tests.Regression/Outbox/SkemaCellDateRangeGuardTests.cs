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

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S145 / Step 7a B-1 (BLOCKER) — the Skema save's date-range guard for the <c>Entries</c> and
/// <c>Absences</c> arrays (security: approval-lock bypass). Sibling of
/// <see cref="SkemaWorkTimeDateRangeGuardTests"/>, which pins the same guard for <c>WorkTime</c> (S56).
///
/// <para><b>Plain language.</b> A Skema save names ONE month (<c>year</c>/<c>month</c>), and the approval
/// lock is checked for THAT month only. Since S56 the work-time days are held to that month; the entry and
/// absence cells never were. Since S145 a save REPLACES the cell it names (it marks the old row superseded,
/// keyed on employee, date, task and source, with no period condition), so an employee could save open
/// October with a cell dated in an APPROVED September and silently change September's recorded hours, which
/// payroll reads (R14). The fix refuses, before anything is written, any entry or absence whose date is
/// outside the requested month: HTTP 400 <c>{ error: "skema_cell_date_out_of_range", message }</c>.</para>
///
/// <para><b>Expected values come from the spec</b> (the approval lock must govern every date a save writes;
/// the WorkTime check is the model), not from what the code does today.</para>
///
/// <para><b>Fixture.</b> A fresh employee on STY01. September 2025 gets a REAL Skema cell (an entry 7.4 h on
/// 2025-09-10, or a SICK_DAY absence 7.4 h on 2025-09-11, both weekdays), then a September APPROVED period
/// is inserted, and October is left open. The attack is a POST for October that carries ONE valid in-month
/// cell and ONE September cell with a different value. A non-entitlement absence type keeps the absence case
/// free of quota arithmetic.</para>
///
/// <para><b>Docker-gated, CI-verified, never locally green.</b> Same <see cref="TestFixtures.DockerHarness"/>
/// and trait as <see cref="SkemaWorkTimeDateRangeGuardTests"/>; the host is shared across the three facts
/// (isolation by case-unique employee ids, as <c>SkemaCellSupersessionTests</c> does) and booted before any
/// employee is seeded.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaCellDateRangeGuardTests : IClassFixture<SkemaCellDateRangeGuardTests.Harness>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string Project = "PROJ-S145-B1";

    private static readonly DateOnly SeptemberCellDay = new(2025, 9, 10);   // Wednesday
    private static readonly DateOnly SeptemberAbsenceDay = new(2025, 9, 11); // Thursday
    private static readonly DateOnly OctoberDay = new(2025, 10, 8);          // Wednesday
    private static readonly DateOnly SeptemberStart = new(2025, 9, 1);
    private static readonly DateOnly SeptemberEnd = new(2025, 9, 30);

    private readonly Harness _fx;

    public SkemaCellDateRangeGuardTests(Harness fx) => _fx = fx;

    /// <summary>
    /// <b>B1-a.</b> POST for October carrying an <c>Entries</c> cell dated in APPROVED September (8.0 h, the
    /// recorded cell is 7.4 h) answers 400 <c>skema_cell_date_out_of_range</c>, and NOTHING moves: September's
    /// cell is still one current row at 7.4 h, no row is superseded, the employee's outbox gains no event,
    /// and October's valid in-month entry in the same request is not written either.
    ///
    /// <para><b>Red conditions.</b> Today the endpoint checks the range for <c>WorkTime</c> only. The request
    /// passes the October lock, the September cell is superseded by an 8.0 h row, and the save answers 200:
    /// the first assertion (<c>Assert.Equal(HttpStatusCode.BadRequest, ...)</c>) trips. The named MUTATION is
    /// removing the new <c>Entries</c>/<c>Absences</c> range check from <c>SkemaEndpoints.cs</c> (the block
    /// beside <c>work_time_date_out_of_range</c>); it must trip this fact on the status assertion. CI-verified
    /// (Docker).</para>
    /// </summary>
    [Fact]
    public async Task Save_EntryDatedInApprovedPriorMonth_Returns400_AndLeavesTheLockedCellUntouched()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, 2025, 9, new
        {
            year = 2025,
            month = 9,
            entries = new[] { new { date = Iso(SeptemberCellDay), projectCode = Project, hours = 7.4m } },
        }, "September entry cell 7.4 h");
        await InsertApprovedSeptemberAsync(emp);

        var entryRowsBefore = await EntryRowCountAsync(emp);
        var outboxBefore = await OutboxCountAsync(emp);
        Assert.Equal(1, entryRowsBefore); // premise: exactly the one recorded September cell

        var rsp = await PostAsync(emp, new
        {
            year = 2025,
            month = 10,
            entries = new[]
            {
                new { date = Iso(OctoberDay), projectCode = Project, hours = 4.0m },
                new { date = Iso(SeptemberCellDay), projectCode = Project, hours = 8.0m },
            },
        });

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.BadRequest,
            $"an October save carrying a September entry must answer 400; got {(int)rsp.StatusCode}: {raw}");
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("skema_cell_date_out_of_range", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));

        // September's recorded cell is unchanged: one row, 7.4 h, current (not superseded).
        var september = await EntryRowsOnAsync(emp, SeptemberCellDay);
        var row = Assert.Single(september);
        Assert.Equal(7.4m, row.Hours);
        Assert.False(row.Superseded, "September's recorded cell must not be superseded by the refused save.");

        // Nothing else was written: no new event, no new row, and October's valid cell is absent too.
        Assert.Equal(outboxBefore, await OutboxCountAsync(emp));
        Assert.Equal(entryRowsBefore, await EntryRowCountAsync(emp));
        Assert.Empty(await EntryRowsOnAsync(emp, OctoberDay));
    }

    /// <summary>
    /// <b>B1-b.</b> The same attack through <c>Absences</c>: a SICK_DAY absence dated in APPROVED September
    /// (3.7 h; the recorded day is 7.4 h) inside an October save answers 400
    /// <c>skema_cell_date_out_of_range</c>; September's absence is still one current row at 7.4 h, no new
    /// event is enqueued, and October's valid in-month absence is not written either.
    ///
    /// <para><b>Red conditions.</b> Today there is no range check on <c>Absences</c>: the save answers 200 and
    /// supersedes September's absence cell, so the status assertion trips. The named MUTATION is the same as
    /// B1-a (remove the new <c>Entries</c>/<c>Absences</c> range check); removing only the <c>Absences</c>
    /// half trips this fact and leaves B1-a green, which is why the two arrays have a fact each.
    /// CI-verified (Docker).</para>
    /// </summary>
    [Fact]
    public async Task Save_AbsenceDatedInApprovedPriorMonth_Returns400_AndLeavesTheLockedCellUntouched()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, 2025, 9, new
        {
            year = 2025,
            month = 9,
            absences = new[] { new { date = Iso(SeptemberAbsenceDay), absenceType = "SICK_DAY", hours = 7.4m } },
        }, "September SICK_DAY 7.4 h");
        await InsertApprovedSeptemberAsync(emp);

        var absenceRowsBefore = await AbsenceRowCountAsync(emp);
        var outboxBefore = await OutboxCountAsync(emp);
        Assert.Equal(1, absenceRowsBefore); // premise: exactly the one recorded September day

        var rsp = await PostAsync(emp, new
        {
            year = 2025,
            month = 10,
            absences = new[]
            {
                new { date = Iso(OctoberDay), absenceType = "SICK_DAY", hours = 4.0m },
                new { date = Iso(SeptemberAbsenceDay), absenceType = "SICK_DAY", hours = 3.7m },
            },
        });

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.BadRequest,
            $"an October save carrying a September absence must answer 400; got {(int)rsp.StatusCode}: {raw}");
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("skema_cell_date_out_of_range", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));

        var september = await AbsenceRowsOnAsync(emp, SeptemberAbsenceDay);
        var row = Assert.Single(september);
        Assert.Equal(7.4m, row.Hours);
        Assert.False(row.Superseded, "September's recorded absence must not be superseded by the refused save.");

        Assert.Equal(outboxBefore, await OutboxCountAsync(emp));
        Assert.Equal(absenceRowsBefore, await AbsenceRowCountAsync(emp));
        Assert.Empty(await AbsenceRowsOnAsync(emp, OctoberDay));
    }

    /// <summary>
    /// <b>B1-c (control).</b> An October save whose entry and absence are BOTH inside October is accepted
    /// (200) and writes both rows: the guard refuses only out-of-range dates, never a whole month's save,
    /// even while a different month (September) is APPROVED.
    ///
    /// <para><b>Red conditions.</b> Passes today and must keep passing. It trips if the new check is built
    /// as a refusal of every save while any other month is locked, or compares against the wrong month
    /// (for example the current date): the status assertion would flip to 400.</para>
    /// </summary>
    [Fact]
    public async Task Save_EntryAndAbsenceInsideRequestedMonth_Succeeds_AndWritesBothRows()
    {
        var emp = await SeedEmployeeAsync();
        await InsertApprovedSeptemberAsync(emp);

        var rsp = await PostAsync(emp, new
        {
            year = 2025,
            month = 10,
            entries = new[] { new { date = Iso(OctoberDay), projectCode = Project, hours = 4.0m } },
            absences = new[] { new { date = Iso(OctoberDay.AddDays(1)), absenceType = "SICK_DAY", hours = 3.7m } },
        });

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"an in-month October save must answer 200; got {(int)rsp.StatusCode}: {raw}");
        var entry = Assert.Single(await EntryRowsOnAsync(emp, OctoberDay));
        Assert.Equal(4.0m, entry.Hours);
        var absence = Assert.Single(await AbsenceRowsOnAsync(emp, OctoberDay.AddDays(1)));
        Assert.Equal(3.7m, absence.Hours);
    }

    // ── HTTP helpers ──

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private Task<HttpResponseMessage> PostAsync(string emp, object payload)
    {
        var client = _fx.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(emp));
        return client.PostAsJsonAsync($"/api/skema/{emp}/save", payload);
    }

    /// <summary>A setup save: asserts the 200 (R62), so a failing fixture can never pass as a red pin.</summary>
    private async Task SaveOkAsync(string emp, int year, int month, object payload, string what)
    {
        var rsp = await PostAsync(emp, payload);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"setup save ({what}, {year}-{month:D2}) must answer 200 before the save under test; got {(int)rsp.StatusCode}: {raw}");
    }

    private static string MintEmployeeToken(string actorId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }

    // ── Seeding and SQL reads (literal statements only: a non-constant command text trips CA2100) ──

    private async Task<string> SeedEmployeeAsync()
    {
        var emp = "emp_s145_b1_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, emp, OrgId, "AC", "OK24", ensureOrg: false);
        return emp;
    }

    /// <summary>An APPROVED whole-September period (the lock the attack tries to bypass). Fixture STATE,
    /// not the approval endpoint under test.</summary>
    private async Task InsertApprovedSeptemberAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED', 'AC', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("emp", emp);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", SeptemberStart);
        cmd.Parameters.AddWithValue("end", SeptemberEnd);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<long> OutboxCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{emp}");
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> EntryRowCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", emp);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> AbsenceRowCountAsync(string emp)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", emp);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<(decimal Hours, bool Superseded)>> EntryRowsOnAsync(string emp, DateOnly date)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT hours, superseded_by_outbox_id IS NOT NULL FROM time_entries_projection " +
            "WHERE employee_id = @e AND date = @d ORDER BY outbox_id", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        var rows = new List<(decimal, bool)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetDecimal(0), reader.GetBoolean(1)));
        return rows;
    }

    private async Task<List<(decimal Hours, bool Superseded)>> AbsenceRowsOnAsync(string emp, DateOnly date)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT hours, superseded_by_outbox_id IS NOT NULL FROM absences_projection " +
            "WHERE employee_id = @e AND date = @d ORDER BY outbox_id", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        var rows = new List<(decimal, bool)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetDecimal(0), reader.GetBoolean(1)));
        return rows;
    }

    // ── The shared container + host (booted once, before any employee is seeded) ──

    public sealed class Harness : IAsyncLifetime
    {
        private TestFixtures.DockerHarness _harness = null!;
        private StatsTidWebApplicationFactory _factory = null!;

        public StatsTidWebApplicationFactory Host => _factory;
        public string ConnectionString { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _harness = await TestFixtures.DockerHarness.StartAsync();
            ConnectionString = _harness.ConnectionString;
            await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
            _factory = new StatsTidWebApplicationFactory(ConnectionString);
            _ = _factory.CreateClient(); // boot the startup seeders now; every case seeds after this
        }

        public async Task DisposeAsync()
        {
            _factory?.Dispose();
            if (_harness is not null)
                await _harness.DisposeAsync();
        }
    }
}
