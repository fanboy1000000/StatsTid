using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using StatsTid.Auth;
using StatsTid.Backend.Api.Http;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Security;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S148 T14b FU-T (5a ruling T7; TASK-14828 deviation 2) - a month with no work at all still asks the Rule Engine, once, with one
/// configuration run for the first employed day.
///
/// <para>
/// <b>In plain language.</b> The compliance check for a month in which the employee registered nothing has nothing to judge, but
/// the Backend still asks the engine (the engine stays the only producer of a compliance answer, and the engine refuses a request
/// with no configuration at all). So it sends one configuration, for the first employed day. That is a design choice with a price:
/// if no configuration is in force on that first employed day the month is refused by name (500 <c>config_not_in_force</c>); a real
/// misconfiguration, and the Skema month already refuses it. Two arms: HK from its hire on Monday 16 March 2026 (a Monday, so the
/// first employed day of March 2026 is the 16th), and an agreement with nothing published.
/// </para>
///
/// <para>
/// The existing facts of <c>ComplianceEmploymentWindowTests</c> (<c>Assert.Single</c> on the engine requests of a workless mid-month
/// hire and of a workless windowless employee) already prove "exactly one engine call"; they do not look inside it, so the one run
/// is proven here. The Backend's rule-engine client goes to the real Rule Engine host in process through a recording wrapper
/// (the shape of <c>RestCheckDatedConfigTests</c>). Docker-gated: CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ComplianceWorklessMonthTests : DatedConfigDockerFixture, IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string CheckCompliancePath = "/api/rules/check-compliance";

    /// <summary>TASK-14825's brief, item 6 (B4): the fixed body of "no configuration in force".</summary>
    private const string NotInForceBody =
        """{"error":"config_not_in_force","reason":"No published agreement configuration is in force for this date. A configuration administrator must publish one."}""";

    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly Hire = new(2026, 3, 16); // Monday

    private RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory? _engine;
    private readonly RecordingHandler _recorder = new();

    public new Task InitializeAsync() => base.InitializeAsync();

    public new async Task DisposeAsync()
    {
        _engine?.Dispose();
        _recorder.Dispose();
        await base.DisposeAsync();
    }

    /// <summary>
    /// <b>Kind N (CI).</b> T7, arm 1. An HK / OK24 employee hired Monday 16 March 2026 (profile, agreement-code rows and
    /// <c>employment_start_date</c> from that day) who has registered nothing in March. <c>GET /api/compliance/{id}/period?year=2026&amp;month=3</c>
    /// answers 200 and makes exactly one engine call (<c>check-compliance</c>) whose <c>configs</c> holds exactly one run, from and to
    /// <b>2026-03-16</b>, for agreement HK.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). N: ruling 18 as amended by T7 (the built behaviour). A Backend that composed the empty
    /// answer itself would make no engine call (the call count assertion fails); one that sent a run per day or none would fail the
    /// run-count assertion (the engine would also answer 400 to a request without runs).
    /// </para>
    /// </summary>
    [Fact]
    public async Task WorklessMonth_OneEngineCall_OneRunForTheFirstEmployedDay()
    {
        var employeeId = await SeedHireAsync("HK");

        using var response = await GetPeriodAsync(employeeId);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {text}");
        var call = Assert.Single(_recorder.Bodies(CheckCompliancePath));
        var configs = JsonDocument.Parse(call).RootElement.GetProperty("configs");
        var run = Assert.Single(configs.EnumerateArray());
        Assert.Equal("2026-03-16", run.GetProperty("from").GetString());
        Assert.Equal("2026-03-16", run.GetProperty("to").GetString());
        Assert.Equal("HK", run.GetProperty("agreementCode").GetString());
    }

    /// <summary>
    /// <b>Kind N (CI).</b> T7, arm 2. The same workless month for an employee hired Monday 16 March 2026 on an agreement with no
    /// published configuration (test-unique code <c>T7NOCFG</c>, OK24): the first employed day has none in force, so the month is
    /// refused: 500 with exactly B4's named body (<c>config_not_in_force</c>), and the engine is never called.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). N. A Backend that answered a workless month with an empty result without
    /// reading the configuration would answer 200 and fail the status and body assertions.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WorklessMonth_NoConfigInForceOnTheFirstEmployedDay_RefusedByName()
    {
        var employeeId = await SeedHireAsync("T7NOCFG");

        using var response = await GetPeriodAsync(employeeId);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(NotInForceBody, text);
        Assert.Empty(_recorder.Bodies(CheckCompliancePath));
    }

    private async Task<string> SeedHireAsync(string agreementCode)
    {
        var employeeId = "emp_t7_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employeeId, OrgId, agreementCode, "OK24", effectiveFrom: Hire);
        await ExecAsync(
            "UPDATE users SET employment_start_date = @s WHERE user_id = @id",
            ("s", Hire), ("id", employeeId));
        return employeeId;
    }

    private async Task<HttpResponseMessage> GetPeriodAsync(string employeeId)
    {
        _engine ??= new RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory();
        _recorder.InnerHandler = _engine.Server.CreateHandler();
        var host = Factory.WithFixedToday(Clock).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name, o =>
                {
                    o.HandlerLifetime = Timeout.InfiniteTimeSpan;
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _recorder);
                })));
        var client = host.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=3");
    }

    /// <summary>Records the path and body of every request, then forwards it to the real Rule Engine host.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly List<(string Path, string Body)> _requests = new();

        public IReadOnlyList<string> Bodies(string pathSuffix)
        {
            lock (_requests)
                return _requests.Where(r => r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).Select(r => r.Body).ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests) _requests.Add((request.RequestUri?.AbsolutePath ?? string.Empty, body));
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
