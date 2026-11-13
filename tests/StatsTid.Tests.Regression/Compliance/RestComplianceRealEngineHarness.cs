using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Http;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Security;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S148 TASK-14813-T (BC-6) - the shared Docker harness of the rest-check route facts
/// (<see cref="RestCheckNotCheckedDaysTests"/>, <see cref="RestFromSkemaIntervalsTests"/>): the real
/// Backend over a real database, with the Backend's named rule-engine client routed to the REAL
/// rule-engine host hosted in-process (no socket), so the whole route runs end to end: the
/// projection reads, the request the Backend builds, the engine's <c>check-compliance</c> mapping
/// and binding, the rest rule, and the Backend's deserialization of the answer. A fact that uses it
/// names no member the sprint creates (HTTP, SQL and the real engine only).
///
/// <para>
/// The engine host is <see cref="RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory"/>
/// (the Rule Engine has no database; its JWT key is the dev key the Backend tests mint with). Docker
/// is unavailable locally, so every fact on this harness is reported CI-pending.
/// </para>
/// </summary>
public sealed class RestComplianceRealEngineHarness : IAsyncDisposable
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory _engine = null!;
    private WebApplicationFactory<Program> _host = null!;
    private long _outboxSeq = 5_000_000;

    public static async Task<RestComplianceRealEngineHarness> StartAsync()
    {
        var self = new RestComplianceRealEngineHarness();
        self._harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(self._harness.ConnectionString);
        self._factory = new StatsTidWebApplicationFactory(self._harness.ConnectionString);
        _ = self._factory.CreateClient(); // boot the seeders once

        self._engine = new RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory();
        var engineHandler = self._engine.Server.CreateHandler();

        self._host = self._factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name, o =>
                {
                    o.HandlerLifetime = Timeout.InfiniteTimeSpan;
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = engineHandler);
                })));
        _ = self._host.CreateClient();
        return self;
    }

    public async ValueTask DisposeAsync()
    {
        _host?.Dispose();
        _engine?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>Seeds users + profile + agreement rows (HK, OK24); a hire date stamps the window.</summary>
    public async Task<string> SeedEmployeeAsync(DateOnly? hire = null)
    {
        var employeeId = "emp_s148_rest_" + Guid.NewGuid().ToString("N")[..8];
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

    /// <summary>One current Skema-sourced project-hours row (the compliance read's input); no clock times.</summary>
    public async Task InsertEntryAsync(string employeeId, DateOnly date, decimal hours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, start_time, end_time, task_id, activity_type,
                 agreement_code, ok_version, voluntary_unsocial_hours, occurred_at, outbox_id, source)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, NULL, NULL, @task, 'NORMAL',
                 'HK', 'OK24', FALSE, NOW(), @outbox, 'SKEMA')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("task", "S148-" + date.ToString("yyyyMMdd"));
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>One current work-time row: the day's intervals as Skema stores them ("HH:mm" pairs).</summary>
    public async Task InsertWorkTimeAsync(string employeeId, DateOnly date, params (string Start, string End)[] intervals)
    {
        var json = "[" + string.Join(",", intervals.Select(i => $$"""{"start":"{{i.Start}}","end":"{{i.End}}"}""")) + "]";
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO work_time_projection (employee_id, date, intervals, occurred_at, outbox_id)
            VALUES (@emp, @date, @intervals::jsonb, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("intervals", json);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>An HTTP client for the Backend under test, authorised as the employee.</summary>
    public HttpClient Client(string employeeId)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
        return client;
    }

    /// <summary>
    /// A client authorised as a non-employee actor (a leader, HR, or a token carrying several scopes).
    /// The actor needs no database row: the read endpoints admit by the token's scopes.
    /// </summary>
    public HttpClient ClientAs(string actorId, string role, params RoleScope[] scopes)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = svc.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "HK", orgId: OrgId, scopes: scopes);
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>The org the seeded employees belong to, for building scopes.</summary>
    public static string EmployeeOrgId => OrgId;

    /// <summary>
    /// One month's approval row for an employee (status as the column holds it: SUBMITTED, REJECTED, DRAFT...).
    /// A REJECTED row carries a decision and a reason, as the rejecting endpoint writes it.
    /// </summary>
    public async Task InsertPeriodAsync(string employeeId, string status, int year, int month)
    {
        var rejected = status == "REJECTED";
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by, approved_at, approved_by, rejection_reason)
            VALUES
                (gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24',
                 NOW(), @emp, @decisionAt, @decisionBy, @reason)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", new DateOnly(year, month, 1));
        cmd.Parameters.AddWithValue("end", new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("decisionAt", rejected ? DateTime.UtcNow : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("decisionBy", rejected ? "leader" : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("reason", rejected ? "rettelser nødvendige" : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
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
