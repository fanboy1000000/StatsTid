using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Abstractions;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S148 / TASK-14825-T — N-3: how long the leader's Teamoversigt takes to answer for one month, recorded as a
/// MEASUREMENT (no threshold is asserted), so CI can compare the figure before and after the engine switch (T14b).
///
/// <para>
/// <b>Why, in plain language.</b> The T14b stack will compute each employee's monthly norm day by day from their dated
/// records, where today's Teamoversigt resolves one weekly norm per distinct agreement and OK version. That may be
/// slower. This fact reads the team overview for March 2026 on a 30-person and a 300-person team, once as a warm-up
/// and then five timed times, and prints the median. CI-W5a records the "before" line on the code just before T14b;
/// TASK-14828-T re-runs this fact unchanged for the "after" line. By the proposal NR-P3 a 300-person median that more
/// than doubles, or exceeds 2 seconds, becomes a finding in the performance register.
/// </para>
///
/// <para>
/// <b>The world.</b> One leader (<c>LOCAL_LEADER</c> on the seeded organisation STY02) and N employees on STY02, each a
/// full-time HK employee with <c>users.ok_version</c> OK24 and the full dated record
/// (<see cref="RegressionSeed.SeedEmployeeAsync(NpgsqlConnection, string, string, string, string, decimal, DateOnly?, string?, bool, CancellationToken)"/>:
/// the <c>users</c>, <c>user_organisations</c>, <c>user_agreement_codes</c> and <c>employee_profiles</c> rows, the
/// dated ones from <c>0001-01-01</c>, fraction 1.000), each reporting PRIMARY to the leader. The full record is there
/// because the T14b stack resolves the norm from each employee's dated record, so the "after" figure must exercise
/// that path. The seeding is not timed.
/// </para>
///
/// <para>
/// <b>The line.</b> Exactly <c>TEAMOVERSIGT-TIMING roster=&lt;N&gt; month=2026-03 reads_ms=[r1,r2,r3,r4,r5]
/// median_ms=&lt;m&gt;</c> (whole milliseconds, the stopwatch around the request alone), through xUnit's
/// <see cref="ITestOutputHelper"/>. The test class name and method name are contract: TASK-14828-T re-runs them.
/// </para>
///
/// <para>
/// <b>Kind: R (CI) plus a measurement.</b> <b>Red conditions</b> (the discriminating assertions, none a threshold),
/// on every timed read: the status is not 200; the response does not hold exactly N employee rows; any row's
/// <c>normExpected</c> is not 162.80 (March 2026: 22 weekdays x 7.40, true before T14b as 22 x
/// <c>DayNorm.ForWeekday(37, 1.0)</c> and after it as each day's 7.40 summed), so a timing over an empty or failing
/// response cannot pass. Docker-gated: CI-pending, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TeamOverviewNormTimingTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";
    private const string Leader = "t825tm_lead";

    private readonly ITestOutputHelper _output;
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public TeamOverviewNormTimingTests(ITestOutputHelper output) => _output = output;

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

    [Theory]
    [InlineData(30)]
    [InlineData(300)]
    public async Task MonthRead_MedianOfFiveReadsAfterWarmUp_IsRecorded(int roster)
    {
        // The host first (its first CreateClient re-runs the startup seeders), the world after.
        var host = _factory.WithFixedToday(new DateOnly(2026, 11, 9));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintLeaderToken());

        await SeedRosterAsync(roster);

        // Warm-up, discarded: it must answer, so the five timed reads are not the first of anything.
        using (var warmUp = await client.GetAsync("/api/approval/team-overview?year=2026&month=3"))
            Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

        var reads = new long[5];
        for (var i = 0; i < reads.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.GetAsync("/api/approval/team-overview?year=2026&month=3");
            stopwatch.Stop();
            reads[i] = stopwatch.ElapsedMilliseconds;

            // Non-vacuity on every timed read (not a threshold): the answer is the full, correct roster.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var rows = body.GetProperty("employees").EnumerateArray().ToList();
            Assert.Equal(roster, rows.Count);
            Assert.All(rows, row => Assert.Equal(162.80m, row.GetProperty("normExpected").GetDecimal()));
        }

        var median = reads.OrderBy(r => r).ElementAt(2);
        _output.WriteLine(
            $"TEAMOVERSIGT-TIMING roster={roster} month=2026-03 reads_ms=[{string.Join(",", reads)}] median_ms={median}");
    }

    /// <summary>The leader and N full-time HK / OK24 employees reporting PRIMARY to the leader (not timed).</summary>
    private async Task SeedRosterAsync(int roster)
    {
        var employees = Enumerable.Range(0, roster).Select(i => $"t825tm_e{i:D3}").ToArray();

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(conn, Leader, Org, "HK", "OK24");
            foreach (var employee in employees)
                await RegressionSeed.SeedEmployeeAsync(conn, employee, Org, "HK", "OK24");

            await using var roles = new NpgsqlCommand(
                """
                INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
                VALUES (@leader, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
                ON CONFLICT DO NOTHING
                """, conn);
            roles.Parameters.AddWithValue("leader", Leader);
            roles.Parameters.AddWithValue("org", Org);
            await roles.ExecuteNonQueryAsync();

            await using var employeeRoles = new NpgsqlCommand(
                """
                INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
                SELECT u, 'EMPLOYEE', @org, 'ORG_ONLY', 'TEST' FROM unnest(@ids) AS u
                ON CONFLICT DO NOTHING
                """, conn);
            employeeRoles.Parameters.AddWithValue("org", Org);
            employeeRoles.Parameters.AddWithValue("ids", employees);
            await employeeRoles.ExecuteNonQueryAsync();
        }

        var reportingLines = new ReportingLineRepository(new DbConnectionFactory(_harness.ConnectionString));
        foreach (var employee in employees)
        {
            await reportingLines.AssignAsync(null, new ReportingLineModel
            {
                ReportingLineId = Guid.Empty,
                EmployeeId = employee,
                ManagerId = Leader,
                OrganisationId = Org,
                Relationship = "PRIMARY",
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Source = "MANUAL",
                Version = 0,
                CreatedBy = "TEST",
            });
        }
    }

    private static string MintLeaderToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: Leader, name: Leader, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, Org, "ORG_ONLY") });
    }
}
