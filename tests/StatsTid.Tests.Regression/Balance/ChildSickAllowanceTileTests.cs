using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2h (1) — the server half: the year overview serves the child's-sick allowance
/// PER ILLNESS, not a yearly remainder. In plain terms: the child's-sick tile on the Årsoversigt no longer shows "days
/// left this year" (there is no yearly cap any more); it shows how many sick days one illness gives and whether the
/// call-home day exists, at today's OK version. The page's own rendering is TASK-14812's vitest (AC-S0-2h (2)); this
/// fact pins what the endpoint serves.
///
/// <para>Expected values come from the agreement texts, never from the code: an HK employee opted in to the child's sick
/// days gets <b>3 days and the call-home day</b> when today is in OK26 (Wednesday 10 June 2026) and <b>2 days and no
/// call-home day</b> when today is in OK24 (Monday 2 March 2026). The host's clock is fixed through the
/// <see cref="FixedTimeProvider"/> seam (the precedent is <c>YearOverviewTests</c>); the employee is seeded after the
/// derived host's first boot. Docker-gated, <b>CI-pending</b> (CI-W2); never run locally.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickAllowanceTileTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// An opted-in HK employee reads the year overview of 2026. With today Wednesday 10 June 2026 (OK26) the tile serves
    /// <c>childSickDaysPerIllness = 3</c> and <c>childSickCallHomeDay = true</c>; with today Monday 2 March 2026 (OK24)
    /// it serves 2 and <c>false</c>. The tile's other eligibility member, <c>childSickEligible</c>, is true in both.
    ///
    /// <para><b>Kind F.</b> Red conditions: the first <c>GetProperty("childSickDaysPerIllness")</c> assertion. The base
    /// serves <c>childSickRemaining</c> and neither new member, so the property is absent and the read throws.</para>
    /// </summary>
    [Fact]
    public async Task OptedIn_ServesDaysPerIllnessAndCallHome()
    {
        var ok26 = await ReadTilesAsync(new DateOnly(2026, 6, 10), "OK26");
        var ok24 = await ReadTilesAsync(new DateOnly(2026, 3, 2), "OK24");

        Assert.Multiple(
            () => Assert.True(ok26.GetProperty("childSickEligible").GetBoolean()),
            () => Assert.Equal(3, ok26.GetProperty("childSickDaysPerIllness").GetInt32()),
            () => Assert.True(ok26.GetProperty("childSickCallHomeDay").GetBoolean()),
            () => Assert.True(ok24.GetProperty("childSickEligible").GetBoolean()),
            () => Assert.Equal(2, ok24.GetProperty("childSickDaysPerIllness").GetInt32()),
            () => Assert.False(ok24.GetProperty("childSickCallHomeDay").GetBoolean()));
    }

    /// <summary>The <c>tiles</c> member of the year overview for a fresh opted-in HK employee, on a host whose clock reads <paramref name="today"/>.</summary>
    private async Task<JsonElement> ReadTilesAsync(DateOnly today, string okVersion)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(today))));
        var client = derived.CreateClient(); // boots the derived host; the employee is seeded after it

        var emp = "emp_s148_tile_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, emp, OrgId, "HK", okVersion, partTimeFraction: 1.000m, ensureOrg: false);
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO employee_entitlement_eligibility (employee_id, entitlement_type, eligible, effective_from)
                VALUES (@e, 'CHILD_SICK', TRUE, '0001-01-01')
                """, conn);
            cmd.Parameters.AddWithValue("e", emp);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: emp, name: emp, role: StatsTidRoles.Employee, agreementCode: "HK", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var rsp = await client.GetAsync($"/api/balance/{emp}/year-overview?year={today.Year}");
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("tiles").Clone();
    }
}
