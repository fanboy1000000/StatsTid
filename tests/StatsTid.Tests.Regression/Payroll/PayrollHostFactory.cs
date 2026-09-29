using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S144 / TASK-14403 — boots the REAL Payroll host (<c>src/Integrations/StatsTid.Integrations.Payroll/Program.cs</c>)
/// in-process, so a test can call <c>/api/payroll/recalculate</c> and
/// <c>/api/payroll/calculate-and-export</c> through the host's own routing, auth, DI and
/// handlers — the only level at which the host's DI wiring of
/// <see cref="PeriodCalculationService"/>'s optional <c>UserAgreementCodeRepository</c> is
/// observable (a service-level test builds the service by hand and would pass with a dropped
/// registration).
///
/// <para>
/// <b>Marker type, not <c>public partial class Program</c>.</b> The Regression project references
/// both the Backend host (which already declares <c>public partial class Program</c>) and this
/// host; a second public global <c>Program</c> would make every <c>WebApplicationFactory&lt;Program&gt;</c>
/// in the suite ambiguous (CS0433). <see cref="RetroactiveCorrectionService"/> is a public type in
/// the Payroll host's assembly, which is all the factory needs to find the entry point — the
/// house idiom for a second host (<c>ExternalHostFactory</c>, <c>RuleEngineHostFactory</c>,
/// <c>OrchestratorHostFactory</c>).
/// </para>
///
/// <para>
/// <b>What is swapped, and why each swap leaves the tested path real.</b>
/// <list type="bullet">
/// <item><b>Configuration</b> (<see cref="CreateHost"/>): <c>ConnectionStrings:EventStore</c> → the
/// test's Postgres container, and the dev JWT settings, injected into HOST configuration so they
/// land before <c>Program.cs</c> reads them (the <c>DbConnectionFactory</c> singleton and
/// <c>AddStatsTidJwtAuth</c>).</item>
/// <item><b><see cref="IRuleClassificationProvider"/></b>: the host fetches classifications over
/// HTTP from the Rule Engine; here an in-memory provider over the set the test passes (the LIVE
/// set, <c>new RuleRegistry().GetAll()</c>) — the same data the HTTP endpoint serves.</item>
/// <item><b><see cref="IHttpClientFactory"/></b>: the calculation's rule-evaluation calls go to the
/// shared Rule Engine stub (<see cref="TestFixtures.DefaultRuleEngineHandler"/>). A refused month
/// never reaches it (the plan is built first); it exists so that when the refusal is MUTATED away
/// the calculation completes and the endpoint answers 200, making the red a clean status flip
/// rather than an incidental connection error.</item>
/// <item><b>The two background pollers</b> (<see cref="OutboxPublisher"/>,
/// <see cref="SettlementExportEmitter"/>) are removed: neither is on the request path, and a test
/// should not race a poller over the same database.</item>
/// </list>
/// Everything else — the planner, <see cref="PeriodCalculationService"/>, the repositories, the
/// endpoint handlers, authorization and the audit middleware — is the production registration.
/// </para>
/// </summary>
internal sealed class PayrollHostFactory : WebApplicationFactory<RetroactiveCorrectionService>
{
    internal const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private readonly string _connectionString;
    private readonly IReadOnlyList<RuleClassification> _classifications;

    public PayrollHostFactory(string connectionString, IReadOnlyList<RuleClassification> classifications)
    {
        _connectionString = connectionString;
        _classifications = classifications;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = _connectionString,
                ["Jwt:SigningKey"] = DevSigningKey,
                ["Jwt:Issuer"] = "statstid",
                ["Jwt:Audience"] = "statstid",
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            }));

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production, as the compose stack runs this host (docker/docker-compose.yml sets no
        // ASPNETCORE_ENVIRONMENT for `payroll`). WebApplicationFactory defaults to Development, which
        // turns on DI ValidateOnBuild — and the host registers ConfigResolutionService without the
        // AgreementConfigRepository / PositionOverrideRepository its constructors need (a dormant,
        // never-resolved registration: nothing in this host consumes it). Under Development the host
        // therefore refuses to build at all; under Production (the real deployment) it builds and the
        // registration is simply never activated. The JWT key is supplied explicitly above, so the
        // Production fail-fast in AddStatsTidJwtAuth does not fire.
        builder.UseEnvironment(Environments.Production);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRuleClassificationProvider>();
            services.AddSingleton<IRuleClassificationProvider>(new InMemoryRuleClassificationProvider(_classifications));

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new SingleClientFactory(new TestFixtures.StubHandler(TestFixtures.DefaultRuleEngineHandler)));

            var pollers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && (d.ImplementationType == typeof(OutboxPublisher)
                        || d.ImplementationType == typeof(SettlementExportEmitter)))
                .ToList();
            foreach (var poller in pollers)
                services.Remove(poller);
        });
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Copied from the private twins (promoting one would touch a file outside this
    /// task's scope): an in-memory <see cref="IRuleClassificationProvider"/> over a fixed set.</summary>
    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
