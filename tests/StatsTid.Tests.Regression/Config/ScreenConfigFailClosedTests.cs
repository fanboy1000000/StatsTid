using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14825-T — AC-C1m (1)'s overtime half and AC-C1o (1), (5): a screen that cannot read its agreement
/// configuration answers an ERROR, by name, and never a value taken from the code.
///
/// <para>
/// <b>Why, in plain language.</b> Before TASK-14825 the overtime screen asked "which compensation model applies?"
/// and, when the configuration database could not be read or knew no such agreement, quietly answered "time off
/// (afspadsering)" from a default written in the code. That is a value nobody decided. Now the screen refuses:
/// <b>503 "Agreement configuration service unavailable"</b> when the read fails, <b>500 "config_not_in_force"</b>
/// when the database is fine but no published configuration applies. A choice saved during an outage writes nothing.
/// The two bodies are the contract fixed by TASK-14825's brief (item 6): they are written out below from the brief,
/// byte for byte, never read back from the implementation.
/// </para>
///
/// <para>
/// <b>How the read is made to fail.</b> The Backend handlers inject the concrete <c>ConfigResolutionService</c>, and
/// the per-day calculator reaches the same singleton through <c>IAgreementConfigLookup</c>, so one replacement of
/// <c>ConfigResolutionService</c> in the test host reaches every site. The replacement is the REAL class built over
/// repositories on an unreachable connection (opening it throws a <c>DbException</c>, which the lookup turns into
/// its "unavailable" outcome); everything else in the host runs on the fixture's real database. Docker-gated:
/// CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScreenConfigFailClosedTests : DatedConfigDockerFixture
{
    private static readonly DateOnly Monday9Nov2026 = new(2026, 11, 9);

    private const string Unreachable = "Host=localhost;Port=1;Database=na;Username=na;Password=na;Timeout=1;Command Timeout=1";
    private const string Org = "T825FC";

    /// <summary>TASK-14825's brief, item 6 (B4): the fixed body of a failed read.</summary>
    private const string UnavailableBody = """{"error":"Agreement configuration service unavailable"}""";

    /// <summary>TASK-14825's brief, item 6 (B4): the fixed body of "no configuration in force".</summary>
    private const string NotInForceBody =
        """{"error":"config_not_in_force","reason":"No published agreement configuration is in force for this date. A configuration administrator must publish one."}""";

    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-C1m (1), the overtime half. A fresh HK / OK26 employee with no <c>overtime_balances</c> row, and the
    /// lookup's database read failing. <b>GET</b> <c>/api/overtime/{id}/compensation-choice?periodYear=2026</c> as
    /// the employee answers 503 with exactly the unavailable body (so no <c>compensationModel</c> member: no
    /// compiled value); <b>PUT</b> <c>{ periodYear: 2026, compensationModel: "UDBETALING" }</c> answers the same
    /// 503 and body and writes no <c>overtime_balances</c> row for (the employee, 2026).
    ///
    /// <para>
    /// <b>Kind: F (CI).</b> <b>Red conditions</b> (argued against TASK-14825's base): the base's
    /// <c>GetActiveConfigAsync</c> catches the failed read and returns the compiled HK configuration
    /// (<c>ConfigResolutionService.cs:281-288</c>), so the GET answers 200
    /// <c>{ compensationModel: "AFSPADSERING", source: "config_default" }</c> (the status assertion fails) and the PUT
    /// answers 200 and writes a row (HK's compiled <c>EmployeeCompensationChoice</c> is true; the status and the row
    /// count assertions fail).
    /// </para>
    /// </summary>
    [Fact]
    public async Task OvertimeModel_ReadFailure_Errors()
    {
        var host = FailingReadHost(Monday9Nov2026);
        using var employee = S120ContractAssert.EmployeeClient(host, "t825fc_rf", Org); // boots the host first
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825fc_rf", Org, "HK", Ok26);

        using (var get = await employee.GetAsync("/api/overtime/t825fc_rf/compensation-choice?periodYear=2026"))
        {
            var text = await get.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);
            Assert.Equal(UnavailableBody, text);
            Assert.DoesNotContain("compensationModel", text);
        }

        using (var put = await employee.PutAsJsonAsync(
            "/api/overtime/t825fc_rf/compensation-choice",
            new { periodYear = 2026, compensationModel = "UDBETALING" }))
        {
            var text = await put.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, put.StatusCode);
            Assert.Equal(UnavailableBody, text);
        }

        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM overtime_balances WHERE employee_id = @e AND period_year = @y",
            ("e", "t825fc_rf"), ("y", 2026)));
    }

    /// <summary>
    /// AC-C1o (1). An employee whose agreement code (<c>T825K</c>, test-unique) has no configuration at all, OK26,
    /// no balance row, on a host whose database works. <b>GET</b> <c>?periodYear=2026</c> answers 500 with exactly
    /// the named body (<c>config_not_in_force</c> and its fixed reason), no <c>compensationModel</c> member;
    /// <b>PUT</b> answers the same 500 and body and writes no <c>overtime_balances</c> row. (Under B12 the routes ask
    /// the OK version in force on 1 January 2026, OK24; the code has none for any OK version, so the refusal holds.)
    ///
    /// <para>
    /// <b>Kind: F (CI).</b> <b>Red conditions</b> (argued against TASK-14825's base): the GET answers 200
    /// <c>"AFSPADSERING"</c> (<c>?? "AFSPADSERING"</c>, <c>OvertimeEndpoints.cs:834</c>), so the status assertion
    /// fails; the PUT answers 400 "not enabled", so its status assertion fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OvertimeModel_NoConfigInForce_RefusedByName()
    {
        var host = Factory.WithFixedToday(Monday9Nov2026);
        using var employee = S120ContractAssert.EmployeeClient(host, "t825fc_nc", Org);
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825fc_nc", Org, "T825K", Ok26);

        using (var get = await employee.GetAsync("/api/overtime/t825fc_nc/compensation-choice?periodYear=2026"))
        {
            var text = await get.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.InternalServerError, get.StatusCode);
            Assert.Equal(NotInForceBody, text);
            Assert.DoesNotContain("compensationModel", text);
        }

        using (var put = await employee.PutAsJsonAsync(
            "/api/overtime/t825fc_nc/compensation-choice",
            new { periodYear = 2026, compensationModel = "UDBETALING" }))
        {
            var text = await put.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.InternalServerError, put.StatusCode);
            Assert.Equal(NotInForceBody, text);
        }

        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM overtime_balances WHERE employee_id = @e AND period_year = @y",
            ("e", "t825fc_nc"), ("y", 2026)));
    }

    /// <summary>
    /// AC-C1o (5). The named refusal on a route that reaches the lookup only INDIRECTLY: the employee of
    /// <see cref="OvertimeModel_NoConfigInForce_RefusedByName"/> (agreement code <c>T825K</c>, no configuration)
    /// reads their own Skema month, <c>GET /api/skema/{id}/month?year=2026&amp;month=3</c>, which asks the lookup
    /// once per weekday through the per-day norm calculator. It answers 500 with exactly the named body and no
    /// <c>dailyNorm</c> member. It proves TASK-14825's ONE Backend mapping reaches a screen that never names the
    /// lookup itself.
    ///
    /// <para>
    /// <b>Kind: F (CI).</b> <b>Red conditions</b> (argued against TASK-14825's base): the base's central read finds
    /// neither a database row nor a compiled one and throws <c>InvalidOperationException</c>
    /// (<c>ConfigResolutionService.cs:98-99</c>), which leaves the route as an anonymous 500 with no named body, so
    /// the body assertion fails (the status alone would pass; the body is the discriminator). If the Skema month
    /// refuses this fixture earlier for another reason, the status assertion names it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SkemaMonth_NoConfigInForce_RefusedByName()
    {
        var host = Factory.WithFixedToday(Monday9Nov2026);
        using var employee = S120ContractAssert.EmployeeClient(host, "t825fc_sk", Org);
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825fc_sk", Org, "T825K", Ok26);

        using var response = await employee.GetAsync("/api/skema/t825fc_sk/month?year=2026&month=3");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(NotInForceBody, text);
        Assert.DoesNotContain("dailyNorm", text);
    }

    // ── The failing-read host ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A derived host (clock pinned to UTC midnight of <paramref name="today"/>) whose
    /// <see cref="ConfigResolutionService"/> is the real class over repositories on an unreachable connection.
    /// Disposed with <see cref="DatedConfigDockerFixture.Factory"/> (derived hosts are tracked by their parent).
    /// </summary>
    private WebApplicationFactory<Program> FailingReadHost(DateOnly today) =>
        Factory.WithFixedToday(today).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ConfigResolutionService>();
                services.AddSingleton(_ =>
                {
                    var broken = new DbConnectionFactory(Unreachable);
                    return new ConfigResolutionService(
                        new AgreementConfigRepository(broken),
                        new LocalConfigurationRepository(broken),
                        new PositionOverrideRepository(broken),
                        new LocalAgreementProfileRepository(broken),
                        NullLogger<ConfigResolutionService>.Instance);
                });
            }));
}
