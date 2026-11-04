using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14716-T (SEC-054 step 2; owner OQ-3 = (a); AC-054-3) — HOST-LEVEL pin that the two raw payroll routes,
/// <c>POST /api/payroll/export</c> and <c>POST /api/payroll/export-period</c>, no longer exist.
///
/// <para>
/// <b>In plain language.</b> Those two routes took pay lines the caller wrote and exported them with no approved month and
/// no second person behind them: a one-person path into payroll (SEC-054). With a leaver's final month payable by HR and the
/// month before the ledger start payable untracked (both S147), nothing needs them, so they were deleted. The one route that
/// still exports is <c>/calculate-and-export</c>, which always starts from an approved month. This fact proves the other
/// two answer "not found" even to a Global Admin, the most powerful caller there is.
/// </para>
///
/// <para>
/// The request bodies are the SHAPE the routes used to accept (a caller-calculated result plus a profile), so on the code
/// before this task the routes would have bound them and answered 200 or 422, never 404.
/// </para>
///
/// Docker-gated (Testcontainers Postgres + full init.sql): CI-pending until the sprint checkpoint, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class RawExportRoutesRetiredTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";

    private PostgreSqlContainer _container = null!;
    private PayrollHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        var cs = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(cs);
        _factory = new PayrollHostFactory(cs, new RuleRegistry().GetAll(), useRealRuleEngine: true);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>
    /// AC-054-3 (kind F, Docker; CI-pending). <c>POST /api/payroll/export</c> as Global Admin, with the body shape the route
    /// used to take, answers <b>404</b>.
    ///
    /// <para><b>Red conditions.</b> Today's code (before TASK-14716) maps the route (<c>GlobalAdminOnly</c>, no scope check):
    /// with this body it answers <b>200</b> (the month is exported with a null period) or <b>422</b> (a refusal), never 404, so
    /// <c>Assert.Equal(HttpStatusCode.NotFound, ...)</c> fails. Re-adding the route fails it again.</para>
    /// </summary>
    [Fact]
    public async Task RawExportRoute_AsGlobalAdmin_Answers404()
    {
        var response = await PostAsGlobalAdminAsync("/api/payroll/export", new
        {
            calculationResult = Result("EMP-S147-RAW-1", new DateOnly(2026, 3, 2)),
            profile = Profile("EMP-S147-RAW-1"),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// AC-054-3 (kind F, Docker; CI-pending). <c>POST /api/payroll/export-period</c> as Global Admin, with the body shape the
    /// route used to take, answers <b>404</b>.
    ///
    /// <para><b>Red conditions.</b> As for <see cref="RawExportRoute_AsGlobalAdmin_Answers404"/>: today the route answers
    /// <b>200</b> or <b>422</b>, so the 404 assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task RawExportPeriodRoute_AsGlobalAdmin_Answers404()
    {
        var response = await PostAsGlobalAdminAsync("/api/payroll/export-period", new
        {
            calculationResults = new[] { Result("EMP-S147-RAW-2", new DateOnly(2026, 3, 2)) },
            profile = Profile("EMP-S147-RAW-2"),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static object Result(string employeeId, DateOnly date) => new
    {
        ruleId = "NORM_CHECK_37H",
        employeeId,
        success = true,
        lineItems = new[]
        {
            new { timeType = "NORMAL_HOURS", hours = 7.4m, rate = 0m, date },
        },
    };

    private static object Profile(string employeeId) => new
    {
        employeeId,
        agreementCode = "HK",
        okVersion = "OK26",
        employmentCategory = "Standard",
        partTimeFraction = 1.0m,
        orgId = "STY_S147_RAW",
    };

    private async Task<HttpResponseMessage> PostAsGlobalAdminAsync(string path, object body)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, body.GetType()),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return await client.SendAsync(request);
    }

    private static string MintGlobalAdminToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: "ROOT-S147",
            name: "ROOT-S147",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
