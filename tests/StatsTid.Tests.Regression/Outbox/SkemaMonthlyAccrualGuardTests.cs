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
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S60 / TASK-6009 / ADR-030 — HTTP-level regression for monthly vacation accrual
/// (Ferieloven samtidighedsferie) at the Skema POST seam
/// (<c>POST /api/skema/{id}/save</c>), mirroring the WAF&lt;Program&gt; + rule-engine-stub
/// pattern of <see cref="SkemaEntitlementEligibilityGuardTests"/>.
///
/// <para>Pins (per the SPRINT-60 TASK-6009 validation criteria):</para>
/// <list type="bullet">
///   <item><description><b>VACATION forskud</b> — a full-time employee may book up to the
///   dynamic ferieår cap (full annual 25 d) early in the ferieår, even though earned-to-date
///   is much lower (manager approval = the §7 agreement, ADR-030). Booking beyond that cap
///   is rejected 422.</description></item>
///   <item><description><b>SPECIAL_HOLIDAY no-forskud</b> — booking beyond earned-to-date is
///   rejected 422 (ferieaftale §13 stk.4), carryover = 0 so the cap proves no-forskud (not a
///   zero-carryover artifact). A within-earned booking succeeds.</description></item>
///   <item><description><b>Dual enforcement</b> — both the pre-tx rule-engine check AND the
///   atomic <c>CheckAndAdjustAsync</c> guard reject the over-cap booking; nothing persists.</description></item>
///   <item><description><b>total_quota stays annual</b> — after the first successful VACATION
///   booking the freshly-seeded balance row's <c>total_quota</c> = annual (25), NOT the
///   forskud/earned cap; carryover counted once.</description></item>
///   <item><description><b>Ferieår-boundary batch anchor</b> — a multi-date batch spanning the
///   ferieår boundary anchors the entitlement-year on the <c>firstAbsenceDate</c> (the MIN
///   of the batch).</description></item>
///   <item><description><b>IMMEDIATE unchanged</b> — a CARE_DAY booking within its annual
///   quota still succeeds (the accrual change does not touch IMMEDIATE types).</description></item>
/// </list>
///
/// <para>
/// The in-process WAF harness has no rule-engine container, so (as in the sibling guard test)
/// <see cref="IHttpClientFactory"/> is replaced by a stub that drives the REAL
/// <see cref="EntitlementValidationRule.Evaluate"/> over the validate-entitlement seam — the
/// per-type <c>bookableLimit</c> the Backend computes is therefore exercised end-to-end. DB
/// facts (employee_profiles, user_agreement_codes, employment_start_date, entitlement_balances)
/// are seeded directly in setup; assertions read <c>absences_projection</c> /
/// <c>entitlement_balances</c> back from the DB.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaMonthlyAccrualGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Seeded employee (init.sql): emp001, STY01, AC, OK24. Self-save → actor == route id.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);

        // The S60 Skema POST seam sources the dated part-time fraction from employee_profiles
        // and the agreement code from user_agreement_codes (both as-of firstAbsenceDate). Seed
        // a full-ferieår, full-time profile + AC agreement for emp001 so accrual is computed
        // against a real fraction (not a 422 employment_profile_missing).
        await SeedEmploymentProfileAsync(Emp001, partTimeFraction: 1.0m);
        await SeedAgreementCodeAsync(Emp001, "AC");
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // VACATION — forskudsferie allowed up to the dynamic ferieår cap (full annual).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Early in the ferieår (Nov, ~3 months in ⇒ earned ≈ 6,25 d) a full-time employee books
    /// 10 VACATION days. earned-to-date is well under 10, but the dynamic forskud cap =
    /// earned + still-accruable = full annual 25 (whole-ferieår hire, null employment_start) ⇒
    /// ALLOWED. The booking persists and total_quota seeds at the ANNUAL 25 (not the cap).
    /// </summary>
    [Fact]
    public async Task Vacation_ForskudWithinDynamicCap_Allowed_TotalQuotaStaysAnnual()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        // 10 VACATION days, one per distinct date (each = 7.4h ≤ the per-day norm cap), all in
        // Nov 2025 (ferieår 2025, reset month 9). firstAbsenceDate ⇒ entitlement_year 2025.
        var dates = DistinctDays(new DateOnly(2025, 11, 3), 10);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var rsp = await PostAbsencesAsync(client, 2025, 11, absences);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, dates[0]));

        // entitlement_year for a Nov-2025 VACATION absence (reset month 9) = 2025.
        var (totalQuota, used, carryover) = await ReadBalanceAsync(Emp001, "VACATION", 2025);
        Assert.Equal(25m, totalQuota);  // annual — NOT the forskud/earned cap
        Assert.Equal(10m, used);        // 10 days booked (forskud)
        Assert.Equal(0m, carryover);    // first-INSERT carryover defaults to 0, counted once
    }

    /// <summary>
    /// Booking beyond the dynamic cap (full annual 25 for a whole-ferieår full-timer) is
    /// rejected 422 and nothing persists — proving the forskud cap is bounded (no infinite
    /// borrowing) and enforced at the pre-tx rule-engine check.
    /// </summary>
    [Fact]
    public async Task Vacation_BeyondDynamicCap_Rejected422_NothingPersisted()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        // 26 distinct VACATION days (each 7.4h) > the full annual 25 forskud cap. Spread one per
        // date starting in Nov 2025 (absences are not month-range-gated; the per-type batch
        // anchors on the MIN = Nov 2025 ⇒ ferieår 2025).
        var dates = DistinctDays(new DateOnly(2025, 11, 3), 26);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var rsp = await PostAbsencesAsync(client, 2025, 11, absences);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, dates[0]));
    }

    // ════════════════════════════════════════════════════════════════════════
    // SPECIAL_HOLIDAY — S80 / TASK-8001 (ADR-033 Slice 2, R1/R2 — D11 calendar-accrual correction).
    // ════════════════════════════════════════════════════════════════════════
    //
    // PRE-S80 (the OLD mis-model these tests pinned): SPECIAL_HOLIDAY was reset_month=9 (Sep–Aug
    // ferieår, MONTHLY_ACCRUAL), so a Nov booking saw only ~3 months earned (≈1,25 d) and a "no
    // forskud beyond earned" 422 was reachable mid-ferieår. POST-S80 (Cirkulære 021-24 §12, the
    // verified law): accrual is the CALENDAR year (1 Jan–31 Dec) and days are TAKEN in the FOLLOWING
    // 1 May–30 Apr window (§12 stk.2). By the time a day can be booked in its taking window the
    // accrual year is FULLY accrued (5 d), so the "beyond earned" forskud scenario is unreachable
    // for SPECIAL_HOLIDAY — only the annual 5-day QUOTA gates. These tests are rewritten to pin the
    // NEW model + the R2 usage→accrual-year mapping (both halves). They FAIL on the unfixed reset-9
    // code (which keys a Nov-2025 booking to ferieår 2025 and earns only ~1,25 d).

    /// <summary>
    /// S80 R2 — a SPECIAL_HOLIDAY booking in the May–Dec head of the taking window keys to accrual
    /// year T−1 (NOT the booking's own year), and that accrual year is fully accrued (5 d), so a
    /// booking that EXCEEDS the annual 5-day quota is rejected 422 (quota, not forskud). Booking 6
    /// distinct days in Nov 2025 ⇒ accrual year 2024 ⇒ earned = full 5 d ⇒ 6 > 5 ⇒ 422; nothing
    /// persists, the balance row (if ensured) is keyed to year 2024 and never used>0.
    /// </summary>
    [Fact]
    public async Task SpecialHoliday_BeyondQuota_Rejected422_NothingPersisted_KeyedToAccrualYear()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        // 6 distinct SPECIAL_HOLIDAY days in Nov 2025 (May–Dec ⇒ accrual year 2024, fully accrued
        // 5 d). 6 > 5 ⇒ quota 422. carryover = 0 (CarryoverMax 0), so the cap is the annual quota.
        var dates = DistinctDays(new DateOnly(2025, 11, 4), 6);
        var absences = dates.Select(d => (d, "SPECIAL_HOLIDAY_ALLOWANCE", 7.4m)).ToArray();

        var rsp = await PostAbsencesAsync(client, 2025, 11, absences);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, dates[0]));

        // R2 keying: the balance is keyed to accrual year 2024 (T−1), NOT 2025. Nothing persisted.
        var b2025 = await TryReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2025);
        Assert.Null(b2025); // the pre-S80 reset-9 code would have keyed/touched year 2025
        var b2024 = await TryReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2024);
        if (b2024 is { } b)
            Assert.Equal(0m, b.Used); // ensure-row INSERT may exist at zero-state, never used>0
    }

    /// <summary>
    /// S80 R2 — a SPECIAL_HOLIDAY booking in the May–Dec head of the taking window: 1 day in Nov
    /// 2025 is within the fully-accrued 5-day quota ⇒ ALLOWED, persists, and the balance is keyed to
    /// accrual year 2024 (T−1) with total_quota = annual 5.
    /// </summary>
    [Fact]
    public async Task SpecialHoliday_MayToDec_WithinQuota_Allowed_KeyedToAccrualYearTMinus1()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var date = new DateOnly(2025, 11, 4); // May–Dec 2025 ⇒ accrual year 2024 (fully accrued 5 d)

        var rsp = await PostAbsencesAsync(
            client, 2025, 11, new[] { (date, "SPECIAL_HOLIDAY_ALLOWANCE", 1 * 7.4m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, date));

        var (totalQuota, used, _) = await ReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2024);
        Assert.Equal(5m, totalQuota); // annual
        Assert.Equal(1m, used);
        // R2 discriminator: nothing is keyed to the booking's own calendar year (the pre-S80 key).
        Assert.Null(await TryReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2025));
    }

    /// <summary>
    /// S80 R2 (the SECOND half of the two-calendar-year mapping) — a SPECIAL_HOLIDAY booking in the
    /// Jan–Apr TAIL of the taking window keys to accrual year T−2. A 1-day booking in Mar 2025
    /// (Jan–Apr ⇒ accrual year 2023, fully accrued) is ALLOWED and keyed to year 2023, NOT 2024 and
    /// NOT 2025. This is the discriminating both-halves AC the Step-0b BLOCKER demanded.
    /// </summary>
    [Fact]
    public async Task SpecialHoliday_JanToApr_WithinQuota_Allowed_KeyedToAccrualYearTMinus2()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var date = new DateOnly(2025, 3, 4); // Jan–Apr 2025 ⇒ accrual year 2023 (T−2, fully accrued)

        var rsp = await PostAbsencesAsync(
            client, 2025, 3, new[] { (date, "SPECIAL_HOLIDAY_ALLOWANCE", 1 * 7.4m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, date));

        var (_, used, _) = await ReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2023);
        Assert.Equal(1m, used);
        // Discriminators: NOT keyed to 2024 (the May–Dec head's year) nor 2025 (the pre-S80 key).
        Assert.Null(await TryReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2024));
        Assert.Null(await TryReadBalanceAsync(Emp001, "SPECIAL_HOLIDAY", 2025));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Ferieår-boundary batch — firstAbsenceDate anchors the entitlement year.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A multi-date VACATION batch spanning the ferieår boundary (one day in Aug 2026 = ferieår
    /// 2025; one in Sep 2026 = ferieår 2026) anchors the per-type batch on the MIN date
    /// (firstAbsenceDate = Aug 2026 ⇒ entitlement_year 2025). The whole batch (3 days) lands in
    /// the 2025 balance row, proving the anchor is the batch MIN, not each absence's own year.
    /// </summary>
    [Fact]
    public async Task Vacation_BoundarySpanningBatch_AnchorsOnFirstAbsenceDate()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var augDate = new DateOnly(2026, 8, 28); // ferieår 2025
        var sepDate = new DateOnly(2026, 9, 1);  // ferieår 2026
        var sep2 = new DateOnly(2026, 9, 2);

        var rsp = await PostAbsencesAsync(client, 2026, 9, new[]
        {
            (augDate, "VACATION", 1 * 7.4m),
            (sepDate, "VACATION", 1 * 7.4m),
            (sep2, "VACATION", 1 * 7.4m),
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, augDate));
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, sepDate));

        // All 3 days aggregated under the firstAbsenceDate-anchored year (2025), not split.
        var (_, used2025, _) = await ReadBalanceAsync(Emp001, "VACATION", 2025);
        Assert.Equal(3m, used2025);
        Assert.Null(await TryReadBalanceAsync(Emp001, "VACATION", 2026)); // no 2026 row created
    }

    // ════════════════════════════════════════════════════════════════════════
    // IMMEDIATE types unchanged — CARE_DAY within annual quota still succeeds.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CARE_DAY (IMMEDIATE, annual 2 d, calendar year) within quota still succeeds and persists
    /// — the accrual activation does not regress IMMEDIATE types. total_quota = annual 2.
    /// </summary>
    [Fact]
    public async Task ImmediateCareDay_WithinQuota_Unchanged_Succeeds()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var date = new DateOnly(2026, 3, 10);

        var rsp = await PostAbsencesAsync(client, 2026, 3, new[] { (date, "CARE_DAY", 1 * 7.4m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, date));

        var (totalQuota, used, _) = await ReadBalanceAsync(Emp001, "CARE_DAY", 2026);
        Assert.Equal(2m, totalQuota);
        Assert.Equal(1m, used);
    }

    /// <summary>
    /// CARE_DAY beyond its annual 2-day quota is rejected 422 — IMMEDIATE rejection path is
    /// unchanged (cap = effectiveQuota + carryover, no bookableLimit override).
    /// </summary>
    [Fact]
    public async Task ImmediateCareDay_BeyondQuota_Rejected422()
    {
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var date = new DateOnly(2026, 4, 6);

        var rsp = await PostAbsencesAsync(client, 2026, 4, new[] { (date, "CARE_DAY", 3 * 7.4m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, date));
    }

    // ── HTTP helpers ──

    private HttpClient CreateRuleStubbedClient()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
            });
        });
        return factory.CreateClient();
    }

    private static HttpClient CreateEmployeeClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp001, Emp001OrgId));
        return client;
    }

    /// <summary>
    /// N distinct WEEKDAY dates starting at <paramref name="start"/> (weekends skipped). Each
    /// absence row must sit on its own date (the per-day norm cap rejects &gt;7,4h on a single
    /// date), so "book K vacation days" is modelled as K rows on K distinct dates.
    ///
    /// <para>
    /// <b>S66 / TASK-6607 / ADR-032 D3 — weekends now excluded.</b> The original comment ("weekend
    /// filtering is unnecessary") held under the pre-ADR-032 flat-7,4 cap, which accepted weekend
    /// entitlement rows. ADR-032 D3 now rejects an entitlement-consuming row on a zero-norm
    /// (weekend) day with <c>422 "Entitlement absence on a non-working day"</c> — so a multi-day
    /// VACATION/SPECIAL_HOLIDAY batch that strode through Sat/Sun would 422 on the weekend row
    /// before the quota/accrual gate these tests target is ever reached. Skipping weekends keeps
    /// these tests pinned to the FORSKUD/EARNED cap they assert (full-timer behaviour unchanged).
    /// Citation-gated rewrite per the S64 discipline.
    /// </para>
    /// </summary>
    private static DateOnly[] DistinctDays(DateOnly start, int count)
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

    // ── DB helpers ──

    private async Task SeedEmploymentProfileAsync(string employeeId, decimal partTimeFraction)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Full-ferieår live row (effective_from sentinel '0001-01-01', open). Idempotent via the
        // live-unique index; ON CONFLICT DO NOTHING in case a prior run seeded it.
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

    private async Task SeedAgreementCodeAsync(string employeeId, string agreementCode)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @a, '0001-01-01', NULL, 1)
            ON CONFLICT (user_id, effective_from) DO UPDATE SET agreement_code = EXCLUDED.agreement_code
            """, conn);
        cmd.Parameters.AddWithValue("u", employeeId);
        cmd.Parameters.AddWithValue("a", agreementCode);
        await cmd.ExecuteNonQueryAsync();
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

    // ── Token minting (mirrors SkemaEntitlementEligibilityGuardTests) ──

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
        // S73 / TASK-7300 (R1): the endpoints now resolve the NAMED RuleEngine client and use
        // RELATIVE request URIs; this stub replaces the whole factory, so it supplies the
        // BaseAddress the production registration sets (behavior-preserving fixture change).
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
