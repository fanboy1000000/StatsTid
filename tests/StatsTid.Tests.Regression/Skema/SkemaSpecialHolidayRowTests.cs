using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// <b>S147 / TASK-14701-T (T2, QUAL-235) — the særlige-feriedage Skema row.</b>
///
/// <para><b>Plain language.</b> Every employee earns five særlige feriedage a year and the balance shows
/// them, but Skema (the only place absence is registered since ADR-032 D5) had no row to register one, so
/// people booked ordinary vacation instead and spent the wrong balance. These facts prove the row now
/// appears where the employee's agreement configures særlige feriedage and only there, that an
/// organisation can hide it like any other absence type, and that a day registered through it counts as
/// used on the balance and in the settlement snapshot, inside the right taking window
/// (<c>[1 May Y+1, 30 Apr Y+2]</c> for accrual year Y, <c>VacationSettlementService.cs:917-921</c>).</para>
///
/// <para><b>Expected values come from the spec</b> (refinement T2 / AC-235-1..7, ADR-033, ADR-032 D1-D3,
/// Cirkulære 021-24 §12), not from the code: the label is "Særlig feriedag"
/// (<c>frontend/src/lib/entitlementConstants.ts:14</c>); a part-day costs <c>hours ÷ that day's norm</c>
/// (ADR-032 D1) and "brøkdele af dage" is source-sanctioned for særlige feriedage (ADR-032 D3).</para>
///
/// <para><b>Docker-gated; CI-pending.</b> Docker is unavailable on the dev machine, so none of these facts
/// has run locally. Each carries its kind and its "Red conditions" argued below; the sprint log flips a
/// fact to CI-verified only with the checkpoint run id that showed it green. The paired AC-235-5
/// (one definition of each label) is a grep gate, not a fact — reported by the pass, not asserted here.</para>
///
/// <para><b>Fixture rules.</b> Absences fall on WEEKDAYS (a zero-norm day is refused by the product,
/// S138 / QUAL-153): 2025-11-04 Tue, 2025-11-05 Wed, 2025-04-30 Wed, 2025-05-01 Thu are asserted in the
/// fixtures. Every fact owns its own users, and the facts that hide a type or close a config own their own
/// organisation or agreement, so no fact's setup reaches another's.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaSpecialHolidayRowTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string SpecialHolidayAllowance = "SPECIAL_HOLIDAY_ALLOWANCE";
    private const string SpecialHoliday = "SPECIAL_HOLIDAY";
    private const string YearEnd = "YEAR_END";

    // "Særlig feriedage" label (escaped so source encoding can never change the asserted value).
    private const string ExpectedLabel = "Særlig feriedag";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot ONCE so the seeders run now; every fact creates its users AFTER this boot.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private VacationSettlementService Service => _factory.Services.GetRequiredService<VacationSettlementService>();

    // ════════════════════════════════════════════════════════════════════════
    // AC-235-1 — the month GET serves the row, labelled "Særlig feriedag".
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-235-1 — kind F.</b> An AC employee's month GET serves <c>SPECIAL_HOLIDAY_ALLOWANCE</c>
    /// labelled "Særlig feriedag", exactly once, on BOTH catalogue surfaces (the legacy
    /// <c>absenceTypes</c> and <c>catalogs.absenceTypes</c> — one computation, two projections), with
    /// <c>fullDayOnly = false</c> (the SPECIAL_HOLIDAY seed is <c>false</c>, so fractions of a day stay
    /// bookable, ADR-032 D3). VACATION is still served beside it.
    ///
    /// <para><b>Red conditions (Docker; argued).</b> On master the base list
    /// (<c>SkemaEndpoints.cs:36-40</c> at <c>b9513879</c>) has no <c>SPECIAL_HOLIDAY_ALLOWANCE</c>, so the
    /// served array holds no such entry and the first assertion (<c>Single</c> over the matching entries:
    /// 0 found) fails.</para>
    /// </summary>
    [Fact]
    public async Task MonthGet_ServesSpecialHolidayRow_LabelledSaerligFeriedag()
    {
        var employeeId = await SeedEmployeeAsync("AC");

        var body = await GetMonthAsync(employeeId, "STY01", 2025, 1);

        foreach (var (surface, types) in new[]
        {
            ("absenceTypes", body.GetProperty("absenceTypes")),
            ("catalogs.absenceTypes", body.GetProperty("catalogs").GetProperty("absenceTypes")),
        })
        {
            var matches = types.EnumerateArray()
                .Where(e => e.GetProperty("type").GetString() == SpecialHolidayAllowance).ToList();
            Assert.True(matches.Count == 1,
                $"{surface}: expected exactly one {SpecialHolidayAllowance} entry, found {matches.Count}.");
            Assert.Equal(ExpectedLabel, matches[0].GetProperty("label").GetString());
            Assert.False(matches[0].GetProperty("fullDayOnly").GetBoolean());
            Assert.Contains(types.EnumerateArray(), e => e.GetProperty("type").GetString() == "VACATION");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-235-2 — no SPECIAL_HOLIDAY config for the agreement => the row is not served.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-235-2 — kind B, boundary pin — holds today</b> (paired with AC-235-1, so the pair proves
    /// "served where configured, and only there"). Every seeded agreement has a SPECIAL_HOLIDAY row
    /// (<c>init.sql:1782-1787</c>, <c>:1825-1828</c>), so the fixture ends the AC_RESEARCH rows for BOTH
    /// OK versions (<c>effective_to</c> before any real date; the dated read at the month-end anchor then
    /// returns nothing). An AC_RESEARCH employee's month GET serves VACATION (the catalogue works) but NOT
    /// <c>SPECIAL_HOLIDAY_ALLOWANCE</c>, on either surface. Fails if the gate ever serves the row
    /// regardless of the config.
    ///
    /// <para><b>Red conditions.</b> None today: the row is not served at all on master, so the assertion
    /// holds trivially; it exists so a gate-less implementation (the row served for every agreement)
    /// fails here while AC-235-1 still passes. Mutation to prove it can fail: drop the
    /// <c>specialHolidayConfigured</c> filter in <c>ComputeAbsenceCatalogAsync</c>.</para>
    /// </summary>
    [Fact]
    public async Task NoSpecialHolidayConfig_RowNotServed()
    {
        var employeeId = await SeedEmployeeAsync("AC_RESEARCH");
        await SetSpecialHolidayConfigWindowAsync("AC_RESEARCH", "OK24", closed: true);
        await SetSpecialHolidayConfigWindowAsync("AC_RESEARCH", "OK26", closed: true);

        var body = await GetMonthAsync(employeeId, "STY01", 2025, 1);

        foreach (var (surface, types) in new[]
        {
            ("absenceTypes", body.GetProperty("absenceTypes")),
            ("catalogs.absenceTypes", body.GetProperty("catalogs").GetProperty("absenceTypes")),
        })
        {
            Assert.Contains(types.EnumerateArray(), e => e.GetProperty("type").GetString() == "VACATION");
            Assert.DoesNotContain(types.EnumerateArray(),
                e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);
        }
    }

    /// <summary>
    /// <b>AC-235-2 (OK-version dimension) — kind F</b> (the "served" half fails on master; the
    /// "not served" half is a boundary that holds today). The gate resolves the OK version IN FORCE AT THE
    /// ANCHOR (the month end) — not the employee's stored <c>users.ok_version</c> (the S73 H2 convention
    /// the save path's valuation uses; accepted by the Orchestrator). One AC_TEACHING employee is stored
    /// as OK24 and views May 2026 (anchor 2026-05-31, inside OK26: <c>OkVersionResolver</c> —
    /// OK26 starts 2026-04-01). (1) With only the OK26 SPECIAL_HOLIDAY row ended, the row is NOT served
    /// (the anchor's version has no config, although the stored OK24 version does). (2) With the OK26 row
    /// restored and only the OK24 row ended, the row IS served (the anchor's version has a config,
    /// although the stored OK24 version has none). An implementation reading <c>user.OkVersion</c> flips
    /// both outcomes.
    ///
    /// <para><b>Red conditions (Docker; argued).</b> Step (2)'s <c>Single</c> fails on master (no row is
    /// ever served); step (1) holds on master. Mutation to prove step (1) can fail: read
    /// <c>user.OkVersion</c> instead of <c>OkVersionResolver.ResolveVersion(anchor)</c> in gate (c).</para>
    /// </summary>
    [Fact]
    public async Task SpecialHolidayGate_FollowsOkVersionInForceAtTheAnchor_NotTheStoredOkVersion()
    {
        var employeeId = await SeedEmployeeAsync("AC_TEACHING", okVersion: "OK24");

        // (1) OK26 row ended, OK24 row open: anchor 2026-05-31 resolves OK26 => not served.
        await SetSpecialHolidayConfigWindowAsync("AC_TEACHING", "OK26", closed: true);
        var notServed = await GetMonthAsync(employeeId, "STY01", 2026, 5);
        Assert.DoesNotContain(notServed.GetProperty("absenceTypes").EnumerateArray(),
            e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);

        // (2) OK26 row restored, OK24 row ended: anchor 2026-05-31 resolves OK26 => served.
        await SetSpecialHolidayConfigWindowAsync("AC_TEACHING", "OK26", closed: false);
        await SetSpecialHolidayConfigWindowAsync("AC_TEACHING", "OK24", closed: true);
        var served = await GetMonthAsync(employeeId, "STY01", 2026, 5);
        var row = Assert.Single(served.GetProperty("absenceTypes").EnumerateArray(),
            e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);
        Assert.Equal(ExpectedLabel, row.GetProperty("label").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-235-3 — the admin visibility list names it; hiding removes the row.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-235-3 — kind F.</b> The admin absence-type list (<c>GET /api/config/{orgId}/absence-types</c>)
    /// names <c>SPECIAL_HOLIDAY_ALLOWANCE</c> labelled "Særlig feriedag" (and still lists VACATION). A
    /// GlobalAdmin hides it for the organisation (<c>POST …/absence-types/visibility</c>): the admin list
    /// then no longer names it, and the employee's month GET no longer serves the row on either surface
    /// while VACATION stays; un-hiding restores both. The org is this fact's own, so the hide reaches no
    /// other fact.
    ///
    /// <para><b>Red conditions (Docker; argued).</b> On master the admin list is the private table at
    /// <c>ConfigEndpoints.cs:616-628</c> (10 entries, no such code), so the first assertion — the list
    /// contains the code — fails; and the month GET serves no row to hide.</para>
    /// </summary>
    [Fact]
    public async Task AdminVisibilityList_IncludesSpecialHoliday_HidingRemovesRow()
    {
        const string org = "S147SH_VIS";
        var employeeId = await SeedEmployeeAsync("AC", orgId: org);
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s147sh_gadmin", org);

        // The admin list names it, with the shared label.
        var listed = await GetAdminListAsync(admin, org);
        var entry = Assert.Single(listed, e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);
        Assert.Equal(ExpectedLabel, entry.GetProperty("label").GetString());
        Assert.Contains(listed, e => e.GetProperty("type").GetString() == "VACATION");

        // Before the hide the employee's month GET serves it (the row exists to be hidden).
        AssertServed(await GetMonthAsync(employeeId, org, 2025, 1), expected: true);

        // Hide: the POST echoes, the admin list drops it, the month GET drops it — both surfaces.
        await SetVisibilityAsync(admin, org, hidden: true);
        Assert.DoesNotContain(await GetAdminListAsync(admin, org),
            e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);
        var hiddenBody = await GetMonthAsync(employeeId, org, 2025, 1);
        AssertServed(hiddenBody, expected: false);
        Assert.Contains(hiddenBody.GetProperty("absenceTypes").EnumerateArray(),
            e => e.GetProperty("type").GetString() == "VACATION");

        // Un-hide restores it on both.
        await SetVisibilityAsync(admin, org, hidden: false);
        Assert.Contains(await GetAdminListAsync(admin, org),
            e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);
        AssertServed(await GetMonthAsync(employeeId, org, 2025, 1), expected: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-235-4 — a day saved in the taking window shows as used (balance + snapshot).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-235-4 — kind R, regression pin</b> (today's save already accepts the code: there is no
    /// absence-code check at the save, and existing tests save it —
    /// <c>SkemaMonthlyAccrualGuardTests</c>, <c>SkemaQuotaRefusalOrderTests</c>,
    /// <c>Adr032RevaluationTests</c>). The row is a UI door onto that behaviour; this keeps the
    /// consumption consequence pinned alongside it.
    ///
    /// <para><b>Expected (derived from the spec).</b> Accrual year 2024's taking window is
    /// <c>[1 May 2025, 30 Apr 2026]</c>. Three full days are saved through the real Skema endpoint, each in
    /// its own month: Wed 2025-04-30 (the day BEFORE the window opens — it keys to accrual year 2023, the
    /// Jan-Apr tail of 2023's window), Thu 2025-05-01 (the window's FIRST day, accrual year 2024) and Tue
    /// 2025-11-04 (inside, accrual year 2024). Balance rows: year 2024 <c>used = 2</c>
    /// (total quota 5), year 2023 <c>used = 1</c>. Settlement close dormant: before any close runs there is
    /// no SPECIAL_HOLIDAY settlement row (the §15 stk.1 flag is unset by default). Settling accrual year
    /// 2024 directly captures <c>used = 2</c> with two recorded components (the day before the window is
    /// NOT in it), <c>earned = 5</c>, payout = 5 − 2 = <b>3</b>, SETTLED.</para>
    ///
    /// <para><b>Red conditions.</b> None today (R). A taking window counted as a calendar year (or
    /// opening a day early) would put the 30 April day in 2024's snapshot (used 3) and fail the
    /// <c>used</c> assertion.</para>
    /// </summary>
    [Fact]
    public async Task DaySavedInTakingWindow_ShowsUsedOnBalanceAndSnapshot()
    {
        const int accrualYear = 2024;
        var employeeId = await SeedEmployeeAsync("AC");
        var dayBeforeWindow = new DateOnly(2025, 4, 30);
        var windowFirstDay = new DateOnly(2025, 5, 1);
        var insideWindow = new DateOnly(2025, 11, 4);
        foreach (var d in new[] { dayBeforeWindow, windowFirstDay, insideWindow })
            Assert.True(d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), $"{d} must be a weekday (S138).");
        Assert.Equal(new DateOnly(accrualYear + 1, 5, 1), windowFirstDay);

        var client = EmployeeSkemaClient(employeeId, "STY01");
        foreach (var d in new[] { dayBeforeWindow, windowFirstDay, insideWindow })
        {
            var save = await PostAbsencesAsync(client, employeeId, d.Year, d.Month, (d, SpecialHolidayAllowance, 7.4m));
            Assert.True(save.StatusCode == HttpStatusCode.OK,
                $"save {d}: expected 200, got {(int)save.StatusCode} {await save.Content.ReadAsStringAsync()}");
        }

        // The balance: the window's two days on accrual year 2024; the day before it on 2023.
        Assert.Equal(2m, await ReadBalanceUsedAsync(employeeId, accrualYear));
        Assert.Equal(5m, await ReadBalanceTotalQuotaAsync(employeeId, accrualYear));
        Assert.Equal(1m, await ReadBalanceUsedAsync(employeeId, accrualYear - 1));

        // The settlement close is dormant: nothing has settled this tuple by itself.
        Assert.Equal(0L, await CountSettlementsAsync(employeeId));

        // The snapshot (direct drive): used counts the window's two days only.
        var outcome = await SettleAsync(employeeId, accrualYear);
        Assert.True(outcome.DidSettle);
        var row = await ReadSettlementAsync(employeeId, accrualYear);
        Assert.NotNull(row);
        Assert.Equal("SETTLED", row!.Value.State);
        using var snap = JsonDocument.Parse(row.Value.SnapshotJson);
        var root = snap.RootElement;
        Assert.Equal(5m, root.GetProperty("earned").GetDecimal());
        Assert.Equal(2m, root.GetProperty("used").GetDecimal());
        Assert.Equal(2, root.GetProperty("recordedAbsences").GetArrayLength());
        Assert.Equal(3m, row.Value.Payout);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AC-235-7 — a part-day save is accepted (R2 expected verdict: ADR-032 D3 "brøkdele af dage").
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-235-7 — kind R, regression pin.</b> The SPECIAL_HOLIDAY seeds stay <c>full_day_only = false</c>
    /// (no row of the type carries <c>true</c>), and a half-day (3.7 h on a 7.4 h norm day, Wed
    /// 2025-11-05) <c>SPECIAL_HOLIDAY_ALLOWANCE</c> save is ACCEPTED (200), recording exactly
    /// <c>feriedage = 0.5</c> (ADR-032 D1: hours ÷ the day's norm) and moving the balance (accrual year
    /// 2024) by exactly 0.5. This is R2's expected verdict ("brøkdele af dage", ADR-032 D3); on a
    /// contradicting verdict this fact flips in TASK-14701-F (to <c>…_Refused_AbsenceFullDayOnly</c>),
    /// not here.
    ///
    /// <para><b>Red conditions.</b> None today (R). Flipping the seed to <c>true</c> turns the save into
    /// 422 <c>absence_full_day_only</c> and fails the status and seed assertions.</para>
    /// </summary>
    [Fact]
    public async Task PartDaySpecialHoliday_Accepted()
    {
        var employeeId = await SeedEmployeeAsync("AC");
        var day = new DateOnly(2025, 11, 5);
        Assert.Equal(DayOfWeek.Wednesday, day.DayOfWeek);

        // The seed: SPECIAL_HOLIDAY is not whole-day-only for any agreement / OK version.
        Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type = @p0 AND full_day_only", SpecialHoliday)));

        var client = EmployeeSkemaClient(employeeId, "STY01");
        var save = await PostAbsencesAsync(client, employeeId, day.Year, day.Month, (day, SpecialHolidayAllowance, 3.7m));

        Assert.True(save.StatusCode == HttpStatusCode.OK,
            $"a part-day særlig feriedag must be accepted; got {(int)save.StatusCode} {await save.Content.ReadAsStringAsync()}");
        var feriedage = await ScalarAsync(
            "SELECT feriedage FROM absences_projection WHERE employee_id = @p0 AND date = @p1", employeeId, day);
        Assert.Equal(0.5m, (decimal)feriedage!);
        Assert.Equal(0.5m, await ReadBalanceUsedAsync(employeeId, 2024));
    }

    // ─────────────────────────────── assertions ───────────────────────────────

    private static void AssertServed(JsonElement body, bool expected)
    {
        foreach (var types in new[]
        {
            body.GetProperty("absenceTypes"),
            body.GetProperty("catalogs").GetProperty("absenceTypes"),
        })
        {
            var present = types.EnumerateArray()
                .Any(e => e.GetProperty("type").GetString() == SpecialHolidayAllowance);
            Assert.Equal(expected, present);
        }
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private async Task<JsonElement> GetMonthAsync(string employeeId, string orgId, int year, int month)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        var rsp = await client.GetAsync($"/api/skema/{employeeId}/month?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> GetAdminListAsync(HttpClient admin, string orgId)
    {
        var rsp = await admin.GetAsync($"/api/config/{orgId}/absence-types?agreementCode=AC&okVersion=OK24");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return rows.EnumerateArray().ToList();
    }

    private static async Task SetVisibilityAsync(HttpClient admin, string orgId, bool hidden)
    {
        var rsp = await admin.PostAsync(
            $"/api/config/{orgId}/absence-types/visibility",
            new StringContent(
                JsonSerializer.Serialize(new { absenceType = SpecialHolidayAllowance, isHidden = hidden }),
                Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var echo = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(SpecialHolidayAllowance, echo.GetProperty("absenceType").GetString());
        Assert.Equal(hidden, echo.GetProperty("isHidden").GetBoolean());
    }

    /// <summary>A client for the employee's OWN Skema saves, on a host derived from the suite's factory
    /// with the rule-engine seam stubbed by the real <c>EntitlementValidationRule</c>.</summary>
    private HttpClient EmployeeSkemaClient(string employeeId, string orgId)
    {
        var client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IHttpClientFactory>(new EntitlementValidationStubFactory())))
            .CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        return client;
    }

    private static async Task<HttpResponseMessage> PostAbsencesAsync(
        HttpClient client, string employeeId, int year, int month, (DateOnly Date, string Type, decimal Hours) absence)
        => await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", new
        {
            year,
            month,
            absences = new[]
            {
                new { date = absence.Date.ToString("yyyy-MM-dd"), absenceType = absence.Type, hours = absence.Hours },
            },
        });

    /// <summary>Replaces the whole <see cref="IHttpClientFactory"/>, so it supplies the BaseAddress the
    /// production named-client registration sets.</summary>
    private sealed class EntitlementValidationStubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new EntitlementValidationStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    /// <summary>A fresh full-time employee (users + dated agreement code + dated profile, via the shared
    /// seed helper) in <paramref name="orgId"/>, stored under <paramref name="okVersion"/>.</summary>
    private async Task<string> SeedEmployeeAsync(string agreementCode, string orgId = "STY01", string okVersion = "OK24")
    {
        var employeeId = "emp_s147_sh_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, orgId, agreementCode, okVersion);
        return employeeId;
    }

    /// <summary>Ends (<paramref name="closed"/> = true: <c>effective_to</c> before any real date, so every
    /// dated read at a real anchor returns nothing) or restores (false: open-ended again) the SPECIAL_HOLIDAY
    /// entitlement row of one agreement + OK version. Only agreements the host's seeder does not re-seed are
    /// used (AC_RESEARCH, AC_TEACHING exist only in init.sql), so a derived host cannot undo it.</summary>
    private async Task SetSpecialHolidayConfigWindowAsync(string agreementCode, string okVersion, bool closed)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            closed
                ? """
                  UPDATE entitlement_configs SET effective_to = DATE '0001-01-02'
                  WHERE entitlement_type = 'SPECIAL_HOLIDAY' AND agreement_code = @ac AND ok_version = @ok
                    AND effective_to IS NULL
                  """
                : """
                  UPDATE entitlement_configs SET effective_to = NULL
                  WHERE entitlement_type = 'SPECIAL_HOLIDAY' AND agreement_code = @ac AND ok_version = @ok
                    AND effective_to = DATE '0001-01-02'
                  """, conn);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.True(rows == 1, $"fixture: expected to change exactly one {agreementCode}/{okVersion} SPECIAL_HOLIDAY row, changed {rows}.");
    }

    // ─────────────────────────────── reads / drivers ───────────────────────────────

    private async Task<decimal> ReadBalanceUsedAsync(string employeeId, int year)
    {
        var used = await ScalarAsync(
            "SELECT used FROM entitlement_balances WHERE employee_id = @p0 AND entitlement_type = @p1 AND entitlement_year = @p2",
            employeeId, SpecialHoliday, year);
        Assert.NotNull(used);
        return (decimal)used!;
    }

    private async Task<decimal> ReadBalanceTotalQuotaAsync(string employeeId, int year)
    {
        var quota = await ScalarAsync(
            "SELECT total_quota FROM entitlement_balances WHERE employee_id = @p0 AND entitlement_type = @p1 AND entitlement_year = @p2",
            employeeId, SpecialHoliday, year);
        Assert.NotNull(quota);
        return (decimal)quota!;
    }

    private async Task<long> CountSettlementsAsync(string employeeId)
        => Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id = @p0 AND entitlement_type = @p1 AND settlement_state <> 'REVERSED'",
            employeeId, SpecialHoliday));

    /// <summary>One SPECIAL_HOLIDAY settlement pass in its own ReadCommitted transaction, committed.</summary>
    private async Task<SettlementOutcome> SettleAsync(string employeeId, int accrualYear)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await Service.SettleAsync(employeeId, SpecialHoliday, accrualYear, YearEnd, conn, tx);
        await tx.CommitAsync();
        return outcome;
    }

    private async Task<(string State, decimal Payout, string SnapshotJson)?> ReadSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, payout_days, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", SpecialHoliday);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetDecimal(1), reader.GetString(2));
    }

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }
}
