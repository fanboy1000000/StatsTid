using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Http;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Security;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S148 / TASK-14828-T (AC-C1f; ADR-043 D12) - the rest check judges each work end by the minimum rest that is in force on
/// that day, from the published configuration.
///
/// <para>
/// <b>In plain language.</b> HK's agreement is republished with a 12-hour minimum rest from Monday 1 June 2026. An employee
/// works Monday 8 June 10:00-20:30 and Tuesday 9 June 08:00-14:00: the rest between them is 11.5 hours. That is enough under the
/// compiled 11 hours and too short under the published 12, so the compliance check must report it, dated Tuesday 9 June (the day
/// the work ends). The second fact is the same with the employee in a position that has a flex-cap override: the override must
/// not swallow the 12 hours.
/// </para>
///
/// <para>
/// <b>Harness.</b> The Backend over a real database with its named rule-engine client routed to the REAL Rule Engine host
/// in-process (the shape of <see cref="RestComplianceRealEngineHarness"/>, which cannot publish a configuration and exposes no
/// connection, so its wiring is repeated here on <see cref="DatedConfigDockerFixture"/>); the configuration is published
/// through the real routes, the Skema intervals and project hours are SQL rows. Docker-gated: CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RestCheckDatedConfigTests : DatedConfigDockerFixture, IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly Mon08Jun = new(2026, 6, 8);
    private static readonly DateOnly Tue09Jun = new(2026, 6, 9);

    private RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory? _engine;
    private long _outboxSeq = 6_000_000;

    public new Task InitializeAsync() => base.InitializeAsync();

    public new async Task DisposeAsync()
    {
        _engine?.Dispose();
        await base.DisposeAsync();
    }

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1f. HK/OK26 republished with <c>MinimumRestHours</c> 12 (derogation allowed, as HK's) applying
    /// from Monday 1 June 2026; an HK employee with no position; Skema intervals Monday 8 June 10:00-20:30 and Tuesday 9 June
    /// 08:00-14:00 with project hours 10.5 and 6.0 (both days fully on the clock). <c>GET /api/compliance/{id}/period?year=2026&amp;month=6</c>
    /// answers 200 with exactly one <c>DAILY_REST</c> finding, in <c>warnings</c>, dated <b>2026-06-09</b>, <c>thresholdValue</c>
    /// <b>12</b> and <c>actualValue</c> <b>11.5</b> (HK allows derogation and 11.5 is at least 8, so a warning).
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack the engine read the compiled 11 h
    /// and 11.5 h of rest passes, so no DAILY_REST finding exists and <c>Assert.Single</c> fails with an empty collection.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MinimumRest12InForce_Flags11Point5HourRest()
    {
        using var admin = AdminAt(Clock);
        await CreateAndPublishAsync(admin, RestBody(12m), "2026-06-01");
        var employeeId = await SeedHkEmployeeWithWorkAsync(position: null);

        var body = await GetPeriodAsync(employeeId);

        AssertTheJune9Finding(body);
    }

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1f (2). The fixture of <see cref="MinimumRest12InForce_Flags11Point5HourRest"/> with the HK
    /// employee in a position that has a database override (a flex cap only, with its version row, applying from Monday 4 May
    /// 2026, so in force over June): the same single finding, dated 2026-06-09, threshold 12, in <c>warnings</c>. It proves end to end
    /// that the override merge carries the minimum rest and the derogation setting through (QUAL-190).
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack the engine read the compiled 11 h and
    /// reported nothing (empty collection). With the merge still dropping <c>MinimumRestHours</c> the merged config would say
    /// 11 h and the same assertion fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MinimumRest12InForce_SurvivesAPositionOverride()
    {
        using var admin = AdminAt(Clock);
        await CreateAndPublishAsync(admin, RestBody(12m), "2026-06-01");
        var position = await SeedPositionAsync();
        await CreateOverrideAsync(admin, OverrideBody("HK", Ok26, position, maxFlexBalance: 30m, weeklyNormHours: null, appliesFrom: "2026-05-04"));
        var employeeId = await SeedHkEmployeeWithWorkAsync(position);

        var body = await GetPeriodAsync(employeeId);

        AssertTheJune9Finding(body);
    }

    private static void AssertTheJune9Finding(JsonElement body)
    {
        var findings = new[] { "violations", "warnings" }
            .SelectMany(list => body.GetProperty(list).EnumerateArray().Select(f => (List: list, Finding: f)))
            .Where(x => x.Finding.GetProperty("violationType").GetInt32() == (int)ComplianceViolationType.DAILY_REST)
            .ToList();
        var rest = Assert.Single(findings);
        Assert.Equal("warnings", rest.List);
        Assert.Equal((int)ComplianceSeverity.WARNING, rest.Finding.GetProperty("severity").GetInt32());
        Assert.Equal("2026-06-09", rest.Finding.GetProperty("date").GetString());
        Assert.Equal(12m, rest.Finding.GetProperty("thresholdValue").GetDecimal());
        Assert.Equal(11.5m, rest.Finding.GetProperty("actualValue").GetDecimal());
    }

    /// <summary>The HK/OK26 body of <see cref="DatedConfigDockerFixture.ConfigBody"/> with HK's derogation setting and the given minimum rest.</summary>
    private static JsonObject RestBody(decimal minimumRestHours)
    {
        var body = JsonSerializer.SerializeToNode(ConfigBody("HK", Ok26))!.AsObject();
        body["minimumRestHours"] = minimumRestHours;
        body["restPeriodDerogationAllowed"] = true;
        return body;
    }

    private async Task<string> SeedHkEmployeeWithWorkAsync(string? position)
    {
        var employeeId = "emp_s148_restdated_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employeeId, OrgId, "HK", Ok26, position: position);
        await InsertWorkTimeAsync(employeeId, Mon08Jun, ("10:00", "20:30"));
        await InsertWorkTimeAsync(employeeId, Tue09Jun, ("08:00", "14:00"));
        await InsertEntryAsync(employeeId, Mon08Jun, 10.5m);
        await InsertEntryAsync(employeeId, Tue09Jun, 6.0m);
        return employeeId;
    }

    private async Task<JsonElement> GetPeriodAsync(string employeeId)
    {
        using var client = EngineRoutedClient(employeeId);
        using var response = await client.GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=6");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>An employee client on a Backend host (clock pinned) whose rule-engine client goes to the real engine host.</summary>
    private HttpClient EngineRoutedClient(string employeeId)
    {
        _engine ??= new RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory();
        var engineHandler = _engine.Server.CreateHandler();
        var host = Factory.WithFixedToday(Clock).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name, o =>
                {
                    o.HandlerLifetime = Timeout.InfiniteTimeSpan;
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = engineHandler);
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
        return client;
    }

    /// <summary>One current Skema-sourced project-hours row (the compliance read's input); no clock times.</summary>
    private Task InsertEntryAsync(string employeeId, DateOnly date, decimal hours) =>
        ExecAsync(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, start_time, end_time, task_id, activity_type,
                 agreement_code, ok_version, voluntary_unsocial_hours, occurred_at, outbox_id, source)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, NULL, NULL, @task, 'NORMAL',
                 'HK', 'OK26', FALSE, NOW(), @outbox, 'SKEMA')
            """,
            ("emp", employeeId), ("date", date), ("hours", hours),
            ("task", "S148-" + date.ToString("yyyyMMdd")), ("outbox", _outboxSeq++));

    /// <summary>One current work-time row: the day's intervals as Skema stores them ("HH:mm" pairs).</summary>
    private Task InsertWorkTimeAsync(string employeeId, DateOnly date, params (string Start, string End)[] intervals)
    {
        var json = "[" + string.Join(",", intervals.Select(i => $$"""{"start":"{{i.Start}}","end":"{{i.End}}"}""")) + "]";
        return ExecAsync(
            """
            INSERT INTO work_time_projection (employee_id, date, intervals, occurred_at, outbox_id)
            VALUES (@emp, @date, @intervals::jsonb, NOW(), @outbox)
            """,
            ("emp", employeeId), ("date", date), ("intervals", json), ("outbox", _outboxSeq++));
    }
}
