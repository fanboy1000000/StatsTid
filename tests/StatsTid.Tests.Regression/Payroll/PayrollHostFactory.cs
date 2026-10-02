using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
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
/// <item><b><see cref="IHttpClientFactory"/></b>: by default the calculation's rule-evaluation calls go to the
/// shared Rule Engine stub (<see cref="TestFixtures.DefaultRuleEngineHandler"/>); with the S145
/// <c>useRealRuleEngine</c> option they go to an in-process real Rule Engine (see below). A refused month
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
///
/// <para>
/// <b>S145 / TASK-14503a — the real-engine option (<c>useRealRuleEngine</c>, off by default).</b> The
/// stub above is right for the facts that pin a status or a refusal: they never read a rule result. A fact
/// that pins what the engine COMPUTES (an overtime figure, a norm total, a payout) needs the real Rule Engine,
/// or it would assert the stub's own arithmetic. With the flag set, the host's <see cref="IHttpClientFactory"/>
/// routes every outbound call to an in-process Rule Engine host (<see cref="RuleEngineHost"/>, the house idiom
/// <c>RuleEngineHostFactory : WebApplicationFactory&lt;RuleRegistry&gt;</c>, JWT settings injected into host
/// configuration as in <c>ExternalSendAuthorizationAndEnvelopeTests</c>). The Payroll host forwards the
/// caller's own bearer token to the engine, exactly as production does, so the engine validates it against
/// the same dev signing key. One path is still answered locally: the post-commit delivery POST to mock payroll
/// (<c>/api/payroll/receive</c>) gets a 200, as <c>PayrollHostRecalcBlockedTests.DeliveringFactory</c> does —
/// otherwise a committed export would read as a 422 ExportResult and the fact could not tell "the month
/// calculated and exported" from "delivery refused". The rule-evaluation path is not stubbed at all.
/// </para>
/// </summary>
internal sealed class PayrollHostFactory : WebApplicationFactory<RetroactiveCorrectionService>
{
    internal const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>The post-commit delivery path (<c>PayrollExportService</c> posts to
    /// <c>{ServiceUrls:MockPayroll}/api/payroll/receive</c>) — answered 200 locally under the real-engine
    /// option; every other outbound call goes to the in-process Rule Engine.</summary>
    private const string DeliveryPath = "/api/payroll/receive";

    private readonly string _connectionString;
    private readonly IReadOnlyList<RuleClassification> _classifications;
    private readonly bool _useRealRuleEngine;

    // Created on first use, only under the real-engine option, and disposed with this factory.
    // Derived factories (WithWebHostBuilder) re-run ConfigureWebHost, so the field is shared, not rebuilt.
    private RuleEngineHost? _ruleEngine;

    /// <param name="connectionString">The test's Postgres container.</param>
    /// <param name="classifications">The rule classification set the planner is given (the LIVE set,
    /// <c>new RuleRegistry().GetAll()</c>).</param>
    /// <param name="useRealRuleEngine">Opt-in (default false = the shared Rule Engine stub): route the host's
    /// rule-evaluation calls to an in-process REAL Rule Engine host instead.</param>
    public PayrollHostFactory(
        string connectionString,
        IReadOnlyList<RuleClassification> classifications,
        bool useRealRuleEngine = false)
    {
        _connectionString = connectionString;
        _classifications = classifications;
        _useRealRuleEngine = useRealRuleEngine;
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
            HttpMessageHandler ruleEngineHandler = _useRealRuleEngine
                ? new RealEngineHandler(EnsureRuleEngine().Server.CreateHandler())
                : new TestFixtures.StubHandler(TestFixtures.DefaultRuleEngineHandler);
            services.AddSingleton<IHttpClientFactory>(new SingleClientFactory(ruleEngineHandler));

            var pollers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && (d.ImplementationType == typeof(OutboxPublisher)
                        || d.ImplementationType == typeof(SettlementExportEmitter)))
                .ToList();
            foreach (var poller in pollers)
                services.Remove(poller);
        });
    }

    private RuleEngineHost EnsureRuleEngine() => _ruleEngine ??= new RuleEngineHost();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ruleEngine?.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_ruleEngine is not null)
            await _ruleEngine.DisposeAsync();
        await base.DisposeAsync();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>The REAL Rule Engine (<see cref="RuleRegistry"/> marks its assembly), hosted in-process. JWT
    /// signing key, issuer and audience go into HOST configuration (fires before the engine's
    /// <c>Program.cs</c> reads <c>builder.Configuration</c> for <c>AddStatsTidJwtAuth</c>), so the bearer token
    /// the Payroll host forwards validates against a known key regardless of the ambient environment. The
    /// engine needs no database.</summary>
    private sealed class RuleEngineHost : WebApplicationFactory<RuleRegistry>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                }));

            return base.CreateHost(builder);
        }
    }

    /// <summary>Answers the delivery POST (<see cref="DeliveryPath"/>) with 200 and sends everything else —
    /// every rule-evaluation call — to the in-process Rule Engine's own handler, unchanged.</summary>
    private sealed class RealEngineHandler : DelegatingHandler
    {
        public RealEngineHandler(HttpMessageHandler engineHandler) : base(engineHandler) { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post
                && string.Equals(request.RequestUri?.AbsolutePath, DeliveryPath, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            return base.SendAsync(request, cancellationToken);
        }
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
