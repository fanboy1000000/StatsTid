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
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14708-T (QUAL-231) — AC-231-1 and AC-231-8, through the two real hosts on ONE database.
///
/// <para>
/// <b>The story, in plain language.</b> Skema stores a sick day as <c>SICK_DAY</c> and a child's sick days as
/// <c>CHILD_SICK_DAY</c>, <c>CHILD_SICK_DAY_2</c>, <c>CHILD_SICK_DAY_3</c>. Before S147 the Rule Engine's list of
/// absences that count toward the norm spelled the child codes differently and had no <c>SICK_DAY</c> at all, so
/// payroll charged a full day of flex for each one while the Skema screen showed it as neutral (0 for the day).
/// These facts save the STORED literal through the real Skema save (the Backend host), then export the same month
/// through the real Payroll host with the real Rule Engine, and assert the flex closing moved by exactly what the
/// screen's total says.
/// </para>
///
/// <para>
/// <b>Expected values come from the spec, not from the code under test.</b> (1) The screen's rule
/// (<c>frontend/src/hooks/useSkema.ts:712-736</c>, <c>computeDayDiffs</c>): a day that carries any registration shows
/// <c>worked + absence - norm</c>; the month's total is the sum of those days. This file re-states that rule in
/// <see cref="SkemaPayrollScenario.ScreenTotal"/> and feeds it the SAME plan the fact seeds. (2) Statens
/// arbejdstidsaftale § 7 nr. 2, as quoted in the research (R1): "Dage med ret til fravær med løn medregnes med det antal
/// timer, den ansatte skulle have arbejdet den pågældende dag" (a paid absence day counts with the hours the employee
/// should have worked that day); its remark names sick days. For a full-time employee the day's planned hours are 7.4
/// (37 / 5). For HK and PROSA this is sourced. For AC-agreement staff no central text sets the credit (PAV 19.3.4): the
/// same rule is applied as a LABELLED DEFAULT BY ANALOGY, owner ruling D-8b (Q-1) = (a), 2026-10-29, so the AC fact
/// below pins a default, not a sourced rule.
/// </para>
///
/// <para>
/// <b>Why the screen total equals the payroll delta here.</b> The screen shows only days that carry a registration;
/// payroll counts the norm for EVERY weekday. Every June 2026 weekday below carries a registration (worked hours, or
/// the sick day), so the two coincide. June 2026 has 22 weekdays, no public holiday and starts on a Monday (no
/// context days from May); the full-time norm is 22 x 7.4 = 162.80 h.
/// </para>
///
/// <para>
/// Docker-gated (Testcontainers Postgres + full <c>init.sql</c>): <b>CI-pending</b>, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SickCodeVocabularyPayrollTests : IClassFixture<FlexLedgerHostFixture>, IAsyncLifetime
{
    private readonly FlexLedgerHostFixture _fx;
    private SkemaPayrollScenario _s = null!;

    public SickCodeVocabularyPayrollTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    public Task InitializeAsync()
    {
        _s = SkemaPayrollScenario.Create(_fx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _s.DisposeAsync();

    /// <summary>
    /// AC-231-1, the child's sick-day codes as STORED (kind F, Docker). A full-time employee works every June 2026
    /// weekday (7.40 h; Tuesday 9 June 8.40 h, so the screen total is not trivially zero) except Wednesday 10 June, on
    /// which the stored child code (<c>CHILD_SICK_DAY</c>, <c>CHILD_SICK_DAY_2</c> or <c>CHILD_SICK_DAY_3</c>, saved
    /// through the Skema save with 7.40 h) stands for the whole day. The month's flex closing equals the opening (0)
    /// plus the screen's total: screen days are 0 on the sick day and +1.00 on 9 June, so the closing is exactly
    /// <b>+1.00</b>.
    ///
    /// <para>
    /// The literals in the <c>InlineData</c> rows are the stored spelling written out on purpose (not the
    /// <c>AbsenceTypes</c> constants, whose values moved in S147): the fact proves what is STORED is what is credited.
    /// Each row is its own employee, because the child's-sick-day quota is per employee and these rows are not about it.
    /// </para>
    ///
    /// <para><b>Red conditions (F; Docker, so not run locally).</b> Before S147 the engine's credit list spelled the
    /// child codes <c>CHILD_SICK_1/2/3</c> and Skema stores <c>CHILD_SICK_DAY[_2|_3]</c>, so the stored absence was
    /// not credited: the sick day cost a full 7.40 h of flex and the month closed at <b>-6.40</b>, failing
    /// <c>closing == screen total (1.00)</c>. Mutation view: if <c>AbsenceClassification</c> drops a child code
    /// (the row for that code), the same assertion fails for that row.</para>
    /// </summary>
    [Theory]
    [InlineData("HK", "CHILD_SICK_DAY")]
    [InlineData("PROSA", "CHILD_SICK_DAY")]
    [InlineData("AC", "CHILD_SICK_DAY")]
    [InlineData("HK", "CHILD_SICK_DAY_2")]
    [InlineData("PROSA", "CHILD_SICK_DAY_2")]
    [InlineData("PROSA", "CHILD_SICK_DAY_3")]
    public async Task StoredChildSickCodes_ThroughSkemaAndPayroll_ChangeClosingByScreenTotal(string agreement, string storedCode)
    {
        var employeeId = await _s.NewEmployeeAsync(agreement, childSickEligible: true);
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 7.4m, workedOnSickDay: 0m, surplusOnTuesday: 1.0m);

        await _s.SaveAndSeedAsync(employeeId, agreement, plan, storedCode);
        await _s.SeedApprovedJuneAsync(employeeId, agreement);
        var response = await _s.ExportJuneAsync(employeeId, agreement);

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var screenTotal = SkemaPayrollScenario.ScreenTotal(plan);
        Assert.Equal(1.00m, screenTotal); // the screen's own figure: the sick day is 0, the surplus Tuesday +1.00
        Assert.Multiple(
            () => Assert.Equal((decimal?)0m, result.OpeningFlexBalance),
            () => Assert.Equal((decimal?)screenTotal, result.ClosingFlexBalance));
    }

    /// <summary>
    /// AC-231-1 / AC-231-8, <c>SICK_DAY</c> as STORED, for HK and PROSA (kind F, Docker; the credit is SOURCED here:
    /// Statens arbejdstidsaftale § 7 nr. 2 and its remark, which names sick days). Same plan as the child-code fact, with
    /// <c>SICK_DAY</c> 7.40 h on Wednesday 10 June: the closing is the screen total, <b>+1.00</b>.
    ///
    /// <para><b>Red conditions (F; Docker).</b> Before S147 the engine's credit list had no <c>SICK_DAY</c> at all: the
    /// stored sick day was not credited, the day cost 7.40 h of flex and the month closed at <b>-6.40</b>, failing
    /// <c>closing == screen total (1.00)</c>. No "<c>SICK_DAY</c> divergence" fact exists any more: RR-1 folded the
    /// credit into S147.</para>
    /// </summary>
    [Theory]
    [InlineData("HK")]
    [InlineData("PROSA")]
    public async Task StoredSickDay_ThroughSkemaAndPayroll_ChangesClosingByScreenTotal(string agreement)
    {
        var employeeId = await _s.NewEmployeeAsync(agreement);
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 7.4m, workedOnSickDay: 0m, surplusOnTuesday: 1.0m);

        await _s.SaveAndSeedAsync(employeeId, agreement, plan, "SICK_DAY");
        await _s.SeedApprovedJuneAsync(employeeId, agreement);
        var response = await _s.ExportJuneAsync(employeeId, agreement);

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var screenTotal = SkemaPayrollScenario.ScreenTotal(plan);
        Assert.Equal(1.00m, screenTotal);
        Assert.Multiple(
            () => Assert.Equal((decimal?)0m, result.OpeningFlexBalance),
            () => Assert.Equal((decimal?)screenTotal, result.ClosingFlexBalance));
    }

    /// <summary>
    /// AC-231-1 / AC-231-8, <c>SICK_DAY</c> for an AC-agreement employee (kind F, Docker). The same plan and the same
    /// expected closing as the HK and PROSA fact: <b>+1.00</b>.
    ///
    /// <para>
    /// <b>The AC credit is a LABELLED DEFAULT BY ANALOGY, not a sourced rule.</b> No central AC text sets what a sick day
    /// counts as (PAV 19.3.4: "Arbejdstid som begreb er ikke nærmere fastlagt"); PAV 19.5.9 requires sick days to be
    /// counted into the flex account and leaves the amount to the local flex agreement. Statens arbejdstidsaftale § 7 nr. 2
    /// is applied by analogy, owner ruling D-8b (Q-1) = (a), 2026-10-29; a local agreement may set another rule (that
    /// per-institution parameter is S148's). If Q-1 is ever ruled differently this fact is the one to re-pin.
    /// </para>
    ///
    /// <para><b>Red conditions (F; Docker).</b> As for HK: before S147 the stored <c>SICK_DAY</c> was not credited for
    /// any agreement, so the AC month closed at <b>-6.40</b>, failing <c>closing == screen total (1.00)</c>.</para>
    /// </summary>
    [Fact]
    public async Task StoredSickDay_AcAgreementEmployee_CreditedByTheSameRule()
    {
        var employeeId = await _s.NewEmployeeAsync("AC");
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 7.4m, workedOnSickDay: 0m, surplusOnTuesday: 1.0m);

        await _s.SaveAndSeedAsync(employeeId, "AC", plan, "SICK_DAY");
        await _s.SeedApprovedJuneAsync(employeeId, "AC");
        var response = await _s.ExportJuneAsync(employeeId, "AC");

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var screenTotal = SkemaPayrollScenario.ScreenTotal(plan);
        Assert.Equal(1.00m, screenTotal);
        Assert.Multiple(
            () => Assert.Equal((decimal?)0m, result.OpeningFlexBalance),
            () => Assert.Equal((decimal?)screenTotal, result.ClosingFlexBalance));
    }

    /// <summary>
    /// RR-3, a PARTIAL sick day (kind F, Docker). On Wednesday 10 June the employee works 3.00 h and registers the day's
    /// remaining 4.40 h as <c>SICK_DAY</c> (saved through Skema); every other weekday is 7.40 h worked. The sick day
    /// closes with a delta of 0 (3.00 + 4.40 - 7.40), so the month's flex closing is exactly <b>0.00</b>, the screen
    /// total, for HK, PROSA and AC (the AC credit is the labelled default, as above).
    ///
    /// <para>
    /// <b>The reading, recorded.</b> The research (RR-3) reads § 7 nr. 2 as <c>max(worked, planned)</c> for the day: the
    /// paid-absence hours count toward the day's planned hours; the implementation credits the stored sick hours and the
    /// Skema save already caps a day's absence at the day's norm. <b>Not pinned here:</b> the over-credit case (hours worked
    /// plus a sick registration that together exceed the norm) is a QUAL-231 residual for S148.
    /// </para>
    ///
    /// <para><b>Red conditions (F; Docker).</b> Before S147 the 4.40 h of stored sick time was not credited: the day closed at
    /// 3.00 - 7.40 = -4.40 and the month at <b>-4.40</b>, failing <c>closing == 0.00</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("HK")]
    [InlineData("PROSA")]
    [InlineData("AC")]
    public async Task PartialSickDay_WorkedPlusRemainingSickHours_ClosingDeltaZero(string agreement)
    {
        var employeeId = await _s.NewEmployeeAsync(agreement);
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 4.4m, workedOnSickDay: 3.0m, surplusOnTuesday: 0m);

        await _s.SaveAndSeedAsync(employeeId, agreement, plan, "SICK_DAY");
        await _s.SeedApprovedJuneAsync(employeeId, agreement);
        var response = await _s.ExportJuneAsync(employeeId, agreement);

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var screenTotal = SkemaPayrollScenario.ScreenTotal(plan);
        Assert.Equal(0.00m, screenTotal); // the partial day is 3.00 + 4.40 - 7.40 = 0 on the screen
        Assert.Multiple(
            () => Assert.Equal((decimal?)0m, result.OpeningFlexBalance),
            () => Assert.Equal((decimal?)screenTotal, result.ClosingFlexBalance));
    }
}

