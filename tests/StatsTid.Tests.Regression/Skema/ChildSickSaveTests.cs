using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2c — the per-code check of a child's sick day. In plain terms: which of the
/// child's sick-day codes an employee may register depends on the OK version of the day itself. Under OK24 an illness
/// gives two days, so a third-day code is refused; from 1 April 2026 (OK26) it gives three. The refusal carries the
/// 422 shape <c>absence_type_not_eligible</c> with <c>kind = "child-sick-not-offered"</c>, which the opt-in's own
/// refusal (no <c>kind</c>) and the chain's (<c>kind = "child-sick-chain"</c>) never carry, so a fact cannot pass on
/// the yearly quota, the vocabulary gate or the opt-in answering instead.
///
/// <para>Expected values come from the agreement texts (OK24: 2 days per illness, CIR1H 9333 § 18 stk. 6, 029-24 § 14;
/// OK26: 3, 021-26 § 14), never from the code. Docker-gated, <b>CI-pending</b> (CI-W2); never run locally.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickSaveTests : IAsyncLifetime
{
    private ChildSickHost _host = null!;

    public async Task InitializeAsync() => _host = await ChildSickHost.StartAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    /// <summary>
    /// AC-S0-2c. HK (OK24), a hand-built save of <c>CHILD_SICK_DAY_3</c> 7,40 h on Monday 2 March 2026 alone. Its days 1
    /// and 2 are Saturday 28 February and Sunday 1 March, which have no planned hours, so the illness chain is satisfied
    /// and only the per-code check can refuse: 422, <c>error = absence_type_not_eligible</c>,
    /// <c>kind = child-sick-not-offered</c>, naming 2 March, and no row is written.
    ///
    /// <para><b>Kind F.</b> Red conditions: the 422 assertion. The base saves it: the vocabulary gate knows the code
    /// (<c>SkemaEndpoints.cs:957-977</c> at <c>6ec6a111</c>), there is no per-code check, and HK's yearly quota of 2
    /// admits one day, so the save answers 200 and the row exists.</para>
    /// </summary>
    [Fact]
    public async Task HkOk24_ChildSickDay3_Returns422()
    {
        var monday = new DateOnly(2026, 3, 2);
        var emp = await _host.SeedEmployeeAsync("HK", "OK24");

        var rsp = await _host.SaveAsync(emp, "HK", monday, ("CHILD_SICK_DAY_3", 7.4m, monday));

        await ChildSickHost.AssertRefusalAsync(rsp, "child-sick-not-offered", "CHILD_SICK_DAY_3", monday);
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// QUAL-236 (Fable 5a W-1). An opted-in HK employee posts a hand-built <c>CHILD_SICK_CALL_HOME</c> of 3,5 h on Monday
    /// 2 March 2026 (OK24) alone, with 08:00-11:54 worked that day (so rule B, which needs work on a call-home day, is
    /// satisfied and cannot be the refusing gate). OK24 grants no call-home day, so the per-code check refuses it: 422,
    /// <c>error = absence_type_not_eligible</c>, <c>kind = child-sick-not-offered</c>, naming <c>CHILD_SICK_CALL_HOME</c>
    /// and 2026-03-02, and neither the absence nor the work-time day is written. The code must belong to
    /// <c>DefaultEntitlementConfigs.ChildSickCodes</c>, the set the per-code check reads.
    ///
    /// <para><b>Kind F.</b> Red conditions: the 422 / <c>kind</c> assertion. On the base the code is not in the
    /// vocabulary, so the save answers a different refusal, 422 <c>absence_type_unknown</c> (<c>SkemaEndpoints.cs</c>
    /// vocabulary gate), which carries no <c>kind</c> and fails the <c>error</c> assertion. Under the code-set mutation
    /// (the code removed from <c>ChildSickCodes</c>) the per-code check skips the code, no later gate refuses a call-home
    /// day that has work on an OK24 date, the save answers 200 and the same assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task HkOk24_CallHome_NotOffered_Returns422()
    {
        var monday = new DateOnly(2026, 3, 2);
        var emp = await _host.SeedEmployeeAsync("HK", "OK24");

        var rsp = await _host.SaveRawAsync(emp, "HK", monday,
            absences: new[] { ("CHILD_SICK_CALL_HOME", 3.5m, monday) },
            workTime: new[] { (monday, new[] { ("08:00", "11:54") }) });

        await ChildSickHost.AssertRefusalAsync(rsp, "child-sick-not-offered", "CHILD_SICK_CALL_HOME", monday);
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
        Assert.Empty(await _host.ReadWorkTimeAsync(emp));
    }

    /// <summary>
    /// QUAL-236 (Fable 5a W-1). An employee with NO child's-sick opt-in posts <c>CHILD_SICK_CALL_HOME</c> of 3,5 h on
    /// Wednesday 10 June 2026 (OK26, where the code is offered), with 08:00-11:54 worked that day (so rule B cannot be the
    /// refusing gate). The opt-in gate refuses it, with today's opt-in body: 422, <c>error = absence_type_not_eligible</c>,
    /// <c>entitlementType = CHILD_SICK</c>, NO <c>kind</c> member, and neither the absence nor the work-time day is
    /// written. The code must belong to <c>ChildSickCodes</c>, the set the opt-in gate reads.
    ///
    /// <para><b>Kind F.</b> Red conditions: the <c>entitlementType</c> assertion. On the base the code is unknown to the
    /// vocabulary, so the save answers 422 <c>absence_type_unknown</c>, which has no <c>entitlementType</c>. Under the
    /// code-set mutation (the code removed from <c>ChildSickCodes</c>) the opt-in gate skips the code, no later gate refuses
    /// a call-home day with work on an OK26 date for an offered code, the save answers 200 and the 422 assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task NoOptIn_CallHome_Returns422_OptInBody()
    {
        var wednesday = new DateOnly(2026, 6, 10);
        var emp = await _host.SeedEmployeeAsync("HK", optedIn: false);

        var rsp = await _host.SaveRawAsync(emp, "HK", wednesday,
            absences: new[] { ("CHILD_SICK_CALL_HOME", 3.5m, wednesday) },
            workTime: new[] { (wednesday, new[] { ("08:00", "11:54") }) });

        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)rsp.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Multiple(
            () => Assert.Equal("absence_type_not_eligible", body.GetProperty("error").GetString()),
            () => Assert.Equal("CHILD_SICK", body.GetProperty("entitlementType").GetString()),
            () => Assert.False(body.TryGetProperty("kind", out _)));
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
        Assert.Empty(await _host.ReadWorkTimeAsync(emp));
    }
}

