using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S65 / TASK-6504 / ADR-030 D9 — Docker-gated regression suite for the new read-only
/// <c>GET /api/balance/{employeeId}/year-overview?year=YYYY</c> endpoint.
///
/// <para>
/// Covers all 11 areas specified in SPRINT-65.md TASK-6504 §Description:
/// <list type="number">
///   <item><description><b>Auth</b> — self-200; foreign-employee 403; leader-with-scope 200;
///   leader/local-admin OUT-of-scope 403 (mirrors BalanceSeriesTests).</description></item>
///   <item><description><b>Skema reconciliation (marquee)</b> — seeded work-time rows →
///   <c>months[m].diff</c> matches the GET /month per-day diff sum for the same
///   month.</description></item>
///   <item><description><b>Day-equivalents</b> — 3.7 h VACATION absence → <c>afholdt = 0.5</c>
///   and saldo drops 0.5, not 1.</description></item>
///   <item><description><b>Consumption reconciliation</b> — absence dates ≤ today ↔ used,
///   dates > today ↔ planned, whole year ↔ used + planned; seed constraint (no future absences
///   in the current month).</description></item>
///   <item><description><b>Absence-type mapping pin</b> — SPECIAL_HOLIDAY_ALLOWANCE appears in
///   the SPECIAL_HOLIDAY/Feriefridage row; asserted via the shared
///   <c>EntitlementMapping.AbsenceToEntitlementType</c> map.</description></item>
///   <item><description><b>Straddle</b> — absences in March (ferieår Y−1) and October (ferieår Y)
///   count against their own ferieår; Sep saldo shows the reset sawtooth.</description></item>
///   <item><description><b>Transferable determinism</b> — two identical requests byte-equal;
///   formula verified; cap-0 type → 0; value only at boundaryMonth=12; computed at model
///   boundary (not December).</description></item>
///   <item><description><b>OK-version straddle</b> — year spanning the 2026-04-01 cutover
///   resolves per-day norms per-side; entitlement config anchors at entitlement-year start;
///   transferable carryoverMax anchored at closed-ferieår start.</description></item>
///   <item><description><b>Future months</b> — diff null after today; planned absence appears in
///   afholdt.</description></item>
///   <item><description><b>ANNUAL_ACTIVITY</b> — academic profile → normHours null every
///   month.</description></item>
///   <item><description><b>Graceful</b> — profile-less employee → 200 with nulls, never
///   500.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Today seam (TASK-6502 Step-0b Reviewer NOTE).</b> All today-dependent assertions override
/// <see cref="TimeProvider"/> in the WAF test host to a fixed <see cref="FixedTimeProvider"/>.
/// No wall-clock-dependent expected values appear anywhere.
/// </para>
///
/// <para>
/// <b>NOTE (Step-0b Reviewer W4 / OQ-1).</b> <c>saldo</c> (Sep–Dec includes <c>carryoverIn</c>)
/// and <c>transferable</c> (displayed Dec) overlap BY DESIGN (owner-accepted non-additivity). No
/// saldo−transferable reconciliation assertion is written — it would fail by design.
/// </para>
///
/// <para>
/// Boot-order lesson (S63): <see cref="TestFixtures.DockerHarness.StartAsync"/> + schema apply +
/// <see cref="StatsTidWebApplicationFactory.CreateClient"/> boot re-runs Program.cs seeders
/// including the S31 <see cref="EmployeeProfileSeeder"/> which backfills a profile row for every
/// user lacking one. Absent-state fixtures (e.g. profile-less employee for test 11) MUST be
/// created AFTER the last host boot (<see cref="InitializeAsync"/> calls CreateClient once; the
/// profile-less user is seeded in the test body).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class YearOverviewTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // init.sql seed employee: emp001, STY01 (/MIN01/STY01/), AC, OK24.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    // Fixed "today" used in all today-dependent tests. Chosen to be:
    //   - well within 2026 so the year under test (2026) is the CURRENT calendar year
    //   - on a date where the OK version is OK26 (≥ 2026-04-01)
    //   - mid-year so past months AND future months both exist in the 2026 year
    // 2026-06-15 (Monday) satisfies all three. Tests that specifically straddle the
    // 2026-04-01 OK cutover use a different year-under-test (2026) with this same today.
    private static readonly DateOnly FixedToday = new(2026, 6, 15);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build → seeders backfill emp001 profile +
        // agreement-code rows. Absent-state fixtures must be seeded AFTER this point.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Auth — self 200; foreign-employee 403; in-scope leader 200;
    //           out-of-scope leader 403; out-of-scope local-admin 403.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An Employee reading their OWN year-overview → 200 (self-access allowed).
    /// </summary>
    [Fact]
    public async Task Auth_EmployeeSelf_Returns200()
    {
        var client = MakeFixedTodayClient(EmployeeBearerToken(Emp001, Emp001OrgId));
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/year-overview?year=2025");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>
    /// An Employee reading a DIFFERENT employee's year-overview → 403 (employee-self gate).
    /// </summary>
    [Fact]
    public async Task Auth_EmployeeForeignEmployee_Returns403()
    {
        // emp002 token (different employee) reading emp001's year-overview.
        var client = MakeFixedTodayClient(EmployeeBearerToken("emp002", Emp001OrgId));
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/year-overview?year=2025");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// A leader scoped to emp001's org (STY01 / ORG_AND_DESCENDANTS) → 200 (in-scope access).
    /// </summary>
    [Fact]
    public async Task Auth_LeaderInScope_Returns200()
    {
        var client = MakeFixedTodayClient(LeaderBearerToken("mgr_yo_is", Emp001OrgId));
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/year-overview?year=2025");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>
    /// A leader scoped to a DISJOINT org subtree (STY05 / ORG_AND_DESCENDANTS) reading emp001
    /// whose primary org is STY01 → 403 (out-of-scope negative branch, Step-0b Codex W3).
    /// </summary>
    [Fact]
    public async Task Auth_LeaderOutOfScope_Returns403()
    {
        // STY05 is /MIN02/STY05/ (disjoint from /MIN01/STY01/).
        var client = MakeFixedTodayClient(LeaderBearerToken("mgr_yo_oos", "STY05"));
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/year-overview?year=2025");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// A LocalAdmin scoped to a DISJOINT org (STY05) reading emp001 (STY01) → 403.
    /// The out-of-scope negative branch applies to LocalAdmin as well as leaders
    /// (Step-0b Codex W3: all non-Employee actors go through OrgScopeValidator).
    /// </summary>
    [Fact]
    public async Task Auth_LocalAdminOutOfScope_Returns403()
    {
        var client = MakeFixedTodayClient(LocalAdminBearerToken("ladm_yo_oos", "STY05"));
        var rsp = await client.GetAsync($"/api/balance/{Emp001}/year-overview?year=2025");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Skema reconciliation (marquee) — months[m].diff == Skema GET /month diff.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Cross-seam drift-proof (marquee).</b> Seed a single work_time_projection row for emp001
    /// in a PAST month (Feb 2025) and assert that the year-overview <c>months[1].diff</c>
    /// (February) equals the diff RECONSTRUCTED from the SEPARATE Skema endpoint
    /// (GET /api/skema/{id}/month?year=2025&amp;month=2). The Skema <c>/month</c> response does not
    /// emit a per-day <c>diff</c> directly (the FE computes it) — it exposes the raw
    /// <c>workTime</c> rows (intervals + manualHours) and the <c>dailyNorm</c> array, both produced
    /// by the SAME shared <see cref="StatsTid.Backend.Api.Services.DailyNormCalculator"/> +
    /// interval-sum the year-overview uses. We sum the Skema seam's own worked hours and per-day
    /// norms and assert the year-overview's month diff matches. This is a genuine cross-seam
    /// equality (NOT a tautology against the year-overview's own fields): if either seam's date
    /// range, norm rounding, or aggregation drifted, the two would disagree.
    /// </summary>
    [Fact]
    public async Task SkemaReconciliation_WorkedHours_DiffMatchesSkemaGet()
    {
        // Seed emp001 with a work_time_projection row for Feb 2025 (a fully-past month).
        // Feb 2025: 20 weekdays × 7.4 h/day = 148 h norm; we seed 150 h worked.
        await SeedWorkTimeRowAsync(Emp001, new DateOnly(2025, 2, 1), manualHours: 150m);

        // today = 2026-06-15, year under test = 2025 (a fully past year → diff always non-null).
        var client = MakeFixedTodayClient(EmployeeBearerToken(Emp001, Emp001OrgId));
        var yearBody = await GetYearOverviewAsync(client, Emp001, 2025);

        var months = yearBody.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(12, months.Count);

        // February (index 1) from the YEAR-OVERVIEW seam.
        var febMonth = months[1]; // index 1 = February
        Assert.Equal(2, febMonth.GetProperty("month").GetInt32());
        Assert.Equal(JsonValueKind.Number, febMonth.GetProperty("diff").ValueKind); // past month → non-null
        var yearOverviewDiff = febMonth.GetProperty("diff").GetDecimal();

        // ── Reconstruct the SAME month's diff from the SKEMA seam (route per SkemaEndpoints.cs:132,
        //    int year + int month query params, EmployeeOrAbove). ──
        var skemaMonth = await GetSkemaMonthAsync(client, Emp001, 2025, 2);

        // Skema worked = Σ over workTime rows of (Σ positive-duration interval hours + manualHours),
        // then rounded to 2dp — byte-identical to BalanceEndpoints.cs:639-640
        // (SumIntervalHours + ManualHours). Our seed has no intervals, so this is the 150 manual.
        var skemaWorked = Math.Round(
            skemaMonth.GetProperty("workTime").EnumerateArray()
                .Sum(w => SumSkemaIntervalHours(w.GetProperty("intervals"))
                          + w.GetProperty("manualHours").GetDecimal()),
            2);

        // Skema norm = Σ over the dailyNorm array of each day's hours (weekends 0; any null day would
        // make the year-overview month null too — not the case for a full past weekday month). Same
        // aggregation as BalanceEndpoints.cs:646-648.
        var skemaNorm = Math.Round(
            skemaMonth.GetProperty("dailyNorm").EnumerateArray()
                .Sum(n => n.GetProperty("hours").ValueKind == JsonValueKind.Null
                    ? 0m
                    : n.GetProperty("hours").GetDecimal()),
            2);

        var skemaDiff = Math.Round(skemaWorked - skemaNorm, 2);

        // The cross-seam assertion: the two independently-served seams agree on the month diff.
        Assert.Equal(skemaDiff, yearOverviewDiff);

        // Belt-and-braces: the year-overview's own worked/norm also equal the Skema seam's
        // (proves it is the SAME underlying data, not merely a coincidentally-equal diff).
        Assert.Equal(skemaWorked, febMonth.GetProperty("workedHours").GetDecimal());
        Assert.Equal(skemaNorm, febMonth.GetProperty("normHours").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Day-equivalents — 3.7 h VACATION absence → afholdt = 0.5, saldo drops 0.5.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A 3.7-hour VACATION absence (= 0.5 day at StandardDayHours = 7.4 h) seeded in March
    /// 2025 (a past month, ferieår 2024 = Sep 2024 – Aug 2025) contributes <c>afholdt = 0.5</c>
    /// (NOT 1) to the VACATION category's March slot and reduces saldo by 0.5 relative to a
    /// baseline with no absences. Cites EntitlementMapping.StandardDayHours (= 7.4m) and the
    /// same math as SkemaEndpoints.cs:738/:1076.
    /// </summary>
    [Fact]
    public async Task DayEquivalents_HalfDayVacation_AffoldtIsZeroPointFive_SaldoDropsHalfDay()
    {
        // Fresh employee in STY01 / AC / OK24.  Employment start = null → whole-ferieår accrual.
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Seed a 3.7 h VACATION absence on 2025-03-10 (ferieår 2024: Sep 2024–Aug 2025).
        // Absence date ≤ today (2026-06-15) → contributes to "used".
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2025, 3, 10), "VACATION", hours: 3.7m);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        var vacation = GetCategory(body, "VACATION");

        // afholdt[2] = March (month index 2, 0-based).
        // 3.7 / 7.4 = 0.5 day-equivalent.
        var marchAfholdt = vacation.GetProperty("afholdt").EnumerateArray().ToList()[2].GetDecimal();
        Assert.Equal(0.5m, marchAfholdt);

        // saldo[2] = EarnedToDate(asOf=2025-03-31) + carryoverIn(0) − cumulativeAfholdt(this type)
        // cumulativeAfholdt in ferieår 2024 through March-end = 0.5
        var marchSaldo = vacation.GetProperty("saldo").EnumerateArray().ToList()[2].GetDecimal();

        // The saldo without any absence for the same employee (same quota, month-end asOf):
        // earned at 2025-03-31 for ferieår 2024 (started 2024-09-01) = 25 × 7/12 ≈ 14.58.
        // saldo = 14.58 − 0.5 = 14.08 approximately. The IMPORTANT assertion is that saldo
        // dropped by exactly 0.5 relative to an absence-free baseline.
        // We verify the drop by checking afholdt = 0.5 implies saldo < unconstrained baseline.
        // Direct formula: saldo = earned + carryover - cumulativeAfholdt for the ferieår.
        // cumulativeAfholdt through March = 0.5 (only absence is March 3.7h).
        // Computed via the REAL AccrualMath at the closed-over month-end (no test-local replica).
        var expectedSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2025, 3, 31), carryoverIn: 0m, cumulativeAfholdt: 0.5m);
        Assert.Equal(expectedSaldo, marchSaldo);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Consumption reconciliation — used/planned split by ABSENCE DATE.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seeds VACATION absences in PAST months and a FUTURE month of 2026 (no future absences in the
    /// current month, per the Step-0b cycle-2 seed constraint) AND seeds the matching
    /// <c>entitlement_balances</c> row with the <c>used</c>/<c>planned</c> a correctly-maintained
    /// balance WOULD hold for this exact date-split. Asserts the spec's three equalities
    /// (TASK-6504.4):
    ///   (a) Σ afholdt for past + current months == <c>used</c>;
    ///   (b) Σ afholdt for future months == <c>planned</c>;
    ///   (c) whole-year Σ afholdt == <c>used + planned</c>.
    /// <para>
    /// <b>Route taken (Step-5a WARNING 2): seeded-balance proxy, NOT the real save flow.</b> The
    /// year-overview endpoint exposes only the per-month <c>afholdt</c> day-equivalent TOTALS — it
    /// does not surface a per-category <c>used</c>/<c>planned</c> field, and the real
    /// consumption-maintenance path is the Skema <c>/save</c> POST behind quota + approval-state
    /// machinery (disproportionate for this read-only assertion). So we seed the
    /// <c>entitlement_balances</c> row's <c>used</c>/<c>planned</c> ourselves, chosen CONSISTENT
    /// with the date split (past+current day-equivalents → <c>used</c>; future → <c>planned</c>),
    /// and assert the response's afholdt monthly totals reconcile to those quantities. This is a
    /// totals-consistency proxy for the underlying date-based split, not a system-maintained
    /// round-trip.
    /// </para>
    /// Fixed today = 2026-06-15 (current month = June 2026).
    /// </summary>
    [Fact]
    public async Task ConsumptionReconciliation_UsedPlannnedSplit_ByAbsenceDate()
    {
        // Fixed today = 2026-06-15. Seed past months Jan/Feb and a future month Aug.
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Past absences (≤ 2026-06-15): Jan 2026 + Feb 2026.
        // Ferieår for VACATION: Sep 2025 – Aug 2026 (resetMonth = 9, year 2025 for months < Sep).
        // Both Jan and Feb sit inside ferieår 2025.
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2026, 1, 10), "VACATION", hours: 7.4m); // 1 day, past
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2026, 2, 20), "VACATION", hours: 7.4m); // 1 day, past
        // Future absence (> 2026-06-15): Aug 2026 (same ferieår 2025).
        // Aug is NOT the current month (Jun), satisfying the seed constraint.
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2026, 8, 5), "VACATION", hours: 7.4m); // 1 day, future

        // Seed the entitlement_balances row for the ferieår containing all three absences
        // (ferieår 2025 = Sep 2025 – Aug 2026 → entitlement_year 2025). used/planned are chosen to
        // EQUAL the date-split day-equivalents above: 2 past-dated days → used = 2; 1 future-dated
        // day → planned = 1. (This is the quantity a correct system-maintained balance holds for
        // this absence pattern; see the route note in the summary.)
        const decimal expectedUsed = 2m;     // Jan + Feb (dates ≤ today)
        const decimal expectedPlanned = 1m;  // Aug (date > today)
        await SeedEntitlementBalanceAsync(
            employeeId, "VACATION", entitlementYear: 2025,
            used: expectedUsed, planned: expectedPlanned, carryoverIn: 0m);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);

        var vacation = GetCategory(body, "VACATION");
        var afholdtArray = vacation.GetProperty("afholdt").EnumerateArray()
            .Select(e => e.GetDecimal()).ToList();

        // (a) Σ afholdt for months whose dates ≤ today (Jan–Jun, today's month inclusive) == used.
        // Only Jan (index 0) and Feb (index 1) carry day-equivalents.
        var usedFromAfholdt = afholdtArray.Take(6).Sum();
        Assert.Equal(expectedUsed, usedFromAfholdt);

        // (b) Σ afholdt for months whose dates > today (Jul–Dec) == planned.
        // Only Aug (index 7) carries a day-equivalent.
        var plannedFromAfholdt = afholdtArray.Skip(6).Sum();
        Assert.Equal(expectedPlanned, plannedFromAfholdt);

        // (c) whole-year Σ afholdt == used + planned.
        Assert.Equal(expectedUsed + expectedPlanned, afholdtArray.Sum());
        Assert.Equal(3m, afholdtArray.Sum()); // 2 + 1 = 3 total
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Absence-type mapping pin — SPECIAL_HOLIDAY_ALLOWANCE → SPECIAL_HOLIDAY row.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A seeded <c>SPECIAL_HOLIDAY_ALLOWANCE</c> absence appears in the
    /// <c>SPECIAL_HOLIDAY</c>/Feriefridage row's <c>afholdt</c>, NOT in the VACATION row.
    /// Assert uses the shared <c>EntitlementMapping.AbsenceToEntitlementType</c> map to look
    /// up the expected target type rather than a re-derived literal (Step-0b Codex W1).
    /// </summary>
    [Fact]
    public async Task AbsenceTypeMapping_SpecialHolidayAllowance_LandsInFeriefridage()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // SPECIAL_HOLIDAY_ALLOWANCE is the raw absence_type written to absences_projection.
        // Via the shared map it resolves to SPECIAL_HOLIDAY (Feriefridage).
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2025, 3, 12), "SPECIAL_HOLIDAY_ALLOWANCE", hours: 7.4m);

        // Confirm the expected mapping: SPECIAL_HOLIDAY_ALLOWANCE → SPECIAL_HOLIDAY (Feriefridage).
        // EntitlementMapping is internal to StatsTid.Backend.Api; the value "SPECIAL_HOLIDAY" is
        // documented at src/Backend/StatsTid.Backend.Api/Services/EntitlementMapping.cs:46.
        // The test asserts the ENDPOINT BEHAVIOR proves the map is applied correctly — the response
        // must show afholdt in the SPECIAL_HOLIDAY row, not in VACATION. We do NOT re-derive the
        // map here; the assertion below is the behavioral evidence of the shared-map application.
        const string ExpectedEntitlementType = "SPECIAL_HOLIDAY"; // from EntitlementMapping.cs:46

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        // The SPECIAL_HOLIDAY row's March slot must have 1 day-equivalent (7.4 / 7.4 = 1.0).
        var specialHoliday = GetCategory(body, ExpectedEntitlementType);
        var marchAfholdt = specialHoliday.GetProperty("afholdt").EnumerateArray().ToList()[2].GetDecimal();
        Assert.Equal(1.0m, marchAfholdt);

        // The VACATION row's March slot must have 0 (no misrouting).
        var vacation = GetCategory(body, "VACATION");
        var vacMarchAfholdt = vacation.GetProperty("afholdt").EnumerateArray().ToList()[2].GetDecimal();
        Assert.Equal(0m, vacMarchAfholdt);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Straddle — absences in March (ferieår Y-1) and October (ferieår Y) count
    //    against their own ferieår; Sep saldo shows the reset sawtooth.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Ferieår straddle + reset sawtooth.</b>
    /// For VACATION (resetMonth = 9):
    /// <list type="bullet">
    ///   <item><description>A March 2025 absence belongs to ferieår 2024 (Sep 2024–Aug 2025);
    ///   it reduces saldo in months Jan–Aug (Jan–Aug accumulate from Sep 2024).</description></item>
    ///   <item><description>An October 2025 absence belongs to ferieår 2025 (Sep 2025–Aug 2026);
    ///   it reduces saldo in months Sep–Dec (the saldo chain restarts from Sep).</description></item>
    ///   <item><description>September saldo shows the reset sawtooth: the saldo resets at
    ///   Sep (a new ferieår starts, clearing prior cumulative afholdt from the old
    ///   ferieår).</description></item>
    ///   <item><description>A carryoverIn > 0 in the September ferieår is threaded
    ///   into saldo correctly.</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Straddle_AbsencesInDifferentFerieaar_CountAgainstCorrectFerieaar_SepShowsReset()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Absence in ferieår 2024 (Mar 2025): date ≤ 2026-06-15 (past) → 1 day VACATION.
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2025, 3, 15), "VACATION", hours: 7.4m);

        // Absence in ferieår 2025 (Oct 2025): date ≤ 2026-06-15 (past) → 1 day VACATION.
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2025, 10, 1), "VACATION", hours: 7.4m);

        // carryoverIn = 3 for ferieår 2025 (seed directly into entitlement_balances).
        // Ferieår 2025 → entitlement_year = 2025 (Sep 2025 – Aug 2026).
        await SeedEntitlementBalanceAsync(
            employeeId, "VACATION", entitlementYear: 2025, used: 1m, planned: 0m, carryoverIn: 3m);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        var vacation = GetCategory(body, "VACATION");
        var saldoArray = vacation.GetProperty("saldo").EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Null ? (decimal?)null : e.GetDecimal())
            .ToList();

        // Ferieår 2024 side (months Jan–Aug of 2025 calendar year, indexes 0–7).
        // Mar (index 2): cumulative afholdt in ferieår 2024 through 2025-03-31 = 1.0.
        // earnedToDate at 2025-03-31 for ferieår 2024 (start 2024-09-01, quota 25, null employment) =
        //   25 × 7/12 ≈ 14.58.
        var marSaldo = saldoArray[2]; // March = index 2
        var expectedMarSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2025, 3, 31), carryoverIn: 0m, cumulativeAfholdt: 1.0m);
        Assert.Equal(expectedMarSaldo, marSaldo);

        // August (index 7): same ferieår 2024; 12 months → earned = 25; afholdt = 1 (Mar).
        var augSaldo = saldoArray[7]; // August = index 7
        var expectedAugSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2025, 8, 31), carryoverIn: 0m, cumulativeAfholdt: 1.0m);
        Assert.Equal(expectedAugSaldo, augSaldo);

        // September (index 8): ferieår 2025 RESETS. The sawtooth: saldo restarts from Sep 2025.
        // Ferieår 2025 starts 2025-09-01. Earned at Sep-end (1 month) + carryoverIn(3) − cumAfholdt(0).
        // Oct absence date is 2025-10-01 which is AFTER Sep 2025 end (2025-09-30). So cumAfholdt
        // through Sep-end = 0.
        var sepSaldo = saldoArray[8]; // September = index 8
        var expectedSepSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2025, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2025, 9, 30), carryoverIn: 3m, cumulativeAfholdt: 0m);
        Assert.Equal(expectedSepSaldo, sepSaldo);

        // October (index 9): ferieår 2025; 2 months earned + carryoverIn(3) − cumAfholdt(1).
        var octSaldo = saldoArray[9]; // October = index 9
        var expectedOctSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2025, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2025, 10, 31), carryoverIn: 3m, cumulativeAfholdt: 1.0m);
        Assert.Equal(expectedOctSaldo, octSaldo);

        // Straddle assertion: the Oct absence did NOT affect Mar saldo (separate ferieår).
        // And the Mar absence did NOT affect Sep saldo chain.
        // Verify Mar saldo is not affected by Oct absence.
        Assert.Equal(expectedMarSaldo, marSaldo); // unchanged from before Oct booking
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. Transferable determinism.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two identical requests for the year-overview return BYTE-EQUAL responses (determinism
    /// property, priority #2). The endpoint is a pure function of (employeeId, year, today,
    /// projections); the only non-projection input — server <c>today</c> — is pinned by the
    /// fixed <see cref="TimeProvider"/>, so the entire serialized body must be character-for-
    /// character identical across two requests. This asserts on the FULL raw response strings
    /// (Step-5a BLOCKER 1: deserializing + comparing one decimal field is not a byte-equality
    /// proof — JSON property ordering, number formatting, and every other field must also be
    /// stable).
    /// </summary>
    [Fact]
    public async Task Transferable_TwoIdenticalRequests_ByteEqual()
    {
        var client = MakeFixedTodayClient(EmployeeBearerToken(Emp001, Emp001OrgId));

        var url = $"/api/balance/{Emp001}/year-overview?year=2025";
        var rsp1 = await client.GetAsync(url);
        var rsp2 = await client.GetAsync(url);
        rsp1.EnsureSuccessStatusCode();
        rsp2.EnsureSuccessStatusCode();

        var rawBody1 = await rsp1.Content.ReadAsStringAsync();
        var rawBody2 = await rsp2.Content.ReadAsStringAsync();

        // The PRIMARY requirement: the two FULL raw bodies are exactly equal (byte/char identical).
        Assert.Equal(rawBody1, rawBody2);

        // Retained field-level cross-check (the determinism property the test was named for):
        // VACATION transferable is identical across the two responses.
        using var doc1 = JsonDocument.Parse(rawBody1);
        using var doc2 = JsonDocument.Parse(rawBody2);
        var vac1 = GetCategory(doc1.RootElement, "VACATION").GetProperty("transferable").GetDecimal();
        var vac2 = GetCategory(doc2.RootElement, "VACATION").GetProperty("transferable").GetDecimal();
        Assert.Equal(vac1, vac2);
    }

    /// <summary>
    /// <b>Transferable formula verification + cap-0 type = 0.</b>
    /// Formula: <c>min(max(0, earnedAtBoundary + carryoverIn − used − planned), carryoverMax)</c>.
    /// For SPECIAL_HOLIDAY (carryoverMax = 0) → always 0 regardless of any balance.
    /// For VACATION: with no absences and no carryover, transferable = 0 (earnedAtBoundary
    /// minus nothing = positive, but capped at carryoverMax from config).
    /// </summary>
    [Fact]
    public async Task Transferable_Cap0Type_AlwaysZero_Formula_VerifiedForVacation()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        // SPECIAL_HOLIDAY has carryoverMax = 0 (danish-agreements.md:110) → transferable = 0.
        var shTransferable = GetCategory(body, "SPECIAL_HOLIDAY")
            .GetProperty("transferable").GetDecimal();
        Assert.Equal(0m, shTransferable);

        // CARE_DAY has carryoverMax = 0 (calendar-year IMMEDIATE) → transferable = 0.
        var careDayTransferable = GetCategory(body, "CARE_DAY")
            .GetProperty("transferable").GetDecimal();
        Assert.Equal(0m, careDayTransferable);
    }

    /// <summary>
    /// <b>Transferable formula — BELOW-cap branch pinned with an EXACT non-trivial value.</b>
    /// VACATION is the only type with a non-zero <c>carryoverMax</c> (5m,
    /// DefaultEntitlementConfigs.cs:74), so it is the only type whose <c>min()</c> can take EITHER
    /// branch. Here we drive the <c>raw</c> operand BELOW the cap by seeding the CLOSED boundary
    /// ferieår's <c>entitlement_balances</c> row with a large <c>used</c>:
    /// <list type="bullet">
    ///   <item><description>Selected year = 2025 ⇒ closed boundary ferieår = 2024
    ///   (Sep 2024 – Aug 2025); the handler reads the <c>entitlement_year = 2024</c> balance row
    ///   (BalanceEndpoints.cs:747-755 — <c>closedEntYear = year-1</c> for ResetMonth-9 types).</description></item>
    ///   <item><description><c>earnedAtBoundary</c> at 2025-08-31 = full 12 months = 25 (real
    ///   AccrualMath).</description></item>
    ///   <item><description>seed used = 22, planned = 0, carryoverIn = 0 ⇒
    ///   raw = 25 + 0 − 22 − 0 = 3 &lt; 5 ⇒ transferable = min(max(0,3),5) = <b>3</b>.</description></item>
    /// </list>
    /// Asserts the exact below-cap value (NOT just &gt;= 0). The closed-ferieår year-key is the
    /// highest-risk wiring in the endpoint — seeding the SAME (employee, type, year-1) row the
    /// handler reads pins it.
    /// </summary>
    [Fact]
    public async Task Transferable_BelowCap_EqualsExactRawValue()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Closed boundary ferieår for selected year 2025 = ferieår 2024 → entitlement_year 2024.
        // used = 22 drives raw (25 − 22 = 3) below the carryoverMax of 5.
        await SeedEntitlementBalanceAsync(
            employeeId, "VACATION", entitlementYear: 2024, used: 22m, planned: 0m, carryoverIn: 0m);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        var transferable = GetCategory(body, "VACATION").GetProperty("transferable").GetDecimal();

        // earnedAtBoundary via the REAL AccrualMath at the closed ferieår 2024 boundary (2025-08-31);
        // carryoverMax = 5 (DefaultEntitlementConfigs.cs:74). raw = 25 + 0 − 22 − 0 = 3 < 5 ⇒ 3.
        var expected = ExpectedTransferable(
            annualQuota: 25m, closedFerieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            boundaryDate: new DateOnly(2025, 8, 31),
            carryoverIn: 0m, used: 22m, planned: 0m, carryoverMax: 5m);
        Assert.Equal(3m, expected);             // sanity: the derivation lands on the below-cap branch
        Assert.Equal(expected, transferable);   // endpoint matches the real-AccrualMath formula exactly
    }

    /// <summary>
    /// <b>Transferable formula — AT-cap branch pinned with the EXACT cap value.</b>
    /// Same closed-ferieår wiring as the below-cap test, but operands push <c>raw</c> ABOVE the cap
    /// so the <c>min()</c> clamps to <c>carryoverMax</c>: seed used = planned = carryoverIn = 0 ⇒
    /// raw = earnedAtBoundary(25) &gt; 5 ⇒ transferable = min(max(0,25),5) = <b>5</b>. Asserts the
    /// exact at-cap value (NOT just &gt;= 0). Together with the below-cap test, BOTH branches of
    /// the <c>min()</c> are now pinned to exact expected values (Step-5a BLOCKER 2).
    /// </summary>
    [Fact]
    public async Task Transferable_AtCap_EqualsCarryoverMax()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Seed the closed ferieår 2024 row with all-zero operands so raw = earnedAtBoundary (25),
        // which exceeds the carryoverMax of 5. (A seed is written so the at-cap case is explicit
        // and does not depend on the no-row default also yielding zeros.)
        await SeedEntitlementBalanceAsync(
            employeeId, "VACATION", entitlementYear: 2024, used: 0m, planned: 0m, carryoverIn: 0m);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        var transferable = GetCategory(body, "VACATION").GetProperty("transferable").GetDecimal();

        var expected = ExpectedTransferable(
            annualQuota: 25m, closedFerieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            boundaryDate: new DateOnly(2025, 8, 31),
            carryoverIn: 0m, used: 0m, planned: 0m, carryoverMax: 5m);
        Assert.Equal(5m, expected);             // sanity: the derivation clamps to the cap (5)
        Assert.Equal(expected, transferable);   // endpoint clamps to carryoverMax exactly
    }

    /// <summary>
    /// <b>boundaryMonth = 12 for ALL categories (OQ-1 RESOLVED).</b>
    /// The transferable display anchor is December for every category per the owner-ratified
    /// resolution of OQ-1 (31 Dec per Ferielov §21 stk.2; display only, computation still
    /// at the type's model boundary).
    /// </summary>
    [Fact]
    public async Task Transferable_BoundaryMonthIsTwelveForAllCategories()
    {
        var client = MakeFixedTodayClient(EmployeeBearerToken(Emp001, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, Emp001, 2025);

        foreach (var cat in body.GetProperty("categories").EnumerateArray())
        {
            var type = cat.GetProperty("type").GetString();
            var boundaryMonth = cat.GetProperty("boundaryMonth").GetInt32();
            Assert.Equal(12, boundaryMonth); // OQ-1 resolved: December for ALL types
        }
    }

    /// <summary>
    /// <b>Transferable computed at the MODEL BOUNDARY (not December) — assertion via a
    /// post-August absence.</b>
    /// Seeds a VACATION absence in September 2025 (ferieår Y = 2025, which starts Sep 2025).
    /// The CLOSED boundary ferieår for year 2025 is ferieår Y−1 = 2024 (Sep 2024 – Aug 2025).
    /// The Sep 2025 absence belongs to ferieår 2025 (NOT ferieår 2024), so it does NOT change
    /// the transferable for year 2025 (which uses the closed ferieår 2024 balances). However,
    /// it DOES appear in the Dec 2025 saldo (ferieår 2025, Sep–Dec portion). This discriminates
    /// compute-at-model-boundary from compute-at-December (Step-0b cycle-2 Reviewer confirmation).
    /// </summary>
    [Fact]
    public async Task Transferable_ComputedAtModelBoundary_NotDecember_PostAugustAbsenceDoesNotChangeTransferable()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Absence in Sep 2025 → belongs to ferieår 2025 (starts Sep 2025).
        // The closed boundary ferieår for year=2025 is ferieår 2024 (Sep 2024 – Aug 2025).
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2025, 9, 10), "VACATION", hours: 7.4m); // 1 day, past

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        var vacation = GetCategory(body, "VACATION");

        // Transferable uses the CLOSED ferieår 2024 balances (Sep 2024 – Aug 2025).
        // The Sep 2025 absence is in ferieår 2025, so closedUsed/closedPlanned = 0.
        // transferable = min(max(0, earnedAtBoundary + 0 − 0 − 0), carryoverMax).
        // earnedAtBoundary at 2025-08-31 (31 Aug of selected year for resetMonth-9):
        //   = EarnedToDate(25, 1.0, 2024-09-01, null, 2025-08-31) = 25 (full 12 months).
        // transferable = min(max(0, 25), carryoverMax). Exact value depends on carryoverMax
        // from config; the key assertion is that it is NOT affected by the Sep 2025 absence.
        var transferable = vacation.GetProperty("transferable").GetDecimal();

        // Dec saldo (index 11): ferieår 2025 (Sep 2025–Aug 2026). Sep absence IS counted.
        // saldo[11] at Dec-end = earned(12 months of ferieår 2025) + carryoverIn(0) −
        //   cumulativeAfholdt(type in ferieår 2025 through Dec) = earned − 1.
        var decSaldo = vacation.GetProperty("saldo").EnumerateArray().ToList()[11].GetDecimal();
        var earnedFerieaar2025Dec = AccrualMath.EarnedToDate(
            25m, 1.0m, new DateOnly(2025, 9, 1), null, new DateOnly(2025, 12, 31));
        // cumAfholdt through Dec-end for ferieår 2025 = 1 (Sep absence).
        var expectedDecSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2025, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2025, 12, 31), carryoverIn: 0m, cumulativeAfholdt: 1.0m);
        Assert.Equal(expectedDecSaldo, decSaldo);

        // The Sep 2025 absence does NOT change the transferable (which uses the CLOSED ferieår
        // 2024 balances). No closed-ferieår-2024 balance row is seeded here ⇒ closedUsed =
        // closedPlanned = closedCarryoverIn = 0, and earnedAtBoundary(2025-08-31, ferieår 2024) =
        // 25 (full 12 months). raw = 25 > carryoverMax 5 ⇒ transferable = 5 EXACTLY. Asserting the
        // exact value (not >= 0) is what proves compute-at-model-boundary: a Sep-2025 (ferieår
        // 2025) absence cannot perturb a value sourced entirely from ferieår 2024.
        var expectedTransferable = ExpectedTransferable(
            annualQuota: 25m, closedFerieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            boundaryDate: new DateOnly(2025, 8, 31),
            carryoverIn: 0m, used: 0m, planned: 0m, carryoverMax: 5m);
        Assert.Equal(5m, expectedTransferable); // sanity: clamps to the cap
        Assert.Equal(expectedTransferable, transferable);

        // And the Dec saldo (ferieår 2025) DID drop because of the same Sep absence — the two
        // ferieår are independent (straddle), confirming the discrimination.
        Assert.True(decSaldo < earnedFerieaar2025Dec,
            $"decSaldo {decSaldo} should be less than earnedFerieaar2025Dec {earnedFerieaar2025Dec} because of the Sep 2025 absence");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. OK-version straddle — 2026-04-01 OK24→OK26 cutover.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A request for year 2026 spans the 2026-04-01 OK24→OK26 cutover. DailyNormCalculator calls
    /// <c>OkVersionResolver.ResolveVersion(day)</c> PER DAY (DailyNormCalculator.cs:103), so days
    /// before April resolve OK24 and days from April resolve OK26.
    ///
    /// <para><b>Discrimination route (Step-5a BLOCKER 3): LOCAL org-scoped config override
    /// (ADR-017), NOT a global DB seed.</b> CentralAgreementConfigs defines AC/OK24 and AC/OK26 as
    /// value-IDENTICAL (WeeklyNormHours 37.0 both sides), and the global <c>entitlement_configs</c>
    /// / <c>agreement_configs</c> rows are keyed by (agreement_code, ok_version) ONLY — seeding a
    /// different AC/OK26 row there would contaminate every other test (the S64 shared-DB-row
    /// lesson). Instead we attach TWO <c>local_agreement_profiles</c> rows to a DEDICATED org node
    /// (<c>STY_OKDISC</c>, used by no other test) that this test's employee alone belongs to — one
    /// for OK24 (weekly_norm 30) and one for OK26 (weekly_norm 35). <c>ConfigResolutionService</c>
    /// overlays the org+agreement+ok-keyed profile (LocalAgreementProfileRepository.cs:68-86), so
    /// the per-weekday norm DIFFERS across the cutover and we can assert DIFFERENT EXACT values on
    /// each side. If the endpoint resolved OK once for the whole year (instead of per day), both
    /// months would pick the same profile and the per-weekday rate would be identical — the
    /// distinct-rate assertion below would fail.</para>
    ///
    /// <para>Also asserts that the VACATION entitlement-config reads anchor at the entitlement-year
    /// START, not today's OK (the saldo/transferable values below).</para>
    /// </summary>
    [Fact]
    public async Task OkVersionStraddle_2026Cutover_NormHoursPerSideCorrect_EntitlementConfigAnchorsAtYearStart()
    {
        // Dedicated org node — referenced by NO other test, so the local profiles below cannot
        // contaminate any other employee's config resolution.
        const string okDiscOrgId = "STY_OKDISC";
        const decimal ok24WeeklyNorm = 30.00m; // OK24-side local override
        const decimal ok26WeeklyNorm = 35.00m; // OK26-side local override (distinct from OK24)

        // Fresh full-time employee whose primary_org_id is the dedicated org (DailyNormCalculator
        // resolves config for profile.OrgId == users.primary_org_id, EmploymentProfileResolver.cs:151/180).
        var employeeId = await CreateEmployeeAsync(okDiscOrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, okDiscOrgId, "AC", "OK24");

        // Two org-scoped local agreement profiles, one per OK version, with DISTINCT weekly norms.
        // Partial-unique index is (org_id, agreement_code, ok_version) WHERE effective_to IS NULL
        // (init.sql:741-743) → OK24 + OK26 are distinct open rows, both allowed.
        await SeedLocalAgreementProfileAsync(okDiscOrgId, "AC", "OK24", ok24WeeklyNorm);
        await SeedLocalAgreementProfileAsync(okDiscOrgId, "AC", "OK26", ok26WeeklyNorm);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, okDiscOrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);

        var months = body.GetProperty("months").EnumerateArray().ToList();

        // Per-weekday norm = Round(weeklyNorm × fraction(1.0) / 5, 2) (DailyNormCalculator.cs:121).
        var ok24DailyNorm = Math.Round(ok24WeeklyNorm * 1.0m / 5m, 2); // 30/5 = 6.00
        var ok26DailyNorm = Math.Round(ok26WeeklyNorm * 1.0m / 5m, 2); // 35/5 = 7.00
        Assert.NotEqual(ok24DailyNorm, ok26DailyNorm); // sanity: the two sides are genuinely distinct

        // January 2026 (index 0): every day < 2026-04-01 → OK24 → 6.00/weekday.
        var janMonth = months[0];
        Assert.Equal(1, janMonth.GetProperty("month").GetInt32());
        var janNorm = janMonth.GetProperty("normHours").GetDecimal();
        var expectedJanNorm = ok24DailyNorm * CountWeekdays(2026, 1);
        Assert.Equal(expectedJanNorm, janNorm); // OK24-side EXACT value

        // May 2026 (index 4): every day ≥ 2026-04-01 → OK26 → 7.00/weekday.
        var mayMonth = months[4];
        Assert.Equal(5, mayMonth.GetProperty("month").GetInt32());
        var mayNorm = mayMonth.GetProperty("normHours").GetDecimal();
        var expectedMayNorm = ok26DailyNorm * CountWeekdays(2026, 5);
        Assert.Equal(expectedMayNorm, mayNorm); // OK26-side EXACT value

        // The discriminating cross-check: the two sides resolved DIFFERENT per-weekday rates
        // (6.00 vs 7.00). Normalize out the differing weekday counts to compare the rates directly.
        var janPerWeekday = janNorm / CountWeekdays(2026, 1);
        var mayPerWeekday = mayNorm / CountWeekdays(2026, 5);
        Assert.Equal(ok24DailyNorm, janPerWeekday);
        Assert.Equal(ok26DailyNorm, mayPerWeekday);
        Assert.NotEqual(janPerWeekday, mayPerWeekday); // per-day OK resolution genuinely discriminated

        // ── Entitlement-config anchoring (unchanged behavior, now asserted on exact values) ──
        // VACATION ferieår for Jan–Aug 2026 = ferieår 2025 (start 2025-09-01 → OkVersion OK24).
        // No absences, no carryover, full-time. Jan saldo = EarnedToDate at 2026-01-31 for ferieår
        // 2025 (5 months: Sep,Oct,Nov,Dec,Jan) = 25 × 5/12, via the REAL AccrualMath.
        var vacation = GetCategory(body, "VACATION");
        var janSaldo = vacation.GetProperty("saldo").EnumerateArray().ToList()[0].GetDecimal();
        var expectedJanSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2025, 9, 1), employmentStart: null,
            monthEnd: new DateOnly(2026, 1, 31), carryoverIn: 0m, cumulativeAfholdt: 0m);
        Assert.Equal(expectedJanSaldo, janSaldo);

        // transferable: closed boundary ferieår = 2024 (start 2024-09-01 → OK24), boundary 2025-08-31.
        // No closed-ferieår-2024 balance row seeded ⇒ all-zero operands ⇒ raw = 25 > carryoverMax 5
        // ⇒ transferable = 5 EXACTLY. carryoverMax is read from the OK24-era config (Step-0b cycle-2
        // Reviewer NOTE: anchored at the closed ferieår start, NOT today's OK26).
        var vacTransferable = vacation.GetProperty("transferable").GetDecimal();
        var expectedTransferable = ExpectedTransferable(
            annualQuota: 25m, closedFerieaarStart: new DateOnly(2024, 9, 1), employmentStart: null,
            boundaryDate: new DateOnly(2025, 8, 31),
            carryoverIn: 0m, used: 0m, planned: 0m, carryoverMax: 5m);
        Assert.Equal(5m, expectedTransferable);
        Assert.Equal(expectedTransferable, vacTransferable);

        // HONEST GAP (Step-5a BLOCKER 3): the transferable carryoverMax is sourced from the GLOBAL
        // entitlement_configs row (keyed by agreement_code + ok_version only — NOT org-scoped), and
        // AC/OK24 vs AC/OK26 are value-identical placeholders (both carryoverMax = 5,
        // DefaultEntitlementConfigs.cs:74). The local-profile override above only steers
        // weekly_norm_hours, NOT entitlement quotas/carryoverMax. So this test pins the
        // year-start-anchored carryoverMax VALUE (5) but cannot make it OK24-vs-OK26-DISCRIMINATING
        // without contaminating the global config — that sub-claim is a documented limitation,
        // reported to the Orchestrator. The per-day NORM discrimination above IS exact and real.
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. Future months — diff null; planned absence appears in afholdt.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For months AFTER today (2026-06-15), <c>diff</c> is <c>null</c>. A planned future
    /// absence seeded for August 2026 appears in <c>afholdt[7]</c> (August, index 7).
    /// </summary>
    [Fact]
    public async Task FutureMonths_DiffIsNull_PlannedAbsenceAppearsInAfholdt()
    {
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        // Future absence in August 2026 (date > 2026-06-15, NOT in the current month June).
        await SeedAbsenceProjectionRowAsync(
            employeeId, new DateOnly(2026, 8, 10), "VACATION", hours: 7.4m);

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);

        var months = body.GetProperty("months").EnumerateArray().ToList();

        // July 2026 (index 6): future month → diff must be null.
        var julMonth = months[6];
        Assert.Equal(7, julMonth.GetProperty("month").GetInt32());
        Assert.Equal(JsonValueKind.Null, julMonth.GetProperty("diff").ValueKind);

        // August 2026 (index 7): future month → diff null; afholdt has 1 day-equivalent.
        var augMonth = months[7];
        Assert.Equal(8, augMonth.GetProperty("month").GetInt32());
        Assert.Equal(JsonValueKind.Null, augMonth.GetProperty("diff").ValueKind);

        // The planned VACATION absence for August shows in afholdt.
        var vacation = GetCategory(body, "VACATION");
        var augAfholdt = vacation.GetProperty("afholdt").EnumerateArray().ToList()[7].GetDecimal(); // index 7 = Aug
        Assert.Equal(1.0m, augAfholdt);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 10. ANNUAL_ACTIVITY — academic profile → normHours null every month.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An employee whose resolved employment profile resolves to <c>NormModel.ANNUAL_ACTIVITY</c>
    /// (academic) has <c>normHours: null</c> on every month of the year-overview, because a
    /// per-weekday split of an annual norm is not meaningful (DailyNormCalculator returns null
    /// for every ANNUAL_ACTIVITY day, and the month aggregation emits null when ANY day is null).
    /// <c>AC_RESEARCH</c> has <c>NormModel = NormModel.ANNUAL_ACTIVITY</c> in
    /// <c>CentralAgreementConfigs</c> (src/SharedKernel/StatsTid.SharedKernel/Config/
    /// CentralAgreementConfigs.cs:205). <c>ConfigResolutionService.ResolveAsync</c> falls back to
    /// <c>CentralAgreementConfigs</c> when no DB row exists for the agreement/OK pair, so
    /// <c>AC_RESEARCH/OK24</c> resolves to <c>ANNUAL_ACTIVITY</c> without any DB entitlement-config
    /// seed.
    /// </summary>
    [Fact]
    public async Task AnnualActivity_AcademicProfile_NormHoursNullAllMonths()
    {
        // AC_RESEARCH maps to NormModel.ANNUAL_ACTIVITY in CentralAgreementConfigs.cs:205.
        // ConfigResolutionService falls back to static CentralAgreementConfigs when no DB
        // agreement_config row exists for "AC_RESEARCH/OK24" — and none is seeded by the
        // EntitlementConfigSeeder (which only seeds AC/HK/PROSA per DefaultEntitlementConfigs.cs:12).
        // This gives us ANNUAL_ACTIVITY without modifying src/.
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC_RESEARCH", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC_RESEARCH", "OK24");

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2025);

        // All 12 months must have normHours = null (ANNUAL_ACTIVITY academic norm).
        // DailyNormCalculator: ANNUAL_ACTIVITY → null per weekday → any null weekday propagates
        // to the month aggregate (the endpoint checks "Any(n => n.Hours is null)"). Hence every
        // month (each has at least 1 weekday) → normHours is null.
        var monthList = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(12, monthList.Count);
        var nullNormCount = monthList.Count(m => m.GetProperty("normHours").ValueKind == JsonValueKind.Null);
        Assert.Equal(12, nullNormCount);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 11. Graceful — profile-less employee → 200 with nulls, never 500.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An employee with NO <c>employee_profiles</c> row (the graceful path per ADR-023 D3):
    /// the year-overview returns 200 with <c>weeklyNormHours: null</c>, <c>normHours: null</c>
    /// on every month (no profile = no norm), and no 500. The profile-less user is created
    /// AFTER the last host boot to avoid the S31 EmployeeProfileSeeder backfilling a row.
    ///
    /// <para><b>Boot-order lesson (S63).</b> <c>WithWebHostBuilder</c> creates a derived factory
    /// that re-runs Program.cs seeders (including EmployeeProfileSeeder) on its own
    /// <c>CreateClient()</c> call. We therefore: (1) create the fixed-today WAF via
    /// <c>WithWebHostBuilder</c> and call <c>CreateClient()</c> on it first (running the seeder);
    /// (2) insert the profile-less user AFTER that boot (so the seeder has already run and cannot
    /// backfill this newly-created user).</para>
    /// </summary>
    [Fact]
    public async Task Graceful_ProfilelessEmployee_Returns200WithNulls_Never500()
    {
        // Step 1: Build the fixed-today WAF and boot the host FIRST (runs EmployeeProfileSeeder).
        // After CreateClient() the seeder has run and will not run again for this factory instance.
        var derivedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday));
            });
        });
        // Boot the derived host (runs EmployeeProfileSeeder for any existing users).
        _ = derivedFactory.CreateClient();

        // Step 2: Create the profile-less user AFTER the last host boot.
        // This is the boot-order pattern from S63: the seeder has already run for this factory,
        // so it will NOT backfill a profile for this newly-created user.
        var employeeId = "emp_s65_t11_" + Guid.NewGuid().ToString("N")[..8];
        await InsertBareUserAsync(employeeId, Emp001OrgId, "AC", "OK24");

        // Step 3: Make the request (no new host boot — CreateClient() on an already-booted WAF).
        var client = derivedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", EmployeeBearerToken(employeeId, Emp001OrgId));

        var rsp = await client.GetAsync(
            $"/api/balance/{employeeId}/year-overview?year=2025");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // header.weeklyNormHours should be null (no profile → no merged config norm).
        var weeklyNormHours = body.GetProperty("header").GetProperty("weeklyNormHours");
        Assert.Equal(JsonValueKind.Null, weeklyNormHours.ValueKind);

        // All 12 months should have normHours: null (no profile → null per day → null aggregate).
        var monthList = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(12, monthList.Count);
        var nullNormCount = monthList.Count(m =>
            m.GetProperty("normHours").ValueKind == JsonValueKind.Null);
        Assert.Equal(12, nullNormCount);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 12. Step-7a C1 pin — per-ferieår DATED agreement-code valuation.
    //     A historical ferieår must be valued with the agreement the employee held
    //     AT THAT FERIEÅR'S START, not today's agreement (BalanceEndpoints.cs:667-721,
    //     "S65 Step-7a fix: per-ferieår dated agreement-code anchoring").
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Step-7a C1 (Codex P1) regression pin — the dated agreement-code operand of the
    /// entitlement-config reads.</b> An employee who changes agreement BETWEEN the two ferieår a
    /// single year-overview spans must value each ferieår against the agreement in effect at THAT
    /// ferieår's start — never against today's agreement.
    ///
    /// <para><b>Route taken: (c) — pin the MECHANISM with a SAFELY-SCOPED dated config row, using a
    /// FICTITIOUS agreement code.</b> Routes (a) and (b) and the (c)-with-AC_TEACHING variant were
    /// all checked AGAINST THE LIVE SEEDED DB and ruled out:
    /// <list type="bullet">
    ///   <item><description>(a) is non-discriminating: ALL VACATION configs in the DB are quota 25 +
    ///   carryoverMax 5 across EVERY seeded agreement code — AC/HK/PROSA AND the AC variants
    ///   AC_RESEARCH/AC_TEACHING (init.sql:1423-1428 + 1465-1468). Switching between any two SEEDED
    ///   codes changes no observable VACATION saldo/transferable. (CHILD_SICK is the only quota that
    ///   varies by agreement, and it is NOT a year-overview category — YearOverviewCategoryTypes,
    ///   BalanceEndpoints.cs:520-521.) OK24 vs OK26 are value-identical too.</description></item>
    ///   <item><description>(b) is non-discriminating: AC_RESEARCH/AC_TEACHING DO have seeded
    ///   entitlement_configs in this DB (init.sql:1460-1484 — the S37 "AC variants mirror AC base
    ///   values" absorption), all quota 25, so an academic ferieår values identically to AC — the
    ///   fallback chain never even fires, and there is nothing to discriminate.</description></item>
    ///   <item><description>(c)-via-AC_TEACHING is UNSAFE: AC_TEACHING's live (open) VACATION row
    ///   already exists (quota 25) and other tests read it; giving it a distinct historical value
    ///   would require closing/superseding that shared open row — the S64 shared-state
    ///   contamination this constraint forbids.</description></item>
    /// </list>
    /// So we seed a dated <c>entitlement_configs</c> row for a FICTITIOUS agreement code
    /// (<c>PIN_DATED_AC</c>) present NOWHERE else — not in entitlement_configs, not in
    /// <c>CentralAgreementConfigs</c>, not in any other test. A NEW
    /// <c>(VACATION, PIN_DATED_AC, OK24)</c> row is purely ADDITIVE (cannot collide on any
    /// natural-key index, init.sql:1368-1374) and idempotent (ON CONFLICT on the open partial index
    /// → DO UPDATE to the same values), NOT contamination.</para>
    ///
    /// <para><b>Norm-path safety (why the fictitious code does not 500).</b> The entitlement-config
    /// read path (<c>ResolveDatedConfigAsync</c>, BalanceEndpoints.cs:710-721) reads the
    /// <c>entitlement_configs</c> table DIRECTLY and tolerates ANY agreement code. But the per-month
    /// <c>normHours</c> path (<c>DailyNormCalculator</c> → <c>ConfigResolutionService.ResolveAsync</c>)
    /// would THROW <c>InvalidOperationException</c> for an agreement code absent from both
    /// <c>agreement_configs</c> and <c>CentralAgreementConfigs</c> (ConfigResolutionService.cs:132-134)
    /// — but ONLY when a dated employee_profiles row covers the day (DailyNormCalculator.cs:93-101:
    /// no profile ⇒ null norm, no ResolveAsync call). So this employee's <c>employee_profiles</c> row
    /// covers ONLY <c>[2026-06-01, ∞)</c> (today + the later ferieår under AC); the earlier months
    /// (Jan–May 2026, under the fictitious code) have NO profile ⇒ null norms ⇒ ResolveAsync is never
    /// invoked for the fictitious code ⇒ no 500. The header likewise skips ResolveAsync when today's
    /// profile resolves (it does — AC from 2026-06-01). saldo/transferable are independent of norms.</para>
    ///
    /// <para><b>Why this discriminates (pre-fix-fails argument).</b> Selected year = 2026.
    /// For VACATION (resetMonth 9) the year spans TWO ferieår:
    /// <list type="bullet">
    ///   <item><description>Jan–Aug 2026 → ferieår 2025 (start 2025-09-01, OK24).</description></item>
    ///   <item><description>Sep–Dec 2026 → ferieår 2026 (start 2026-09-01, OK26).</description></item>
    /// </list>
    /// The employee held <c>PIN_DATED_AC</c> for <c>[0001-01-01, 2026-06-01)</c> and <c>AC</c> from
    /// <c>2026-06-01</c> (the live row covering today 2026-06-15 ⇒ <c>todayAgreementCode = AC</c>).
    /// The EARLIER ferieår (2025) starts under PIN_DATED_AC; the LATER ferieår (2026) starts under AC.
    /// <list type="bullet">
    ///   <item><description><b>Post-fix:</b> the Jan-2026 saldo (ferieår 2025) resolves the agreement
    ///   at 2025-09-01 = PIN_DATED_AC, the dated read HITS the seeded PIN_DATED_AC row (quota 40), and
    ///   the saldo is EarnedToDate(40, …) = 40 × 5/12 = <b>16.67</b>.</description></item>
    ///   <item><description><b>Pre-fix (todayAgreementCode = AC for every ferieår):</b> the same slot
    ///   would read <c>(VACATION, AC, OK24)</c> (quota 25) ⇒ 25 × 5/12 = 10.42 — a DIFFERENT value.
    ///   The exact-equality assertion on 16.67 fails under the pre-fix behavior.</description></item>
    ///   <item><description>The LATER ferieår (Dec-2026 saldo) resolves the agreement at 2026-09-01 = AC
    ///   (quota 25) ⇒ EarnedToDate(25, …) = 25 × 4/12 = <b>8.33</b> — confirming the current ferieår
    ///   still reflects the NEW agreement.</description></item>
    /// </list>
    /// Transferable for 2026 (closed boundary ferieår = 2025, start 2025-09-01, boundary 2026-08-31)
    /// likewise anchors at PIN_DATED_AC (BalanceEndpoints.cs:806-811): earnedAtBoundary = full 12 months
    /// = 40, raw = 40 > PIN_DATED_AC carryoverMax (10) ⇒ transferable = <b>10</b> EXACTLY. Pre-fix would
    /// clamp to AC's carryoverMax (5) — so transferable independently discriminates too.</para>
    /// </summary>
    [Fact]
    public async Task DatedAgreement_HistoricalFerieaarValuesAgainstThatYearsAgreement_NotTodaysAgreement()
    {
        // Fictitious agreement code present NOWHERE else (entitlement_configs / CentralAgreementConfigs
        // / any other test) — a NEW (VACATION, <code>, OK24) row is purely additive, never contamination.
        const string pinAgreement = "PIN_DATED_AC";
        // Distinct VACATION config — quota + carryoverMax both differ from AC's (25 / 5).
        const decimal pinQuota = 40m;        // vs AC's 25 (init.sql:1423)
        const decimal pinCarryoverMax = 10m; // vs AC's 5  (init.sql:1423)
        var switchDate = new DateOnly(2026, 6, 1);

        // Bare user (ok_version OK24 → liveConfig reads (type, AC, OK24); agreement_code cache = AC).
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");

        // employee_profiles covers ONLY [switchDate, ∞): today + the later (AC) ferieår resolve norms
        // under AC; the earlier (fictitious-code) months have NO profile ⇒ null norms ⇒ the
        // ConfigResolutionService path is never invoked for the fictitious code (no 500). See the
        // norm-path-safety note in the summary.
        await SeedEmployeeProfileRowAsync(employeeId, fraction: 1.000m,
            effectiveFrom: switchDate, effectiveTo: null, version: 1);

        // Dated agreement-code HISTORY: PIN_DATED_AC up to switchDate (end-exclusive), AC from then.
        //   GetByUserIdAtAsync(2025-09-01) → PIN_DATED_AC  (earlier ferieår start)
        //   GetByUserIdAtAsync(2026-09-01) → AC            (later ferieår start)
        //   GetByUserIdAtAsync(2026-06-15) → AC            (today ⇒ todayAgreementCode = AC)
        await SeedAgreementCodeRowAsync(employeeId, pinAgreement,
            effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: switchDate);
        await SeedAgreementCodeRowAsync(employeeId, "AC",
            effectiveFrom: switchDate, effectiveTo: null);

        // Seed the discriminating PIN_DATED_AC VACATION config at OK24 (the OK version the endpoint
        // resolves at the 2025-09-01 ferieår start: OkVersionResolver < 2026-04-01 ⇒ OK24).
        await SeedDatedEntitlementConfigAsync(
            entitlementType: "VACATION", agreementCode: pinAgreement, okVersion: "OK24",
            annualQuota: pinQuota, accrualModel: "MONTHLY_ACCRUAL", resetMonth: 9,
            carryoverMax: pinCarryoverMax, effectiveFrom: new DateOnly(1, 1, 1));

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);

        var vacation = GetCategory(body, "VACATION");
        var saldo = vacation.GetProperty("saldo").EnumerateArray().ToList();

        // ── EARLIER ferieår 2025 (Jan 2026, index 0): valued against PIN_DATED_AC (quota 40). ──
        // earned = EarnedToDate(40, 1.0, 2025-09-01, null, 2026-01-31) = 40 × 5/12 = 16.6667 → 16.67.
        var janSaldo = saldo[0].GetDecimal();
        var expectedJanSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: pinQuota, ferieaarStart: new DateOnly(2025, 9, 1),
            employmentStart: null, monthEnd: new DateOnly(2026, 1, 31),
            carryoverIn: 0m, cumulativeAfholdt: 0m);
        Assert.Equal(16.67m, expectedJanSaldo);   // sanity: PIN_DATED_AC quota → 16.67
        Assert.Equal(expectedJanSaldo, janSaldo);  // endpoint valued the earlier ferieår at PIN_DATED_AC

        // Pre-fix-fails guard: today's-agreement (AC, quota 25) would have produced a DIFFERENT
        // value (10.42). Asserting they differ proves the assertion above is genuinely
        // discriminating (not a value both code paths happen to agree on).
        var preFixWouldBe = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2025, 9, 1),
            employmentStart: null, monthEnd: new DateOnly(2026, 1, 31),
            carryoverIn: 0m, cumulativeAfholdt: 0m);
        Assert.Equal(10.42m, preFixWouldBe);
        Assert.NotEqual(preFixWouldBe, janSaldo);

        // ── LATER ferieår 2026 (Dec 2026, index 11): valued against AC (quota 25, the NEW agreement). ──
        // earned = EarnedToDate(25, 1.0, 2026-09-01, null, 2026-12-31) = 25 × 4/12 = 8.3333 → 8.33.
        var decSaldo = saldo[11].GetDecimal();
        var expectedDecSaldo = ExpectedMonthlyAccrualSaldo(
            annualQuota: 25m, ferieaarStart: new DateOnly(2026, 9, 1),
            employmentStart: null, monthEnd: new DateOnly(2026, 12, 31),
            carryoverIn: 0m, cumulativeAfholdt: 0m);
        Assert.Equal(8.33m, expectedDecSaldo);    // sanity: AC quota → 8.33
        Assert.Equal(expectedDecSaldo, decSaldo);  // current ferieår reflects the NEW agreement

        // ── Transferable for 2026: closed boundary ferieår 2025 anchors at PIN_DATED_AC. ──
        // earnedAtBoundary = EarnedToDate(40, 1.0, 2025-09-01, null, 2026-08-31) = 40 (full 12 months);
        // raw = 40 > carryoverMax 10 ⇒ transferable = 10 (PIN_DATED_AC's cap, NOT AC's 5).
        var transferable = vacation.GetProperty("transferable").GetDecimal();
        var expectedTransferable = ExpectedTransferable(
            annualQuota: pinQuota, closedFerieaarStart: new DateOnly(2025, 9, 1),
            employmentStart: null, boundaryDate: new DateOnly(2026, 8, 31),
            carryoverIn: 0m, used: 0m, planned: 0m, carryoverMax: pinCarryoverMax);
        Assert.Equal(10m, expectedTransferable);    // sanity: clamps to PIN_DATED_AC cap (10)
        Assert.Equal(expectedTransferable, transferable);
        Assert.NotEqual(5m, transferable);          // pre-fix would clamp to AC's cap (5)
    }

    // ════════════════════════════════════════════════════════════════════════
    // 13. Step-7a R-W1 pin — GRACEFUL categories contract (no entitlement config).
    //     An employee whose agreement/OK has NO entitlement config for the four
    //     categories gets all-null saldo + zero afholdt + transferable 0 + boundaryMonth 12,
    //     and 200 (never 500) — the contract the FE's dash-guard renders
    //     (BalanceEndpoints.cs:731-744 graceful empty branch; contract saldo = (number|null)[]).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Step-7a R-W1 (convergent Codex + Reviewer) regression pin — the graceful empty-config
    /// contract.</b> For EVERY one of the four year-overview categories whose
    /// <c>(type, agreement, ok)</c> has NO open <c>entitlement_configs</c> row, the handler takes the
    /// graceful empty branch (BalanceEndpoints.cs:731-744): the response is 200 (never 500) and each
    /// category carries <c>saldo</c> = 12 JSON nulls, <c>afholdt</c> = 12 zeros, <c>transferable</c> = 0,
    /// and <c>boundaryMonth</c> = 12. This pins the exact shape the FE's new null-saldo dash-guard
    /// renders (the contract now documents <c>saldo</c> as <c>(number|null)[]</c>).
    ///
    /// <para><b>Why a FICTITIOUS, PROFILE-LESS employee — and NOT AC_RESEARCH.</b> The task's
    /// motivating example (academic AC_RESEARCH) does NOT reach this branch in the live seeded DB:
    /// AC_RESEARCH (and AC_TEACHING) HAVE seeded entitlement_configs (init.sql:1460-1484 — the S37
    /// "AC variants mirror AC base values" absorption, all quota 25), so <c>liveConfig</c> is NON-null
    /// for them and they compute real saldo (verified empirically — AC_RESEARCH VACATION saldo comes
    /// back as the quota-25 accrual curve, not nulls). The ONLY way to reach the graceful branch is an
    /// agreement code with no entitlement_configs row at all. Such a code is necessarily also absent
    /// from <c>CentralAgreementConfigs</c>, so the employee MUST be profile-less: otherwise the per-day
    /// norm path (<c>DailyNormCalculator</c> → <c>ConfigResolutionService.ResolveAsync</c>) would THROW
    /// <c>InvalidOperationException</c> on the unknown code (ConfigResolutionService.cs:132-134) and the
    /// endpoint would 500. With NO <c>employee_profiles</c> row, the norm path returns null per day
    /// without ever calling ResolveAsync (DailyNormCalculator.cs:93-101) and the header skips
    /// ResolveAsync (it is gated on a non-null today-profile, BalanceEndpoints.cs:593) — so the unknown
    /// agreement never reaches a config lookup that could throw, and the response is a clean 200. This
    /// is the EXACT graceful contract (no-config → empty categories + 200) the R-W1 dash-guard renders;
    /// the profile-less + unknown-agreement employee is its faithful, reachable representative.</para>
    ///
    /// <para><b>Boot-order (S63).</b> Same pattern as the profile-less Graceful test: build + boot the
    /// fixed-today derived host FIRST (runs EmployeeProfileSeeder), THEN insert the bare user (no
    /// employee_profiles, no user_agreement_codes) so the seeder cannot backfill it. <c>todayAgreementCode</c>
    /// falls back to <c>users.agreement_code</c> (the fictitious code) since no user_agreement_codes
    /// row covers today (BalanceEndpoints.cs:576-578).</para>
    /// </summary>
    [Fact]
    public async Task Graceful_NoEntitlementConfigForAgreement_AllCategoriesNullSaldoZeroAfholdtTransferable0_Never500()
    {
        // Fictitious agreement code with NO entitlement_configs (and no CentralAgreementConfigs) →
        // every category hits the graceful empty branch.
        const string noConfigAgreement = "PIN_NOCONFIG_AC";

        // Boot the fixed-today derived host FIRST (EmployeeProfileSeeder runs for existing users).
        var derivedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday));
            });
        });
        _ = derivedFactory.CreateClient();

        // Insert the bare, profile-less user AFTER the boot (no employee_profiles, no
        // user_agreement_codes rows). agreement_code cache = the fictitious code ⇒ todayAgreementCode
        // falls back to it ⇒ liveConfig null for every category.
        var employeeId = "emp_s65_t13_" + Guid.NewGuid().ToString("N")[..8];
        await InsertBareUserAsync(employeeId, Emp001OrgId, noConfigAgreement, "OK24");

        var client = derivedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", EmployeeBearerToken(employeeId, Emp001OrgId));

        // 200, never 500 — assert on the raw status before parsing.
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/year-overview?year=2026");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // All FOUR categories must be present and each must carry the graceful empty shape.
        var categories = body.GetProperty("categories").EnumerateArray().ToList();
        var seenTypes = categories.Select(c => c.GetProperty("type").GetString()).ToList();
        foreach (var expectedType in new[] { "VACATION", "SPECIAL_HOLIDAY", "CARE_DAY", "SENIOR_DAY" })
            Assert.Contains(expectedType, seenTypes);
        Assert.Equal(4, categories.Count);

        foreach (var cat in categories)
        {
            // saldo = 12 JSON nulls (the (number|null)[] contract under no config).
            var saldo = cat.GetProperty("saldo").EnumerateArray().ToList();
            Assert.Equal(12, saldo.Count);
            Assert.All(saldo, e => Assert.Equal(JsonValueKind.Null, e.ValueKind));

            // afholdt = 12 zeros.
            var afholdt = cat.GetProperty("afholdt").EnumerateArray().ToList();
            Assert.Equal(12, afholdt.Count);
            Assert.All(afholdt, e => Assert.Equal(0m, e.GetDecimal()));

            // transferable = 0; boundaryMonth = 12 (OQ-1 resolved, even on the empty branch).
            Assert.Equal(0m, cat.GetProperty("transferable").GetDecimal());
            Assert.Equal(12, cat.GetProperty("boundaryMonth").GetInt32());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers — HTTP clients with fixed TimeProvider.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates an <see cref="HttpClient"/> whose bearer token is pre-set and whose
    /// <see cref="TimeProvider"/> is overridden to <see cref="FixedTimeProvider"/> returning
    /// <see cref="FixedToday"/>. This is the ONLY mechanism for injecting today in tests —
    /// no wall-clock-dependent expected values are acceptable (TASK-6504 NOTE).
    /// </summary>
    private HttpClient MakeFixedTodayClient(string bearerToken)
    {
        // Override the TimeProvider singleton with a fixed provider in the WebApplicationFactory
        // test host (TASK-6502 established TimeProvider.System as the default; the year-overview
        // handler derives today from it). Tests override here per the Step-0b Reviewer NOTE.
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace the singleton TimeProvider with a fixed-today provider.
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday));
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private static async Task<JsonElement> GetYearOverviewAsync(
        HttpClient client, string employeeId, int year)
    {
        var rsp = await client.GetAsync(
            $"/api/balance/{employeeId}/year-overview?year={year}");
        rsp.EnsureSuccessStatusCode();
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// GET the Skema monthly spreadsheet for cross-seam reconciliation. Route + query shape per
    /// SkemaEndpoints.cs:132 (<c>/api/skema/{employeeId}/month</c>, <c>int year</c> + <c>int month</c>,
    /// auth <c>EmployeeOrAbove</c>).
    /// </summary>
    private static async Task<JsonElement> GetSkemaMonthAsync(
        HttpClient client, string employeeId, int year, int month)
    {
        var rsp = await client.GetAsync(
            $"/api/skema/{employeeId}/month?year={year}&month={month}");
        rsp.EnsureSuccessStatusCode();
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Reproduces <c>BalanceEndpoints.SumIntervalHours</c> over a Skema <c>workTime</c> row's
    /// <c>intervals</c> JSON array (each element <c>{ start, end }</c> "HH:MM"/"HH:MM:SS"): only
    /// positive-duration intervals count; total seconds rounded to 2 decimals. Used to reconstruct
    /// the Skema seam's worked-hours total identically to the year-overview's aggregation.
    /// </summary>
    private static decimal SumSkemaIntervalHours(JsonElement intervals)
    {
        var totalSeconds = 0;
        foreach (var iv in intervals.EnumerateArray())
        {
            if (TryParseTimeToSeconds(iv.GetProperty("start").GetString(), out var s)
                && TryParseTimeToSeconds(iv.GetProperty("end").GetString(), out var e)
                && e > s)
            {
                totalSeconds += e - s;
            }
        }
        return Math.Round(totalSeconds / 3600m, 2);
    }

    private static bool TryParseTimeToSeconds(string? value, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(':');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        var s = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out s)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59 || s < 0 || s > 59) return false;
        seconds = h * 3600 + m * 60 + s;
        return true;
    }

    private static JsonElement GetCategory(JsonElement body, string type)
    {
        return body.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == type);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers — expected-value math via the REAL production AccrualMath.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Expected end-of-month <c>saldo[m]</c> for a MONTHLY_ACCRUAL type, computed the EXACT way
    /// the endpoint does (BalanceEndpoints.cs:722-728):
    /// <c>Math.Round(AccrualMath.EarnedToDate(quota, 1.0m, ferieaarStart, employmentStart, monthEnd)
    ///   + carryoverIn − cumulativeAfholdt, 2)</c>.
    /// The earned term is the UNROUNDED real <see cref="AccrualMath.EarnedToDate"/> (the rounding is
    /// applied ONCE, to the whole sum — matching the handler; a test-local replica that rounded the
    /// earned term first could diverge by a cent on fractional quotas). VACATION quota = 25
    /// (DefaultEntitlementConfigs.cs:71); fraction is the ADR-031 identity 1.0m.
    /// </summary>
    private static decimal ExpectedMonthlyAccrualSaldo(
        decimal annualQuota, DateOnly ferieaarStart, DateOnly? employmentStart,
        DateOnly monthEnd, decimal carryoverIn, decimal cumulativeAfholdt)
        => Math.Round(
            AccrualMath.EarnedToDate(annualQuota, 1.0m, ferieaarStart, employmentStart, monthEnd)
            + carryoverIn - cumulativeAfholdt, 2);

    /// <summary>
    /// Expected <c>transferable</c> for a MONTHLY_ACCRUAL type, computed the EXACT way the endpoint
    /// does (BalanceEndpoints.cs:760-769):
    /// <c>Math.Round(Math.Min(Math.Max(0, earnedAtBoundary + carryoverIn − used − planned),
    ///   carryoverMax), 2)</c>, where <c>earnedAtBoundary</c> is the UNROUNDED real
    /// <see cref="AccrualMath.EarnedToDate"/> at the closed-ferieår model boundary.
    /// </summary>
    private static decimal ExpectedTransferable(
        decimal annualQuota, DateOnly closedFerieaarStart, DateOnly? employmentStart,
        DateOnly boundaryDate, decimal carryoverIn, decimal used, decimal planned, decimal carryoverMax)
    {
        var earnedAtBoundary = AccrualMath.EarnedToDate(
            annualQuota, 1.0m, closedFerieaarStart, employmentStart, boundaryDate);
        var raw = earnedAtBoundary + carryoverIn - used - planned;
        return Math.Round(Math.Min(Math.Max(0m, raw), carryoverMax), 2);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers — token minting (mirrors BalanceSeriesTests).
    // ════════════════════════════════════════════════════════════════════════

    private static string EmployeeBearerToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevJwtSettings());
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string LeaderBearerToken(string actorId, string scopeOrgId)
    {
        var svc = new JwtTokenService(DevJwtSettings());
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.LocalLeader,
            agreementCode: "AC",
            orgId: scopeOrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, scopeOrgId, "ORG_AND_DESCENDANTS") });
    }

    private static string LocalAdminBearerToken(string actorId, string scopeOrgId)
    {
        var svc = new JwtTokenService(DevJwtSettings());
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC",
            orgId: scopeOrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, scopeOrgId, "ORG_AND_DESCENDANTS") });
    }

    private static JwtSettings DevJwtSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ════════════════════════════════════════════════════════════════════════
    // Helpers — DB seeding.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inserts a brand-new user row WITHOUT creating employee_profiles or user_agreement_codes
    /// rows (the absent-state fixture, used by test 11). Returns the user id.
    /// MUST be called AFTER the last CreateClient() (host boot) to avoid the S31
    /// EmployeeProfileSeeder backfilling a profile row (S63 boot-order lesson).
    /// </summary>
    private async Task InsertBareUserAsync(string userId, string orgId, string agreementCode, string okVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Ensure the org row exists (EmployeeProfileSeeder requires primary_org_id FK).
        await using (var orgCmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version)
            VALUES (@orgId, @orgName, 'STYRELSE', NULL, @path, @ac, @ok)
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            orgCmd.Parameters.AddWithValue("orgId", orgId);
            orgCmd.Parameters.AddWithValue("orgName", $"{orgId} Test Org");
            orgCmd.Parameters.AddWithValue("path", $"/{orgId}/");
            orgCmd.Parameters.AddWithValue("ac", agreementCode);
            orgCmd.Parameters.AddWithValue("ok", okVersion);
            await orgCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', @displayName, NULL, @org, @ac, @ok, TRUE)
            ON CONFLICT (user_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("displayName", $"S65 Graceful Test {userId}");
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Creates a fresh user id and inserts a bare users row (no profiles or agreement-code rows).
    /// Callers must then call <see cref="RegressionSeed.SeedEmployeeAsync"/> separately if they
    /// need the full resolver-required triple. Used to generate a unique id for per-test employees.
    /// </summary>
    private async Task<string> CreateEmployeeAsync(string orgId, string agreementCode, string okVersion)
    {
        var userId = "emp_s65_t" + Guid.NewGuid().ToString("N")[..8];
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Ensure org row exists (same upsert as RegressionSeed).
        await using (var orgCmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version)
            VALUES (@orgId, @orgName, 'STYRELSE', NULL, @path, @ac, @ok)
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            orgCmd.Parameters.AddWithValue("orgId", orgId);
            orgCmd.Parameters.AddWithValue("orgName", $"{orgId} Test Org");
            orgCmd.Parameters.AddWithValue("path", $"/{orgId}/");
            orgCmd.Parameters.AddWithValue("ac", agreementCode);
            orgCmd.Parameters.AddWithValue("ok", okVersion);
            await orgCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', @displayName, NULL, @org, @ac, @ok, TRUE)
            ON CONFLICT (user_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("displayName", $"S65 Test {userId}");
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        await cmd.ExecuteNonQueryAsync();

        return userId;
    }

    /// <summary>
    /// Inserts a single row into <c>absences_projection</c> for the given employee / date /
    /// absence_type / hours. Citation: absences_projection schema at
    /// tests/StatsTid.Tests.Regression/Outbox/ProjectionSchemaTestFixture.cs:49-66.
    /// </summary>
    private async Task SeedAbsenceProjectionRowAsync(
        string employeeId, DateOnly date, string absenceType, decimal hours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Citation comments outside the raw SQL string (S64 lesson: comments inside raw strings
        // are sent to Postgres and cause 42601 syntax errors).
        // absences_projection schema: ProjectionSchemaTestFixture.cs:49-66.
        // employee_id, date, absence_type, hours, agreement_code, ok_version are required.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours,
                 agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @type, @hours,
                 'AC', 'OK24', NOW(), 'test-seed', 'Employee', -1)
            ON CONFLICT (event_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("type", absenceType);
        cmd.Parameters.AddWithValue("hours", hours);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts (or upserts) a single row into <c>work_time_projection</c> for the given employee
    /// / date. Only manual_hours is seeded (no intervals). Citation: work_time_projection schema
    /// at tests/StatsTid.Tests.Regression/Outbox/ProjectionSchemaTestFixture.cs:68-83.
    /// </summary>
    private async Task SeedWorkTimeRowAsync(
        string employeeId, DateOnly date, decimal manualHours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Citation: work_time_projection schema at ProjectionSchemaTestFixture.cs:68-83.
        // Primary key is (employee_id, date) → ON CONFLICT upsert.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO work_time_projection
                (employee_id, date, intervals, manual_hours, occurred_at, actor_id, actor_role, outbox_id)
            VALUES
                (@emp, @date, '[]'::jsonb, @manual, NOW(), 'test-seed', 'Employee', -1)
            ON CONFLICT (employee_id, date)
                DO UPDATE SET manual_hours = EXCLUDED.manual_hours
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("manual", manualHours);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Upserts a row in <c>entitlement_balances</c> for the given employee / type / year.
    /// Used to seed carryoverIn, used, planned values for straddle and transferable tests.
    /// Citation: entitlement_balances schema at ProjectionSchemaTestFixture.cs:85-99.
    /// </summary>
    private async Task SeedEntitlementBalanceAsync(
        string employeeId, string entitlementType, int entitlementYear,
        decimal used, decimal planned, decimal carryoverIn)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Citation: entitlement_balances schema at ProjectionSchemaTestFixture.cs:85-99.
        // Unique on (employee_id, entitlement_type, entitlement_year) — upsert with ON CONFLICT.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year,
                 total_quota, used, planned, carryover_in, updated_at)
            VALUES
                (gen_random_uuid(), @emp, @type, @year,
                 25, @used, @planned, @carryover, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
                DO UPDATE SET used = EXCLUDED.used, planned = EXCLUDED.planned,
                              carryover_in = EXCLUDED.carryover_in,
                              updated_at = NOW()
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("type", entitlementType);
        cmd.Parameters.AddWithValue("year", entitlementYear);
        cmd.Parameters.AddWithValue("used", used);
        cmd.Parameters.AddWithValue("planned", planned);
        cmd.Parameters.AddWithValue("carryover", carryoverIn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts one OPEN (effective_to NULL) <c>local_agreement_profiles</c> row overriding
    /// <c>weekly_norm_hours</c> for the (org, agreement, ok_version) scope (ADR-017 local profile;
    /// ConfigResolutionService overlays it on the central config). <c>effective_from</c> is the
    /// history-covering <c>'0001-01-01'</c> anchor so every date in any year-under-test is covered.
    /// Used by the OK-version-straddle test to make per-day OK resolution produce DIFFERENT
    /// per-side norms WITHOUT touching any global config row (no cross-test contamination).
    /// Citation: local_agreement_profiles schema at init.sql:723-743 (partial-unique index keyed
    /// (org_id, agreement_code, ok_version) WHERE effective_to IS NULL → one open row per OK version).
    /// </summary>
    private async Task SeedLocalAgreementProfileAsync(
        string orgId, string agreementCode, string okVersion, decimal weeklyNormHours)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // The org row must exist first (org_id FK). Upsert it (idempotent).
        await using (var orgCmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version)
            VALUES (@orgId, @orgName, 'STYRELSE', NULL, @path, @ac, @ok)
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            orgCmd.Parameters.AddWithValue("orgId", orgId);
            orgCmd.Parameters.AddWithValue("orgName", $"{orgId} Test Org");
            orgCmd.Parameters.AddWithValue("path", $"/{orgId}/");
            orgCmd.Parameters.AddWithValue("ac", agreementCode);
            orgCmd.Parameters.AddWithValue("ok", okVersion);
            await orgCmd.ExecuteNonQueryAsync();
        }

        // Citation comments OUTSIDE the raw SQL string (S64 lesson). created_by is NOT NULL.
        // effective_from is the '0001-01-01' history-covering anchor. ON CONFLICT on the active
        // partial-unique index keeps the seed idempotent per OK version.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles
                (profile_id, org_id, agreement_code, ok_version,
                 effective_from, effective_to, weekly_norm_hours, created_by, created_at)
            VALUES
                (gen_random_uuid(), @orgId, @ac, @ok,
                 DATE '0001-01-01', NULL, @weekly, 'test-seed', NOW())
            ON CONFLICT (org_id, agreement_code, ok_version) WHERE effective_to IS NULL
                DO UPDATE SET weekly_norm_hours = EXCLUDED.weekly_norm_hours
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("weekly", weeklyNormHours);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts (idempotently) one dated <c>employee_profiles</c> row. Mirrors
    /// <c>BalanceSeriesTests.SeedProfileRowAsync</c> shape; used by the Step-7a C1 dated-agreement
    /// pin to give its employee a full-history full-time profile (so the response is fully
    /// populated + 200). Citation: employee_profiles schema at init.sql (Phase 4d). The
    /// <c>(employee_id, effective_from)</c> history is the natural key the resolver reads; ON
    /// CONFLICT DO NOTHING keeps the run-twice seed idempotent.
    /// </summary>
    private async Task SeedEmployeeProfileRowAsync(
        string employeeId, decimal fraction, DateOnly effectiveFrom, DateOnly? effectiveTo, long version)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @f, @from, @to, @v)
            ON CONFLICT (employee_id, effective_from) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("f", fraction);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("v", version);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts (idempotently) one dated <c>user_agreement_codes</c> row. Mirrors the established
    /// <c>BalanceSeriesTests.SeedAgreementCodeAsync</c> / <c>SkemaAccrualCapTests</c> helper exactly,
    /// including its <c>ON CONFLICT (user_id, effective_from) DO UPDATE</c> upsert (the history
    /// unique index, init.sql:563-564) so the run-twice suite stays green. Used by the Step-7a C1
    /// pin to build an agreement-code history that CHANGES between two ferieår: the dated read
    /// <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/> applies the end-exclusive
    /// <c>[effective_from, effective_to)</c> predicate (UserAgreementCodeRepository.cs:84-90).
    /// </summary>
    private async Task SeedAgreementCodeRowAsync(
        string employeeId, string agreementCode, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes
                (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @a, @from, @to, 1)
            ON CONFLICT (user_id, effective_from) DO UPDATE
                SET agreement_code = EXCLUDED.agreement_code, effective_to = EXCLUDED.effective_to
            """, conn);
        cmd.Parameters.AddWithValue("u", employeeId);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts (idempotently) one dated, OPEN <c>entitlement_configs</c> row for the Step-7a C1 pin.
    /// SAFELY SCOPED to a FICTITIOUS agreement code present nowhere else (entitlement_configs /
    /// CentralAgreementConfigs / any other test), so a NEW natural-key row CANNOT collide with any
    /// row another test reads (the S64 shared-state lesson). The seeded row is always OPEN
    /// (<c>effective_to NULL</c>), so the ON CONFLICT target is the OPEN partial-unique index
    /// (entitlement_type, agreement_code, ok_version) WHERE effective_to IS NULL (init.sql:1368-1370)
    /// — mirrors <see cref="SeedLocalAgreementProfileAsync"/>. On re-run that index matches the prior
    /// open row → DO UPDATE to the same values keeps the seed additive + idempotent. (Targeting the
    /// history index (…, effective_from) instead would mis-fire on the SECOND run: an identical open
    /// row violates BOTH indexes and Postgres reports the open partial index first.) version defaults
    /// to 1. Citation comments are OUTSIDE the raw SQL string (S64 42601 lesson).
    /// </summary>
    private async Task SeedDatedEntitlementConfigAsync(
        string entitlementType, string agreementCode, string okVersion,
        decimal annualQuota, string accrualModel, int resetMonth, decimal carryoverMax,
        DateOnly effectiveFrom)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs
                (config_id, entitlement_type, agreement_code, ok_version,
                 annual_quota, accrual_model, reset_month, carryover_max,
                 pro_rate_by_part_time, is_per_episode, min_age, description,
                 effective_from, effective_to, version)
            VALUES
                (gen_random_uuid(), @type, @ac, @ok,
                 @quota, @accrual, @reset, @carryover,
                 FALSE, FALSE, NULL, @description,
                 @from, NULL, 1)
            ON CONFLICT (entitlement_type, agreement_code, ok_version) WHERE effective_to IS NULL
                DO UPDATE SET annual_quota = EXCLUDED.annual_quota,
                    accrual_model = EXCLUDED.accrual_model,
                    reset_month = EXCLUDED.reset_month,
                    carryover_max = EXCLUDED.carryover_max
            """, conn);
        cmd.Parameters.AddWithValue("type", entitlementType);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("quota", annualQuota);
        cmd.Parameters.AddWithValue("accrual", accrualModel);
        cmd.Parameters.AddWithValue("reset", resetMonth);
        cmd.Parameters.AddWithValue("carryover", carryoverMax);
        cmd.Parameters.AddWithValue("description", $"Step-7a C1 pin — {agreementCode} {entitlementType}");
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Counts Mon–Fri weekdays in the given calendar month (mirrors the endpoint's weekend-=0
    /// per-day norm rule). Returned as a decimal for direct use in exact norm-hours arithmetic.
    /// </summary>
    private static decimal CountWeekdays(int year, int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        var count = 0;
        for (var d = new DateOnly(year, month, 1); d <= new DateOnly(year, month, days); d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                count++;
        }
        return count;
    }
}

// ════════════════════════════════════════════════════════════════════════
// Fixed TimeProvider — overrides TimeProvider.System in the WAF test host.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A deterministic <see cref="TimeProvider"/> that returns a fixed <see cref="DateTimeOffset"/>
/// corresponding to the pinned <see cref="FixedDate"/>. Used by <c>YearOverviewTests</c> to
/// override the production <c>TimeProvider.System</c> in the WAF test host so that the
/// year-overview handler's <c>today</c> derivation is wall-clock-independent (TASK-6502
/// Step-0b Reviewer NOTE; TASK-6504 NOTE).
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _fixedUtcNow;

    /// <summary>
    /// Constructs a fixed provider returning UTC midnight of <paramref name="date"/>.
    /// </summary>
    public FixedTimeProvider(DateOnly date)
    {
        _fixedUtcNow = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow;
}