/// <summary>
/// S147 / TASK-14708-T — the two-host scenario the Skema-to-payroll facts share (this file and
/// <c>FlexCapCrossingTests</c>): the REAL Backend host (Skema save, with the real
/// <see cref="EntitlementValidationRule"/> behind a stub, as the sibling Skema suites do) and the REAL Payroll host
/// with the real Rule Engine (<see cref="FlexLedgerHostFixture.RealEngineHost"/>), both on the fixture's one database.
/// Absences are written ONLY by the Skema save; hours are raw projection rows (payroll reads its own hours).
/// </summary>
internal sealed class SkemaPayrollScenario : IAsyncDisposable
{
    internal const string OrgId = FlexLedgerHostFixture.OrgId;
    private const string Ok = "OK26";
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>June 2026: 22 weekdays, Monday 1 June to Tuesday 30 June, no public holiday, no context days from May.</summary>
    internal static readonly DateOnly June = new(2026, 6, 1);
    internal static readonly DateOnly JuneEnd = new(2026, 6, 30);

    /// <summary>The day the stored absence goes on: Wednesday 10 June 2026 (a weekday: S138, a zero-norm day is refused).</summary>
    internal static readonly DateOnly AbsenceDay = new(2026, 6, 10);

    /// <summary>The surplus day: Tuesday 9 June 2026.</summary>
    internal static readonly DateOnly SurplusDay = new(2026, 6, 9);

