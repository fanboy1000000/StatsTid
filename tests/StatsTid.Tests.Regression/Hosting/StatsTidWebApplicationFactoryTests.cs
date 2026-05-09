using System.Net;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S27 / TASK-2701 smoke proof for <see cref="StatsTidWebApplicationFactory"/>. Boots
/// the real <c>StatsTid.Backend.Api</c> against a per-test Postgres testcontainer,
/// issues one auth-protected GET, then stops the real <c>OutboxPublisher</c> via the
/// pinned-verbatim mechanism. Confirms the harness is wireup-clean before TASK-2710
/// builds its read-your-write D-tests on top of it.
///
/// <para>
/// Smoke shape: GET <c>/api/balance/{employeeId}/summary</c> against the booted
/// host with NO Authorization header. The endpoint is gated by
/// <c>RequireAuthorization("EmployeeOrAbove")</c> (see
/// <c>BalanceEndpoints.cs:193</c>), so the auth middleware short-circuits with
/// <c>401 Unauthorized</c> before the not-found path at <c>BalanceEndpoints.cs:60</c>
/// runs. A 401 here proves: (1) the host booted, (2) JWT bearer middleware is
/// registered and runs, (3) the route mapped, and (4) the connection string was
/// accepted (otherwise the agreement-config seeder at <c>Program.cs:67-73</c> would
/// have failed and CreateClient would have thrown). That's the harness invariant
/// TASK-2710 needs.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class StatsTidWebApplicationFactoryTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();

        // Backend.Api's AgreementConfigSeeder + EntitlementConfigSeeder run during
        // Program.cs host startup against the configured connection. The
        // Segmentation DockerHarness applies a 4-table subset DDL only — for full
        // Backend.Api boot we need every table init.sql defines. ApplyFullSchemaAsync
        // is idempotent (CREATE TABLE IF NOT EXISTS throughout init.sql).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    [Fact]
    public async Task Harness_BootsBackendApi_AndStopsPublisherCleanly()
    {
        // ── (1) Boot proof: CreateClient triggers Program.cs host build, including
        //         the agreement-config + entitlement-config seeders. If the connection
        //         string override didn't land, or the schema wasn't applied, this
        //         throws here.
        var client = _factory.CreateClient();

        // ── (2) Routing + auth-middleware proof: GET an auth-gated endpoint with no
        //         bearer token. We expect 401 Unauthorized — auth fires before the
        //         not-found path the endpoint would otherwise return for a missing
        //         employee. A 401 here pins that the JWT middleware is registered
        //         and the route was mapped.
        var response = await client.GetAsync(
            "/api/balance/EMP-NONEXISTENT/summary?year=2026&month=5");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // ── (3) Stop-publisher proof: the verbatim DI-resolution mechanism returns
        //         without throwing. The functional proof that "stop actually means
        //         stop" lives in TASK-2710's publisher-stall RYW tests; here we
        //         only pin the call signature works.
        await _factory.StopPublisherAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
        {
            await _harness.DisposeAsync();
        }
    }
}
