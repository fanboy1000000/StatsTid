using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StatsTid.Auth;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Security;
using StatsTid.Tests.Shared;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S148 TASK-14826-T, AC-C1i and AC-C1e's host arm - the real Rule Engine route, in process, with
/// no database (<see cref="RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory"/>).
/// Not Docker-gated.
///
/// <para>
/// <b>Plain language.</b> After ADR-043 D12 the engine takes the agreement rules from the request. A
/// request that arrives without them must be stopped with a fixed 400 sentence, on each of the five
/// routes that read rules; the route that reads none (<c>evaluate-absence</c>) must keep answering;
/// and a route that is handed rules must compute with them. The bodies are raw JSON, so this file
/// names no member the sprint adds. The sentences S1, S3 and S6 are the ruled ones of TASK-14826's
/// brief (item 3), quoted as written.
/// </para>
/// </summary>
public sealed class EngineConfigRequiredTests
    : IClassFixture<RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory>
{
    private const string S1 = "An agreement configuration is required.";
    private const string S3 = "Every piece needs a Config.";
    private const string S6 = "Configs are required.";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly DateOnly Monday = new(2026, 9, 14);
    private static readonly DateOnly Sunday = new(2026, 9, 20);

    private readonly RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory _factory;

    public EngineConfigRequiredTests(RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory factory)
    {
        _factory = factory;
    }

    private static object Profile() => new
    {
        employeeId = "EMP001",
        agreementCode = "HK",
        okVersion = "OK26",
        employmentCategory = "STANDARD",
    };

    private static object[] Week() =>
        Enumerable.Range(0, 5).Select(i => (object)new
        {
            employeeId = "EMP001",
            date = Monday.AddDays(i),
            hours = 7.40m,
            agreementCode = "HK",
            okVersion = "OK26",
        }).ToArray();

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object body)
    {
        var (status, text) = await PostRawAsync(path, body);
        return (status, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>The status and raw body, for an answer that is not JSON (the framework's own binding refusal).</summary>
    private async Task<(HttpStatusCode Status, string Text)> PostRawAsync(string path, object body)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: "system:engine-config-required-test",
            name: "Engine config test",
            role: StatsTidRoles.Employee,
            agreementCode: "system");

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _factory.CreateClient().SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text);
    }

    private static void AssertRefusal(HttpStatusCode status, JsonElement body, string sentence)
    {
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var members = body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "error", "success" }, members);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(sentence, body.GetProperty("error").GetString());
    }

    /// <summary>
    /// Kind <b>F</b> (Regression, non-Docker; AC-C1i). Red conditions: on the engine before this
    /// sprint's switch each of the five routes answers 200 to its request, because it reads the
    /// compiled table and ignores an unknown member; the 400 and the exact body fail on every route.
    /// Expected values from TASK-14826's brief, item 3: <c>evaluate</c>, <c>evaluate-flex</c> and
    /// <c>check-overtime-governance</c> answer S1; <c>evaluate-timeline</c> (one piece without
    /// <c>config</c>) answers S3; <c>check-compliance</c> (no <c>configs</c>) answers S6; the body is
    /// <c>{ success: false, error }</c> and nothing else.
    /// </summary>
    [Fact]
    public async Task EachConfigReadingEndpoint_RequestWithoutConfig_400FixedSentence()
    {
        var evaluate = await PostAsync("/api/rules/evaluate", new
        {
            ruleId = "NORM_CHECK_37H",
            profile = Profile(),
            entries = Week(),
            periodStart = Monday,
            periodEnd = Sunday,
        });
        AssertRefusal(evaluate.Status, evaluate.Body, S1);

        var flex = await PostAsync("/api/rules/evaluate-flex", new
        {
            profile = Profile(),
            entries = Week(),
            absences = Array.Empty<object>(),
            periodStart = Monday,
            periodEnd = Sunday,
            previousBalance = 0m,
        });
        AssertRefusal(flex.Status, flex.Body, S1);

        var governance = await PostAsync("/api/rules/check-overtime-governance", new
        {
            profile = Profile(),
            entries = Week(),
            periodStart = Monday,
            periodEnd = Sunday,
            overtimeHoursInPeriod = 0m,
            hasPreApproval = false,
        });
        AssertRefusal(governance.Status, governance.Body, S1);

        var timeline = await PostAsync("/api/rules/evaluate-timeline", new
        {
            employeeId = "EMP001",
            periodStart = Monday,
            periodEnd = Sunday,
            pieces = new[]
            {
                new { from = Monday, to = Sunday, partTimeFraction = 1.0m, agreementCode = "HK" },
            },
            entries = Week(),
            absences = Array.Empty<object>(),
            previousFlexBalance = 0m,
        });
        AssertRefusal(timeline.Status, timeline.Body, S3);

        var compliance = await PostAsync("/api/rules/check-compliance", new
        {
            profile = Profile(),
            entries = Week(),
            periodStart = Monday,
            periodEnd = Sunday,
            workIntervals = Array.Empty<object>(),
        });
        AssertRefusal(compliance.Status, compliance.Body, S6);
    }

    /// <summary>
    /// S148 T14b FU-T (5a ruling T5). Kind <b>R</b> (Regression, non-Docker; the real engine host binds the request the
    /// Backend's overtime-governance route sends). Plain language: the engine's employment profile requires an
    /// <c>employmentCategory</c>, so a governance request whose profile lacks it cannot bind and the engine answers 400;
    /// the Backend's route used to send exactly that profile, so in the composed system the warning never ran. With the
    /// member it answers 200 and the governance rule's own result. Both requests carry the config, so the missing member
    /// is the only difference; a 400 for the missing config would be the sentence S1, not this. Red conditions: a change
    /// that made the engine stop requiring the member turns the 400 arm red; one that stopped it binding the member turns
    /// the 200 arm red. Green before and after the Backend's fix (it is engine-side); the Backend half (the route now
    /// sends the member) is proven by the composed smoke probe at CI-W5.
    /// </summary>
    [Fact]
    public async Task OvertimeGovernance_ProfileWithoutEmploymentCategory_400_WithIt_200()
    {
        object Request(object profile) => new
        {
            profile,
            entries = Array.Empty<object>(),
            periodStart = Monday,
            periodEnd = Sunday,
            overtimeHoursInPeriod = 0m,
            hasPreApproval = false,
            config = EngineConfigRequests.Config("HK", "OK26"),
        };

        var without = await PostRawAsync("/api/rules/check-overtime-governance", Request(new
        {
            employeeId = "EMP001",
            agreementCode = "HK",
            okVersion = "OK26",
        }));
        var with = await PostAsync("/api/rules/check-overtime-governance", Request(Profile()));

        Assert.Equal(HttpStatusCode.BadRequest, without.Status);
        Assert.Equal(HttpStatusCode.OK, with.Status);
        Assert.Equal("OVERTIME_GOVERNANCE_CHECK", with.Body.GetProperty("ruleId").GetString());
    }

    /// <summary>
    /// Kind <b>R</b> (a regression pin: <c>evaluate-absence</c> reads no agreement config and is
    /// unchanged by ADR-043 D12, BC-24). Discriminating assertion: the 200 with a successful result
    /// for a request that carries no config member; a change that made the route demand one would
    /// turn it red with a 400.
    /// </summary>
    [Fact]
    public async Task EvaluateAbsence_RequestWithoutConfig_Still200()
    {
        var (status, body) = await PostAsync("/api/rules/evaluate-absence", new
        {
            profile = Profile(),
            absences = new[]
            {
                new
                {
                    employeeId = "EMP001",
                    date = Monday.AddDays(2),
                    absenceType = AbsenceTypes.SickDay,
                    hours = 7.40m,
                    agreementCode = "HK",
                    okVersion = "OK26",
                },
            },
            periodStart = Monday,
            periodEnd = Sunday,
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    /// <summary>
    /// Kind <b>F</b> (Regression, non-Docker; AC-C1e's host arm, T-BC-6). Red conditions: on the engine
    /// before this sprint's switch the route ignores the <c>config</c> member and answers
    /// <c>NormHoursTotal</c> 37.00, so the 32.00 assertion fails. The config is HK/OK26's compiled
    /// config with a 32.0-hour week, posted as the first and only config of the request; expected 32.00
    /// from the spec (a full-timer's weekly norm is the config's WeeklyNormHours).
    /// </summary>
    [Fact]
    public async Task Evaluate_HostUsesTheRequestConfig()
    {
        var config = EngineConfigRequests.With(
            EngineConfigRequests.Config("HK", "OK26"), nameof(AgreementRuleConfig.WeeklyNormHours), 32.0m);

        var (status, body) = await PostAsync("/api/rules/evaluate", new
        {
            ruleId = "NORM_CHECK_37H",
            profile = Profile(),
            entries = Week(),
            periodStart = Monday,
            periodEnd = Sunday,
            config,
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(32.00m, body.GetProperty("normHoursTotal").GetDecimal());
    }
}
