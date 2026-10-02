using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Orchestrator.Services;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Orchestrator;

/// <summary>
/// S145 / TASK-14506 — the Orchestrator's weekly pipeline over the new engine (refinement :271, :628).
///
/// <para>
/// <b>Plain language.</b> <c>WeeklyCalculationPipeline</c> (the Orchestrator's composite weekly
/// calculation, <c>WeeklyCalculationPipeline.cs:106-115</c> for the overtime call) hands the Rule Engine a
/// caller-supplied period and the hours for it, over HTTP. S145 rewrites the engine behind that call
/// without touching the pipeline: the HTTP contract is kept, so <c>src/Orchestrator/**</c> needs no code
/// change. This fact proves it from the pipeline's side. A Monday–Sunday call (14–20 September 2026, HK,
/// 8 h on each weekday = 40.00 h) returns the SAME HOURS as before — 3.00 h of OVERTIME_50, norm 37.00,
/// actual 40.00 — but the overtime line is now dated on the week's <b>Sunday</b> (20 September), where it
/// used to be dated on the period's first day (Monday 14 September).
/// </para>
///
/// <para>
/// <b>The engine is in process.</b> The pipeline talks HTTP, so the test hands it an
/// <see cref="HttpMessageHandler"/> that behaves like the engine for the route under test: it
/// deserializes the pipeline's <c>/api/rules/evaluate</c> body into the engine's own
/// <see cref="EvaluateRequest"/> and runs the REAL <see cref="RuleRegistry"/> — the same dispatch the
/// <c>Program.cs</c> route runs. The Backend fetches (time entries, absences) are served from memory, and
/// the two routes the fact does not assert on (<c>evaluate-absence</c>, <c>evaluate-flex</c>) answer a
/// stub body. This is the Orchestrator-pipeline fact of the "One arithmetic path" criterion (:628).
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate): MU-21 makes the legacy dispatch date lines on
/// the period's first day again; the pipeline then returns the same 3.00 h dated Monday 14 September and
/// the date assertion trips.
/// </para>
/// </summary>
public sealed class WeeklyPipelineOnCoreTests
{
    private const string BackendUrl = "http://backend.test";
    private const string RuleEngineUrl = "http://rule-engine.test";
    private const string EmployeeId = "USR-S145";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Orchestrator_WeeklyPipeline_MondayToSundayCall_SameHours_DatedSunday()
    {
        // Monday 14 – Sunday 20 September 2026; 8 h on each weekday; full-time HK (norm 37.00,
        // thresholds 37.00 / 40.00): 40.00 h → excess 3.00, all in the 50% band.
        var monday = new DateOnly(2026, 9, 14);
        var sunday = new DateOnly(2026, 9, 20);
        var entries = Enumerable.Range(0, 5)
            .Select(i => new TimeEntry
            {
                EmployeeId = EmployeeId,
                Date = monday.AddDays(i),
                Hours = 8.00m,
                AgreementCode = "HK",
                OkVersion = "OK26",
            })
            .ToList();

        var handler = new InProcessEngineHandler(entries);
        var pipeline = BuildPipeline(handler);

        var result = await pipeline.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["employeeId"] = EmployeeId,
                ["agreementCode"] = "HK",
                ["okVersion"] = "OK26",
                ["periodStart"] = "2026-09-14",
                ["periodEnd"] = "2026-09-20",
                ["weeklyNormHours"] = 37.0m,
                ["partTimeFraction"] = 1.0m,
                ["previousFlexBalance"] = 0.0m,
            },
            ct: CancellationToken.None);

        Assert.True(result.Success);

        // The overtime call really went to /api/rules/evaluate with OVERTIME_CALC.
        Assert.Contains(handler.EvaluatedRuleIds, id => id == "OVERTIME_CALC");

        var overtime = RuleResult(result, "OVERTIME_CALC");
        var overtimeLine = Assert.Single(overtime.GetProperty("lineItems").EnumerateArray().ToList());
        Assert.Equal("OVERTIME_50", overtimeLine.GetProperty("timeType").GetString());
        Assert.Equal(3.00m, overtimeLine.GetProperty("hours").GetDecimal());   // the SAME hours as before
        Assert.Equal(1.5m, overtimeLine.GetProperty("rate").GetDecimal());
        Assert.Equal(sunday.ToString("yyyy-MM-dd"), overtimeLine.GetProperty("date").GetString());   // …now dated Sunday

        // The norm result for the same call is unchanged: norm 37.00, actual 40.00, one NORMAL_HOURS line per entry.
        var norm = RuleResult(result, "NORM_CHECK_37H");
        Assert.Equal(37.00m, norm.GetProperty("normHoursTotal").GetDecimal());
        Assert.Equal(40.00m, norm.GetProperty("actualHoursTotal").GetDecimal());
        Assert.Equal(5, norm.GetProperty("lineItems").GetArrayLength());
    }

    private static JsonElement RuleResult(WeeklyCalculationResult result, string ruleId)
    {
        var matches = result.RuleResults
            .OfType<JsonElement>()
            .Where(e => e.ValueKind == JsonValueKind.Object
                        && e.TryGetProperty("ruleId", out var id)
                        && id.GetString() == ruleId)
            .ToList();
        Assert.True(matches.Count == 1, $"Expected exactly one '{ruleId}' result in the pipeline output, found {matches.Count}.");
        return matches[0];
    }

    private static WeeklyCalculationPipeline BuildPipeline(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:Backend"] = BackendUrl,
                ["ServiceUrls:RuleEngine"] = RuleEngineUrl,
            })
            .Build();

        return new WeeklyCalculationPipeline(
            new StubHttpClientFactory(handler),
            config,
            NullLogger<WeeklyCalculationPipeline>.Instance);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>
    /// Serves the Backend fetches from memory and answers <c>/api/rules/evaluate</c> by running the real
    /// <see cref="RuleRegistry"/> over the engine's own request contract.
    /// </summary>
    private sealed class InProcessEngineHandler : HttpMessageHandler
    {
        private readonly RuleRegistry _registry = new();
        private readonly IReadOnlyList<TimeEntry> _entries;

        public List<string> EvaluatedRuleIds { get; } = new();

        public InProcessEngineHandler(IReadOnlyList<TimeEntry> entries) => _entries = entries;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get)
            {
                if (path.Contains("/api/time-entries/", StringComparison.Ordinal))
                    return Json(HttpStatusCode.OK, JsonSerializer.Serialize(_entries, Web));
                if (path.Contains("/api/absences/", StringComparison.Ordinal))
                    return Json(HttpStatusCode.OK, "[]");
                return Json(HttpStatusCode.NotFound, """{"error":"unexpected GET"}""");
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
            {
                var evaluate = JsonSerializer.Deserialize<EvaluateRequest>(body, Web)!;
                EvaluatedRuleIds.Add(evaluate.RuleId);
                var calculation = _registry.Evaluate(
                    evaluate.RuleId, evaluate.Profile, evaluate.Entries, evaluate.PeriodStart, evaluate.PeriodEnd);
                return Json(
                    calculation.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                    JsonSerializer.Serialize(calculation, Web));
            }

            // evaluate-absence and evaluate-flex are not what this fact asserts on.
            return Json(HttpStatusCode.OK, """{"ok":true}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
