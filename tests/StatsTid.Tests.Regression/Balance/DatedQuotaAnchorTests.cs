using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S81 / TASK-8102 (R5/R6) — the legal-sensitive quota-correctness discriminating suite for the
/// dated-entitlement-config anchor cutover at the three Convention-A sites (Skema quota validation,
/// Balance <c>/summary</c>, Balance <c>/series</c>). Each test was verified RED on the pre-8102 code
/// (the pre-fix anchor triple: live <c>user.OkVersion</c> + month-start agreement + year-start row
/// date) before the fix landed — the RED evidence is recorded inline per case.
///
/// <para>
/// <b>The fix under test.</b> The Step-2 quota read now resolves the YEAR-START-dated OK version AND
/// the YEAR-START-dated agreement (R1), and the Step-1 reset_month read is anchored at the OPERATION
/// DATE's OK + agreement (R2, re-derivation — correct-by-construction even when reset_month diverges
/// across natural keys for the unconstrained IMMEDIATE types). Result: quota validation == the
/// year-overview / year-start-aligned quota, closing the validation-vs-display split-brain.
/// </para>
///
/// <para>
/// <b>R9 reachability (confirmed during 8101, re-confirmed here).</b> A retroactive cross-OK
/// registration is reachable for EVERY employee as the fixed 2026-04-01 OK boundary
/// (<see cref="OkVersionResolver"/>) passes (R6a/R5 — live OK26, a booking into an OK24-era ferieår).
/// A retroactive cross-agreement registration is reachable via the dated <c>user_agreement_codes</c>
/// history (R6b/R6c — an AC↔HK switch mid-ferieår). So R6b/R6c are LIVE fixtures (not collapsed to
/// the OK-version vector), and R5 is a LIVE fixture (not an invariant assertion): CARE_DAY's
/// reset_month is unconstrained across OK versions (only VACATION is schema-pinned to 9), so a real
/// (employee, CARE_DAY) CAN reach divergent operation-date-key vs year-start-key reset months.
/// </para>
///
/// <para>
/// HTTP harness mirrors <see cref="StatsTid.Tests.Regression.Outbox.SkemaMonthlyAccrualGuardTests"/>:
/// the in-process WAF has no rule-engine container, so <see cref="IHttpClientFactory"/> is stubbed to
/// drive the REAL <see cref="EntitlementValidationRule.Evaluate"/> over the validate-entitlement seam
/// (the per-type bookableLimit the Backend computes off the resolved config is exercised end-to-end).
/// DB facts (employee_profiles, user_agreement_codes, entitlement_configs quotas/reset_month,
/// users.ok_version) are seeded/mutated directly; assertions read responses + entitlement_balances back.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedQuotaAnchorTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // init.sql seed employee: emp001, STY01, AC, OK24. Self-save → actor == route id.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build → seeders backfill emp001 profile +
        // agreement-code rows. Per-test fixture mutations happen AFTER this point.
        _ = _factory.CreateClient();

        // The Skema POST seam sources the dated part-time fraction from employee_profiles
        // (as-of firstAbsenceDate); seed a full-ferieår, full-time profile so MONTHLY_ACCRUAL
        // accrual is computed against a real fraction (not a 422 employment_profile_missing).
        await SeedEmploymentProfileAsync(Emp001, partTimeFraction: 1.0m);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6a — OK-version case. Live user.OkVersion = OK26; a VACATION ferieår whose
    // year-start (2025-09-01) resolves OK24. OK24 quota 25 ≠ OK26 quota 30.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>RED on pre-8102 code.</b> A VACATION batch of 27 days booked in May 2026 (firstAbsenceDate
    /// 2026-05-xx ⇒ OK26 era) keys to ferieår 2025 (reset_month 9 ⇒ year-start 2025-09-01 ⇒ OK24).
    /// The year-start quota is OK24's 25; the live OK26 quota is 30. The dynamic VACATION forskud cap
    /// equals the (year-start-dated) annual quota for a whole-ferieår full-timer, so:
    /// <list type="bullet">
    ///   <item><description>PRE-fix (Step-2 read at live OK26 ⇒ quota 30): cap 30, 27 ≤ 30 ⇒
    ///     <b>200 OK</b> (observed RED: the over-OK24-quota booking was wrongly ALLOWED).</description></item>
    ///   <item><description>POST-fix (Step-2 read at year-start OK24 ⇒ quota 25): cap 25, 27 &gt; 25 ⇒
    ///     <b>422</b>, nothing persists.</description></item>
    /// </list>
    /// Also pins validation == display: <c>/summary</c> + <c>/year-overview</c> report the OK24 25,
    /// not the live OK26 30, for the same ferieår.
    /// </summary>
    [Fact]
    public async Task R6a_OkVersion_ValidationUsesYearStartOk_NotLiveOk()
    {
        await SetUserOkVersionAsync(Emp001, "OK26");           // live OK26
        await SeedSingleAgreementHistoryAsync(Emp001, "AC");   // single agreement (isolate the OK dimension)
        await SetVacationQuotaAsync("AC", "OK24", 25m);        // year-start quota (seed value, pinned)
        await SetVacationQuotaAsync("AC", "OK26", 30m);        // live quota (raised) — the pre-fix value

        // 27 distinct VACATION weekdays in May 2026 (OK26 era). firstAbsenceDate ⇒ ferieår 2025.
        var dates = DistinctWeekdays(new DateOnly(2026, 5, 4), 27);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var client = CreateEmployeeClient();
        var rsp = await PostAbsencesAsync(client, 2026, 5, absences);

        // POST-fix: rejected against the year-start OK24 quota (25). 27 > 25.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, dates[0])); // nothing persisted

        // validation == display: /summary's totalQuota for the same month is the SAME year-start
        // (OK24) quota 25 the validation now uses — NOT the live OK26 30. /summary and the validation
        // path resolve through the SAME shared resolver + year-start anchor post-8102, so this pins
        // that the two seams agree (the split-brain is closed). The year-overview seam is
        // independently characterized by YearOverviewTests; it shares the identical resolver.
        var summary = await GetSummaryAsync(2026, 5);
        Assert.Equal(25m, VacationTotalQuota(summary));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6b — Agreement case. Live OK24; agreement history HK → AC (switch 2026-01-01).
    // A VACATION ferieår whose year-start (2025-09-01) is under HK, operation date under AC.
    // HK quota 20 ≠ AC quota 25.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>RED on pre-8102 code.</b> A VACATION batch booked in Feb 2026 (firstAbsenceDate 2026-02-xx,
    /// agreement AC) keys to ferieår 2025 (year-start 2025-09-01), which the employee lived under HK.
    /// HK VACATION quota = 20, AC = 25. The cap is the year-start AGREEMENT's quota, so booking 22:
    /// <list type="bullet">
    ///   <item><description>PRE-fix (Step-2 read at the month-start agreement AC ⇒ quota 25): cap 25,
    ///     22 ≤ 25 ⇒ <b>200 OK</b> (observed RED: wrongly ALLOWED against AC's higher quota).</description></item>
    ///   <item><description>POST-fix (Step-2 read at the year-start agreement HK ⇒ quota 20): cap 20,
    ///     22 &gt; 20 ⇒ <b>422</b>, nothing persists.</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task R6b_Agreement_ValidationUsesYearStartAgreement_NotMonthStartAgreement()
    {
        await SetUserOkVersionAsync(Emp001, "OK24");           // isolate the agreement dimension
        // HK until 2026-01-01, then AC. Year-start 2025-09-01 ⇒ HK; operation Feb 2026 ⇒ AC.
        await SeedTwoPeriodAgreementHistoryAsync(
            Emp001, first: "HK", switchOn: new DateOnly(2026, 1, 1), second: "AC");
        await SetVacationQuotaAsync("HK", "OK24", 20m);        // year-start (HK) quota — the post-fix value
        await SetVacationQuotaAsync("AC", "OK24", 25m);        // month-start (AC) quota — the pre-fix value

        var dates = DistinctWeekdays(new DateOnly(2026, 2, 3), 22);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var client = CreateEmployeeClient();
        var rsp = await PostAbsencesAsync(client, 2026, 2, absences);

        // POST-fix: rejected against the year-start AGREEMENT (HK) quota 20. 22 > 20.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, dates[0]));

        // validation == display: /summary reports the year-start HK quota 20, not the month-start AC 25.
        var summary = await GetSummaryAsync(2026, 2);
        Assert.Equal(20m, VacationTotalQuota(summary));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6c — /series case (the agreement-quota vector; /series renders only MONTHLY_ACCRUAL types).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>RED on pre-8102 code.</b> Same HK→AC history as R6b. <c>/series</c> for Feb 2026 keys
    /// VACATION to ferieår 2025 (year-start 2025-09-01 ⇒ HK). The curve's <c>annualQuota</c> is the
    /// year-start agreement's quota:
    /// <list type="bullet">
    ///   <item><description>PRE-fix (month-start AC): <c>annualQuota = 25</c> (observed RED).</description></item>
    ///   <item><description>POST-fix (year-start HK): <c>annualQuota = 20</c>.</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task R6c_Series_UsesYearStartAgreementQuota()
    {
        await SetUserOkVersionAsync(Emp001, "OK24");
        await SeedTwoPeriodAgreementHistoryAsync(
            Emp001, first: "HK", switchOn: new DateOnly(2026, 1, 1), second: "AC");
        await SetVacationQuotaAsync("HK", "OK24", 20m);
        await SetVacationQuotaAsync("AC", "OK24", 25m);

        var series = await GetSeriesAsync(2026, 2);
        var vacation = series.GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("type").GetString() == "VACATION");

        // The ferieår under the curve is 2025 (reset_month 9, seriesAsOf 2026-02-28).
        Assert.Equal(2025, vacation.GetProperty("entitlementYear").GetInt32());
        // POST-fix: the year-start agreement (HK) quota 20, NOT the month-start AC 25.
        Assert.Equal(20m, vacation.GetProperty("annualQuota").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6d — No-history-change case: the common single-agreement employee is byte-identical
    // to pre-S81 (no regression). Booking within quota succeeds; /summary quota == 25.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A plain OK24 / single-AC employee (no OK or agreement straddle) books 10 VACATION days within
    /// the unchanged 25-day quota — succeeds (200) and persists, and <c>/summary</c> reports the
    /// annual 25. The anchor cutover is a no-op when every anchor resolves to the same key (the
    /// common case), so this is byte-identical to pre-S81.
    /// </summary>
    [Fact]
    public async Task R6d_NoStraddle_CommonEmployee_Unchanged()
    {
        await SetUserOkVersionAsync(Emp001, "OK24");
        await SeedSingleAgreementHistoryAsync(Emp001, "AC");
        await SetVacationQuotaAsync("AC", "OK24", 25m); // seed value (pinned)

        // 10 VACATION weekdays in Nov 2025 (ferieår 2025, all OK24/AC). Within the 25 cap.
        var dates = DistinctWeekdays(new DateOnly(2025, 11, 3), 10);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var client = CreateEmployeeClient();
        var rsp = await PostAbsencesAsync(client, 2025, 11, absences);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, dates[0]));

        var (totalQuota, used, _) = await ReadBalanceAsync(Emp001, "VACATION", 2025);
        Assert.Equal(25m, totalQuota);
        Assert.Equal(10m, used);

        var summary = await GetSummaryAsync(2025, 11);
        Assert.Equal(25m, VacationTotalQuota(summary));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6e — the COMMON live-OK26 employee on the SUCCESS path is byte-identical (Step-5a Codex
    // coverage WARNING: R6d pins only a live-OK24 employee, and R6a's live-OK26 case is the REJECTION
    // path [nothing persists]; neither exercises a live-OK26 successful booking + the in-tx balance
    // write + the year-start read with the op-date OK key present).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A live <c>OK26</c> single-AC employee books 10 VACATION days in May 2026 within an UNCHANGED
    /// 25-day quota. The operation date (2026-05, OK26 era) drives the Step-1 reset_month read at the
    /// OK26 key (present), while the ferieår-2025 year-start (2025-09-01, OK24) drives the Step-2 quota
    /// read at the OK24 key (present); OK24 and OK26 quotas are set EQUAL (25) so there is no
    /// divergence — the path is byte-identical to pre-S81. Pins the success/write path for the common
    /// today-employee: 200, persists to entitlement_year 2025 (the year-start ferieår), reads 25.
    /// </summary>
    [Fact]
    public async Task R6e_CommonLiveOk26_SuccessPath_Unchanged()
    {
        await SetUserOkVersionAsync(Emp001, "OK26");           // the common case TODAY: live OK26
        await SeedSingleAgreementHistoryAsync(Emp001, "AC");
        await SetVacationQuotaAsync("AC", "OK24", 25m);        // year-start (ferieår 2025 ⇒ OK24) ...
        await SetVacationQuotaAsync("AC", "OK26", 25m);        // ... == live OK26: no divergence ⇒ pure no-regression

        var dates = DistinctWeekdays(new DateOnly(2026, 5, 4), 10);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var client = CreateEmployeeClient();
        var rsp = await PostAbsencesAsync(client, 2026, 5, absences);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, dates[0]));

        // Keys to the year-start ferieår (2025), reads the (equal) 25 quota — success path unchanged.
        var (totalQuota, used, _) = await ReadBalanceAsync(Emp001, "VACATION", 2025);
        Assert.Equal(25m, totalQuota);
        Assert.Equal(10m, used);
        var summary = await GetSummaryAsync(2026, 5);
        Assert.Equal(25m, VacationTotalQuota(summary));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 — reset_month divergence (defense-in-depth, IMMEDIATE type CARE_DAY). Proves the R2
    // re-derivation: the consumed entitlement year is derived from the OPERATION-DATE key's
    // reset_month, NOT the live key's.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>RED on pre-8102 code.</b> Live <c>user.OkVersion</c> = OK26 (CARE_DAY OK26 reset_month 1,
    /// seed); the OK24 CARE_DAY reset_month is mutated to 7 (reachable — CARE_DAY reset_month is
    /// unconstrained across OK versions). A CARE_DAY booking on 2025-04-15 has operation-date OK24
    /// (date &lt; 2026-04-01). The two keys disagree on reset_month, so they disagree on the consumed
    /// entitlement year:
    /// <list type="bullet">
    ///   <item><description>PRE-fix Step-1 reads the LIVE key (CARE_DAY, AC, OK26) ⇒ reset_month 1 ⇒
    ///     Apr ≥ 1 ⇒ year <b>2025</b> (observed RED: the balance keyed to 2025).</description></item>
    ///   <item><description>POST-fix Step-1 reads the OPERATION-DATE key (CARE_DAY, AC, OK24) ⇒
    ///     reset_month 7 ⇒ Apr &lt; 7 ⇒ year <b>2024</b>.</description></item>
    /// </list>
    /// The 1-day booking is within the CARE_DAY annual quota (2) either way, so it succeeds (200);
    /// the discriminator is WHICH entitlement_year the persisted balance row keys to.
    /// </summary>
    [Fact]
    public async Task R5_ResetMonthDivergence_YearDerivedFromOperationDateKey()
    {
        await SetUserOkVersionAsync(Emp001, "OK26");          // live OK26 (CARE_DAY OK26 reset_month 1)
        await SeedSingleAgreementHistoryAsync(Emp001, "AC");
        await SetCareDayResetMonthAsync("AC", "OK24", 7);     // operation-date key (OK24) reset_month 7

        var date = new DateOnly(2025, 4, 15);                 // OK24 era; Apr — straddles reset 1 vs 7
        var client = CreateEmployeeClient();
        var rsp = await PostAbsencesAsync(client, 2025, 4, new[] { (date, "CARE_DAY", 7.4m) });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, date));

        // POST-fix: re-derived from the operation-date key (reset_month 7) ⇒ entitlement_year 2024.
        var (_, used2024, _) = await ReadBalanceAsync(Emp001, "CARE_DAY", 2024);
        Assert.Equal(1m, used2024);
        // The pre-fix live-key derivation (reset_month 1) would have keyed to 2025 — assert it did NOT.
        Assert.Null(await TryReadBalanceAsync(Emp001, "CARE_DAY", 2025));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 — graceful-vs-fail-closed isolation regression. The VacationSettlementService leaver /
    // termination / deferred path STAYS fail-closed (throws, never values against the live quota —
    // ADR-033 D10) and is NOT routed through the new graceful DatedEntitlementConfigResolver.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Structural isolation pin: <see cref="VacationSettlementService"/> does NOT take a
    /// <see cref="StatsTid.Infrastructure.DatedEntitlementConfigResolverFactory"/> dependency, so the
    /// 8102 graceful resolver cutover cannot have leaked into the fail-closed settlement family. (The
    /// fail-closed THROW behaviour itself is exercised by
    /// <c>TerminationSettlementTests.Termination_MissingDatedConfig_FailsClosed_NoLiveFallback_NoRow</c>
    /// and <c>...LeaverDeferred_MissingDatedConfig_FailsClosed_NoLiveFallback_NoRow</c>; this pin
    /// guards against a future refactor wiring the graceful resolver into the leaver path.)
    /// </summary>
    [Fact]
    public void R4_SettlementService_DoesNotDependOnGracefulResolver()
    {
        var ctor = typeof(StatsTid.Infrastructure.VacationSettlementService)
            .GetConstructors()
            .Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(
            typeof(StatsTid.Infrastructure.DatedEntitlementConfigResolverFactory), paramTypes);
        Assert.DoesNotContain(
            typeof(StatsTid.Infrastructure.DatedEntitlementConfigResolver), paramTypes);
    }

    // ════════════════════════════════════════════════════════════════════════
    // HTTP helpers (mirror SkemaMonthlyAccrualGuardTests).
    // ════════════════════════════════════════════════════════════════════════

    // Fixed "today" for the Balance read endpoints (OK26 era so the header resolves cleanly; the
    // entitlement quota anchors on the REQUESTED month, not today, so the value is month-driven).
    private static readonly DateOnly FixedToday = new(2026, 6, 15);

    private HttpClient CreateEmployeeClient()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday));
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp001, Emp001OrgId));
        return client;
    }

    private static DateOnly[] DistinctWeekdays(DateOnly start, int count)
    {
        var days = new DateOnly[count];
        var d = start;
        for (var i = 0; i < count; i++)
        {
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                d = d.AddDays(1);
            days[i] = d;
            d = d.AddDays(1);
        }
        return days;
    }

    private static async Task<HttpResponseMessage> PostAbsencesAsync(
        HttpClient client, int year, int month, (DateOnly Date, string Type, decimal Hours)[] absences)
    {
        var request = new
        {
            year,
            month,
            absences = absences.Select(a => new
            {
                date = a.Date.ToString("yyyy-MM-dd"),
                absenceType = a.Type,
                hours = a.Hours,
            }).ToArray(),
        };
        return await client.PostAsJsonAsync($"/api/skema/{Emp001}/save", request);
    }

    private async Task<JsonElement> GetSummaryAsync(int year, int month)
    {
        var client = CreateEmployeeClient();
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/summary?year={year}&month={month}");
        rsp.EnsureSuccessStatusCode();
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> GetSeriesAsync(int year, int month)
    {
        var client = CreateEmployeeClient();
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/series?year={year}&month={month}");
        rsp.EnsureSuccessStatusCode();
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static decimal VacationTotalQuota(JsonElement summary)
        => summary.GetProperty("entitlements").EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "VACATION")
            .GetProperty("totalQuota").GetDecimal();

    // ════════════════════════════════════════════════════════════════════════
    // DB seed / mutation helpers.
    // ════════════════════════════════════════════════════════════════════════

    private async Task SeedEmploymentProfileAsync(string employeeId, decimal partTimeFraction)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @f, '0001-01-01', NULL, 1)
            ON CONFLICT (employee_id, effective_from) DO UPDATE SET part_time_fraction = EXCLUDED.part_time_fraction
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("f", partTimeFraction);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SetUserOkVersionAsync(string employeeId, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET ok_version = @ok WHERE user_id = @e", conn);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("e", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>One open agreement-code row covering all of history (the common single-agreement case).</summary>
    private async Task SeedSingleAgreementHistoryAsync(string employeeId, string agreementCode)
    {
        await DeleteAgreementHistoryAsync(employeeId);
        await InsertAgreementCodeAsync(employeeId, agreementCode, new DateOnly(1, 1, 1), null);
    }

    /// <summary>
    /// A two-period dated agreement history: <paramref name="first"/> from the sentinel until
    /// <paramref name="switchOn"/> (exclusive), then <paramref name="second"/> open thereafter.
    /// </summary>
    private async Task SeedTwoPeriodAgreementHistoryAsync(
        string employeeId, string first, DateOnly switchOn, string second)
    {
        await DeleteAgreementHistoryAsync(employeeId);
        await InsertAgreementCodeAsync(employeeId, first, new DateOnly(1, 1, 1), switchOn);
        await InsertAgreementCodeAsync(employeeId, second, switchOn, null);
    }

    private async Task DeleteAgreementHistoryAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_agreement_codes WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertAgreementCodeAsync(
        string employeeId, string agreementCode, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @a, @from, @to, 1)
            """, conn);
        cmd.Parameters.AddWithValue("u", employeeId);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Set the (open) VACATION quota for a (agreement, OK) pair. Sentinel reseed, NOT supersession.</summary>
    private async Task SetVacationQuotaAsync(string agreementCode, string okVersion, decimal quota)
        => await SetEntitlementColumnAsync("VACATION", agreementCode, okVersion, "annual_quota", quota);

    /// <summary>Set CARE_DAY reset_month for a (agreement, OK) pair (unconstrained — only VACATION is pinned to 9).</summary>
    private async Task SetCareDayResetMonthAsync(string agreementCode, string okVersion, int resetMonth)
        => await SetEntitlementColumnAsync("CARE_DAY", agreementCode, okVersion, "reset_month", resetMonth);

    private async Task SetEntitlementColumnAsync(
        string entitlementType, string agreementCode, string okVersion, string column, object value)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Update only the open (effective_to IS NULL) seed row — no supersession (ADR-021 D5 preserved).
        await using var cmd = new NpgsqlCommand(
            $"""
            UPDATE entitlement_configs SET {column} = @v
            WHERE entitlement_type = @t AND agreement_code = @a AND ok_version = @ok AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("v", value);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        var affected = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, affected); // sentinel: the seed row must exist (catches drift in the seed shape)
    }

    private async Task<int> CountAbsenceRowsAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<(decimal TotalQuota, decimal Used, decimal Carryover)> ReadBalanceAsync(
        string employeeId, string entitlementType, int year)
    {
        var b = await TryReadBalanceAsync(employeeId, entitlementType, year);
        Assert.NotNull(b);
        return b!.Value;
    }

    private async Task<(decimal TotalQuota, decimal Used, decimal Carryover)?> TryReadBalanceAsync(
        string employeeId, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT total_quota, used, carryover_in FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    // ── Token minting (mirrors SkemaMonthlyAccrualGuardTests / YearOverviewTests) ──

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var tokenService = new JwtTokenService(DevSettings());
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ── Rule-engine stub: drives the REAL EntitlementValidationRule over the HTTP seam ──

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }

    private sealed class RuleEngineStubHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
            var result = EntitlementValidationRule.Evaluate(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(result, Camel), Encoding.UTF8, "application/json"),
            };
        }
    }
}
