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
/// S73 / TASK-7301 — the FULL-DAY-ONLY guard matrix + the R3 served-contract pins (SPRINT-73
/// R2/R3; owner ruling D-A 2026-06-13: CARE_DAY + SENIOR_DAY registrations must equal the
/// day's ADR-032 consumption basis EXACTLY). Reuses the
/// <see cref="Adr032ConsumptionPinTests"/> scaffold verbatim (rule-stub WAF + direct-seed +
/// projection/balance read-back).
///
/// <para><b>The guard matrix (R2):</b> exact-basis 200 (+ consumes exactly 1.0 recorded
/// feriedage per ADR-032 D2); under 422 <c>absence_full_day_only</c> carrying
/// <c>requiredHours</c>; over 422 (the pre-existing D3 norm-cap fires first — the OUTCOME is
/// rejection with either typed error); the ANNUAL_ACTIVITY academic fallback (exact
/// 7.4 × fraction → 200); the dated-read case (a fraction change mid-month: each day's
/// required value follows the dated basis); zero-norm days stay covered by the existing D3
/// 422 (pinned unchanged).</para>
///
/// <para><b>The D-A consequence pins:</b> a full omsorgsdag + ANY other same-day absence →
/// the total-cap 422 (the existing per-day cap totals ALL absence types, so a full day is the
/// day's ONLY absence); a full omsorgsdag + same-day WORK hours (project entries + work time)
/// → 200 (work is not an absence).</para>
///
/// <para><b>The R3 pins:</b> the month GET serves <c>fullDayOnly</c> on BOTH absence-type DTO
/// surfaces (the catalog — <c>absenceTypes</c> + <c>catalogs.absenceTypes</c> — AND the
/// visible-row <c>rowPreferences.absenceTypes</c> items) and the per-day dated
/// <c>consumptionBasis: [{date, hours|null}]</c> array; the served==guard IDENTITY test
/// drives both the served array and a guard rejection from ONE fixture and asserts the served
/// value equals the guard's <c>requiredHours</c> (the S72-B1 cross-surface-drift class).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaFullDayOnlyGuardTests : IAsyncLifetime
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
        // Boot ONCE so the S31 profile seeder runs now; tests create their users AFTER this
        // boot and own their profile rows (the S63 boot-order lesson).
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // Weekday/weekend anchors (the Adr032ConsumptionPinTests convention): 2025-01-06 is a
    // Monday; 2025-01-11 a Saturday. The mid-March pair brackets the dated fraction change.
    private static readonly DateOnly Monday = new(2025, 1, 6);
    private static readonly DateOnly Saturday = new(2025, 1, 11);
    private static readonly DateOnly FractionSwitch = new(2025, 3, 16);   // profile boundary
    private static readonly DateOnly MondayBeforeSwitch = new(2025, 3, 10); // fraction 1.0 → basis 7.4
    private static readonly DateOnly MondayAfterSwitch = new(2025, 3, 17);  // fraction 0.5 → basis 3.7

    // ════════════════════════════════════════════════════════════════════════
    // 1. The guard matrix (R2).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Exact basis: a half-timer's CARE_DAY at exactly 3.7h (= the day's ADR-032
    /// basis) is allowed and consumes EXACTLY 1.0 recorded feriedag (ADR-032 D2).</summary>
    [Fact]
    public async Task FullDay_ExactBasis_Allowed200_ConsumesExactlyOneFeriedag()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.500m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 3.7m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, Monday));
        var used = await ReadUsedAsync(employeeId, "CARE_DAY", 2025);
        Assert.Equal(1.0m, used);
    }

    /// <summary>Under the basis: a partial CARE_DAY (1.85h on a 3.7h-basis day) is rejected
    /// with the typed 422 {error, absenceType, date, requiredHours}; nothing persists.</summary>
    [Fact]
    public async Task FullDay_UnderBasis_Rejected422_TypedErrorCarriesRequiredHours()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.500m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 1.85m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", body.GetProperty("error").GetString());
        Assert.Equal("CARE_DAY", body.GetProperty("absenceType").GetString());
        Assert.Equal("2025-01-06", body.GetProperty("date").GetString());
        Assert.Equal(3.7m, body.GetProperty("requiredHours").GetDecimal());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, Monday));
    }

    /// <summary>Over the basis: 7.4h on a 3.7h-basis day. The pre-existing D3 norm-cap fires
    /// first (total > norm) — the pinned OUTCOME is rejection with EITHER typed error and
    /// nothing persisted.</summary>
    [Fact]
    public async Task FullDay_OverBasis_RejectedWithEitherTypedError_NothingPersisted()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.500m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 7.4m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error").GetString();
        Assert.True(
            error is "absence_full_day_only" or "Total absence hours exceed norm day",
            $"Expected one of the two typed rejections, got '{error}'.");
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, Monday));
    }

    /// <summary>SENIOR_DAY is full-day gated too: an eligible (age 65) full-timer's PARTIAL
    /// senior day is rejected with the typed 422 (requiredHours = 7.4).</summary>
    [Fact]
    public async Task FullDay_SeniorDayPartial_Rejected422_TypedError()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m, birthDate: new DateOnly(1960, 1, 1));
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "SENIOR_DAY", 3.7m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", body.GetProperty("error").GetString());
        Assert.Equal("SENIOR_DAY", body.GetProperty("absenceType").GetString());
        Assert.Equal(7.4m, body.GetProperty("requiredHours").GetDecimal());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, Monday));
    }

    /// <summary>The ANNUAL_ACTIVITY academic fallback (ADR-032 D3, deliberately KEPT — R2):
    /// an AC_RESEARCH half-timer's basis is 7.4 × 0.5 = 3.7; the exact value is allowed and
    /// consumes 1.0 feriedag. Academics keep their omsorgsdage.</summary>
    [Fact]
    public async Task FullDay_AcademicFallback_Exact74TimesFraction_Allowed200()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.500m, agreementCode: "AC_RESEARCH");
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 3.7m) });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, Monday));
    }

    /// <summary>The academic UNDER case rejects with requiredHours = the 7.4 × fraction
    /// fallback (NOT a weekday norm — AC_RESEARCH has none).</summary>
    [Fact]
    public async Task FullDay_AcademicFallback_UnderBasis_Rejected422()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.500m, agreementCode: "AC_RESEARCH");
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 2.0m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", body.GetProperty("error").GetString());
        Assert.Equal(3.7m, body.GetProperty("requiredHours").GetDecimal());
    }

    /// <summary>The DATED-read pin: a fraction change mid-month (1.0 → 0.5 at 2025-03-16)
    /// makes each day's required value follow the DATED basis — one save carrying the exact
    /// old-basis day (7.4 on Mar 10) AND the exact new-basis day (3.7 on Mar 17) succeeds,
    /// each consuming exactly 1.0 feriedag.</summary>
    [Fact]
    public async Task FullDay_DatedBasis_FractionChangeMidMonth_ExactPerDayValues_Allowed()
    {
        var employeeId = await SeedFractionSwitchEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, 2025, 3, new[]
        {
            (MondayBeforeSwitch, "CARE_DAY", 7.4m), // dated fraction 1.0 ⇒ basis 7.4
            (MondayAfterSwitch, "CARE_DAY", 3.7m),  // dated fraction 0.5 ⇒ basis 3.7
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, MondayBeforeSwitch));
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, MondayAfterSwitch));
    }

    /// <summary>The DATED-read rejection side: the NEW basis (3.7) on the OLD-fraction day is
    /// under that day's dated basis (7.4) → typed 422 with requiredHours 7.4; the OLD basis
    /// (7.4) on the NEW-fraction day exceeds that day's norm → rejected (either typed error).</summary>
    [Fact]
    public async Task FullDay_DatedBasis_WrongDaysValue_RejectedPerDatedBasis()
    {
        var employeeId = await SeedFractionSwitchEmployeeAsync();
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        // New-basis hours on the old-fraction day: under 7.4 → the typed full-day 422.
        var underRsp = await PostAbsencesAsync(client, employeeId, 2025, 3,
            new[] { (MondayBeforeSwitch, "CARE_DAY", 3.7m) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, underRsp.StatusCode);
        var underBody = await underRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", underBody.GetProperty("error").GetString());
        Assert.Equal(7.4m, underBody.GetProperty("requiredHours").GetDecimal());

        // Old-basis hours on the new-fraction day: over 3.7 → rejection (norm-cap may fire first).
        var overRsp = await PostAbsencesAsync(client, employeeId, 2025, 3,
            new[] { (MondayAfterSwitch, "CARE_DAY", 7.4m) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overRsp.StatusCode);
        var overBody = await overRsp.Content.ReadFromJsonAsync<JsonElement>();
        var overError = overBody.GetProperty("error").GetString();
        Assert.True(
            overError is "absence_full_day_only" or "Total absence hours exceed norm day",
            $"Expected one of the two typed rejections, got '{overError}'.");

        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, MondayBeforeSwitch));
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, MondayAfterSwitch));
    }

    /// <summary>Zero-norm days stay covered by the EXISTING D3 422 (pinned unchanged): a
    /// CARE_DAY on a Saturday rejects with the non-working-day error, not the full-day one.</summary>
    [Fact]
    public async Task FullDay_ZeroNormDay_ExistingNonWorkingDay422_Unchanged()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Saturday.Year, Saturday.Month,
            new[] { (Saturday, "CARE_DAY", 7.4m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement absence on a non-working day", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, Saturday));
    }

    /// <summary>H1 — the rounded full-day value must be BOOKABLE. An academic on fraction
    /// 0.335 has basis 7.4 × 0.335 = 2.4790 → RoundBasis 2.48 (ADR-032 D1 AwayFromZero). The
    /// full-day rule + the served consumptionBasis both use 2.48; pre-fix the D3 per-day cap
    /// compared against the RAW 2.4790, so the exact full day (2.48) was UNBOOKABLE. The cap
    /// now uses the SAME rounded basis: 2.48 → 200 (consumes 1.0 feriedag); 2.49 (above the
    /// rounded basis) → 422.</summary>
    [Fact]
    public async Task FullDay_RoundedAcademicBasis_ExactRoundedValueBookable_OverRejected()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.335m, agreementCode: "AC_RESEARCH");
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        // The served per-day basis IS the rounded value (the R3 identity, AwayFromZero 2-dec).
        var month = await GetMonthAsync(employeeId, Monday.Year, Monday.Month);
        var served = month.GetProperty("consumptionBasis").EnumerateArray()
            .Single(e => e.GetProperty("date").GetString() == "2025-01-06")
            .GetProperty("hours").GetDecimal();
        Assert.Equal(2.48m, served); // Math.Round(7.4 × 0.335, 2, AwayFromZero)

        // The exact rounded full day is bookable (pre-fix the raw 2.4790 D3 cap rejected it).
        var okRsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 2.48m) });
        Assert.Equal(HttpStatusCode.OK, okRsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(employeeId, Monday)); // persisted (was 422 pre-fix)
        // S73 Step-7a B1 — the booking records EXACTLY 1.0 feriedag (not 2.48 / 2.479 = 1.0004):
        // the full-day consumption divisor is the SAME rounded basis the guard required.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, Monday));

        // 2.49 (above the rounded basis) is rejected (the D3 cap fires at the rounded value).
        var overEmployeeId = await SeedEmployeeAsync(fraction: 0.335m, agreementCode: "AC_RESEARCH");
        var overClient = CreateEmployeeClient(overEmployeeId, CreateRuleStubbedClient());
        var overRsp = await PostAbsencesAsync(overClient, overEmployeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 2.49m) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overRsp.StatusCode);
        Assert.Equal(0, await CountAbsenceRowsAsync(overEmployeeId, Monday));
    }

    /// <summary>S73 Step-7a B1 — TWO full-day CARE_DAY bookings by a NON-clean-fraction academic
    /// (fraction 0.335 → basis 2.479 → RoundBasis 2.48) in the SAME year each consume EXACTLY
    /// 1.0 feriedag, so the running total is 2.0 — NOT 2.0008. The CARE_DAY annual quota is 2
    /// days; pre-fix the 1.0004-per-booking drift breached the quota on the SECOND booking
    /// (2.0008 &gt; 2.0 → a spurious quota 422). Post-fix both succeed (2.0 ≤ 2.0).</summary>
    [Fact]
    public async Task FullDay_RoundedBasis_TwoBookingsSameYear_DoNotBreachQuota()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.335m, agreementCode: "AC_RESEARCH");
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        // Two distinct weekdays in the SAME entitlement year (2025) — Mon 2025-01-06 + Tue 2025-01-07.
        var tuesday = new DateOnly(2025, 1, 7);

        var firstRsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 2.48m) });
        Assert.Equal(HttpStatusCode.OK, firstRsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, Monday));

        // The SECOND full day must NOT trip the 2-day quota (it would at 1.0004 + 1.0004 = 2.0008).
        var secondRsp = await PostAbsencesAsync(client, employeeId, tuesday.Year, tuesday.Month,
            new[] { (tuesday, "CARE_DAY", 2.48m) });
        Assert.Equal(HttpStatusCode.OK, secondRsp.StatusCode); // 200, not a quota 422
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, tuesday));

        // The recorded usage is exactly 2.0 days, at the quota cap.
        Assert.Equal(2.0m, await ReadUsedAsync(employeeId, "CARE_DAY", 2025));
    }

    /// <summary>H2 — the full-day guard resolves the OK version FROM THE ABSENCE DATE, not the
    /// live user.OkVersion. An OK26-current employee (live ok_version OK26) edits a March-2026
    /// (OK24-dated) CARE_DAY absence on AC. AC's seeded CARE_DAY config exists under OK24
    /// (full_day_only TRUE); this fixture CLOSES the OK26 CARE_DAY config row so it is absent at
    /// the live OK version. Pre-fix the guard read user.OkVersion (OK26) → no OK26 config → the
    /// full-day rule was SKIPPED and a partial CARE_DAY slipped through (200). The fix resolves
    /// OkVersionResolver.ResolveVersion(2026-03-09) = OK24 → finds the OK24 config → enforces the
    /// full-day rule → the partial 422s. (AC's agreement_configs resolve the norm under both OK
    /// versions, so the basis is 7.4 either way — the discriminator is purely the CARE_DAY
    /// entitlement config's OK version.)</summary>
    [Fact]
    public async Task FullDay_CrossOkVersion_GuardReadsAbsenceDatedOkConfig()
    {
        // Close AC's OK26 CARE_DAY entitlement config so the live (OK26) read finds nothing;
        // the OK24 row (full_day_only TRUE) remains — the absence-dated read must find it.
        await CloseCareDayConfigAsync(agreementCode: "AC", okVersion: "OK26");

        // OK26-live, full-time employee on AC, covering March 2026.
        var employeeId = await SeedCrossOkEmployeeAsync("AC");
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var march = new DateOnly(2026, 3, 9); // a Monday in the OK24-dated window
        var rsp = await PostAbsencesAsync(client, employeeId, 2026, 3,
            new[] { (march, "CARE_DAY", 2.0m) }); // partial; full-time basis is 7.4

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", body.GetProperty("error").GetString());
        Assert.Equal(7.4m, body.GetProperty("requiredHours").GetDecimal());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, march));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. The D-A consequence pins.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>D-A consequence 1 (the ratified arithmetic): a FULL CARE_DAY plus ANY other
    /// same-day absence exceeds the all-types per-day cap → the total-cap 422; nothing
    /// persists. A full omsorgsdag is the day's ONLY absence.</summary>
    [Fact]
    public async Task FullDayPlusOtherSameDayAbsence_TotalCap422_NothingPersisted()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var rsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month, new[]
        {
            (Monday, "CARE_DAY", 7.4m),
            (Monday, "SICK_DAY", 1.0m),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Total absence hours exceed norm day", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, Monday));
    }

    /// <summary>D-A consequence 2: a FULL CARE_DAY plus same-day WORK hours (a project entry
    /// AND a work-time registration) is LEGAL — work is not an absence and never enters the
    /// absence cap. The save succeeds and the care day consumes 1.0 feriedag.</summary>
    [Fact]
    public async Task FullDayPlusSameDayWorkHours_Allowed200()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        var request = new
        {
            year = Monday.Year,
            month = Monday.Month,
            absences = new[]
            {
                new { date = Monday.ToString("yyyy-MM-dd"), absenceType = "CARE_DAY", hours = 7.4m },
            },
            entries = new[]
            {
                new { date = Monday.ToString("yyyy-MM-dd"), projectCode = "S73P", hours = 4.0m },
            },
            workTime = new[]
            {
                new
                {
                    date = Monday.ToString("yyyy-MM-dd"),
                    intervals = new[] { new { start = "08:00", end = "12:00" } },
                    manualHours = 0m,
                },
            },
        };
        var rsp = await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", request);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, Monday));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. The R3 served-contract pins.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>R3 both-surfaces field pin: the month GET serves fullDayOnly on the CATALOG
    /// surfaces (the legacy `absenceTypes` + `catalogs.absenceTypes` — one computation, two
    /// projections) AND on the visible-row `rowPreferences.absenceTypes` items (configured
    /// container). TRUE for CARE_DAY/SENIOR_DAY, FALSE for VACATION.</summary>
    [Fact]
    public async Task MonthGet_ServesFullDayOnly_OnCatalogAndVisibleRowSurfaces()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m, birthDate: new DateOnly(1960, 1, 1));
        await ExecAsync("INSERT INTO user_skema_preferences (employee_id) VALUES (@p0)", employeeId);
        await ExecAsync(
            """
            INSERT INTO user_absence_selections (employee_id, absence_type, sort_order)
            VALUES (@p0, 'CARE_DAY', 0), (@p0, 'VACATION', 1)
            """, employeeId);

        var body = await GetMonthAsync(employeeId, Monday.Year, Monday.Month);

        // Catalog surface 1 — the legacy absenceTypes field.
        AssertTypeFlag(body.GetProperty("absenceTypes"), "CARE_DAY", expected: true);
        AssertTypeFlag(body.GetProperty("absenceTypes"), "SENIOR_DAY", expected: true);
        AssertTypeFlag(body.GetProperty("absenceTypes"), "VACATION", expected: false);

        // Catalog surface 2 — catalogs.absenceTypes (must agree with surface 1 by construction).
        var catalogTypes = body.GetProperty("catalogs").GetProperty("absenceTypes");
        AssertTypeFlag(catalogTypes, "CARE_DAY", expected: true);
        AssertTypeFlag(catalogTypes, "SENIOR_DAY", expected: true);
        AssertTypeFlag(catalogTypes, "VACATION", expected: false);

        // Visible-row surface — rowPreferences.absenceTypes (configured container).
        var visible = body.GetProperty("rowPreferences").GetProperty("absenceTypes");
        AssertTypeFlag(visible, "CARE_DAY", expected: true);
        AssertTypeFlag(visible, "VACATION", expected: false);
    }

    /// <summary>R3 consumptionBasis pin: one entry per day of the viewed month, derived from
    /// the SAME calculator path the guard uses — weekday 3.7 (half-time), weekend 0, and NULL
    /// for days no dated profile covers (a profile starting mid-month).</summary>
    [Fact]
    public async Task MonthGet_ServesPerDayConsumptionBasis_WeekdayWeekendAndNullCases()
    {
        // Half-timer whose profile STARTS 2025-01-15 — days before it serve null.
        var employeeId = await CreateUserAsync(OrgId, "AC", "OK24", birthDate: null);
        await SeedAgreementCodeAsync(employeeId, "AC", new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, 0.500m, new DateOnly(2025, 1, 15), null, 1);

        var body = await GetMonthAsync(employeeId, 2025, 1);

        var basis = body.GetProperty("consumptionBasis").EnumerateArray().ToList();
        Assert.Equal(31, basis.Count); // every day of January, in order

        var byDate = basis.ToDictionary(
            e => e.GetProperty("date").GetString()!,
            e => e.GetProperty("hours"));

        // Before the dated profile: null (no invented value — R3/R5 fail-closed server-side).
        Assert.Equal(JsonValueKind.Null, byDate["2025-01-06"].ValueKind);
        // Covered weekday: the real half-time norm.
        Assert.Equal(3.7m, byDate["2025-01-20"].GetDecimal());
        // Covered weekend: 0 (the same ADR-032 D3 path the guard uses).
        Assert.Equal(0m, byDate["2025-01-18"].GetDecimal());
    }

    /// <summary>The R3 served==guard IDENTITY pin: ONE fixture (an 0.8-fraction employee,
    /// basis 37 × 0.8 ÷ 5 = 5.92) drives BOTH surfaces — the GET's served
    /// consumptionBasis[date] and a partial-save guard rejection — and asserts the served
    /// value EQUALS the guard's requiredHours; the served value itself is then bookable.</summary>
    [Fact]
    public async Task ServedConsumptionBasis_EqualsGuardRequiredHours_OneFixture()
    {
        var employeeId = await SeedEmployeeAsync(fraction: 0.800m);
        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());

        // Surface 1 — the served per-day basis for the anchor Monday.
        var month = await GetMonthAsync(employeeId, Monday.Year, Monday.Month);
        var served = month.GetProperty("consumptionBasis").EnumerateArray()
            .Single(e => e.GetProperty("date").GetString() == "2025-01-06")
            .GetProperty("hours").GetDecimal();
        Assert.Equal(5.92m, served); // 37 × 0.8 ÷ 5

        // Surface 2 — a partial CARE_DAY on the same day: the guard's requiredHours must be
        // the SAME value the GET served (the identity).
        var rejectRsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", 2.0m) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejectRsp.StatusCode);
        var rejectBody = await rejectRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", rejectBody.GetProperty("error").GetString());
        Assert.Equal(served, rejectBody.GetProperty("requiredHours").GetDecimal());

        // And the served value IS bookable (the FE snap writes exactly this number).
        var okRsp = await PostAbsencesAsync(client, employeeId, Monday.Year, Monday.Month,
            new[] { (Monday, "CARE_DAY", served) });
        Assert.Equal(HttpStatusCode.OK, okRsp.StatusCode);
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, Monday));
    }

    // ── assertion helpers ──

    private static void AssertTypeFlag(JsonElement typeArray, string type, bool expected)
    {
        var item = typeArray.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == type);
        Assert.Equal(expected, item.GetProperty("fullDayOnly").GetBoolean());
    }

    // ── scenario seeding (mirrors Adr032ConsumptionPinTests) ──

    /// <summary>Fresh employee: users row (+ optional birth_date), an OPEN dated agreement
    /// row and an OPEN dated profile row from '0001-01-01' at the given fraction.</summary>
    private async Task<string> SeedEmployeeAsync(
        decimal fraction, string agreementCode = "AC", DateOnly? birthDate = null)
    {
        var employeeId = await CreateUserAsync(OrgId, agreementCode, "OK24", birthDate);
        await SeedAgreementCodeAsync(employeeId, agreementCode, new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, fraction, new DateOnly(1, 1, 1), null, 1);
        return employeeId;
    }

    /// <summary>The dated-read fixture: 1.0 fraction up to (exclusive) 2025-03-16, 0.5 from
    /// then on — the per-day basis flips 7.4 → 3.7 mid-March.</summary>
    private async Task<string> SeedFractionSwitchEmployeeAsync()
    {
        var employeeId = await CreateUserAsync(OrgId, "AC", "OK24", birthDate: null);
        await SeedAgreementCodeAsync(employeeId, "AC", new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, 1.000m, new DateOnly(1, 1, 1), FractionSwitch, 1);
        await SeedProfileRowAsync(employeeId, 0.500m, FractionSwitch, null, 1);
        return employeeId;
    }

    /// <summary>H2 fixture: an OK26-live, full-time employee on a custom agreement, with an
    /// OPEN dated agreement row and an OPEN dated profile row covering March 2026.</summary>
    private async Task<string> SeedCrossOkEmployeeAsync(string agreementCode)
    {
        var employeeId = await CreateUserAsync(OrgId, agreementCode, "OK26", birthDate: null);
        await SeedAgreementCodeAsync(employeeId, agreementCode, new DateOnly(1, 1, 1), null);
        await SeedProfileRowAsync(employeeId, 1.000m, new DateOnly(1, 1, 1), null, 1);
        return employeeId;
    }

    /// <summary>H2 fixture: closes the seeded CARE_DAY entitlement config for the given
    /// agreement + OK version (stamps effective_to to a sentinel before any real date) so both
    /// the live (GetCurrentOpenAsync) and dated (GetByTypeAtAsync) reads at that OK version
    /// return null. The OTHER OK version's CARE_DAY row is untouched and stays the discriminator.</summary>
    private async Task CloseCareDayConfigAsync(string agreementCode, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE entitlement_configs SET effective_to = DATE '0001-01-02'
            WHERE entitlement_type = 'CARE_DAY' AND agreement_code = @ac AND ok_version = @ok
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> CreateUserAsync(
        string orgId, string agreementCode, string okVersion, DateOnly? birthDate)
    {
        var userId = "emp_s73_fdo_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active, birth_date)
            VALUES (@u, @u, 'dev-only', 'S73 Full-Day-Only Pin User', NULL, @org, @ac, @ok, TRUE, @dob)
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("dob", (object?)birthDate ?? DBNull.Value);
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

    // ── HTTP helpers (mirror Adr032ConsumptionPinTests) ──

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

    private async Task<JsonElement> GetMonthAsync(string employeeId, int year, int month)
    {
        var client = CreateEmployeeClient(employeeId, _factory.CreateClient());
        var rsp = await client.GetAsync($"/api/skema/{employeeId}/month?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
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

    // ── DB read-back helpers ──

    private async Task<int> CountAbsenceRowsAsync(string employeeId, DateOnly date)
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @p0 AND date = @p1",
            employeeId, date));
    }

    private async Task<decimal> ReadFeriedageAsync(string employeeId, DateOnly date)
    {
        var result = await ScalarAsync(
            "SELECT feriedage FROM absences_projection WHERE employee_id = @p0 AND date = @p1",
            employeeId, date);
        Assert.NotNull(result);
        Assert.IsNotType<DBNull>(result);
        return (decimal)result!;
    }

    private async Task<decimal> ReadUsedAsync(string employeeId, string entitlementType, int year)
    {
        var result = await ScalarAsync(
            """
            SELECT used FROM entitlement_balances
            WHERE employee_id = @p0 AND entitlement_type = @p1 AND entitlement_year = @p2
            """, employeeId, entitlementType, year);
        Assert.NotNull(result);
        return (decimal)result!;
    }

    private async Task ExecAsync(string sql, params object[] args)
        => await ScalarAsync(sql, args);

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
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
    // (the Adr032ConsumptionPinTests / SkemaMonthlyAccrualGuardTests convention; the stub
    // replaces the whole factory, so it supplies the BaseAddress the production named-client
    // registration sets — TASK-7300 relative-URI cutover.)

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
