using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14804-T — the paid-absence day bound (S0-7, ADR-043 D2): after a Skema save, the
/// credit-class paid absence a day holds may not exceed <c>max(0, planned − worked)</c>. In plain
/// terms: a day is worth its planned hours; paid absence and work together may not claim more.
/// The bound judges what the day holds AFTER the save — the request's rows plus the rows already
/// stored for the touched day (a re-saved cell replaces its old row) — and a save that carries
/// only work is judged too. Every refusal is 422 with <c>kind = "paid-absence-day-bound"</c>
/// (ruled B4), which today's per-request cap bodies do not carry, so no fact here can pass on the
/// old cap answering instead.
///
/// <para>Expected values come from the refinement (<c>REF:346-350</c>): a full-time 37-hour
/// employee plans 7,4 on a weekday and 0 on Saturday. Dates: Wednesday 10 June 2026 and Saturday
/// 13 June 2026 (OK26, no seeded public holiday).</para>
///
/// <para>Docker facts: CI-pending (CI-W1). Never run locally.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PaidAbsenceDayBoundTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string BoundKind = "paid-absence-day-bound";

    private static readonly DateOnly Wednesday = new(2026, 6, 10);
    private static readonly DateOnly Saturday = new(2026, 6, 13);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot once so the startup seeders run before the tests create their employees.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>AC-S0-7a. Ferie 7,4 saved, then Sygdom 7,4 saved in a second request on the same
    /// 7,4 h day: the second save is refused, the day still holds only the Vacation row.
    ///
    /// <para>Kind: F. Red conditions: the final 422 and the <c>kind</c> assertion. The base cap sums
    /// only the request's own absences (<c>SkemaEndpoints.cs:1044</c>, in-lock <c>:2034</c>), so the
    /// second save (7,4 alone) answers 200.</para></summary>
    [Fact]
    public async Task Ferie74ThenSygdom74SameDay_SecondSave422()
    {
        var emp = await SeedEmployeeAsync();
        var client = CreateEmployeeClient(emp);

        var first = await SaveAsync(client, emp, Wednesday, absences: new[] { ("VACATION", 7.4m) });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SaveAsync(client, emp, Wednesday, absences: new[] { ("SICK_DAY", 7.4m) });

        await AssertBoundRefusalAsync(second);
        Assert.Equal(new[] { ("VACATION", 7.4m) }, await ReadCurrentAbsencesAsync(emp, Wednesday));
    }

    /// <summary>AC-S0-7b. A request with NO absences and 7,4 h of work on a day already holding
    /// Sygdom 7,4 is refused (the other direction). Both work shapes the rule reads are tried: a
    /// work-time day (08:00-15:24) and a project entry of 7,4 h (the rule takes the larger of the
    /// two). No work row is written.
    ///
    /// <para>Kind: F. Red conditions: the 422 assertions. A work-only save skips both absence blocks
    /// of the base save (<c>SkemaEndpoints.cs:982</c>, <c>:1985</c>), so it answers 200.</para></summary>
    [Fact]
    public async Task WorkSave74OnDayHoldingSygdom74_422()
    {
        var emp = await SeedEmployeeAsync();
        var client = CreateEmployeeClient(emp);

        var sick = await SaveAsync(client, emp, Wednesday, absences: new[] { ("SICK_DAY", 7.4m) });
        Assert.Equal(HttpStatusCode.OK, sick.StatusCode);

        var workTime = await SaveAsync(client, emp, Wednesday, workTime: ("08:00", "15:24"));
        await AssertBoundRefusalAsync(workTime);

        var entry = await SaveAsync(client, emp, Wednesday, entryHours: 7.4m);
        await AssertBoundRefusalAsync(entry);

        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, Wednesday));
        Assert.Equal(0L, await CountEntryRowsAsync(emp, Wednesday));
        Assert.Equal(new[] { ("SICK_DAY", 7.4m) }, await ReadCurrentAbsencesAsync(emp, Wednesday));
    }

    /// <summary>AC-S0-7c. 3,0 h worked and 4,4 h sick is accepted (4,4 = 7,4 − 3,0); re-saving the
    /// Sygdom cell at 4,5 is refused and the stored row stays 4,4.
    ///
    /// <para>Which step proves what: the re-save of the same cell at 4,4 (expected 200) proves a
    /// replaced cell is not counted beside its replacement; without that exclusion the day would
    /// read 4,4 + 4,4 = 8,8 &gt; 4,4 and the re-save would be refused. The 4,5 refusal's body
    /// (<c>totalHours</c> 4,5, not 8,9) proves the same on the refusing side. The bare 422 does
    /// not prove it, because 4,4 + 4,5 would also breach.</para>
    ///
    /// <para>Kind: F, on its last assertions. Red conditions: 4,5 ≤ 7,4 passes the base cap, so the
    /// 4,5 save answers 200 and the 422 assertion trips. The 4,4-accepted half holds today.</para></summary>
    [Fact]
    public async Task Worked30Sick44_Accepted_Sick45_422()
    {
        var emp = await SeedEmployeeAsync();
        var client = CreateEmployeeClient(emp);

        var work = await SaveAsync(client, emp, Wednesday, workTime: ("08:00", "11:00"));
        Assert.Equal(HttpStatusCode.OK, work.StatusCode);

        var sick44 = await SaveAsync(client, emp, Wednesday, absences: new[] { ("SICK_DAY", 4.4m) });
        Assert.Equal(HttpStatusCode.OK, sick44.StatusCode);

        // The same cell at the same value: replaced, not added to itself.
        var sick44Again = await SaveAsync(client, emp, Wednesday, absences: new[] { ("SICK_DAY", 4.4m) });
        Assert.Equal(HttpStatusCode.OK, sick44Again.StatusCode);

        var sick45 = await SaveAsync(client, emp, Wednesday, absences: new[] { ("SICK_DAY", 4.5m) });
        var body = await AssertBoundRefusalAsync(sick45);
        Assert.Equal(4.5m, body.GetProperty("totalHours").GetDecimal());
        Assert.Equal(4.4m, body.GetProperty("maxHours").GetDecimal());

        Assert.Equal(new[] { ("SICK_DAY", 4.4m) }, await ReadCurrentAbsencesAsync(emp, Wednesday));
    }

    /// <summary>One request that carries 4,0 Sygdom and 4,0 paid leave (8,0 &gt; 7,4) is refused by
    /// today's all-types cap, which answers first; its body keeps the old shape and carries no
    /// <c>kind</c>, <c>plannedHours</c> or <c>workedHours</c> (TASK-14810 relies on this). Nothing is
    /// written.
    ///
    /// <para>Kind: R. Red conditions: if the bound answered before the cap, the body would carry
    /// <c>kind</c> and the "no kind" assertion trips; if the cap stopped answering, the status or
    /// the <c>error</c> string trips.</para></summary>
    [Fact]
    public async Task SingleRequestOverThePlan_AllTypesCapAnswersFirst_NoKind()
    {
        var emp = await SeedEmployeeAsync();
        var client = CreateEmployeeClient(emp);

        var rsp = await SaveAsync(client, emp, Wednesday,
            absences: new[] { ("SICK_DAY", 4.0m), ("LEAVE_WITH_PAY", 4.0m) });

        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"got {(int)rsp.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Total absence hours exceed norm day", body.GetProperty("error").GetString());
        Assert.Equal(8.0m, body.GetProperty("totalHours").GetDecimal());
        Assert.Equal(7.4m, body.GetProperty("maxHours").GetDecimal());
        Assert.False(body.TryGetProperty("kind", out _));
        Assert.False(body.TryGetProperty("plannedHours", out _));
        Assert.False(body.TryGetProperty("workedHours", out _));
        Assert.Empty(await ReadCurrentAbsencesAsync(emp, Wednesday));
    }

    /// <summary>AC-S0-7d (QUAL-240). A Saturday (planned 0) holds no credit-class paid absence:
    /// Sygdom 7,4 and Tjenestefrihed med løn (LEAVE_WITH_PAY) 3,7 are each refused, and no row
    /// is written.
    ///
    /// <para>Kind: F. Red conditions: the base zero-norm branch admits SICK_DAY and LEAVE_WITH_PAY up
    /// to a flat 7,4 h (<c>SkemaEndpoints.cs:1071-1079</c>, in-lock <c>:2059-2067</c>), so both
    /// saves answer 200.</para></summary>
    [Fact]
    public async Task SaturdaySickAndSaturdayPaidLeave_422()
    {
        var emp = await SeedEmployeeAsync();
        var client = CreateEmployeeClient(emp);

        var sick = await SaveAsync(client, emp, Saturday, absences: new[] { ("SICK_DAY", 7.4m) });
        await AssertBoundRefusalAsync(sick);

        var leave = await SaveAsync(client, emp, Saturday, absences: new[] { ("LEAVE_WITH_PAY", 3.7m) });
        await AssertBoundRefusalAsync(leave);

        Assert.Empty(await ReadCurrentAbsencesAsync(emp, Saturday));
    }

    // ── assertions ──

    private static async Task<JsonElement> AssertBoundRefusalAsync(HttpResponseMessage rsp)
    {
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected the day-bound 422, got {(int)rsp.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(BoundKind, body.GetProperty("kind").GetString());
        return body;
    }

    // ── the save ──

    /// <summary>One Skema save for the date's month. Any of the three arrays may be omitted.</summary>
    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client, string emp, DateOnly date,
        (string Type, decimal Hours)[]? absences = null,
        (string Start, string End)? workTime = null,
        decimal? entryHours = null)
    {
        var d = date.ToString("yyyy-MM-dd");
        var request = new Dictionary<string, object> { ["year"] = date.Year, ["month"] = date.Month };
        if (absences is not null)
            request["absences"] = absences.Select(a => new { date = d, absenceType = a.Type, hours = a.Hours }).ToArray();
        if (workTime is { } w)
            request["workTime"] = new[]
            {
                new { date = d, intervals = new[] { new { start = w.Start, end = w.End } }, manualHours = 0m },
            };
        if (entryHours is { } h)
            request["entries"] = new[] { new { date = d, projectCode = "S148P", hours = h } };
        return client.PostAsJsonAsync($"/api/skema/{emp}/save", request);
    }

    // ── seeding (a fresh full-time 37-hour employee, OK26, from 0001-01-01) ──

    private async Task<string> SeedEmployeeAsync()
    {
        var emp = "emp_s148_bound_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, emp, OrgId, "AC", "OK26", ensureOrg: false);
        return emp;
    }

    // ── read-back ──

    /// <summary>The current (not superseded) absence rows of the day, ordered by type.</summary>
    private async Task<(string Type, decimal Hours)[]> ReadCurrentAbsencesAsync(string emp, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT absence_type, hours FROM absences_projection
            WHERE employee_id = @e AND date = @d AND superseded_by_outbox_id IS NULL
            ORDER BY absence_type
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        var rows = new List<(string, decimal)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add((r.GetString(0), r.GetDecimal(1)));
        return rows.ToArray();
    }

    private async Task<long> CountWorkTimeRowsAsync(string emp, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM work_time_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountEntryRowsAsync(string emp, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ── HTTP ──

    private HttpClient CreateEmployeeClient(string emp)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IHttpClientFactory>(new PaidAbsenceRuleStubFactory())));
        var client = factory.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: emp, name: emp, role: StatsTidRoles.Employee, agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>Replaces the Rule Engine HTTP client with the REAL <c>EntitlementValidationRule</c> over
/// the validate-entitlement seam (the convention of the other Skema Docker suites), supplying the
/// BaseAddress the production named-client registration sets. Shared by the two S0-7 suites.</summary>
internal sealed class PaidAbsenceRuleStubFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new Handler(), disposeHandler: false)
    {
        BaseAddress = new Uri("http://rule-engine:8080"),
    };

    private sealed class Handler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
            var result = EntitlementValidationRule.Evaluate(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(result, Camel), Encoding.UTF8, "application/json"),
            };
        }
    }
}
