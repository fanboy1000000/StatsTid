using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Orchestrator.Services;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S133 / TASK-13306 (QUAL-095) — rewired from verification theater. The legacy version of this file
/// declared its OWN private <c>GetServiceUrl</c> switch inside the test body and asserted THAT — the
/// shipped <see cref="TaskDispatcher"/> was never touched, so the routing table could drift or break
/// and every test stayed GREEN (a re-implementing "spec" test, PAT-014 corollary).
///
/// <para>These tests now construct the REAL <see cref="TaskDispatcher"/> and drive its
/// <see cref="TaskDispatcher.GetServiceUrl"/>. Falsifiability: change a route key or a default URL in
/// <c>TaskDispatcher</c> and the matching assertion goes RED; the added
/// <see cref="GetServiceUrl_ConfigOverride_WinsOverBuiltInDefault"/> covers the
/// <c>configuration["ServiceUrls:*"] ?? default</c> fallback the local switch could never exercise.
/// <c>GetServiceUrl</c> only reads the route map built in the constructor, so the injected
/// <see cref="IHttpClientFactory"/> and logger are never invoked (a no-op factory suffices).</para>
/// </summary>
public class TaskDispatcherTests
{
    [Fact]
    public void GetServiceUrl_RuleEvaluation_GoesToRuleEngineDefault()
    {
        var dispatcher = BuildDispatcher();
        Assert.Equal("http://rule-engine:8080", dispatcher.GetServiceUrl("rule-evaluation"));
    }

    [Fact]
    public void GetServiceUrl_PayrollExport_GoesToPayrollDefault()
    {
        var dispatcher = BuildDispatcher();
        Assert.Equal("http://payroll:8080", dispatcher.GetServiceUrl("payroll-export"));
    }

    [Fact]
    public void GetServiceUrl_ExternalIntegration_GoesToExternalDefault()
    {
        var dispatcher = BuildDispatcher();
        Assert.Equal("http://external:8080", dispatcher.GetServiceUrl("external-integration"));
    }

    [Fact]
    public void GetServiceUrl_Unknown_ReturnsNull()
    {
        var dispatcher = BuildDispatcher();
        Assert.Null(dispatcher.GetServiceUrl("unknown-task"));
    }

    /// <summary>
    /// The config-override path the old local switch could never reach: when
    /// <c>ServiceUrls:RuleEngine</c> is configured, <c>GetServiceUrl</c> returns it instead of the
    /// built-in default. RED if the constructor stops reading configuration (e.g. hardcodes the
    /// default), which is the exact regression the shipped fallback exists to prevent.
    /// </summary>
    [Fact]
    public void GetServiceUrl_ConfigOverride_WinsOverBuiltInDefault()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test:9999",
            })
            .Build();

        var dispatcher = BuildDispatcher(config);

        Assert.Equal("http://rule-engine.test:9999", dispatcher.GetServiceUrl("rule-evaluation"));
        // Unconfigured routes still fall back to their defaults.
        Assert.Equal("http://payroll:8080", dispatcher.GetServiceUrl("payroll-export"));
    }

    // ── construction / helpers ─────────────────────────────────────────────────

    private static TaskDispatcher BuildDispatcher(IConfiguration? configuration = null)
        => new(
            new NoopHttpClientFactory(),
            NullLogger<TaskDispatcher>.Instance,
            configuration ?? new ConfigurationBuilder().Build());

    // GetServiceUrl never touches the factory — the route map is built once in the constructor from
    // configuration. This stub exists only to satisfy the constructor signature.
    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
