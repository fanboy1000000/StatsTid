using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Orchestrator.Services;

namespace StatsTid.Tests.Unit.Orchestrator;

/// <summary>
/// QUAL-007 (S132 TASK-132-3a) — the weekly calculation pipeline must not persist a FAILED
/// Backend data fetch as a COMPLETED calculation.
///
/// <para><b>The defect (baseline behavior):</b> <see cref="WeeklyCalculationPipeline.ExecuteAsync"/>
/// fetched the employee's time-entries and absences from the Backend over HTTP and went straight
/// to <c>ReadAsStringAsync</c> + <c>Deserialize</c> with NO <c>IsSuccessStatusCode</c> check. A
/// 403 (or any error) comes back with a JSON error body; the pipeline deserialized that error
/// body as if it were the data, ran the rule engine over garbage, and returned a result marked
/// <c>Success = true</c> — a wrong result presented as right (a silent domain-correctness
/// failure).</para>
///
/// <para><b>The RED-on-old contract:</b> these tests stub a Backend fetch to a non-2xx with a
/// VALID-JSON error body. On the OLD code the pipeline deserializes that valid JSON fine and
/// returns <c>Success = true</c> (no exception), so <c>Assert.NotNull(ex)</c> FAILS (RED). On the
/// fixed code the pipeline throws, so the same assertions PASS (GREEN). The error body is
/// deliberately valid JSON so the RED failure is the MISSING guard, not an incidental
/// deserialization throw.</para>
///
/// <para><b>Why the surfacing is a throw:</b> the pipeline's sole caller,
/// <c>OrchestratorControlLoop.ExecuteWeeklyCalculation</c>, marks its task <c>"completed"</c>
/// unconditionally whenever <c>ExecuteAsync</c> returns and only its <c>catch</c> block marks it
/// <c>"failed"</c> — it never reads <c>WeeklyCalculationResult.Success</c>. So an exception is the
/// only signal the caller treats as a failed fetch; returning <c>Success=false</c> would still be
/// persisted as a completed calc.</para>
///
/// <para>HTTP mocking mirrors the <c>StubHandler</c> convention in
/// <c>StatsTid.Tests.Unit.Payroll.HttpRuleClassificationProviderTests</c>.</para>
/// </summary>
public sealed class WeeklyCalculationPipelineFetchFailureTests
{
    private const string BackendUrl = "http://backend.test";
    private const string RuleEngineUrl = "http://rule-engine.test";
    private const string EmployeeId = "USR01";

    // A VALID-JSON error body — see the class remark on why validity matters for RED-on-old.
    private const string ForbiddenBody = """{"error":"forbidden","message":"scope denied"}""";

    [Theory]
    [InlineData("time-entries")]
    [InlineData("absences")]
    public async Task ExecuteAsync_WhenBackendFetchReturnsNonSuccess_SurfacesFailure_NotCompletedSuccess(
        string failingDataset)
    {
        // Arrange: the named Backend dataset fetch fails with 403 (+valid JSON error body); the
        // other dataset and every rule-engine call succeed, so the ONLY thing that can make the
        // pipeline fail is the guarded fetch.
        var handler = new RoutingHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;

            if (path.Contains("/api/time-entries/", StringComparison.Ordinal))
                return failingDataset == "time-entries"
                    ? Json(HttpStatusCode.Forbidden, ForbiddenBody)
                    : Json(HttpStatusCode.OK, "[]");

            if (path.Contains("/api/absences/", StringComparison.Ordinal))
                return failingDataset == "absences"
                    ? Json(HttpStatusCode.Forbidden, ForbiddenBody)
                    : Json(HttpStatusCode.OK, "[]");

            // Rule-engine evaluate / evaluate-absence / evaluate-flex all succeed.
            return Json(HttpStatusCode.OK, """{"ok":true}""");
        });

        var pipeline = BuildPipeline(handler);

        // Act: capture whatever the pipeline does — a return value OR a throw.
        var ex = await Record.ExceptionAsync(() => pipeline.ExecuteAsync(Params(), ct: CancellationToken.None));

        // Assert (RED-on-old): baseline returns a Success=true result (no exception) → ex is null
        // → this fails. Fixed code throws → ex is the descriptive HttpRequestException.
        Assert.NotNull(ex);
        var httpEx = Assert.IsType<HttpRequestException>(ex);
        Assert.Equal(HttpStatusCode.Forbidden, httpEx.StatusCode);
        // Descriptive surfacing: names the dataset, the employee, and the status.
        Assert.Contains(failingDataset, httpEx.Message, StringComparison.Ordinal);
        Assert.Contains(EmployeeId, httpEx.Message, StringComparison.Ordinal);
        Assert.Contains("403", httpEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllFetchesSucceed_ReturnsCompletedSuccess()
    {
        // Guards AC#2: the happy path is unchanged when both Backend fetches succeed.
        var handler = new RoutingHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/api/time-entries/", StringComparison.Ordinal)
                || path.Contains("/api/absences/", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "[]");
            return Json(HttpStatusCode.OK, """{"ok":true}""");
        });

        var pipeline = BuildPipeline(handler);

        var result = await pipeline.ExecuteAsync(Params(), ct: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(EmployeeId, result.EmployeeId);
    }

    // ── construction / helpers ─────────────────────────────────────────────

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

    private static Dictionary<string, object> Params() => new()
    {
        ["employeeId"] = EmployeeId,
        ["agreementCode"] = "AC",
        ["okVersion"] = "OK26",
        ["periodStart"] = "2026-05-01",
        ["periodEnd"] = "2026-05-07",
        ["weeklyNormHours"] = 37.0m,
        ["partTimeFraction"] = 1.0m,
        ["previousFlexBalance"] = 0.0m,
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        // disposeHandler:false — the same handler backs the whole test; the pipeline creates one
        // client per ExecuteAsync and must not dispose our shared handler.
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request);
    }
}
