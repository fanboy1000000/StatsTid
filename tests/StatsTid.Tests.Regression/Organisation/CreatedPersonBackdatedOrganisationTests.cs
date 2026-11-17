using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T (AC-C13k part 1; NR-P7, DV-7) — a person created today and then backdated still has an
/// institution for the backdated days.
///
/// <para>
/// <b>In plain language.</b> HR creates Anna on Tuesday 10 November 2026, then realises she started on
/// Monday 2 November and moves her start date, profile and agreement code back to that day. Those earlier
/// days need an institution. The create therefore dates her first institution from the start of history
/// (not from today), so every day she is later backdated to resolves to the institution she was created in.
/// </para>
///
/// <para>
/// <b>Fixture.</b> Fixed clock Tuesday 10 November 2026; a GlobalAdmin; Anna is made by the product's user
/// create in STY01, then the three backdating routes are called. The resolver is built on the harness's
/// connection factory. CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class CreatedPersonBackdatedOrganisationTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _host = _factory.WithFixedToday(Clock);
        _ = _host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>R.</b> Anna is created in STY01 on Tuesday 10 November 2026 (no hire date); HR sets her employment
    /// start date to Monday 2 November 2026, her profile from 2 November and her agreement code from
    /// 2 November (each answers 2xx); then the resolver returns <c>OrgId</c> STY01 for every day Monday
    /// 2 to Monday 9 November 2026. <b>Discriminating assertion:</b> the resolver answering STY01 (and not
    /// throwing) for those days. Today the resolver reads the live column (STY01), so it passes and must
    /// keep passing; it breaks if the create dated the organisation row from 10 November, because the
    /// resolver would then throw <c>EmployeeProfileNotFoundException</c> for 2 to 9 November.
    /// </summary>
    [Fact]
    public async Task CreatedTuesday10November2026_BackdatedToMonday2November_ResolverReturnsCreatingOrganisation()
    {
        var cs = _harness.ConnectionString;
        var client = GlobalAdminClient(_host);
        var annaId = await CreateUserAsync(client, "STY01");
        var start = new DateOnly(2026, 11, 2);

        // 1) employment start date (If-Match = the users version).
        var startRsp = await PutWithIfMatchAsync(client, $"/api/admin/employees/{annaId}/employment-start-date",
            await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", annaId)),
            new { employmentStartDate = start.ToString("yyyy-MM-dd") });
        Assert.True(IsSuccess(startRsp.StatusCode), $"start-date PUT answered {(int)startRsp.StatusCode}.");

        // 2) the profile from 2 November (If-Match = the profile's live version).
        var profile = await client.GetAsync($"/api/admin/employee-profiles/{annaId}");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var profileVersion = (await profile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64();
        var profileRsp = await PutWithIfMatchAsync(client, $"/api/admin/employee-profiles/{annaId}", profileVersion,
            new
            {
                effectiveFrom = start.ToString("yyyy-MM-dd"),
                partTimeFraction = 1.000m,
                position = (string?)null,
                employmentCategory = "Standard",
            });
        Assert.True(IsSuccess(profileRsp.StatusCode), $"profile PUT answered {(int)profileRsp.StatusCode}.");

        // 3) the agreement code from 2 November (If-Match = the users version).
        var agreementRsp = await PutWithIfMatchAsync(client, $"/api/admin/users/{annaId}/agreement-code",
            await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", annaId)),
            new { agreementCode = "AC", effectiveFrom = start.ToString("yyyy-MM-dd") });
        Assert.True(IsSuccess(agreementRsp.StatusCode), $"agreement-code PUT answered {(int)agreementRsp.StatusCode}.");

        var resolver = new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory));
        for (var day = start; day <= new DateOnly(2026, 11, 9); day = day.AddDays(1))
        {
            var resolved = await resolver.GetByEmployeeIdAtAsync(annaId, day);
            Assert.NotNull(resolved);
            Assert.Equal("STY01", resolved!.OrgId);
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static async Task<HttpResponseMessage> PutWithIfMatchAsync(
        HttpClient client, string url, long version, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(req);
    }
}
