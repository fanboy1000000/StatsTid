using System.Net;
using System.Net.Http.Json;
using StatsTid.Tests.Regression.Hosting;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C13k (2); NR-P7, DV-7) - a person created today and backdated is paid for the backdated
/// days.
///
/// <para>
/// <b>In plain language.</b> HR creates Anna on Tuesday 10 November 2026, then realises she started on Monday 2
/// November and moves her start date, profile and agreement code back to that day. Payroll must calculate November
/// with no gap: the days from 2 to 9 November need an institution (her creating one) and a configuration, and both are
/// there.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class CreatedPersonBackdatedPayrollTests : DatedPayrollFixture
{
    private static readonly DateOnly Nov01 = new(2026, 11, 1);
    private static readonly DateOnly Nov02 = new(2026, 11, 2);
    private static readonly DateOnly Nov09 = new(2026, 11, 9);
    private static readonly DateOnly Nov30 = new(2026, 11, 30);

    /// <summary>
    /// <b>Kind R (CI).</b> AC-C13k (2). Anna is created through the user create on Tuesday 10 November 2026 (clock injected)
    /// in STY01; HR moves her employment start date, profile and agreement code to Monday 2 November 2026 (the three
    /// routes, TASK-14820-T's resolver fact is part 1). November 2026 (opens on a Sunday; October holds none of her
    /// employed days, so no previous month is needed) with 7.4 h on every weekday from 2 November, November APPROVED:
    /// <c>calculate-and-export</c> answers <b>200</b>, the body carries no <c>employment_record_gap</c>, and the export
    /// lines carry NORMAL_HOURS for 2 to 9 November (the discriminating assertion: the 200 with lines for the backdated days).
    ///
    /// <para>
    /// <b>Red conditions.</b> R: this holds before the stack as well (the organisation came from the live column). It
    /// fails if the create dated her first institution from 10 November (the days 2-9 would have no organisation and the
    /// month would be refused as a record gap) or if payroll's per-day configuration lookup could not resolve those days.
    /// </para>
    /// </summary>
    [Fact]
    public async Task November2026_CalculatesWithNoRecordGap()
    {
        var host = BackendHostAt(new DateOnly(2026, 11, 10));
        var client = GlobalAdminClient(host);
        var annaId = await CreateUserAsync(client, "STY01");
        var cs = ConnectionString;
        var start = Nov02;

        await PutWithIfMatchAsync(client, $"/api/admin/employees/{annaId}/employment-start-date",
            await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", annaId)),
            new { employmentStartDate = "2026-11-02" });
        using (var profile = await client.GetAsync($"/api/admin/employee-profiles/{annaId}"))
        {
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            var profileVersion = (await profile.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("version").GetInt64();
            await PutWithIfMatchAsync(client, $"/api/admin/employee-profiles/{annaId}", profileVersion,
                new { effectiveFrom = "2026-11-02", partTimeFraction = 1.000m, position = (string?)null, employmentCategory = "Standard" });
        }
        await PutWithIfMatchAsync(client, $"/api/admin/users/{annaId}/agreement-code",
            await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", annaId)),
            new { agreementCode = "AC", effectiveFrom = "2026-11-02" });

        await SeedWeekdayHoursAsync(annaId, "AC", "OK26", start, Nov30);
        await SeedApprovedMonthAsync(annaId, "STY01", "AC", "OK26", Nov01, Nov30);

        var response = await CalculateAndExportAsync(annaId, "AC", "OK26", "STY01", Nov01, Nov30);

        AssertOk(response);
        Assert.DoesNotContain("employment_record_gap", response.Json, StringComparison.Ordinal);
        var result = ReadResult(response.Json);
        Assert.Equal(21 * 7.4m, result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").Sum(l => l.Hours));
        Assert.True(
            result.ExportLines.Any(l => l.SourceTimeType == "NORMAL_HOURS" && l.PeriodStart <= Nov02 && l.PeriodEnd >= Nov09),
            "expected NORMAL_HOURS export lines covering 2 to 9 November 2026");
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> BackendHostAt(DateOnly today)
    {
        var host = Factory.WithFixedToday(today);
        _ = host.CreateClient(); // boot the seeders before anyone is seeded (PAT-008)
        return host;
    }

    private static async Task PutWithIfMatchAsync(HttpClient client, string url, long version, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var response = await client.SendAsync(request);
        Assert.True((int)response.StatusCode is >= 200 and < 300,
            $"PUT {url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
