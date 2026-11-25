using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Abstractions;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S148 / TASK-14825-T (B6) — what the year overview costs now that every weekday resolves its own configuration:
/// one timing line, recorded at CI-W5a, no threshold.
///
/// <para>
/// <b>Why, in plain language.</b> TASK-14825 asks the configuration lookup once per weekday (about 780 reads for one
/// year overview of a full-time employee) and registered that as a known limit (performance register F10;
/// TASK-14828 later restored the reuse, and F10 is DONE). This fact gives the limit a figure: it reads one
/// employee's <c>GET /api/balance/{id}/year-overview?year=2026</c> once as a warm-up and then five timed times and
/// prints the median, so the performance register's row for it (O-16) has a number to point at.
/// </para>
///
/// <para>
/// <b>The line.</b> Exactly <c>YEAROVERVIEW-TIMING year=2026 reads_ms=[r1,r2,r3,r4,r5] median_ms=&lt;m&gt;</c> (whole
/// milliseconds, the stopwatch around the request alone), through xUnit's <see cref="ITestOutputHelper"/>. The world:
/// one full-time HK / OK24 employee in STY02 (<see cref="RegressionSeed"/>, every dated row from <c>0001-01-01</c>),
/// the host's clock pinned to Monday 9 November 2026.
/// </para>
///
/// <para>
/// <b>Kind: R (CI) plus a measurement.</b> <b>Red conditions</b>: the status of any read (the warm-up and each timed
/// one) is not 200, so a timing over a failing response cannot pass. Nothing is compared with a limit. Docker-gated:
/// CI-pending, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class YearOverviewTimingTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";
    private const string Employee = "t825yo_emp";

    private readonly ITestOutputHelper _output;
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public YearOverviewTimingTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task YearRead_MedianOfFiveReadsAfterWarmUp_IsRecorded()
    {
        // The host first (its first CreateClient re-runs the startup seeders), the employee after.
        var host = _factory.WithFixedToday(new DateOnly(2026, 11, 9));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken());

        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, Employee, Org, "HK", "OK24");

        // Warm-up, discarded: it must answer, so the five timed reads are not the first of anything.
        using (var warmUp = await client.GetAsync($"/api/balance/{Employee}/year-overview?year=2026"))
            Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

        var reads = new long[5];
        for (var i = 0; i < reads.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.GetAsync($"/api/balance/{Employee}/year-overview?year=2026");
            stopwatch.Stop();
            reads[i] = stopwatch.ElapsedMilliseconds;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the only assertion on a timed read
        }

        var median = reads.OrderBy(r => r).ElementAt(2);
        _output.WriteLine($"YEAROVERVIEW-TIMING year=2026 reads_ms=[{string.Join(",", reads)}] median_ms={median}");
    }

    private static string MintEmployeeToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: Employee, name: Employee, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
    }
}
