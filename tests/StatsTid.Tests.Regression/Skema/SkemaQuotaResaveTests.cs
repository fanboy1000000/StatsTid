using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S145 / TASK-14502 — the Skema A0 endpoint pins, part 2 of 2: <b>a re-saved vacation day uses the
/// quota once</b> (QUAL-193, R23, W1). The cell-replacement half of A0 lives in
/// <see cref="SkemaCellSupersessionTests"/>.
///
/// <para><b>Plain language.</b> Booking a vacation day adds its day-equivalent (1.0 for a full day, 0.5 for
/// a half) to <c>entitlement_balances.used</c>. Today a corrected booking is counted AGAIN in full: change a
/// full day to a half day and <c>used</c> rises by 0.5 instead of falling by 0.5. Worse, at an exhausted
/// quota even a harmless correction is refused, because the check asks "is there room for one MORE day?"
/// when the question is "does the CHANGE fit?". The ruled meaning (R23): the quota moves by the SIGNED
/// difference between the new value and the value the save replaces. An unchanged re-save moves nothing; a
/// reduction frees room and must NEVER be refused (even when an earlier profile change left <c>used</c>
/// above the cap); an increase is held to the cap exactly as a first booking is.</para>
///
/// <para><b>Expected values come from the spec</b> (refinement A0 and the quota criterion
/// <c>:566-572</c>, the W1 ruling, SYSTEM_TARGET.md section "Vacation Entitlement"), never from what the
/// code returns today. The VACATION cap is the flat 25 of a whole-ferieår full-timer (the forskud branch,
/// ADR-030/031); <c>planned</c> is 0 throughout (C-Q1).</para>
///
/// <para><b>Fixture rules.</b> All fixtures use VACATION, which permits partial days (it is not
/// full-day-only). Every dated absence sits on a WEEKDAY, nudged by <see cref="NextWeekday"/> (a zero-norm
/// day is rejected by the product and skipped by revaluation, S138/QUAL-153). An "exhausted" balance is
/// built by REAL saves (twenty-four full days) so <c>used</c> equals the sum of the recorded rows; the
/// "above the cap" state of <see cref="UsedAboveCapAfterRevaluation_Reduction_Succeeds"/> comes from a
/// REAL ADR-032 D4 revaluation through the admin profile endpoint (the precedent of
/// <c>Adr032RevaluationTests</c>), not from a SQL write. Every setup save asserts its own 200 (R62), and
/// every case runs on a host whose rule-engine client is the entitlement-validation stub, whose handler
/// delegates to <see cref="EntitlementValidationRule.Evaluate"/> (without it the save fails closed). The
/// clock is pinned (PAT-008) at <see cref="F"/>, because the revaluation covers absences dated on or after
/// "today".</para>
///
/// <para><b>Docker-gated, CI-verified, never locally green.</b> Docker does not run on the owner's machine.
/// Each fact's "Red conditions" argue its RED: what happens today and which assertion trips, and which
/// production mutation (its MD-id, plan partition table R59) an evidence run applies to prove the pin can
/// fail on the finished product.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaQuotaResaveTests : IClassFixture<SkemaQuotaResaveTests.Harness>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    /// <summary>The pinned "today" (PAT-008): Monday 2025-03-03, on the OK24 side of the 2026-04-01
    /// cutover. The D4 revaluation re-values absences dated on or after the profile change's effective
    /// day, which the endpoint pins to "today".</summary>
    private static readonly DateOnly F = new(2025, 3, 3);

    /// <summary>The replaced cell's day: Wednesday 2025-03-05, a weekday and no public holiday.</summary>
    private static readonly DateOnly Day = NextWeekday(new DateOnly(2025, 3, 5));

    /// <summary>The ferieår 2024 (1 Sep 2024 - 31 Aug 2025) holds every date below; VACATION resets in
    /// month 9, so the balance row is keyed 2024.</summary>
    private const int VacationYear = 2024;

    /// <summary>The VACATION cap: the flat annual 25 of a whole-ferieår full-timer, no carryover.</summary>
    private const decimal Cap = 25.0m;

    private readonly Harness _fx;

    public SkemaQuotaResaveTests(Harness fx) => _fx = fx;

    // ════════════════════════════════════════════════════════════════════════
    // A0 — quota counted once (criterion "quota counted once", R23)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Q1.</b> Re-saving a VACATION day with the SAME hours leaves <c>entitlement_balances.used</c>
    /// unchanged: the cell is replaced, so the day is still one day.
    ///
    /// <para><b>Red conditions.</b> Today the re-save adds the full requested day again: after 7.4 h then
    /// 7.4 h, <c>used</c> is 2.0 and <c>Assert.Equal(1.0m, used)</c> trips. Listed under <b>MD-06</b> (the
    /// in-lock delta is the unsigned sum of the new rows, so the second save adds +1.0).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Vacation_SameHours_UsedUnchanged()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Day, 7.4m, "VACATION 7.4 h");
        Assert.Equal(1.0m, await UsedAsync(emp));

        await SaveOkAsync(emp, Day, 7.4m, "VACATION re-save, same 7.4 h");

        Assert.Equal(1.0m, await UsedAsync(emp));
    }

    /// <summary>
    /// <b>Q2.</b> Re-saving a VACATION day with FEWER hours makes <c>used</c> fall by the signed delta,
    /// new minus replaced: a full day (1.0) re-saved as a half day (0.5) moves <c>used</c> by 0.5 - 1.0 =
    /// -0.5, from 1.0 to 0.5.
    ///
    /// <para><b>Red conditions.</b> Today the half day is added on top of the full day: <c>used</c> is 1.5
    /// and <c>Assert.Equal(0.5m, used)</c> trips. Listed under <b>MD-06</b> (unsigned delta: +0.5).</para>
    /// </summary>
    [Fact]
    public async Task Resave_Vacation_FewerHours_UsedFallsBySignedDelta()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Day, 7.4m, "VACATION 7.4 h");
        Assert.Equal(1.0m, await UsedAsync(emp));

        await SaveOkAsync(emp, Day, 3.7m, "VACATION re-save, fewer hours 3.7 h");

        Assert.Equal(0.5m, await UsedAsync(emp));
    }

    /// <summary>
    /// <b>Q3.</b> At an EXHAUSTED quota (<c>used</c> = the cap, 25.0) an UNCHANGED replacement returns 200:
    /// the change is zero, so it fits, and <c>used</c> stays 25.0.
    ///
    /// <para><b>Red conditions.</b> Today the pre-check asks the rule engine whether one more full day
    /// fits (used 25.0 + requested 1.0 &gt; 25.0) and answers 422 before the transaction
    /// (<c>SkemaEndpoints.cs:1420-1429</c>): the status assertion trips with 422. Listed under
    /// <b>MD-06</b> (unsigned in-lock delta: the guard sees 25 + 1 &gt; 25 and refuses). MD-09 does not
    /// reach it: with nothing planned the pre-check would let a zero-delta save through (C-Q1).</para>
    /// </summary>
    [Fact]
    public async Task ExhaustedQuota_UnchangedReplacement_Returns200()
    {
        var emp = await SeedEmployeeAsync();
        await BookFullDaysAsync(emp, BaselineFullDays);
        await SaveOkAsync(emp, Day, 7.4m, "the 25th full day");
        Assert.Equal(Cap, await UsedAsync(emp)); // exhausted by real saves

        var rsp = await SaveAsync(emp, Day, 7.4m);

        await AssertStatusAsync(rsp, HttpStatusCode.OK, "an unchanged replacement at an exhausted quota");
        Assert.Equal(Cap, await UsedAsync(emp));
    }

    /// <summary>
    /// <b>Q4.</b> At an exhausted quota a REDUCED replacement returns 200 and frees room: the full day
    /// (1.0) re-saved as a half day (0.5) moves <c>used</c> from 25.0 to 24.5.
    ///
    /// <para><b>Red conditions.</b> Today the pre-check refuses it (used 25.0 + requested 0.5 &gt; 25.0):
    /// the status assertion trips with 422. Listed under <b>MD-06</b> (unsigned delta +0.5 against a full
    /// quota: guard refuses).</para>
    /// </summary>
    [Fact]
    public async Task ExhaustedQuota_ReducedReplacement_Returns200()
    {
        var emp = await SeedEmployeeAsync();
        await BookFullDaysAsync(emp, BaselineFullDays);
        await SaveOkAsync(emp, Day, 7.4m, "the 25th full day");
        Assert.Equal(Cap, await UsedAsync(emp));

        var rsp = await SaveAsync(emp, Day, 3.7m);

        await AssertStatusAsync(rsp, HttpStatusCode.OK, "a reduced replacement at an exhausted quota");
        Assert.Equal(24.5m, await UsedAsync(emp));
    }

    /// <summary>
    /// <b>Q5 (guard: passes today).</b> At an exhausted quota an INCREASE beyond the cap still returns 422:
    /// a half day (0.5) re-saved as a full day (1.0) is a change of +0.5 on a balance already at 25.0, so it
    /// would end at 25.5. The refusal is the quota's (<c>"Entitlement quota exceeded"</c>), nothing is
    /// written, <c>used</c> stays 25.0 and the stored cell is still the half day.
    ///
    /// <para><b>Red conditions.</b> Passes today (the pre-check refuses 25.0 + 1.0) and must keep passing
    /// after the fix, where the same refusal comes from the mirrored pre-check or the in-lock guard.
    /// <b>MD-18</b> (the delta &gt; 0 branch runs no pre-check and the ungated adjust) lets the increase
    /// through: the status assertion trips with 200.</para>
    /// </summary>
    [Fact]
    public async Task ExhaustedQuota_IncreaseBeyondCap_Returns422()
    {
        var emp = await SeedEmployeeAsync();
        await BookFullDaysAsync(emp, BaselineFullDays);                              // 24.0
        await SaveOkAsync(emp, NextWeekday(new DateOnly(2025, 2, 5)), 3.7m, "a half day"); // 24.5
        await SaveOkAsync(emp, Day, 3.7m, "the cell, as a half day");                // 25.0
        Assert.Equal(Cap, await UsedAsync(emp));

        var rsp = await SaveAsync(emp, Day, 7.4m);

        await AssertStatusAsync(rsp, HttpStatusCode.UnprocessableEntity, "an increase beyond the cap");
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal(Cap, await UsedAsync(emp));
        Assert.Equal(new[] { 3.7m }, await AbsenceHoursOnAsync(emp, Day));
    }

    /// <summary>
    /// <b>Q6.</b> With <c>used</c> already ABOVE the cap after an ADR-032 D4 revaluation, a REDUCTION
    /// succeeds: a day that is a full day under the new half-time norm (3.7 h, recorded 1.0) re-saved as a
    /// half day (1.85 h, 0.5) returns 200 and <c>used</c> falls from 26.0 to 25.5, still above the cap
    /// (C-Q2). A reduction must never be refused, whatever a profile change did to the balance.
    ///
    /// <para><b>Fixture.</b> A full-timer books twelve full days (7.4 h) and two half days (3.7 h) on
    /// weekdays after the pinned day: <c>used</c> 13.0. A REAL admin profile change halves the part-time
    /// fraction (norm 7.4 to 3.7), so the revaluation re-records every day at the new norm (twelve times
    /// 2.0 + two times 1.0): <c>used</c> 26.0 = cap + 1, asserted before the save under test. (The revaluation
    /// is the ungated path, <c>EntitlementBalanceRepository.cs:108-112</c>; it is driven end to end as in
    /// <c>Adr032RevaluationTests</c>.)</para>
    ///
    /// <para><b>Red conditions.</b> Today the pre-check asks whether 0.5 more fits on top of 26.0 and
    /// answers 422: the status assertion trips. Listed under <b>MD-08</b> (the delta &lt;= 0 branch calls the
    /// guarded adjust: 26.0 - 0.5 = 25.5 &gt; 25 refuses), <b>MD-09</b> (the pre-check runs for delta &lt;= 0:
    /// 26.0 - 1.0 + 0.5 = 25.5 &gt; 25 refuses) and <b>MD-06</b> (unsigned delta +0.5 against a balance
    /// over the cap).</para>
    /// </summary>
    [Fact]
    public async Task UsedAboveCapAfterRevaluation_Reduction_Succeeds()
    {
        var emp = await SeedEmployeeAsync(profileFrom: F);
        var days = WeekdaysFrom(F.AddDays(1), 14); // 4-21 March 2025, all weekdays, all after "today"
        var fullDays = days.Take(12).ToArray();
        var halfDays = days.Skip(12).ToArray();

        await SaveManyOkAsync(emp,
            fullDays.Select(d => (d, 7.4m)).Concat(halfDays.Select(d => (d, 3.7m))).ToArray(),
            "twelve full days and two half days");
        Assert.Equal(13.0m, await UsedAsync(emp)); // 12 x 1.0 + 2 x 0.5

        // The REAL D4 revaluation: full-time -> half-time, effective today.
        var admin = ClientWith(MintGlobalAdminToken());
        var version = await ReadProfileVersionAsync(admin, emp);
        var put = await PutProfileAsync(admin, emp, F, 0.500m, ifMatch: $"\"{version}\"");
        await AssertStatusAsync(put, HttpStatusCode.OK, "the profile change that triggers the revaluation");
        Assert.Equal(26.0m, await UsedAsync(emp)); // 12 x 2.0 + 2 x 1.0 = cap + 1: above the cap, for real

        var target = halfDays[^1]; // a full day under the new norm (3.7 h, recorded 1.0)
        var rsp = await SaveAsync(emp, target, 1.85m);

        await AssertStatusAsync(rsp, HttpStatusCode.OK, "a reduction while used is above the cap");
        Assert.Equal(25.5m, await UsedAsync(emp)); // 26.0 + (0.5 - 1.0)
    }

    /// <summary>
    /// <b>Q7.</b> NEAR the cap an increase that still fits returns 200: with <c>used</c> at cap - 0.5
    /// (24.5), a half day (0.5) re-saved as a full day (1.0) is a change of +0.5 and ends at exactly the
    /// cap, 25.0. This is R23's mirror image: a change that fits must be accepted.
    ///
    /// <para><b>Red conditions.</b> Today the pre-check asks whether a whole extra day fits (24.5 + 1.0 &gt;
    /// 25.0) and refuses with 422: the status assertion trips. Listed under <b>MD-07</b> (the pre-check
    /// sends <c>used</c> without subtracting the replaced rows, so it still sees 24.5 + 1.0) and
    /// <b>MD-06</b> (unsigned delta +1.0: the guard sees 24.5 + 1.0 &gt; 25).</para>
    /// </summary>
    [Fact]
    public async Task NearCap_ResaveIncreaseWithinCap_Returns200()
    {
        var emp = await SeedEmployeeAsync();
        await BookFullDaysAsync(emp, BaselineFullDays);                    // 24.0
        await SaveOkAsync(emp, Day, 3.7m, "the cell, as a half day");      // 24.5 = cap - 0.5
        Assert.Equal(24.5m, await UsedAsync(emp));

        var rsp = await SaveAsync(emp, Day, 7.4m);

        await AssertStatusAsync(rsp, HttpStatusCode.OK, "an increase that still fits within the cap");
        Assert.Equal(Cap, await UsedAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fixture composition
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Twenty-four weekdays from 2 January 2025 (to 4 February): the "twenty-four full days"
    /// every exhausted-quota case starts from. All in ferieår 2024, none equal to <see cref="Day"/>.</summary>
    private static readonly IReadOnlyList<DateOnly> BaselineFullDays = WeekdaysFrom(new DateOnly(2025, 1, 2), 24);

    /// <summary>Books each date as one full VACATION day (7.4 h) by real Skema saves and asserts that
    /// <c>used</c> equals the number of days (the setup premise of every exhausted-quota case).</summary>
    private async Task BookFullDaysAsync(string emp, IReadOnlyList<DateOnly> dates)
    {
        await SaveManyOkAsync(emp, dates.Select(d => (d, 7.4m)).ToArray(), $"{dates.Count} full days");
        Assert.Equal((decimal)dates.Count, await UsedAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // HTTP helpers
    // ════════════════════════════════════════════════════════════════════════

    private static object Payload(DateOnly monthOf, (DateOnly Date, decimal Hours)[] vacation) => new
    {
        year = monthOf.Year,
        month = monthOf.Month,
        absences = vacation.Select(v => new
        {
            date = v.Date.ToString("yyyy-MM-dd"),
            absenceType = "VACATION",
            hours = v.Hours,
        }).ToArray(),
    };

    private Task<HttpResponseMessage> SaveAsync(string emp, DateOnly date, decimal hours)
        => ClientWith(MintEmployeeToken(emp)).PostAsJsonAsync(
            $"/api/skema/{emp}/save", Payload(date, new[] { (date, hours) }));

    /// <summary>A setup save of one VACATION day: asserts the 200 (R62).</summary>
    private async Task SaveOkAsync(string emp, DateOnly date, decimal hours, string what)
        => await AssertSetupOkAsync(await SaveAsync(emp, date, hours), what);

    /// <summary>A setup save of many VACATION days, one request per calendar month (the save is keyed by
    /// year and month); asserts the 200 of each (R62).</summary>
    private async Task SaveManyOkAsync(string emp, (DateOnly Date, decimal Hours)[] days, string what)
    {
        foreach (var month in days.GroupBy(d => (d.Date.Year, d.Date.Month)))
        {
            var rsp = await ClientWith(MintEmployeeToken(emp)).PostAsJsonAsync(
                $"/api/skema/{emp}/save", Payload(month.First().Date, month.ToArray()));
            await AssertSetupOkAsync(rsp, $"{what}, {month.Key.Year}-{month.Key.Month:D2}");
        }
    }

    private static async Task AssertSetupOkAsync(HttpResponseMessage rsp, string what)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"setup save ({what}) must answer 200 before the save under test; got {(int)rsp.StatusCode}: {raw}");
    }

    /// <summary>Asserts the status of the save under test and echoes the body, so a red run names the
    /// refusal it got instead of only a number.</summary>
    private static async Task AssertStatusAsync(HttpResponseMessage rsp, HttpStatusCode expected, string what)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected,
            $"{what}: expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
    }

    private HttpClient ClientWith(string bearer)
    {
        var client = _fx.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private async Task<long> ReadProfileVersionAsync(HttpClient admin, string emp)
    {
        var rsp = await admin.GetAsync($"/api/admin/employee-profiles/{emp}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return (await rsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64();
    }

    private static Task<HttpResponseMessage> PutProfileAsync(
        HttpClient admin, string emp, DateOnly effectiveFrom, decimal partTimeFraction, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{emp}")
        {
            Content = JsonContent.Create(new { effectiveFrom, partTimeFraction, position = (string?)null }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return admin.SendAsync(req);
    }

    private static string MintEmployeeToken(string actorId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }

    private static string MintGlobalAdminToken()
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: "ADMIN_S145_QUOTA", name: "S145 Quota Admin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ════════════════════════════════════════════════════════════════════════
    // Seeding and SQL reads (literal statements only: a non-constant command text trips CA2100)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A fresh full-time AC/OK24 employee on STY01: user, an OPEN dated agreement row from
    /// 0001-01-01 and an OPEN full-time profile row from <paramref name="profileFrom"/> (default
    /// 0001-01-01). The revaluation fixture seeds the profile AT "today", so the admin profile change
    /// routes as a same-day in-place edit (the precedent of <c>Adr032RevaluationTests</c>). Seeded AFTER
    /// the host's first boot (see <see cref="Harness"/>), so no startup seeder erases it.</summary>
    private async Task<string> SeedEmployeeAsync(DateOnly? profileFrom = null)
    {
        var emp = "emp_s145_quota_" + Guid.NewGuid().ToString("N")[..8];
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S145 Quota Resave User', NULL, @org, 'AC', 'OK24', TRUE)
            """, conn))
        {
            userCmd.Parameters.AddWithValue("u", emp);
            userCmd.Parameters.AddWithValue("org", OrgId);
            await userCmd.ExecuteNonQueryAsync();
        }

        await using (var profileCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, effective_from, effective_to,
                                           version, employment_category)
            VALUES (gen_random_uuid(), @e, 1.000, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("e", emp);
            profileCmd.Parameters.AddWithValue("from", profileFrom ?? new DateOnly(1, 1, 1));
            await profileCmd.ExecuteNonQueryAsync();
        }

        await using (var agreementCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', '0001-01-01', NULL, 1)
            """, conn))
        {
            agreementCmd.Parameters.AddWithValue("u", emp);
            await agreementCmd.ExecuteNonQueryAsync();
        }

        return emp;
    }

    /// <summary><c>entitlement_balances.used</c> of the employee's VACATION ferieår 2024.</summary>
    private async Task<decimal> UsedAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("y", VacationYear);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"expected a VACATION/{VacationYear} balance row for {emp}.");
        return Convert.ToDecimal(result);
    }

    /// <summary>Every <c>absences_projection</c> row's hours on the day (a refused save must leave exactly
    /// the stored cell: one row).</summary>
    private async Task<decimal[]> AbsenceHoursOnAsync(string emp, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT hours FROM absences_projection WHERE employee_id = @e AND date = @d ORDER BY outbox_id", conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", date);
        var hours = new List<decimal>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            hours.Add(reader.GetDecimal(0));
        return hours.ToArray();
    }

    // ── Date helpers: the weekday nudge ──

    /// <summary>The weekday nudge: a Saturday or Sunday moves to the Monday after. A zero-norm day is
    /// rejected by the product for entitlement absences and skipped by revaluation, so a fixture date
    /// that lands on one would prove nothing (S138, QUAL-153).</summary>
    private static DateOnly NextWeekday(DateOnly d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(1);
        return d;
    }

    /// <summary><paramref name="count"/> consecutive weekdays starting on or after
    /// <paramref name="start"/>.</summary>
    private static IReadOnlyList<DateOnly> WeekdaysFrom(DateOnly start, int count)
    {
        var dates = new List<DateOnly>(count);
        var d = NextWeekday(start);
        while (dates.Count < count)
        {
            dates.Add(d);
            d = NextWeekday(d.AddDays(1));
        }
        return dates;
    }

    // ════════════════════════════════════════════════════════════════════════
    // The shared container + host (one per class; cases isolate by their own employee ids)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One Postgres testcontainer and one booted API for the class. Isolation between facts is by
    /// case-unique employee ids, never by cleanup (the <c>SendCommandMatrixFixture</c> precedent). The host
    /// carries the entitlement-validation stub (R62) and the pinned clock (PAT-008) in ONE
    /// <c>ConfigureTestServices</c> (as <c>Adr032RevaluationTests</c> does), and is booted once BEFORE any
    /// fixture is seeded: a derived host's first <c>CreateClient()</c> re-runs the startup seeders against
    /// the same database.
    /// </summary>
    public sealed class Harness : IAsyncLifetime
    {
        private TestFixtures.DockerHarness _harness = null!;
        private StatsTidWebApplicationFactory _factory = null!;

        public WebApplicationFactory<Program> Host { get; private set; } = null!;
        public string ConnectionString { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _harness = await TestFixtures.DockerHarness.StartAsync();
            ConnectionString = _harness.ConnectionString;
            await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
            _factory = new StatsTidWebApplicationFactory(ConnectionString);
            Host = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(F));
                }));
            _ = Host.CreateClient(); // boot the startup seeders now; every case seeds after this
        }

        public async Task DisposeAsync()
        {
            Host?.Dispose();
            _factory?.Dispose();
            if (_harness is not null)
                await _harness.DisposeAsync();
        }
    }

    // ── Rule-engine stub: drives the REAL EntitlementValidationRule over the HTTP seam ──

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        // The endpoints resolve the NAMED RuleEngine client and use RELATIVE request URIs; this stub
        // replaces the whole factory, so it supplies the BaseAddress the production registration sets.
        public HttpClient CreateClient(string name) => new(new EntitlementValidationStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }
}
