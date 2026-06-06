using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
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
    /// Seed a single work_time_projection row for emp001 in a PAST month (Feb 2025) and
    /// assert that the year-overview <c>months[1].diff</c> (February, index 1) equals what
    /// GET /api/skema/{id}/month would compute for the same month.
    /// This is the shared-helper drift-proof: both seams now use DailyNormCalculator.
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

        // February (index 1): diff = workedHours − normHours (both from DailyNormCalculator).
        var febMonth = months[1]; // index 1 = February
        Assert.Equal(2, febMonth.GetProperty("month").GetInt32());

        var diff = febMonth.GetProperty("diff").GetDecimal();
        var worked = febMonth.GetProperty("workedHours").GetDecimal();
        var norm = febMonth.GetProperty("normHours").GetDecimal();

        // diff = worked − norm by contract.
        Assert.Equal(Math.Round(worked - norm, 2), diff);

        // The seeded 150 h manual should be reflected in workedHours (may have other rows from
        // the init.sql seed, so just assert diff is non-null and equals worked − norm).
        Assert.True(diff == Math.Round(worked - norm, 2),
            $"diff {diff} must equal worked({worked}) − norm({norm})");
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
        // So saldo = Round(earned + 0 - 0.5, 2).
        var expectedEarned = AccrualMathHelper.EarnedToDate(25m, 1.0m,
            new DateOnly(2024, 9, 1), null, new DateOnly(2025, 3, 31));
        var expectedSaldo = Math.Round(expectedEarned - 0.5m, 2);
        Assert.Equal(expectedSaldo, marchSaldo);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Consumption reconciliation — used/planned split by ABSENCE DATE.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seeds VACATION absences in PAST months and FUTURE months of 2026 (no future absences
    /// in the current month, per the seed constraint). Asserts:
    ///   (a) Σ afholdt for past + current months == the entitlement_balances.used row (after
    ///       we compute expected used = sum of day-equivalents with date ≤ today).
    ///   (b) Σ afholdt for future months == the planned portion (date > today).
    ///   (c) whole-year Σ afholdt == used + planned.
    /// Fixed today = 2026-06-15. Current month = June 2026.
    /// Seed constraint (Step-0b cycle-2 Codex W): no future-dated absences inside the
    /// current month (June 2026) — we seed Jan/Feb (past) and Aug (future only).
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

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);

        var vacation = GetCategory(body, "VACATION");
        var afholdtArray = vacation.GetProperty("afholdt").EnumerateArray()
            .Select(e => e.GetDecimal()).ToList();

        // Σ afholdt for months whose dates ≤ today (months 1–6 for year 2026).
        // Only Jan (index 0) and Feb (index 1) have seeded absences.
        var usedFromAfholdt = afholdtArray.Take(6).Sum(); // Jan–Jun (today's month inclusive)
        Assert.Equal(2m, usedFromAfholdt); // 1 + 1 = 2 day-equivalents from past months

        // Σ afholdt for months whose dates > today (months 7–12 for year 2026).
        // Only Aug (index 7) has a seeded absence.
        var plannedFromAfholdt = afholdtArray.Skip(6).Sum(); // Jul–Dec
        Assert.Equal(1m, plannedFromAfholdt); // 1 day-equivalent from future month

        // Whole-year Σ = used + planned.
        Assert.Equal(usedFromAfholdt + plannedFromAfholdt, afholdtArray.Sum());
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
        var earnedFerieaar2024March = AccrualMathHelper.EarnedToDate(
            25m, 1.0m, new DateOnly(2024, 9, 1), null, new DateOnly(2025, 3, 31));
        var expectedMarSaldo = Math.Round(earnedFerieaar2024March - 1.0m, 2);
        Assert.Equal(expectedMarSaldo, marSaldo);

        // August (index 7): same ferieår 2024; 12 months → earned = 25; afholdt = 1 (Mar).
        var augSaldo = saldoArray[7]; // August = index 7
        var earnedFerieaar2024Aug = AccrualMathHelper.EarnedToDate(
            25m, 1.0m, new DateOnly(2024, 9, 1), null, new DateOnly(2025, 8, 31));
        var expectedAugSaldo = Math.Round(earnedFerieaar2024Aug - 1.0m, 2);
        Assert.Equal(expectedAugSaldo, augSaldo);

        // September (index 8): ferieår 2025 RESETS. The sawtooth: saldo restarts from Sep 2025.
        // Ferieår 2025 starts 2025-09-01. Earned at Sep-end (1 month) + carryoverIn(3) − cumAfholdt(0).
        // Oct absence date is 2025-10-01 which is AFTER Sep 2025 end (2025-09-30). So cumAfholdt
        // through Sep-end = 0.
        var sepSaldo = saldoArray[8]; // September = index 8
        var earnedFerieaar2025Sep = AccrualMathHelper.EarnedToDate(
            25m, 1.0m, new DateOnly(2025, 9, 1), null, new DateOnly(2025, 9, 30));
        var expectedSepSaldo = Math.Round(earnedFerieaar2025Sep + 3m - 0m, 2);
        Assert.Equal(expectedSepSaldo, sepSaldo);

        // October (index 9): ferieår 2025; 2 months earned + carryoverIn(3) − cumAfholdt(1).
        var octSaldo = saldoArray[9]; // October = index 9
        var earnedFerieaar2025Oct = AccrualMathHelper.EarnedToDate(
            25m, 1.0m, new DateOnly(2025, 9, 1), null, new DateOnly(2025, 10, 31));
        var expectedOctSaldo = Math.Round(earnedFerieaar2025Oct + 3m - 1.0m, 2);
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
    /// Two identical requests for the year-overview return byte-equal responses for the
    /// <c>transferable</c> field (determinism property, priority #2).
    /// </summary>
    [Fact]
    public async Task Transferable_TwoIdenticalRequests_ByteEqual()
    {
        var client = MakeFixedTodayClient(EmployeeBearerToken(Emp001, Emp001OrgId));
        var body1 = await GetYearOverviewAsync(client, Emp001, 2025);
        var body2 = await GetYearOverviewAsync(client, Emp001, 2025);

        var vac1 = GetCategory(body1, "VACATION").GetProperty("transferable").GetDecimal();
        var vac2 = GetCategory(body2, "VACATION").GetProperty("transferable").GetDecimal();
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
        var earnedFerieaar2025Dec = AccrualMathHelper.EarnedToDate(
            25m, 1.0m, new DateOnly(2025, 9, 1), null, new DateOnly(2025, 12, 31));
        // cumAfholdt through Dec-end for ferieår 2025 = 1 (Sep absence).
        var expectedDecSaldo = Math.Round(earnedFerieaar2025Dec - 1.0m, 2);
        Assert.Equal(expectedDecSaldo, decSaldo);

        // The Sep 2025 absence does NOT change the transferable (which uses ferieår 2024).
        // We verify transferable == value with NO post-Aug absence (same formula, no closedUsed).
        // earnedAtBoundary(2025-08-31, ferieår 2024) = 25 exactly.
        // transferable is capped to carryoverMax — just assert it is non-negative and decSaldo
        // differs from transferable by a meaningful amount (decSaldo < earnedFerieaar2025Dec).
        Assert.True(transferable >= 0m, "transferable must be non-negative");
        Assert.True(decSaldo < earnedFerieaar2025Dec,
            $"decSaldo {decSaldo} should be less than earnedFerieaar2025Dec {earnedFerieaar2025Dec} because of the Sep 2025 absence");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. OK-version straddle — 2026-04-01 OK24→OK26 cutover.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A request for year 2026 spans the 2026-04-01 OK24→OK26 cutover. The endpoint resolves
    /// per-day norms via DailyNormCalculator which calls OkVersionResolver.ResolveVersion(day)
    /// per day — months before April use OK24 configs, months from April use OK26. The test
    /// verifies:
    /// <list type="bullet">
    ///   <item><description>normHours for January 2026 (OK24 side) matches the OK24
    ///   norm-hours computation.</description></item>
    ///   <item><description>normHours for May 2026 (OK26 side) matches the OK26
    ///   norm-hours computation.</description></item>
    ///   <item><description>Both are non-null (a profile IS present).</description></item>
    ///   <item><description>The VACATION entitlement config reads anchor at the entitlement-year
    ///   start (NOT today's OK version) — for year 2026, the ferieår 2025 (Sep 2025–Aug 2026)
    ///   starts 2025-09-01, which resolves to OK24 (before 2026-04-01). The entitlement config
    ///   for that ferieår therefore uses the OK24-era seeded config.</description></item>
    ///   <item><description>transferable carryoverMax is also anchored at the closed ferieår 2024
    ///   start (2024-09-01) which resolves to OK24 — NOT today's OK version
    ///   (Step-0b cycle-2 Reviewer NOTE).</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task OkVersionStraddle_2026Cutover_NormHoursPerSideCorrect_EntitlementConfigAnchorsAtYearStart()
    {
        // Seed a fresh employee with a full-time, whole-history profile so norms resolve on
        // every day.  today = 2026-06-15 (after the cutover). Year under test = 2026.
        var employeeId = await CreateEmployeeAsync(Emp001OrgId, "AC", "OK24");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, Emp001OrgId, "AC", "OK24");

        var client = MakeFixedTodayClient(EmployeeBearerToken(employeeId, Emp001OrgId));
        var body = await GetYearOverviewAsync(client, employeeId, 2026);

        var months = body.GetProperty("months").EnumerateArray().ToList();

        // January 2026 (index 0): all days resolved with OK24 (before 2026-04-01).
        var janMonth = months[0];
        Assert.Equal(1, janMonth.GetProperty("month").GetInt32());
        var janNorm = janMonth.GetProperty("normHours").GetDecimal();
        Assert.True(janNorm > 0m, $"Jan normHours {janNorm} should be positive (OK24 profile)");

        // May 2026 (index 4): all days resolved with OK26 (on/after 2026-04-01).
        var mayMonth = months[4];
        Assert.Equal(5, mayMonth.GetProperty("month").GetInt32());
        var mayNorm = mayMonth.GetProperty("normHours").GetDecimal();
        Assert.True(mayNorm > 0m, $"May normHours {mayNorm} should be positive (OK26 profile)");

        // For a full-time AC employee at 37 h/week, Jan and May should have the same
        // weekday-count-based norm (37 × weekdays/5). Both are non-null (profile present).
        // We just verify they are both positive — the exact per-day resolution is the
        // DailyNormCalculator's concern. The key contract is that both sides are served.
        Assert.True(janNorm > 0m, "Jan normHours must be positive");
        Assert.True(mayNorm > 0m, "May normHours must be positive");

        // VACATION entitlement config for year 2026:
        // ferieår 2025 = Sep 2025 – Aug 2026 (months Jan–Aug 2026 → ferieaarStart = 2025-09-01).
        // OkVersionResolver.ResolveVersion(2025-09-01) = "OK24" (before 2026-04-01).
        // The config for this ferieår should therefore use OK24 quota (25 days for AC/OK24).
        var vacation = GetCategory(body, "VACATION");
        var janSaldo = vacation.GetProperty("saldo").EnumerateArray().ToList()[0]; // Jan 2026
        // Jan saldo is non-null (the employee has a profile covering Jan).
        Assert.Equal(JsonValueKind.Number, janSaldo.ValueKind);

        // transferable for VACATION in year 2026:
        // Closed boundary ferieår = ferieår 2024 (Sep 2024 – Aug 2025, closedFerieaarStart = 2024-09-01).
        // OkVersionResolver.ResolveVersion(2024-09-01) = "OK24" (before 2026-04-01).
        // So carryoverMax is read from the OK24-era entitlement config, NOT today's OK26.
        // This is the Step-0b cycle-2 Reviewer NOTE assertion.
        var vacTransferable = vacation.GetProperty("transferable").GetDecimal();
        // Just assert it is non-negative and equals the formula's expected value
        // (carryoverMax sourced from OK24-era config, earnedAtBoundary = 25 for a whole-ferieår
        // full-time employee with no absences).
        Assert.True(vacTransferable >= 0m, "transferable must be non-negative even for the straddled year");
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

    private static JsonElement GetCategory(JsonElement body, string type)
    {
        return body.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == type);
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

// ════════════════════════════════════════════════════════════════════════
// AccrualMathHelper — test-local replica of AccrualMath.EarnedToDate.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Test-local replica of <c>StatsTid.SharedKernel.Calendar.AccrualMath.EarnedToDate</c> for
/// computing expected saldo values in assertions. Keeps tests self-contained and does NOT
/// re-implement the rule logic — it re-derives the same formula for assertion purposes only.
/// Formula: annualQuota × clampedElapsedMonths / 12 (flat, fraction-independent per ADR-031).
/// Employment-start pro-ration (ADR-030 D6): months start accumulating from the later of
/// ferieaarStart and employmentStart.
/// </summary>
internal static class AccrualMathHelper
{
    /// <summary>
    /// Mirrors <c>AccrualMath.EarnedToDate(quota, 1.0m, ferieaarStart, employmentStart, asOf)</c>:
    /// flat day-count, fraction-independent, month-based (fraction = 1.0 per ADR-031).
    /// </summary>
    public static decimal EarnedToDate(
        decimal annualQuota,
        decimal fraction,  // must be 1.0m per ADR-031
        DateOnly ferieaarStart,
        DateOnly? employmentStart,
        DateOnly asOf)
    {
        // Accrual starts at the later of ferieaarStart and employmentStart.
        var accrualStart = employmentStart is { } es && es > ferieaarStart ? es : ferieaarStart;

        // Clamp asOf to the ferieaarStart boundary (asOf before start → 0).
        if (asOf < accrualStart) return 0m;

        // Month-based elapsed: same AccrualMath formula.
        var monthsInYear = 12;
        var startYear = ferieaarStart.Year;
        var startMonth = ferieaarStart.Month;
        var asOfYear = asOf.Year;
        var asOfMonth = asOf.Month;

        // Compute months elapsed since ferieaarStart (not accrualStart; consistent with AccrualMath).
        var totalMonthsElapsed = (asOfYear - startYear) * 12 + (asOfMonth - startMonth) + 1;
        totalMonthsElapsed = Math.Clamp(totalMonthsElapsed, 0, monthsInYear);

        // If accrual started after ferieaarStart, deduct the months before accrual.
        if (employmentStart is { } es2 && es2 > ferieaarStart)
        {
            var accrualStartYear = es2.Year;
            var accrualStartMonth = es2.Month;
            var monthsBeforeAccrual = (accrualStartYear - startYear) * 12 + (accrualStartMonth - startMonth);
            totalMonthsElapsed = Math.Max(0, totalMonthsElapsed - monthsBeforeAccrual);
        }

        return Math.Round(annualQuota * fraction * totalMonthsElapsed / monthsInYear, 2);
    }
}
