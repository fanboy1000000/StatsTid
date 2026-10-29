using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatsTid.Infrastructure.Security;
using StatsTid.Orchestrator.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Security;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S147 / TASK-14703-T (AC-230-16's host half; BC-6; mutation M-10) — each of the three hosts that
/// register <see cref="OrgScopeValidator"/> (Backend, Payroll, Orchestrator) must be able to BUILD it.
///
/// <para><b>Why this exists, in plain language.</b> The validator now takes the server clock
/// (<see cref="TimeProvider"/>) for its departed-self clause. Before S147 only the Backend registered a
/// <see cref="TimeProvider"/>. If the Payroll or the Orchestrator host forgot to add one, the host would
/// still START (the container builds lazily) and the break would show only at the first request that
/// needs the validator. So each fact resolves the validator from the host's own services — the same
/// resolution a request does.</para>
///
/// <para><b>Kind N</b> for all three (each names the new constructor dependency; the facts resolve
/// <see cref="OrgScopeValidator"/> whose constructor this sprint changed). Red conditions (argued here
/// because Docker facts cannot be run locally): <b>M-10</b> — delete the
/// <c>AddSingleton(TimeProvider.System)</c> line from the host's <c>Program.cs</c> (Payroll and
/// Orchestrator; for the Backend, the line at its <c>Program.cs</c> TimeProvider registration). Then
/// <c>GetRequiredService&lt;OrgScopeValidator&gt;()</c> throws <c>InvalidOperationException: Unable to
/// resolve service for type 'System.TimeProvider' ...</c> and the fact fails with that exception, not a
/// compile error. (Under the mutation the earlier constructor, without a clock, is gone, so no other
/// path can satisfy the dependency.)</para>
///
/// <para>Docker-gated: the hosts connect to Postgres at start (Backend seeders), so the three share one
/// container. CI-pending until CI-W2.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TimeProviderRegistrationHostTests : IClassFixture<DepartedSelfDbFixture>
{
    private const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private readonly DepartedSelfDbFixture _fx;
    public TimeProviderRegistrationHostTests(DepartedSelfDbFixture fx) => _fx = fx;

    /// <summary>Kind N (M-10). The Backend host builds the validator with a registered clock.</summary>
    [Fact]
    public async Task Backend_HostStarts_ValidatorResolves()
    {
        await using var factory = new StatsTidWebApplicationFactory(_fx.ConnectionString);
        _ = factory.CreateClient(); // boots the host and its seeders
        Assert.NotNull(factory.Services.GetRequiredService<OrgScopeValidator>());
        Assert.NotNull(factory.Services.GetRequiredService<TimeProvider>());
    }

    /// <summary>Kind N (M-10). The Payroll host (which gained <c>AddSingleton(TimeProvider.System)</c> in
    /// S147) builds the validator.</summary>
    [Fact]
    public async Task Payroll_HostStarts_ValidatorResolves()
    {
        await using var factory = new PayrollHostFactory(_fx.ConnectionString, new RuleRegistry().GetAll());
        _ = factory.CreateClient();
        Assert.NotNull(factory.Services.GetRequiredService<OrgScopeValidator>());
        Assert.NotNull(factory.Services.GetRequiredService<TimeProvider>());
    }

    /// <summary>Kind N (M-10). The Orchestrator host (which gained <c>AddSingleton(TimeProvider.System)</c>
    /// in S147) builds the validator. A minimal factory mirroring the private one in
    /// <c>OrchestratorTaskReadAuthorizationTests</c>.</summary>
    [Fact]
    public async Task Orchestrator_HostStarts_ValidatorResolves()
    {
        await using var factory = new OrchestratorStartFactory(_fx.ConnectionString);
        _ = factory.CreateClient();
        Assert.NotNull(factory.Services.GetRequiredService<OrgScopeValidator>());
        Assert.NotNull(factory.Services.GetRequiredService<TimeProvider>());
    }

    private sealed class OrchestratorStartFactory(string connectionString)
        : WebApplicationFactory<OrchestratorControlLoop>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = connectionString,
                    ["Jwt:SigningKey"] = DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                }));
            return base.CreateHost(builder);
        }
    }
}