/// <summary>
/// The shared host of the child's-sick Docker facts (<see cref="ChildSickSaveTests"/>,
/// <see cref="ChildSickIllnessChainTests"/>, <see cref="ChildSickCatalogueTests"/>): one Postgres with the full
/// <c>init.sql</c>, the real Backend host with the real entitlement rule behind a stub (the convention of the Skema
/// suites), and a fresh full-time (37 h, fraction 1,000) employee per fact, opted in to the child's sick days
/// (ADR-029: no row means ineligible) unless the fact is about the opt-in.
/// </summary>
internal sealed class ChildSickHost : IAsyncDisposable
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    internal const string OrgId = "STY01";

    private readonly TestFixtures.DockerHarness _harness;
    private readonly StatsTidWebApplicationFactory _factory;
    private readonly IDisposable _stubbed;
    private readonly HttpClient _client;

    private ChildSickHost(
        TestFixtures.DockerHarness harness, StatsTidWebApplicationFactory factory, IDisposable stubbed, HttpClient client)
    {
        _harness = harness;
        _factory = factory;
        _stubbed = stubbed;
        _client = client;
    }

    internal static async Task<ChildSickHost> StartAsync()
    {
        var harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(harness.ConnectionString);
        var factory = new StatsTidWebApplicationFactory(harness.ConnectionString);
        // Boot once so the startup seeders run before the facts create their employees.
        _ = factory.CreateClient();
        var stubbed = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IHttpClientFactory>(new PaidAbsenceRuleStubFactory())));
        return new ChildSickHost(harness, factory, stubbed, stubbed.CreateClient());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _stubbed.Dispose();
        _factory.Dispose();
        await _harness.DisposeAsync();
    }

    /// <summary>A fresh full-time employee of <paramref name="agreement"/> (profile from 0001-01-01), opted in unless told otherwise.</summary>
    internal async Task<string> SeedEmployeeAsync(string agreement, string okVersion = "OK26", bool optedIn = true)
    {
        var emp = "emp_s148_cs_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, emp, OrgId, agreement, okVersion, partTimeFraction: 1.000m, ensureOrg: false);
        if (optedIn)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO employee_entitlement_eligibility (employee_id, entitlement_type, eligible, effective_from)
                VALUES (@e, 'CHILD_SICK', TRUE, '0001-01-01')
                """, conn);
            cmd.Parameters.AddWithValue("e", emp);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        return emp;
    }

    /// <summary>One Skema save of the month of <paramref name="month"/>: every absence is (code, hours, date); all dates must lie in that month.</summary>
    internal async Task<HttpResponseMessage> SaveAsync(
        string emp, string agreement, DateOnly month, params (string Type, decimal Hours, DateOnly Date)[] absences)
    {
        Assert.All(absences, a => Assert.True(a.Date.Year == month.Year && a.Date.Month == month.Month, "one save covers one month"));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/skema/{emp}/save")
        {
            Content = JsonContent.Create(new
            {
                year = month.Year,
                month = month.Month,
                absences = absences.Select(a => new { date = a.Date.ToString("yyyy-MM-dd"), absenceType = a.Type, hours = a.Hours }).ToArray(),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(emp, agreement));
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// One Skema save of one month with absences, work-time days (intervals "HH:mm") and project-hour entries; any may be omitted.
    /// All dates must lie in the month of <paramref name="month"/>.
    /// </summary>
    internal async Task<HttpResponseMessage> SaveRawAsync(
        string emp, string agreement, DateOnly month,
        (string Type, decimal Hours, DateOnly Date)[]? absences = null,
        (DateOnly Date, (string Start, string End)[] Intervals)[]? workTime = null,
        (DateOnly Date, decimal Hours)[]? entries = null)
    {
        var body = new Dictionary<string, object> { ["year"] = month.Year, ["month"] = month.Month };
        if (absences is not null)
            body["absences"] = absences.Select(a => new { date = a.Date.ToString("yyyy-MM-dd"), absenceType = a.Type, hours = a.Hours }).ToArray();
        if (workTime is not null)
            body["workTime"] = workTime.Select(w => new
            {
                date = w.Date.ToString("yyyy-MM-dd"),
                intervals = w.Intervals.Select(i => new { start = i.Start, end = i.End }).ToArray(),
            }).ToArray();
        if (entries is not null)
            body["entries"] = entries.Select(e => new { date = e.Date.ToString("yyyy-MM-dd"), projectCode = "S148P", hours = e.Hours }).ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/skema/{emp}/save") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(emp, agreement));
        return await _client.SendAsync(request);
    }

    /// <summary>The employee's stored work-time days as (date, "start-end;..." of the intervals), ordered by date.</summary>
    internal async Task<(DateOnly Date, string Intervals)[]> ReadWorkTimeAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT date, COALESCE((SELECT string_agg((i->>'start') || '-' || (i->>'end'), ';' ORDER BY ord)
                                   FROM jsonb_array_elements(intervals) WITH ORDINALITY AS t(i, ord)), '')
            FROM work_time_projection WHERE employee_id = @e ORDER BY date
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        var rows = new List<(DateOnly, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add((r.GetFieldValue<DateOnly>(0), r.GetString(1)));
        return rows.ToArray();
    }

    /// <summary>The month view (the catalogue and the grid), as the employee.</summary>
    internal async Task<JsonElement> GetMonthAsync(string emp, string agreement, int year, int month)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/skema/{emp}/month?year={year}&month={month}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(emp, agreement));
        var rsp = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Every current (not superseded) absence row of the employee, ordered by date then type.</summary>
    internal async Task<(DateOnly Date, string Type, decimal Hours)[]> ReadAbsencesAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT date, absence_type, hours FROM absences_projection
            WHERE employee_id = @e AND superseded_by_outbox_id IS NULL
            ORDER BY date, absence_type
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        var rows = new List<(DateOnly, string, decimal)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add((r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetDecimal(2)));
        return rows.ToArray();
    }

    /// <summary>
    /// The refusal every new child's-sick fact asserts: 422, the <c>absence_type_not_eligible</c> shape, the refusal's
    /// own <c>kind</c> (never the opt-in's, which carries none), the refused code and the dependent date.
    /// </summary>
    internal static async Task AssertRefusalAsync(HttpResponseMessage rsp, string kind, string absenceType, DateOnly date, string? message = null)
    {
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)rsp.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Multiple(
            () => Assert.Equal("absence_type_not_eligible", body.GetProperty("error").GetString()),
            () => Assert.Equal(kind, body.GetProperty("kind").GetString()),
            () => Assert.Equal(absenceType, body.GetProperty("absenceType").GetString()),
            () => Assert.Equal(date.ToString("yyyy-MM-dd"), body.GetProperty("date").GetString()),
            () => { if (message is not null) Assert.Equal(message, body.GetProperty("message").GetString()); });
    }

    internal static async Task AssertOkAsync(HttpResponseMessage rsp)
    {
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {text}");
    }

    private static string Token(string emp, string agreement) =>
        new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: emp, name: emp, role: StatsTidRoles.Employee, agreementCode: agreement, orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
}
