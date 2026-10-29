using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Http;
using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S147 TASK-14706-T (QUAL-234 interim, refinement T7, AC-234i-1 server half) - Docker-gated HTTP
/// pins: <c>GET /api/compliance/{employeeId}/period</c> now serves <c>restNotCheckedDays</c>, the
/// days the 11-hour rest check could not measure.
///
/// <para>
/// <b>Plain language.</b> A month registered in Skema has hours but no clock times, so the rest
/// check cannot measure those days. Before S147 the screen showed "no breach" for them. These facts
/// prove that the REAL Backend route (projection read, normalization, employment-window read, profile
/// resolve, deserialization of the rule engine's answer) hands the dates to the caller, that a real
/// breach on timed days is still reported beside them, and that a month with no employed day returns
/// the empty list rather than a missing member.
/// </para>
///
/// <para>
/// <b>Harness.</b> The Backend's named rule-engine client gets an in-process primary handler that runs
/// the rule engine's own <c>/api/rules/check-compliance</c> body - <see cref="AgreementConfigProvider"/>
/// then <see cref="RestPeriodRule.Evaluate"/> - on the request the Backend really sent (the idiom of
/// <c>ComplianceEmploymentWindowTests</c>, no socket). The expected dates are written from the spec
/// (the seeded rows), not read back from the rule.
/// </para>
///
/// <para>
/// <b>CI-pending.</b> Docker is unavailable locally; both facts are reported CI-pending until CI-W2.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RestCheckNotCheckedDaysTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;
    private long _outboxSeq = 5_000_000;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot the seeders once

        _host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name, o =>
                {
                    o.HandlerLifetime = Timeout.InfiniteTimeSpan;
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new InProcessRuleEngineHandler());
                })));
        _ = _host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Kind <b>F</b> (AC-234i-1 server half). A Skema-registered March 2026 (HK, OK24): hours-only
    /// weekdays Mon 2, Tue 3 and Wed 4 (source SKEMA, no clock times), plus two TIMED weekdays Thu 12
    /// 14:00-22:00 and Fri 13 06:00-14:00 whose rest gap is exactly 8 hours.
    ///
    /// Red conditions (today, before the 14706 implementation): the response has NO
    /// <c>restNotCheckedDays</c> member, so <c>GetProperty("restNotCheckedDays")</c> throws
    /// KeyNotFoundException; the rule skipped every day without clock times and the Backend served the
    /// rule engine's result verbatim, which had no such member (S147 refinement T7, QUAL-234).
    ///
    /// Also asserted: the 8-hour gap is still reported as a daily-rest finding (violationType 0, actual
    /// 8.0, threshold 11.0) in violations OR warnings - HK OK24 may derogate, which makes it a warning;
    /// either way it is not hidden by the new member - and the not-checked list holds exactly the
    /// three hours-only dates, in order, none of the timed days.
    /// </summary>
    [Fact]
    public async Task SkemaMonth_HoursOnlyDays_AreServed_AndTheEightHourGapIsStillFound()
    {
        var employeeId = await SeedEmployeeAsync();
        await InsertEntryAsync(employeeId, new DateOnly(2026, 3, 2), 7.4m, null, null);
        await InsertEntryAsync(employeeId, new DateOnly(2026, 3, 3), 7.4m, null, null);
        await InsertEntryAsync(employeeId, new DateOnly(2026, 3, 4), 7.4m, null, null);
        await InsertEntryAsync(employeeId, new DateOnly(2026, 3, 12), 8m, new TimeOnly(14, 0), new TimeOnly(22, 0));
        await InsertEntryAsync(employeeId, new DateOnly(2026, 3, 13), 8m, new TimeOnly(6, 0), new TimeOnly(14, 0));

        var rsp = await Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=3");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var dates = body.GetProperty("restNotCheckedDays").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "2026-03-02", "2026-03-03", "2026-03-04" }, dates);

        var findings = body.GetProperty("violations").EnumerateArray()
            .Concat(body.GetProperty("warnings").EnumerateArray())
            .Where(f => f.GetProperty("violationType").GetInt32() == (int)ComplianceViolationType.DAILY_REST)
            .ToList();
        var rest = Assert.Single(findings);
        Assert.Equal(8.0m, rest.GetProperty("actualValue").GetDecimal());
        Assert.Equal(11.0m, rest.GetProperty("thresholdValue").GetDecimal());
    }

    /// <summary>
    /// Kind <b>F</b>. A month with NO employed day (hire 2026-05-10, month April 2026): the route's own
    /// empty result returns <c>restNotCheckedDays</c> as an EMPTY ARRAY, never a missing member.
    ///
    /// Red conditions (today): <c>GetProperty("restNotCheckedDays")</c> throws KeyNotFoundException,
    /// the empty-month result is built in the endpoint and had no such member.
    /// </summary>
    [Fact]
    public async Task MonthWithNoEmployedDay_ReturnsAnEmptyNotCheckedList()
    {
        var hire = new DateOnly(2026, 5, 10);
        var employeeId = await SeedEmployeeAsync(hire);

        var rsp = await Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=4");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var list = body.GetProperty("restNotCheckedDays");
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Empty(list.EnumerateArray());
    }

    // --------------------------------------------------------------------------------------
    // harness
    // --------------------------------------------------------------------------------------

    /// <summary>
    /// The rule engine's <c>/api/rules/check-compliance</c> body, run in-process on the request the
    /// Backend actually sent: config from the agreement provider, then the real rule.
    /// </summary>
    private sealed class InProcessRuleEngineHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var req = await request.Content!.ReadFromJsonAsync<CheckComplianceRequest>(Json, cancellationToken)
                      ?? throw new InvalidOperationException("empty compliance request");
            var config = AgreementConfigProvider.GetConfig(
                req.Profile.AgreementCode, req.Profile.OkVersion, req.Profile.Position);
            var result = RestPeriodRule.Evaluate(req.Profile, req.Entries, req.PeriodStart, req.PeriodEnd, config);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: Json) };
        }
    }

    /// <summary>Seeds users + profile + agreement rows (HK, OK24); a hire date stamps the window.</summary>
    private async Task<string> SeedEmployeeAsync(DateOnly? hire = null)
    {
        var employeeId = "emp_s147_rnc_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "HK", "OK24", effectiveFrom: hire);
        if (hire is not null)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET employment_start_date = @s WHERE user_id = @id", conn);
            cmd.Parameters.Add(new NpgsqlParameter("s", NpgsqlTypes.NpgsqlDbType.Date) { Value = hire.Value });
            cmd.Parameters.AddWithValue("id", employeeId);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        return employeeId;
    }

    /// <summary>One current Skema-sourced projection row (the compliance read's input).</summary>
    private async Task InsertEntryAsync(string employeeId, DateOnly date, decimal hours, TimeOnly? start, TimeOnly? end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, start_time, end_time, task_id, activity_type,
                 agreement_code, ok_version, voluntary_unsocial_hours, occurred_at, outbox_id, source)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @start, @end, @task, 'NORMAL',
                 'HK', 'OK24', FALSE, NOW(), @outbox, 'SKEMA')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.Add(new NpgsqlParameter("start", NpgsqlTypes.NpgsqlDbType.Time) { Value = (object?)start ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("end", NpgsqlTypes.NpgsqlDbType.Time) { Value = (object?)end ?? DBNull.Value });
        cmd.Parameters.AddWithValue("task", "S147-" + date.ToString("yyyyMMdd"));
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    private HttpClient Client(string employeeId)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
        return client;
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
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "HK",
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }
}
