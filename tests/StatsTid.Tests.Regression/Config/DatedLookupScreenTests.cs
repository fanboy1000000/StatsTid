using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14825-T — AC-C1o (2), (3), (4): each screen reads the configuration "as it was on the day it shows".
///
/// <para>
/// <b>Why, in plain language.</b> TASK-14825 makes the overtime screen and the Skema month ask the configuration
/// lookup for a specific day. Two examples: the overtime compensation model for a year is the one in force on
/// 1 January of that year (a version published for November does not rewrite the January answer); and a Skema month
/// in which a new agreement version, or an institution's own local profile, starts on Monday 16 March 2026 shows 7.40
/// hours for the 13th and the version's own figure from the 16th. The facts below pin exactly those day-by-day
/// answers.
/// </para>
///
/// <para>
/// <b>Fixtures.</b> A GlobalAdmin client on a host pinned to Monday 9 November 2026 publishes the versions through the
/// real routes: the clone route copies the seeded version of the key as a DRAFT (so the new version equals the seed
/// in every member), one literal SQL statement then sets the single member the fact varies on that DRAFT (the
/// create and edit routes' request has no <c>defaultCompensationModel</c> member, and a weekly norm is set the same
/// way for symmetry), and the publish route dates it. Nothing is exported, so the publishes write no hand-off.
/// Employees are seeded with <see cref="RegressionSeed"/> (every dated row from <c>0001-01-01</c>) AFTER the host's
/// first <c>CreateClient()</c> (the boot-order rule on <c>StatsTidWebApplicationFactory.WithFixedToday</c>). The
/// employee's own requests go through the same host with the employee's token. Each fact builds its own world on a
/// fresh database. Docker-gated: CI-pending until CI-W5a. A run on a throwaway local PostgreSQL 16 cluster is a
/// pre-check, not CI evidence.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedLookupScreenTests : DatedConfigDockerFixture
{
    private static readonly DateOnly Monday9Nov2026 = new(2026, 11, 9);
    private const string Ok24 = "OK24";

    /// <summary>
    /// AC-C1o (2). An HK / OK26 version equal to the seed except <c>defaultCompensationModel</c> <c>"UDBETALING"</c>,
    /// dated Monday 2 November 2026, and an HK / OK24 version equal to the seed except the same member, dated Monday
    /// 2 February 2026. An HK / OK26 employee with no balance row. GET <c>?periodYear=2026</c> answers
    /// <c>"AFSPADSERING"</c>, source <c>"config_default"</c>: the route asks the OK version of Thursday 1 January 2026
    /// (OK24), and the HK / OK24 seed is the version in force that day (the 2 February version not yet), so a
    /// central read that ignored its day and took the newest OK24 version would answer <c>"UDBETALING"</c>. GET
    /// <c>?periodYear=2027</c> answers <c>"UDBETALING"</c>: OK26 governs on Friday 1 January 2027 and the
    /// November version is in force that day (discriminating against "always the seed").
    ///
    /// <para>
    /// <b>What it does not pin:</b> the employee's live OK label is OK26, the same as the OK version of 2027, so a
    /// route that asked the live label instead of the day's own OK version would pass. That choice (B12) is pinned by
    /// <see cref="OvertimeModel_LiveLabelOk24_ReadsTheOk26VersionOfThe2027YearStart"/> and
    /// <see cref="OvertimeChoiceWrite_LiveLabelOk24_RefusedByTheOk26VersionOfThe2027YearStart"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Kind: F (CI).</b> <b>Red conditions</b> (argued against TASK-14825's base): the base reads the ACTIVE row of
    /// the employee's live OK version (OK26), which after the publish is the November version, so BOTH years answer
    /// <c>"UDBETALING"</c> and the 2026 assertion fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OvertimeModel_ReadsTheVersionInForceOnThePeriodYearStart()
    {
        using var admin = AdminAt(Monday9Nov2026);
        var hk26 = await SeedVersionIdAsync("HK", Ok26);
        var hk24 = await SeedVersionIdAsync("HK", Ok24);

        var (id26, v26) = await CloneDraftAsync(admin, hk26);
        await ExecAsync(
            "UPDATE agreement_configs SET default_compensation_model = 'UDBETALING' WHERE config_id = @id AND status = 'DRAFT'",
            ("id", id26));
        await PublishOkAsync(admin, id26, v26, "2026-11-02");

        var (id24, v24) = await CloneDraftAsync(admin, hk24);
        await ExecAsync(
            "UPDATE agreement_configs SET default_compensation_model = 'UDBETALING' WHERE config_id = @id AND status = 'DRAFT'",
            ("id", id24));
        await PublishOkAsync(admin, id24, v24, "2026-02-02");

        // Same host as the admin: the employee's own token on the same client, per request.
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825ds_ot", "T825DS", "HK", Ok26);

        var y2026 = await GetCompensationChoiceAsync(admin, "t825ds_ot", "T825DS", 2026);
        var y2027 = await GetCompensationChoiceAsync(admin, "t825ds_ot", "T825DS", 2027);

        Assert.Equal(("AFSPADSERING", "config_default"), y2026);
        Assert.Equal(("UDBETALING", "config_default"), y2027);
    }

    /// <summary>
    /// B12, the GET route. An HK employee whose LIVE OK label is OK24, and one published version beyond the seeds:
    /// HK / OK26 with <c>defaultCompensationModel</c> <c>"UDBETALING"</c>, dated Monday 2 November 2026. GET
    /// <c>?periodYear=2027</c> answers <c>"UDBETALING"</c>, source <c>"config_default"</c>: the route asks the OK
    /// version in force on Friday 1 January 2027 (OK26), not the employee's label, and the November version is in
    /// force that day. A route that asked the live label would read the HK / OK24 seed and answer
    /// <c>"AFSPADSERING"</c>.
    ///
    /// <para>
    /// <b>Kind: N (the mutation below), also F (CI).</b> <b>Red conditions</b>: replacing
    /// <c>OkVersionResolver.ResolveVersion(periodYearStart)</c> with the user's live OK version in the GET route (and
    /// the PUT route) answers <c>"AFSPADSERING"</c> and fails the assertion. TASK-14825's base asked the live label
    /// too, but a compile-level revert is not the evidence; the named mutation is.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OvertimeModel_LiveLabelOk24_ReadsTheOk26VersionOfThe2027YearStart()
    {
        using var admin = AdminAt(Monday9Nov2026);
        var hk26 = await SeedVersionIdAsync("HK", Ok26);

        var (id26, v26) = await CloneDraftAsync(admin, hk26);
        await ExecAsync(
            "UPDATE agreement_configs SET default_compensation_model = 'UDBETALING' WHERE config_id = @id AND status = 'DRAFT'",
            ("id", id26));
        await PublishOkAsync(admin, id26, v26, "2026-11-02");

        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825ds_l24", "T825DS", "HK", Ok24);

        var y2027 = await GetCompensationChoiceAsync(admin, "t825ds_l24", "T825DS", 2027);

        Assert.Equal(("UDBETALING", "config_default"), y2027);
    }

    /// <summary>
    /// B12, the PUT route. The same employee (live label OK24) and an HK / OK26 version dated Monday 2 November 2026,
    /// equal to the seed except <c>employeeCompensationChoice</c> false (the HK / OK24 seed keeps it true). PUT
    /// <c>{ periodYear: 2027, compensationModel: "UDBETALING" }</c> answers 400 with the route's "not enabled" body
    /// and writes no <c>overtime_balances</c> row: the route asks OK26 as of 1 January 2027, where the choice is off.
    /// A route that asked the live label would read the OK24 seed, where the choice is on, and answer 200.
    ///
    /// <para>
    /// <b>Kind: N (the mutation named above), also F (CI).</b> <b>Red conditions</b>: under that mutation the PUT
    /// answers 200 and writes a row; the status assertion fails first.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OvertimeChoiceWrite_LiveLabelOk24_RefusedByTheOk26VersionOfThe2027YearStart()
    {
        using var admin = AdminAt(Monday9Nov2026);
        var hk26 = await SeedVersionIdAsync("HK", Ok26);

        var (id26, v26) = await CloneDraftAsync(admin, hk26);
        await ExecAsync(
            "UPDATE agreement_configs SET employee_compensation_choice = FALSE WHERE config_id = @id AND status = 'DRAFT'",
            ("id", id26));
        await PublishOkAsync(admin, id26, v26, "2026-11-02");

        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825ds_p24", "T825DS", "HK", Ok24);

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/overtime/t825ds_p24/compensation-choice")
        {
            Content = JsonContent.Create(new { periodYear = 2027, compensationModel = "UDBETALING" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", EmployeeToken("t825ds_p24", "T825DS"));
        using var response = await admin.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"Employee compensation choice is not enabled for this agreement"}""", text);
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM overtime_balances WHERE employee_id = @e AND period_year = @y",
            ("e", "t825ds_p24"), ("y", 2027)));
    }

    /// <summary>
    /// AC-C1o (3). A GlobalAdmin publishes an HK / OK24 version with weekly norm 32 dated Monday 16 March 2026 (a
    /// Monday: the weekly norm is week-read). A full-time HK / OK24 employee's
    /// <c>GET /api/skema/{id}/month?year=2026&amp;month=3</c>: <c>dailyNorm</c> is 7.40 on every weekday 2–13 March
    /// (Friday 13 March included) and 6.40 on every weekday 16–31 March (Monday 16 March included); weekends 0.
    ///
    /// <para>
    /// <b>Kind: F (CI).</b> <b>Red conditions</b> (argued against TASK-14825's base): the base reads the ACTIVE row for
    /// every day, so every weekday of the month is 6.40 and the 2–13 March days fail. A lookup that cached one day's
    /// configuration for the next (TASK-14825's BC-6) would show every weekday 7.40 and fail the 16–31 March days.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SkemaMonthNorm_VersionFromMonday16March2026_EachDayItsOwn()
    {
        using var admin = AdminAt(Monday9Nov2026);
        var hk24 = await SeedVersionIdAsync("HK", Ok24);

        var (id, version) = await CloneDraftAsync(admin, hk24);
        await ExecAsync(
            "UPDATE agreement_configs SET weekly_norm_hours = 32 WHERE config_id = @id AND status = 'DRAFT'",
            ("id", id));
        await PublishOkAsync(admin, id, version, "2026-03-16");

        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825ds_ver", "T825DS", "HK", Ok24);

        var norms = await GetSkemaMarchNormsAsync(admin, "t825ds_ver", "T825DS");

        Assert.Equal(ExpectedMarch(before: 7.40m, from16March: 6.40m), norms);
    }

    /// <summary>
    /// AC-C1o (4). STY01's (HK, OK24) local profile is first created, by SQL (the fixture exports no month, so
    /// TASK-14823's hand-off lists nothing), with weekly norm 35 from Monday 16 March 2026; no row for that key
    /// exists before. A full-time HK / OK24 employee in STY01: <c>dailyNorm</c> is 7.40 on every weekday 2–13 March
    /// and 7.00 on every weekday 16–31 March 2026; weekends 0.
    ///
    /// <para>
    /// <b>Kind: F (CI).</b> <b>Red conditions</b> (argued against TASK-14825's base): the base reads the open profile
    /// row undated, so every weekday is 7.00 and the 2–13 March days fail. It is the only fact on TASK-14825's new
    /// dated local read before the T14b stack.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SkemaMonthNorm_LocalProfileFromMonday16March2026_EachDayItsOwn()
    {
        using var admin = AdminAt(Monday9Nov2026);

        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM local_agreement_profiles WHERE org_id = @o AND agreement_code = 'HK' AND ok_version = 'OK24'",
            ("o", "STY01")));
        await ExecAsync(
            """
            INSERT INTO local_agreement_profiles
                (profile_id, org_id, agreement_code, ok_version, effective_from, effective_to, weekly_norm_hours, created_by, version)
            VALUES (gen_random_uuid(), 'STY01', 'HK', 'OK24', @from, NULL, 35, 'T825', 1)
            """, ("from", new DateOnly(2026, 3, 16)));

        await RegressionSeed.SeedEmployeeAsync(ConnectionString, "t825ds_loc", "STY01", "HK", Ok24);

        var norms = await GetSkemaMarchNormsAsync(admin, "t825ds_loc", "STY01");

        Assert.Equal(ExpectedMarch(before: 7.40m, from16March: 7.00m), norms);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The seeded version of a key (in force from <c>0001-01-01</c>), the clone source.</summary>
    private async Task<Guid> SeedVersionIdAsync(string agreementCode, string okVersion)
    {
        var seed = await ConfigRepo.GetInForceAsync(agreementCode, okVersion, new DateOnly(2026, 1, 1));
        Assert.NotNull(seed);
        return seed!.ConfigId;
    }

    /// <summary>Clones <paramref name="sourceId"/> as a DRAFT of the same key; returns its id and ETag version.</summary>
    private static async Task<(Guid Id, long Version)> CloneDraftAsync(HttpClient admin, Guid sourceId)
    {
        using var response = await admin.PostAsync($"/api/agreement-configs/{sourceId}/clone", content: null);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"clone of {sourceId} expected 201, got {(int)response.StatusCode}: {text}");
        return (JsonDocument.Parse(text).RootElement.GetProperty("configId").GetGuid(),
            S118ContractAssert.EtagVersion(response));
    }

    /// <summary>An employee-self request on the admin's host: the employee's token replaces the admin's for this call.</summary>
    private static Task<HttpResponseMessage> AsEmployeeAsync(
        HttpClient hostClient, string employeeId, string orgId, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", EmployeeToken(employeeId, orgId));
        return hostClient.SendAsync(request);
    }

    private static string EmployeeToken(string employeeId, string orgId)
    {
        var tokenService = new StatsTid.Auth.JwtTokenService(new StatsTid.SharedKernel.Security.JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!",
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTid.SharedKernel.Security.StatsTidRoles.Employee,
            agreementCode: "HK", orgId: orgId,
            scopes: new[] { new StatsTid.SharedKernel.Security.RoleScope(
                StatsTid.SharedKernel.Security.StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static async Task<(string Model, string Source)> GetCompensationChoiceAsync(
        HttpClient hostClient, string employeeId, string orgId, int periodYear)
    {
        using var response = await AsEmployeeAsync(
            hostClient, employeeId, orgId, $"/api/overtime/{employeeId}/compensation-choice?periodYear={periodYear}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"compensation-choice GET {periodYear} expected 200, got {(int)response.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement;
        return (root.GetProperty("compensationModel").GetString()!, root.GetProperty("source").GetString()!);
    }

    /// <summary>The employee's March 2026 Skema <c>dailyNorm</c> rows as (date, hours), ordered by date.</summary>
    private static async Task<List<(string Date, decimal Hours)>> GetSkemaMarchNormsAsync(
        HttpClient hostClient, string employeeId, string orgId)
    {
        using var response = await AsEmployeeAsync(
            hostClient, employeeId, orgId, $"/api/skema/{employeeId}/month?year=2026&month=3");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"skema month GET expected 200, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("dailyNorm").EnumerateArray()
            .Select(row => (row.GetProperty("date").GetString()!, row.GetProperty("hours").GetDecimal()))
            .OrderBy(row => row.Item1, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The literal expectation for March 2026 (31 rows): weekends 0; every weekday before Monday 16 March
    /// <paramref name="before"/>; every weekday from the 16th <paramref name="from16March"/>. March 2026 holds no
    /// public holiday.
    /// </summary>
    private static List<(string Date, decimal Hours)> ExpectedMarch(decimal before, decimal from16March)
    {
        var expected = new List<(string, decimal)>();
        for (var day = 1; day <= 31; day++)
        {
            var date = new DateOnly(2026, 3, day);
            var hours = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0m
                : day < 16 ? before : from16March;
            expected.Add((date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), hours));
        }
        return expected;
    }
}
