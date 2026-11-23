using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Abstractions;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14828-T (R4 of TASK-14825's Step 5a cycle 1) - what a complete Skema month read and a Skema save cost now
/// that the per-day norm calculator reuses one configuration lookup per stretch of unchanged days: two timing lines, recorded
/// at CI-W5, no threshold.
///
/// <para>
/// <b>Why, in plain language.</b> The year overview already has a timing fact (<c>YearOverviewTimingTests</c>); the Skema
/// month read (it serves the daily norm, the consumption basis, the month-end figure and the minimum rest, so it resolves
/// the configuration several times) and the Skema save (one lookup per absence weekday) are the other two screens that ask the
/// lookup per day. Each fact reads or saves once as a warm-up and then five timed times and prints the median, so the performance
/// register's row for the limit has figures for all three screens.
/// </para>
///
/// <para>
/// <b>The lines.</b> Exactly <c>SKEMA-MONTH-TIMING month=2026-03 reads_ms=[r1,r2,r3,r4,r5] median_ms=&lt;m&gt;</c> and
/// <c>SKEMA-SAVE-TIMING month=2026-11 saves_ms=[s1,s2,s3,s4,s5] median_ms=&lt;m&gt;</c> (whole milliseconds, the stopwatch around the
/// request alone), through <see cref="ITestOutputHelper"/>. The month read world has HK/OK24 republished with a 32-hour week from
/// Monday 16 March 2026, so the month has two stretches; the save world is the current month (clock Monday 9 November 2026,
/// a month an employee may still save) with sick days on 2 to 6 November. The number of configuration reads is not counted by
/// these facts (no statement counter in the fixture); TASK-14828's report holds its own local counts.
/// </para>
///
/// <para>
/// <b>Kind: R (CI) plus a measurement.</b> <b>Red conditions</b>: the status of any read or save (the warm-up and each timed
/// one) is not 200, or the month read does not hold 31 daily-norm rows, so a timing over a failing response cannot pass. Nothing
/// is compared with a limit. Docker-gated: CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaTimingTests : DatedConfigDockerFixture
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";

    private static readonly DateOnly Clock = new(2026, 11, 9);

    private readonly ITestOutputHelper _output;

    public SkemaTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task MonthRead_MedianOfFiveReadsAfterWarmUp_IsRecorded()
    {
        const string employee = "t828tm_month";
        using var admin = AdminAt(Clock); // boots a host (the seeders) before anyone is seeded
        await CreateAndPublishAsync(admin, ConfigBody("HK", "OK24", weeklyNorm: 32m), "2026-03-16");
        using var client = EmployeeClient(employee);
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employee, Org, "HK", "OK24");

        using (var warmUp = await client.GetAsync($"/api/skema/{employee}/month?year=2026&month=3"))
            Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

        var reads = new long[5];
        for (var i = 0; i < reads.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.GetAsync($"/api/skema/{employee}/month?year=2026&month=3");
            stopwatch.Stop();
            reads[i] = stopwatch.ElapsedMilliseconds;

            // Non-vacuity on every timed read (not a threshold): a full month answered.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(31, body.GetProperty("dailyNorm").GetArrayLength());
        }

        _output.WriteLine(
            $"SKEMA-MONTH-TIMING month=2026-03 reads_ms=[{string.Join(",", reads)}] median_ms={reads.OrderBy(r => r).ElementAt(2)}");
    }

    [Fact]
    public async Task Save_MedianOfFiveSavesAfterWarmUp_IsRecorded()
    {
        const string employee = "t828tm_save";
        using var admin = AdminAt(Clock); // boots a host (the seeders) before anyone is seeded
        using var client = EmployeeClient(employee);
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employee, Org, "HK", "OK26");

        var absences = Enumerable.Range(2, 5)
            .Select(day => new { date = new DateOnly(2026, 11, day), absenceType = "SICK_DAY", hours = 7.4m })
            .ToArray();

        using (var warmUp = await client.PostAsJsonAsync($"/api/skema/{employee}/save", new { year = 2026, month = 11, absences }))
            Assert.True(warmUp.StatusCode == HttpStatusCode.OK, $"save warm-up expected 200, got {(int)warmUp.StatusCode}: {await warmUp.Content.ReadAsStringAsync()}");

        var saves = new long[5];
        for (var i = 0; i < saves.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync($"/api/skema/{employee}/save", new { year = 2026, month = 11, absences });
            stopwatch.Stop();
            saves[i] = stopwatch.ElapsedMilliseconds;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the only assertion on a timed save
        }

        _output.WriteLine(
            $"SKEMA-SAVE-TIMING month=2026-11 saves_ms=[{string.Join(",", saves)}] median_ms={saves.OrderBy(r => r).ElementAt(2)}");
    }

    /// <summary>An employee client on a Backend host whose clock is pinned to <see cref="Clock"/>.</summary>
    private HttpClient EmployeeClient(string employeeId)
    {
        var client = Factory.WithFixedToday(Clock).CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