    /// <summary>The full-time day norm (37 h / 5) the screen shows for a working day.</summary>
    internal const decimal DayNorm = 7.4m;

    private static long _outboxSeq = 90_000;

    private readonly FlexLedgerHostFixture _fx;
    private readonly StatsTidWebApplicationFactory _backend;
    private readonly HttpClient _skema;

    private SkemaPayrollScenario(FlexLedgerHostFixture fx, StatsTidWebApplicationFactory backend, HttpClient skema)
    {
        _fx = fx;
        _backend = backend;
        _skema = skema;
    }

    internal static SkemaPayrollScenario Create(FlexLedgerHostFixture fx)
    {
        var backend = new StatsTidWebApplicationFactory(fx.ConnectionString);
        // Boot the host first: its start-up seeders backfill profile rows for users that exist at boot, and every
        // employee below is created AFTER the boot with its own complete rows.
        _ = backend.CreateClient();
        var stubbed = backend.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory())));
        return new SkemaPayrollScenario(fx, backend, stubbed.CreateClient());
    }

    public ValueTask DisposeAsync()
    {
        _skema.Dispose();
        _backend.Dispose();
        return ValueTask.CompletedTask;
    }

    // ── The plan: one record per June weekday, used for BOTH the seeding and the screen's total ──

    /// <summary>One June weekday as the grid would show it: hours worked, absence hours, and the day norm.</summary>
    internal sealed record PlannedDay(DateOnly Date, decimal Worked, decimal Absence, decimal Norm);

    /// <summary>
    /// Every June 2026 weekday at the day norm worked, except the absence day (Wednesday 10 June), which carries
    /// <paramref name="workedOnSickDay"/> worked and <paramref name="sickDayAbsenceHours"/> absence; the surplus Tuesday
    /// (9 June) carries <paramref name="surplusOnTuesday"/> extra worked hours.
    /// </summary>
    internal static IReadOnlyList<PlannedDay> FullMonthPlan(
        decimal sickDayAbsenceHours, decimal workedOnSickDay, decimal surplusOnTuesday)
    {
        var plan = new List<PlannedDay>();
        for (var d = June; d <= JuneEnd; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            if (d == AbsenceDay)
                plan.Add(new PlannedDay(d, workedOnSickDay, sickDayAbsenceHours, DayNorm));
            else if (d == SurplusDay)
                plan.Add(new PlannedDay(d, DayNorm + surplusOnTuesday, 0m, DayNorm));
            else
                plan.Add(new PlannedDay(d, DayNorm, 0m, DayNorm));
        }
        return plan;
    }

    /// <summary>
    /// The Skema screen's month total, re-stated from <c>useSkema.ts:712-736</c> (<c>computeDayDiffs</c>): a day that
    /// carries ANY registration (worked or absence) shows <c>round2(worked + absence - norm)</c>; a day with none is
    /// blank; the total is their sum. Written from the screen's rule, never read from the code under test.
    /// </summary>
    internal static decimal ScreenTotal(IEnumerable<PlannedDay> plan) =>
        plan.Where(day => day.Worked != 0m || day.Absence != 0m)
            .Sum(day => Math.Round(day.Worked + day.Absence - day.Norm, 2));

    // ── Seeding ──

    internal async Task<string> NewEmployeeAsync(string agreement, bool childSickEligible = false)
    {
        var employeeId = "EMP-S147-" + Guid.NewGuid().ToString("N")[..10];
        await using (var conn = _fx.Db.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(conn, employeeId, OrgId, agreement, Ok, partTimeFraction: 1.0m);
        }

        if (childSickEligible)
        {
            // The child's-sick-day gate is opt-in per employee (ADR-029): no row means ineligible. A row open from the
            // beginning of time covers any absence date.
            await using var conn = _fx.Db.Create();
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO employee_entitlement_eligibility (employee_id, entitlement_type, eligible, effective_from)
                VALUES (@e, 'CHILD_SICK', TRUE, '0001-01-01')
                """, conn);
            cmd.Parameters.AddWithValue("e", employeeId);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        return employeeId;
    }

    /// <summary>
    /// The absence day's absence goes through the real Skema save (the stored literal <paramref name="storedCode"/>); every
    /// worked hour is a raw projection row. The Skema save runs FIRST, before the month is approved (an approved month
    /// refuses the save).
    /// </summary>
    internal async Task SaveAndSeedAsync(
        string employeeId, string agreement, IReadOnlyList<PlannedDay> plan, string storedCode)
    {
        var absenceDay = plan.Single(d => d.Absence != 0m);
        var save = await SaveAbsencesAsync(employeeId, (absenceDay.Date, storedCode, absenceDay.Absence));
        Assert.True(save.Status == HttpStatusCode.OK, $"the Skema save of {storedCode} expected 200, got {(int)save.Status}: {save.Json}");

        foreach (var day in plan.Where(d => d.Worked != 0m))
            await SeedHoursAsync(employeeId, agreement, day.Date, day.Worked);
    }

    internal async Task SeedHoursAsync(string employeeId, string agreement, DateOnly date, decimal hours)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @agreement, @ok, FALSE, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("agreement", agreement);
        cmd.Parameters.AddWithValue("ok", Ok);
        cmd.Parameters.AddWithValue("outbox", Interlocked.Increment(ref _outboxSeq));
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    internal Task SeedApprovedJuneAsync(string employeeId, string agreement) =>
        SeedApprovedMonthAsync(employeeId, agreement, June, JuneEnd);

    internal async Task SeedApprovedMonthAsync(string employeeId, string agreement, DateOnly start, DateOnly end)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S147', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("agreement", agreement);
        cmd.Parameters.AddWithValue("ok", Ok);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>Removes the month's approval row (fixture-only: the "month reopened for a late change" state a correction starts from).</summary>
    internal async Task RemoveApprovalAsync(string employeeId, DateOnly start)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM approval_periods WHERE employee_id = @e AND period_start = @s", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("s", start);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ── HTTP: Skema save (Backend host) and payroll (Payroll host, real engine) ──

    internal async Task<(HttpStatusCode Status, string Json)> SaveAbsencesAsync(
        string employeeId, params (DateOnly Date, string Type, decimal Hours)[] absences)
    {
        var body = new
        {
            year = June.Year,
            month = June.Month,
            absences = absences.Select(a => new
            {
                date = a.Date.ToString("yyyy-MM-dd"),
                absenceType = a.Type,
                hours = a.Hours,
            }).ToArray(),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/skema/{employeeId}/save")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
        var response = await _skema.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal Task<(HttpStatusCode Status, string Json)> ExportJuneAsync(string employeeId, string agreement) =>
        _fx.PostAsync("/api/payroll/calculate-and-export", PayrollBody(employeeId, agreement), realEngine: true);

    internal Task<(HttpStatusCode Status, string Json)> RecalculateJuneAsync(string employeeId, string agreement)
    {
        var body = PayrollBody(employeeId, agreement);
        body["reason"] = "S147 flex-cap crossing fact";
        body["idempotencyToken"] = Guid.NewGuid();
        return _fx.PostAsync("/api/payroll/recalculate", body, realEngine: true);
    }

    private static Dictionary<string, object?> PayrollBody(string employeeId, string agreement) => new()
    {
        ["profile"] = new EmploymentProfile
        {
            EmployeeId = employeeId,
            AgreementCode = agreement,
            OkVersion = Ok,
            EmploymentCategory = "Standard",
            PartTimeFraction = 1.0m,
            OrgId = OrgId,
        },
        ["entries"] = Array.Empty<TimeEntry>(),
        ["absences"] = Array.Empty<AbsenceEntry>(),
        ["periodStart"] = June,
        ["periodEnd"] = JuneEnd,
    };

    internal static void AssertOk((HttpStatusCode Status, string Json) response) =>
        Assert.True(response.Status == HttpStatusCode.OK, $"expected 200, got {(int)response.Status}: {response.Json}");

    // ── Token (mirrors the sibling Skema regression suites) ──

    private static string MintEmployeeToken(string actorId)
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
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }

    // ── Rule-engine stub for the Skema save: drives the REAL EntitlementValidationRule over the HTTP seam ──

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
