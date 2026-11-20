using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14825-T — AC-C1g: the payroll host builds under <c>ValidateOnBuild</c>.
///
/// <para>
/// <b>Why, in plain language.</b> .NET can check, when a service starts, that every registered service has all of
/// its ingredients registered too (<c>ValidateOnBuild</c>, on by default only in the Development environment). The
/// payroll service used to register the configuration lookup without the two repositories it needs (QUAL-184), so
/// in Development it refused to start at all, and the only reason the compose stack ran was that it runs in
/// Production, where that check is off. The lookup the engine switch (T14b) will use is built from those same
/// registrations, so this fact starts the real payroll host the strict way and fails if any registration is missing.
/// </para>
///
/// <para>
/// <b>What is real.</b> The host is the real payroll <c>Program.cs</c> through
/// <c>WebApplicationFactory&lt;RetroactiveCorrectionService&gt;</c> (the marker type and why:
/// <see cref="PayrollHostFactory"/>), in <see cref="Environments.Development"/>, with the dev JWT settings in HOST
/// configuration as <c>PayrollHostFactory.CreateHost</c> supplies them and a connection string that reaches no
/// database (nothing in the build opens one). <b>What is removed:</b> the two background pollers
/// (<see cref="OutboxPublisher"/>, <see cref="SettlementExportEmitter"/>), because a started poller would poll the
/// absent database; they are the only registrations whose ingredients this fact does not validate. Everything else,
/// the configuration lookup and its repositories included, is the production registration.
/// </para>
///
/// <para>
/// <b>Kind: F (Regression, non-Docker)</b>: the host builds without a database. <b>Red conditions</b>: on the base
/// (TASK-14825's parent) building the provider throws <c>InvalidOperationException</c> "Unable to resolve service
/// for type '…AgreementConfigRepository' while attempting to activate '…ConfigResolutionService'" (QUAL-184), so the
/// build assertion fails. It also fails if any of the lookup's repositories loses its registration
/// (<c>AgreementConfigRepository</c>, <c>PositionOverrideRepository</c>, <c>LocalConfigurationRepository</c>,
/// <c>LocalAgreementProfileRepository</c>).
/// </para>
/// </summary>
public sealed class PayrollHostValidateOnBuildTests
{
    private const string Unreachable = "Host=localhost;Port=1;Database=na;Username=na;Password=na;Timeout=1;Command Timeout=1";

    [Fact]
    public void HostBuilds_UnderValidateOnBuild()
    {
        using var factory = new DevelopmentPayrollHost();

        // Building the provider is the assertion: any registration with an unregistered ingredient throws here.
        var services = factory.Services;

        // The check was really on: ValidateOnBuild is the Development default, so prove the environment.
        Assert.True(services.GetRequiredService<IHostEnvironment>().IsDevelopment());

        // The configuration service resolves (its four repositories are registered).
        Assert.NotNull(services.GetRequiredService<ConfigResolutionService>());
    }

    private sealed class DevelopmentPayrollHost : WebApplicationFactory<RetroactiveCorrectionService>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = Unreachable,
                    ["Jwt:SigningKey"] = PayrollHostFactory.DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                    ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureTestServices(services =>
            {
                var pollers = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && (d.ImplementationType == typeof(OutboxPublisher)
                            || d.ImplementationType == typeof(SettlementExportEmitter)))
                    .ToList();
                foreach (var poller in pollers)
                    services.Remove(poller);
            });
        }
    }
}
