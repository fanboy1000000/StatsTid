using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Balance;
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
/// its "unavailable" outcome); everything else in the host runs on the fixture's real database. The two per-layer
/// facts build the same service with a WORKING central repository and the one other layer's repository on the
/// unreachable connection. Docker-gated: CI-pending until CI-W5a. A run on a throwaway local PostgreSQL 16 cluster
/// is a pre-check, not CI evidence.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScreenConfigFailClosedTests : ScreenNormFixture
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

    /// <summary>
    /// <b>S148 / TASK-14828-T (AC-C1m (1), the Teamoversigt half; kind F (CI)).</b> An HK / OK24 employee on a leader's roster,
    /// and the lookup's database read failing. <c>GET /api/approval/team-overview?year=2026&amp;month=3</c> as the leader answers 503
    /// with exactly <c>{ "error": "Agreement configuration service unavailable" }</c> (B4's fixed body, taken from TASK-14825's
    /// brief, never read back from a report), and no <c>normExpected</c> member: no value taken from the code.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). On the code before the stack Teamoversigt fell back to the
    /// compiled HK configuration when the read failed (<c>ApprovalEndpoints.cs:1361-1363</c> at <c>bb529e62</c>): 200 with the
    /// compiled 162.80, so the status assertion fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Teamoversigt_ReadFailure_Errors()
    {
        var host = FailingReadHost(Monday9Nov2026);
        _ = host.CreateClient(); // boot the host before anyone is seeded
        var leader = await SeedLeaderAsync(host, "t828fc_tl", Org);
        await SeedRosterMemberAsync("t828fc_te", Org, "t828fc_tl");

        using var response = await leader.GetAsync("/api/approval/team-overview?year=2026&month=3");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(UnavailableBody, text);
        Assert.DoesNotContain("normExpected", text);
    }

    /// <summary>
    /// <b>S148 / TASK-14828-T (AC-C1m (1), the balance half; kind F (CI)).</b> An HK / OK24 employee reads their own balance summary,
    /// <c>GET /api/balance/{id}/summary?year=2026&amp;month=3</c>, with the lookup's database read failing: 503 with exactly the unavailable
    /// body (B4) and no <c>normHoursExpected</c> member.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack the balance fell back to the compiled weekly
    /// norm when the read failed (<c>BalanceEndpoints.cs:162</c> at <c>bb529e62</c>): 200 with 162.80, so the status assertion fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Balance_ReadFailure_Errors()
    {
        var host = FailingReadHost(Monday9Nov2026);
        _ = host.CreateClient(); // boot the host before anyone is seeded
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t828fc_be", Org, "HK", "OK24");

        using var employee = S120ContractAssert.EmployeeClient(host, "t828fc_be", Org);
        using var response = await employee.GetAsync("/api/balance/t828fc_be/summary?year=2026&month=3");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(UnavailableBody, text);
        Assert.DoesNotContain("normHoursExpected", text);
    }

    /// <summary>
    /// Fail-closed per layer (service level). The central read works (the HK / OK26 seed, in force on Monday 9 November
    /// 2026) and only the named layer's repository is on the unreachable connection: <c>ResolveAsync</c> throws
    /// <see cref="ConfigLookupUnavailableException"/> whose <c>Layer</c> is that layer, and returns no configuration.
    /// A position is given, so the override layer is read; the local layer is read on every call.
    ///
    /// <para>
    /// <b>Kind: F (CI); the <c>local agreement profile</c> case is also N.</b> <b>Red conditions</b>: the mutation
    /// that restores the old local read's <c>catch (Exception) { return config; }</c> around the profile read makes
    /// the local case return the central configuration instead of throwing, so <c>ThrowsAsync</c> fails. The override
    /// case is red under the same swallow applied to the override read.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("position override")]
    [InlineData("local agreement profile")]
    public async Task ResolveAsync_OnlyOneLayerUnreadable_ThrowsNamingThatLayer(string failingLayer)
    {
        var service = LayerFailingService(failingLayer);

        var ex = await Assert.ThrowsAsync<ConfigLookupUnavailableException>(() =>
            service.ResolveAsync(Org, "HK", Ok26, "T825POS", Monday9Nov2026));

        Assert.Equal(failingLayer, ex.Layer);
    }

    /// <summary>
    /// Fail-closed per layer (route level). An HK / OK26 employee reads their own Skema month, <c>GET
    /// /api/skema/{id}/month?year=2026&amp;month=3</c>, which asks the lookup once per weekday. The central read works
    /// and only one other layer's repository is unreachable: the position override (the employee holds position
    /// <c>T825POS</c>) or the local profile (no position, so only the local read can fail). Either answers 503 with
    /// exactly the unavailable body and no <c>dailyNorm</c> member.
    ///
    /// <para>
    /// <b>Kind: F (CI); the <c>local agreement profile</c> case is also N.</b> <b>Red conditions</b>: under the
    /// mutation named on <see cref="ResolveAsync_OnlyOneLayerUnreadable_ThrowsNamingThatLayer"/> the local case
    /// answers 200 with the central norms and the status assertion fails; the override case is red under the same
    /// swallow applied to the override read.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("position override", "T825POS")]
    [InlineData("local agreement profile", null)]
    public async Task SkemaMonth_OnlyOneLayerUnreadable_Errors(string failingLayer, string? position)
    {
        var host = Factory.WithFixedToday(Monday9Nov2026).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ConfigResolutionService>();
                services.AddSingleton(_ => LayerFailingService(failingLayer));
            }));
        using var employee = S120ContractAssert.EmployeeClient(host, "t825fc_ly", Org); // boots the host first
        await RegressionSeed.SeedEmployeeAsync(
            ConnectionString, "t825fc_ly", Org, "HK", Ok26, position: position);

        using var response = await employee.GetAsync("/api/skema/t825fc_ly/month?year=2026&month=3");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(UnavailableBody, text);
        Assert.DoesNotContain("dailyNorm", text);
    }

    // ── The failing-read host ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The real <see cref="ConfigResolutionService"/> with a working central repository (the fixture's database) and
    /// the repository of <paramref name="failingLayer"/> ("position override" or "local agreement profile", the
    /// service's own layer names) on the unreachable connection.
    /// </summary>
    private ConfigResolutionService LayerFailingService(string failingLayer)
    {
        var broken = new DbConnectionFactory(Unreachable);
        return new ConfigResolutionService(
            new AgreementConfigRepository(Db),
            new LocalConfigurationRepository(Db),
            new PositionOverrideRepository(failingLayer == "position override" ? broken : Db),
            new LocalAgreementProfileRepository(failingLayer == "local agreement profile" ? broken : Db),
            NullLogger<ConfigResolutionService>.Instance);
    }

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
