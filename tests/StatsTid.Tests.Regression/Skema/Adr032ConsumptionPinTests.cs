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

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S66 / TASK-6607 / ADR-032 — the consumption-correctness pin suite at the Skema save seam
/// (<c>POST /api/skema/{id}/save</c>). Pins the ADR-032 D1/D2/D3/D5 behaviours that the
/// merged code (ConsumptionCalculator + two-phase in-lock valuation + norm-based guard + the
/// retired POST bypass) now exhibits. Sibling of
/// <see cref="SkemaAccrualCapTests"/> (full-timer flat-cap pins, byte-identical, KEPT) and
/// <see cref="StatsTid.Tests.Regression.Outbox.SkemaMonthlyAccrualGuardTests"/> — this file
/// reuses their exact rule-stub WAF + direct-seed scaffold.
///
/// <para><b>What changed (the half-time case has NO prior fixture — Census A, Step-0b).</b>
/// A half-time employee's natural full work day is 3.7h. Under ADR-032 D1 that consumes a FULL
/// feriedag (3.7 ÷ fullDayHours(=3.7) = 1.0), NOT 0.5; and a 7.4h entry on that day now EXCEEDS
/// the day's real norm (3.7) ⇒ 422. Both are pinned below against a booking dated in the
/// 0.5-fraction window (≥ 2025-01-01). Full-time 5-day employees stay byte-identical — those
/// pins live in <see cref="SkemaAccrualCapTests"/> and are deliberately not duplicated.</para>
///
/// <para><b>Single-valuation identity (ADR-032 D2).</b> For a booked batch the per-row 4dp
/// feriedage are the ONE valuation: Σ AbsenceRegistered.Feriedage (events JSONB) ==
/// Σ absences_projection.feriedage == the entitlement_balances.used delta. Pinned end-to-end in
/// <see cref="SingleValuationIdentity_HalfTimeBatch_EventSumEqualsProjectionSumEqualsUsedDelta"/>.</para>
///
/// <para>The in-process WAF harness has no rule-engine container, so (as in the sibling guard
/// suites) <see cref="IHttpClientFactory"/> is replaced by a stub that drives the REAL
/// <see cref="EntitlementValidationRule.Evaluate"/> over the validate-entitlement seam. DB facts
/// are seeded directly; assertions read <c>absences_projection</c> / <c>events</c> /
/// <c>entitlement_balances</c> back from the DB.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Adr032ConsumptionPinTests : IAsyncLifetime
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

    // Weekday anchors INSIDE the 0.5-fraction window (≥ 2025-01-01). 2025-01-06 is a Monday.
    private static readonly DateOnly HalfTimeMonday = new(2025, 1, 6);
    // A weekend day (Saturday 2025-01-11) for the zero-norm guard pins.
    private static readonly DateOnly HalfTimeSaturday = new(2025, 1, 11);

    // ════════════════════════════════════════════════════════════════════════
    // 1. Census-A half-time-window fixture (the case that had NO prior fixture).
    //    7.4h ⇒ 422 (D3 norm cap); 3.7h full day ⇒ exactly 1.0 feriedag (D1).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D3: a 7.4-hour absence on a HALF-TIME weekday (real norm 3.7h) exceeds the day's
    /// norm cap ⇒ 422 "Total absence hours exceed norm day". The pre-ADR-032 flat-7.4 cap would
    /// have ALLOWED it. Nothing persists. This is the genuinely-affected case Census A says had
    /// no fixture before TASK-6607.
    /// </summary>
    [Fact]
    public async Task HalfTimeWindow_FullDayHoursEntry_ExceedsNormCap_Rejected422_NothingPersisted()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        // 7.4h on a 0.5-fraction weekday → exceeds the 3.7h norm ⇒ 422.
        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeMonday.Year, HalfTimeMonday.Month,
            new[] { (HalfTimeMonday, "VACATION", 7.4m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Total absence hours exceed norm day", body.GetProperty("error").GetString());
        Assert.Equal(3.7m, body.GetProperty("maxHours").GetDecimal()); // the day's real half-time norm
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, HalfTimeMonday));
    }

    /// <summary>
    /// ADR-032 D1: a 3.7-hour absence (the half-timer's natural FULL work day) on a 0.5-fraction
    /// weekday consumes EXACTLY 1.0 feriedag (3.7 ÷ fullDayHours(3.7) = 1.0), NOT 0.5. Asserted
    /// on the recorded <c>absences_projection.feriedage</c> AND the <c>entitlement_balances.used</c>
    /// delta. The pre-ADR-032 flat divisor would have recorded 0.5.
    /// </summary>
    [Fact]
    public async Task HalfTimeWindow_NaturalFullDay_ConsumesExactlyOneFeriedag()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeMonday.Year, HalfTimeMonday.Month,
            new[] { (HalfTimeMonday, "VACATION", 3.7m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, HalfTimeMonday)); // D1: full day = 1.0

        // entitlement_year for a Jan-2025 VACATION (reset month 9) = 2024 (Jan < Sep).
        var (_, used) = await ReadBalanceAsync(employeeId, "VACATION", 2024);
        Assert.Equal(1.0m, used); // used delta == the single recorded feriedag (not 0.5)
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. The single-valuation identity (ONE end-to-end test) — ADR-032 D2.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D2 single-valuation identity, pinned as ONE assertion over a multi-row HALF-TIME
    /// batch with a PARTIAL day (3.7h full + 1.85h half) on two distinct weekdays:
    /// <c>Σ AbsenceRegistered.Feriedage (events JSONB) == Σ absences_projection.feriedage ==
    /// the entitlement_balances.used delta</c>, each equal to the per-row-4dp sum
    /// (1.0 + 0.5 = 1.5). The partial day exercises the per-row 4dp rounding site (one rounding
    /// site, no aggregate-vs-per-row drift).
    /// </summary>
    [Fact]
    public async Task SingleValuationIdentity_HalfTimeBatch_EventSumEqualsProjectionSumEqualsUsedDelta()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var day1 = HalfTimeMonday;            // Mon — 3.7h full day  → 1.0
        var day2 = HalfTimeMonday.AddDays(1); // Tue — 1.85h half day → 0.5
        var rsp = await PostAbsencesAsync(client, employeeId, day1.Year, day1.Month, new[]
        {
            (day1, "VACATION", 3.7m),
            (day2, "VACATION", 1.85m),
        });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        const decimal expectedSum = 1.5m; // 1.0 + 0.5 (per-row 4dp)

        var eventFeriedageSum = await SumEventFeriedageAsync(employeeId);
        var projectionFeriedageSum = await SumProjectionFeriedageAsync(employeeId);
        var (_, usedDelta) = await ReadBalanceAsync(employeeId, "VACATION", 2024);

        // The identity: every aggregate is the SAME per-row-4dp sum.
        Assert.Equal(expectedSum, eventFeriedageSum);
        Assert.Equal(expectedSum, projectionFeriedageSum);
        Assert.Equal(expectedSum, usedDelta);
        Assert.Equal(eventFeriedageSum, projectionFeriedageSum);
        Assert.Equal(projectionFeriedageSum, usedDelta);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. D1 examples — IMMEDIATE types + ANNUAL_ACTIVITY academic.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D1 applies to IMMEDIATE entitlement types too: a half-timer's CARE_DAY full day
    /// (3.7h) consumes exactly 1.0 feriedag — NO 5÷N factor, the same norm-based basis as VACATION.
    /// </summary>
    [Fact]
    public async Task HalfTime_CareDayFullDay_ConsumesExactlyOneDay()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        // CARE_DAY (IMMEDIATE, reset January) → entitlement_year = calendar year 2025.
        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeMonday.Year, HalfTimeMonday.Month,
            new[] { (HalfTimeMonday, "CARE_DAY", 3.7m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, HalfTimeMonday));
        var (_, used) = await ReadBalanceAsync(employeeId, "CARE_DAY", 2025);
        Assert.Equal(1.0m, used);
    }

    /// <summary>
    /// ADR-032 D3 ANNUAL_ACTIVITY fallback: an academic (AC_RESEARCH, ANNUAL_ACTIVITY norm = null)
    /// may book VACATION on a weekday — it is NOT a blanket null-reject. The recorded feriedage =
    /// hours ÷ (7.4 × fraction): a full-time academic's 7.4h day = 7.4 ÷ 7.4 = 1.0.
    /// </summary>
    [Fact]
    public async Task AnnualActivityAcademic_BooksVacationOnWeekday_Allowed_FeriedageOverNormFallback()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedAcademicEmployeeAsync(); // AC_RESEARCH, full-time
        var client = CreateEmployeeClient(employeeId, httpClient);

        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeMonday.Year, HalfTimeMonday.Month,
            new[] { (HalfTimeMonday, "VACATION", 7.4m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode); // academics stay bookable (not null-rejected)
        // fullDayHours = 7.4 × 1.0 = 7.4 ⇒ feriedage = 7.4 / 7.4 = 1.0.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, HalfTimeMonday));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Guard matrix (ADR-032 D3) — weekend / mixed-day / over-norm.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>ADR-032 D3: a VACATION (entitlement-consuming) row on a zero-norm weekend ⇒ 422
    /// "Entitlement absence on a non-working day". Nothing persists.</summary>
    [Fact]
    public async Task Guard_WeekendVacation_Rejected422()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeSaturday.Year, HalfTimeSaturday.Month,
            new[] { (HalfTimeSaturday, "VACATION", 3.7m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement absence on a non-working day", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, HalfTimeSaturday));
    }

    /// <summary>ADR-032 D3: a SICK_DAY (NON-entitlement) row on a weekend keeps the legacy
    /// behaviour — allowed under the flat-7.4 cap (a sick hour on a Saturday is not entitlement-
    /// gated). Persists with NULL feriedage (consumes no entitlement).</summary>
    [Fact]
    public async Task Guard_WeekendSickDay_Allowed_LegacyBehavior()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeSaturday.Year, HalfTimeSaturday.Month,
            new[] { (HalfTimeSaturday, "SICK_DAY", 7.4m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(employeeId, HalfTimeSaturday));
        Assert.Null(await ReadFeriedageNullableAsync(employeeId, HalfTimeSaturday)); // no entitlement consumed
    }

    /// <summary>ADR-032 D3: a MIXED weekend day (VACATION + SICK_DAY on the same Saturday) ⇒ 422
    /// that NAMES the offending entitlement row (VACATION) only — the non-entitlement SICK_DAY is
    /// not the cause. Nothing persists.</summary>
    [Fact]
    public async Task Guard_MixedWeekendDay_Rejected422_NamesEntitlementRowOnly()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeSaturday.Year, HalfTimeSaturday.Month,
            new[]
            {
                (HalfTimeSaturday, "VACATION", 3.7m),
                (HalfTimeSaturday, "SICK_DAY", 3.7m),
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement absence on a non-working day", body.GetProperty("error").GetString());
        var named = body.GetProperty("absenceTypes").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "VACATION" }, named); // only the entitlement row named, not SICK_DAY
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, HalfTimeSaturday));
    }

    /// <summary>ADR-032 D3: on a positive-norm day, the TOTAL across all absence types is capped at
    /// the day's real norm. A half-timer (norm 3.7h) booking VACATION 2.0h + SICK_DAY 2.0h = 4.0h
    /// &gt; 3.7 ⇒ 422 "Total absence hours exceed norm day". Nothing persists.</summary>
    [Fact]
    public async Task Guard_PositiveNormDay_AllTypesTotalOverNorm_Rejected422()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedHalfTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var rsp = await PostAbsencesAsync(client, employeeId, HalfTimeMonday.Year, HalfTimeMonday.Month,
            new[]
            {
                (HalfTimeMonday, "VACATION", 2.0m),
                (HalfTimeMonday, "SICK_DAY", 2.0m),
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Total absence hours exceed norm day", body.GetProperty("error").GetString());
        Assert.Equal(3.7m, body.GetProperty("maxHours").GetDecimal());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, HalfTimeMonday));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. OK stamping — entry-date-resolved across the OK24/OK26 boundary (D2).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D2 + TASK-1801 precedent: <c>AbsenceRegistered.OkVersion</c> is stamped from the
    /// ABSENCE ENTRY DATE (<c>OkVersionResolver.ResolveVersion(date)</c>), NOT the live
    /// <c>user.OkVersion</c>. A two-row batch straddling the 2026-04-01 OK24→OK26 cutover
    /// (one day in March 2026 = OK24, one in April 2026 = OK26) records DIFFERENT per-row OK
    /// versions on the events. The user row's <c>ok_version</c> is OK24 throughout, so a live-stamp
    /// would record OK24 for both — the discriminator.
    /// </summary>
    [Fact]
    public async Task OkStamping_BatchStraddlesOk24Ok26Boundary_PerRowEntryDateResolved()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedFullTimeEmployeeAsync(); // user.ok_version = OK24
        var client = CreateEmployeeClient(employeeId, httpClient);

        var marDay = new DateOnly(2026, 3, 31); // Tuesday, OK24 (boundary is 2026-04-01)
        var aprDay = new DateOnly(2026, 4, 1);  // Wednesday, OK26
        var rsp = await PostAbsencesAsync(client, employeeId, 2026, 4, new[]
        {
            (marDay, "VACATION", 7.4m),
            (aprDay, "VACATION", 7.4m),
        });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal("OK24", await ReadEventOkVersionAsync(employeeId, marDay));
        Assert.Equal("OK26", await ReadEventOkVersionAsync(employeeId, aprDay));
        // The two genuinely differ — proves per-row entry-date resolution, not a single live stamp.
        Assert.NotEqual(
            await ReadEventOkVersionAsync(employeeId, marDay),
            await ReadEventOkVersionAsync(employeeId, aprDay));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. Deny-pin — POST /api/absences retired; GET response shape unchanged.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-032 D5: the legacy <c>POST /api/absences</c> bypass is retired. A POST to that route
    /// ⇒ 404/405 (no handler) — no absence-write path bypasses the consumption guard.
    /// </summary>
    [Fact]
    public async Task DenyPin_PostAbsences_IsRetired()
    {
        var client = CreateEmployeeClient("emp001", _factory.CreateClient());

        var rsp = await client.PostAsJsonAsync("/api/absences", new
        {
            employeeId = "emp001",
            date = "2026-05-04",
            absenceType = "VACATION",
            hours = 7.4m,
        });

        Assert.True(
            rsp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected 404/405 for the retired POST /api/absences; got {(int)rsp.StatusCode} {rsp.StatusCode}.");
    }

    /// <summary>
    /// ADR-032 D5: <c>GET /api/absences/{employeeId}</c> is RETAINED and response-compatible — the
    /// Orchestrator service (<c>WeeklyCalculationPipeline</c>) consumes it. After a Skema save the
    /// GET returns each absence with the unchanged field set
    /// {employeeId, date, absenceType, hours, agreementCode, okVersion} — NO feriedage leak into
    /// the GET contract (the WeeklyCalculationPipeline shape is byte-compatible).
    /// </summary>
    [Fact]
    public async Task GetAbsences_ResponseShape_Unchanged_NoFeriedageLeak()
    {
        var httpClient = CreateRuleStubbedClient();
        var employeeId = await SeedFullTimeEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, httpClient);

        var day = new DateOnly(2026, 5, 4); // Monday, OK26
        var saveRsp = await PostAbsencesAsync(client, employeeId, 2026, 5,
            new[] { (day, "VACATION", 7.4m) });
        Assert.Equal(HttpStatusCode.OK, saveRsp.StatusCode);

        var getClient = CreateEmployeeClient(employeeId, _factory.CreateClient());
        var getRsp = await getClient.GetAsync($"/api/absences/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);

        var arr = await getRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, arr.GetArrayLength());
        var row = arr.EnumerateArray().Single();

        // Exact field set (WeeklyCalculationPipeline compat) — these present, feriedage ABSENT.
        Assert.Equal(employeeId, row.GetProperty("employeeId").GetString());
        Assert.Equal("2026-05-04", row.GetProperty("date").GetString());
        Assert.Equal("VACATION", row.GetProperty("absenceType").GetString());
        Assert.Equal(7.4m, row.GetProperty("hours").GetDecimal());
        Assert.True(row.TryGetProperty("agreementCode", out _));
        Assert.True(row.TryGetProperty("okVersion", out _));
        Assert.False(row.TryGetProperty("feriedage", out _), "GET /api/absences must NOT leak feriedage (WeeklyCalculationPipeline compat).");
    }

    // ── Scenario seeding ──

    /// <summary>
    /// Fresh AC/OK24 full-time employee, OPEN dated profile (1.0, '0001-01-01') + AC agreement.
    /// Used for the OK-stamping and full-time GET pins (byte-identical valuation).
    /// </summary>
    private async Task<string> SeedFullTimeEmployeeAsync()
    {
        var employeeId = await CreateUserAsync(OrgId, "AC", "OK24");
        await SeedAgreementCodeAsync(employeeId, "AC", new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, 1.000m, new DateOnly(1, 1, 1), null, 1);
        return employeeId;
    }

    /// <summary>
    /// Fresh AC/OK24 employee with a HALF-TIME (0.5) profile open from '0001-01-01' + AC agreement.
    /// The 2025-01 booking window therefore resolves a 0.5 fraction ⇒ per-day norm 3.7h.
    /// </summary>
    private async Task<string> SeedHalfTimeEmployeeAsync()
    {
        var employeeId = await CreateUserAsync(OrgId, "AC", "OK24");
        await SeedAgreementCodeAsync(employeeId, "AC", new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, 0.500m, new DateOnly(1, 1, 1), null, 1);
        return employeeId;
    }

    /// <summary>
    /// Fresh academic employee on AC_RESEARCH (ANNUAL_ACTIVITY norm = null) — full-time profile +
    /// AC_RESEARCH agreement. Exercises the ADR-032 D3 academic fallback (7.4 × fraction).
    /// </summary>
    private async Task<string> SeedAcademicEmployeeAsync()
    {
        var employeeId = await CreateUserAsync(OrgId, "AC_RESEARCH", "OK24");
        await SeedAgreementCodeAsync(employeeId, "AC_RESEARCH", new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, 1.000m, new DateOnly(1, 1, 1), null, 1);
        return employeeId;
    }

    // ── HTTP helpers (mirror SkemaAccrualCapTests) ──

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

    private static HttpClient CreateEmployeeClient(string employeeId, HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, OrgId));
        return client;
    }

    private static async Task<HttpResponseMessage> PostAbsencesAsync(
        HttpClient client, string employeeId, int year, int month,
        (DateOnly Date, string Type, decimal Hours)[] absences)
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
        return await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", request);
    }

    // ── DB helpers ──

    private async Task<string> CreateUserAsync(string orgId, string agreementCode, string okVersion)
    {
        var userId = "emp_s66_adr032_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S66 ADR-032 Consumption Pin User', NULL, @org, @ac, @ok, TRUE)
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task SeedProfileRowAsync(
        string employeeId, decimal fraction, DateOnly effectiveFrom, DateOnly? effectiveTo, long version)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @f, @from, @to, @v)
            ON CONFLICT (employee_id, effective_from) DO UPDATE SET part_time_fraction = EXCLUDED.part_time_fraction
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("f", fraction);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("v", version);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedAgreementCodeAsync(
        string employeeId, string agreementCode, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes
                (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @a, @from, @to, 1)
            ON CONFLICT (user_id, effective_from) DO UPDATE SET agreement_code = EXCLUDED.agreement_code
            """, conn);
        cmd.Parameters.AddWithValue("u", employeeId);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
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

    /// <summary>Recorded feriedage for the (employee, date) absence row. Asserts non-null.</summary>
    private async Task<decimal> ReadFeriedageAsync(string employeeId, DateOnly date)
    {
        var v = await ReadFeriedageNullableAsync(employeeId, date);
        Assert.NotNull(v);
        return v!.Value;
    }

    private async Task<decimal?> ReadFeriedageNullableAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT feriedage FROM absences_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull)
            return null;
        return (decimal)result;
    }

    /// <summary>Σ of the recorded absences_projection.feriedage for the employee (non-null rows).</summary>
    private async Task<decimal> SumProjectionFeriedageAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COALESCE(SUM(feriedage), 0) FROM absences_projection WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Σ of the AbsenceRegistered.Feriedage from the canonical event JSONB payloads.
    /// Reads <c>outbox_events.event_payload</c> — the SYNC write in the save tx (the canonical
    /// <c>events</c> table is populated later by the async OutboxPublisher; reading it here would
    /// race the drain). EventSerializer camelCase since S3 ⇒ the key is 'feriedage'.</summary>
    private async Task<decimal> SumEventFeriedageAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM((event_payload ->> 'feriedage')::numeric), 0)
            FROM outbox_events
            WHERE stream_id = @s AND event_type = 'AbsenceRegistered'
              AND (event_payload ->> 'feriedage') IS NOT NULL
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The AbsenceRegistered.OkVersion stamped on the event for (employee, date). Reads
    /// the sync-written <c>outbox_events.event_payload</c> (see <see cref="SumEventFeriedageAsync"/>
    /// for why not the async-drained <c>events</c> table).</summary>
    private async Task<string> ReadEventOkVersionAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (event_payload ->> 'okVersion')
            FROM outbox_events
            WHERE stream_id = @s AND event_type = 'AbsenceRegistered'
              AND (event_payload ->> 'date') = @d
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("d", date.ToString("yyyy-MM-dd"));
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(decimal TotalQuota, decimal Used)> ReadBalanceAsync(
        string employeeId, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT total_quota, used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Expected an entitlement_balances row for {entitlementType}/{year}.");
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    // ── Token minting ──

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
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false);
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
