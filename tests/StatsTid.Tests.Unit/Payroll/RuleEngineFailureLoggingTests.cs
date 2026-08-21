using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// SEC-039 / QUAL-061 (data-confidentiality hygiene): on a failed rule-engine HTTP call,
/// <see cref="PeriodCalculationService"/>'s failure path must NOT log the whole response body.
///
/// <para>
/// Plain-language: when the Rule Engine returns a non-success response, its error body typically
/// echoes the request payload — the employee id plus per-day hours / rates / balances, i.e.
/// confidential employment data. The old code logged that entire body, which turned the
/// application log stream into an unmanaged copy of employment data. The fix logs only a bounded,
/// non-confidential diagnostic (HTTP status + rule id + employee id) so a failure is still
/// triageable without dumping the payload.
/// </para>
///
/// <para>
/// This is a RED-on-old test: the stub Rule Engine returns a non-success response whose body
/// carries a recognizable employment-data sentinel (a rate/balance value). The assertion is that
/// the captured warning log does NOT contain that sentinel (nor the raw body fields) while it DOES
/// still emit the status and rule id. It FAILS on the baseline (full body → sentinel present in
/// the log line) and PASSES after the fix. It drives the private <c>CallTimeRuleAsync</c> helper
/// directly via reflection — the same idiom used by the segmentation regression tests — so the
/// test stays surgical (no planner / event-store / DB setup, none of which the logging path
/// touches).
/// </para>
/// </summary>
public sealed class RuleEngineFailureLoggingTests
{
    // A distinctive token standing in for a confidential per-day rate/balance value. It appears
    // ONLY inside the (echoed) response body, so its presence in a log line proves the whole body
    // was logged.
    private const string Sentinel = "SENSITIVE-RATE-263.55-BALANCE-123.456";

    private static string ErrorBodyEchoingEmploymentData() =>
        // Shape mimics a rule-engine error that echoes the request payload back: employee id plus
        // per-day line items with hours + rate, and a running flex balance — all employment data.
        "{\"error\":\"internal\",\"echo\":{" +
        "\"employeeId\":\"emp-42\"," +
        "\"lineItems\":[{\"date\":\"2026-01-15\",\"hours\":7.4,\"rate\":263.55,\"wageType\":\"1000\"}]," +
        "\"flexBalance\":123.456," +
        $"\"marker\":\"{Sentinel}\"}}}}";

    [Fact]
    public async Task CallTimeRule_OnNonSuccess_DoesNotLogResponseBody_ButLogsDiagnostic()
    {
        // Arrange: a Rule Engine that fails with 500 and echoes employment data in the body.
        var handler = new FixedResponseHandler(
            HttpStatusCode.InternalServerError, ErrorBodyEchoingEmploymentData());
        var httpClient = new HttpClient(handler);

        var capturingLogger = new CapturingLogger<PeriodCalculationService>();
        var pcs = BuildPcs(handler, capturingLogger);

        var profile = new EmploymentProfile
        {
            EmployeeId = "emp-42",
            AgreementCode = "AC",
            OkVersion = "2026.1",
            EmploymentCategory = "FULLTIME",
        };

        const string ruleId = "TIME_RULE";

        // Act: invoke the private failure path directly (it uses only the passed-in client,
        // the configured rule-engine URL, the shared JsonOptions, and the logger).
        var method = typeof(PeriodCalculationService).GetMethod(
            "CallTimeRuleAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(
            pcs,
            new object[]
            {
                httpClient,
                ruleId,
                profile,
                Array.Empty<TimeEntry>(),
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                CancellationToken.None,
            })!;
        await task;

        // Behaviour is unchanged: a non-success response still yields a null result.
        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        Assert.Null(result);

        // A single warning was emitted on the failure path.
        var warning = Assert.Single(capturingLogger.Warnings);

        // Confidentiality: the confidential body must NOT have leaked into the log.
        Assert.DoesNotContain(Sentinel, warning);
        Assert.DoesNotContain("263.55", warning);       // per-day rate
        Assert.DoesNotContain("123.456", warning);      // flex balance
        Assert.DoesNotContain("lineItems", warning);    // no raw body fields at all

        // Diagnosability: status + rule id (and the employee id, an acceptable identifier) remain.
        Assert.Contains("500", warning);
        Assert.Contains(ruleId, warning);
        Assert.Contains("emp-42", warning);
    }

    private static PeriodCalculationService BuildPcs(
        HttpMessageHandler handler, ILogger<PeriodCalculationService> logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();

        // The mapping service, event store, and connection factory are assigned but never
        // dereferenced by the constructor, and are untouched by the rule-call logging path under
        // test — so null! is safe here and keeps the fixture minimal (no DB / event store).
        return new PeriodCalculationService(
            new SingleClientHttpFactory(handler),
            mappingService: null!,
            eventStore: null!,
            connectionFactory: null!,
            configuration,
            logger);
    }

    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FixedResponseHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class SingleClientHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Minimal in-test <see cref="ILogger{T}"/> that records the fully-formatted message
    /// text of each warning-or-above entry (the format placeholders already substituted), so the
    /// test can assert on exactly what would land in the log stream.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<string> Warnings = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
