using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StatsTid.Tests.Smoke;

/// <summary>
/// Smoke tests run against Docker Compose services.
/// Requires: docker compose up (from docker/ directory)
/// </summary>
public class SmokeTests
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string BackendUrl = "http://localhost:5100";
    private const string RuleEngineUrl = "http://localhost:5200";
    private const string OrchestratorUrl = "http://localhost:5300";
    private const string PayrollUrl = "http://localhost:5400";
    private const string ExternalUrl = "http://localhost:5500";
    private const string MockPayrollUrl = "http://localhost:5600";
    private const string MockExternalUrl = "http://localhost:5700";

    // Matches the dev signing key in docker-compose.yml
    private const string JwtSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private static string GenerateTestToken(string employeeId = "SMOKE001", string role = "GlobalAdmin", string orgId = "MIN01")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var scopes = JsonSerializer.Serialize(new[]
        {
            new { Role = role, OrgId = orgId, ScopeType = "GLOBAL" }
        });

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", employeeId),
                new Claim("role", role),
                new Claim("org_id", orgId),
                new Claim("agreement_code", "AC"),
                new Claim("scopes", scopes),
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "statstid",
            Audience = "statstid",
            SigningCredentials = credentials,
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(descriptor);
    }

    private HttpRequestMessage WithAuth(HttpRequestMessage request)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateTestToken());
        return request;
    }

    [Fact]
    public async Task AllServices_HealthCheck_ReturnsHealthy()
    {
        var urls = new[] { BackendUrl, RuleEngineUrl, OrchestratorUrl, PayrollUrl, ExternalUrl, MockPayrollUrl, MockExternalUrl };

        foreach (var url in urls)
        {
            var response = await _client.GetAsync($"{url}/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("healthy", body.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task RuleEngine_EvaluateNormCheck_ReturnsResult()
    {
        var request = new
        {
            ruleId = "NORM_CHECK_37H",
            profile = new
            {
                employeeId = "SMOKE001",
                agreementCode = "AC",
                okVersion = "OK24",
                weeklyNormHours = 37.0m,
                employmentCategory = "Standard",
                partTimeFraction = 1.0m
            },
            entries = new[]
            {
                new { employeeId = "SMOKE001", date = "2024-04-01", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-02", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-03", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-04", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-05", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
            },
            periodStart = "2024-04-01",
            periodEnd = "2024-04-07"
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{RuleEngineUrl}/api/rules/evaluate")
        {
            Content = JsonContent.Create(request)
        };
        WithAuth(httpRequest);

        var response = await _client.SendAsync(httpRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("NORM_CHECK_37H", result.GetProperty("ruleId").GetString());
    }

    [Fact]
    public async Task Backend_RegisterAndRetrieveTimeEntry()
    {
        // OrgScopeValidator deny-before-GLOBAL (S64 census): ValidateEmployeeAccessAsync
        // resolves the target employee BEFORE the GLOBAL scope short-circuit, so a GlobalAdmin
        // POSTing for a NEVER-SEEDED target gets 403 ("Target employee not found"), not 201.
        // Target an init.sql-seeded employee (emp001, org STY01, agreement_code AC) so the
        // GlobalAdmin end-to-end 201 write path is exercised.
        const string seededEmployeeId = "emp001";
        var registerRequest = new
        {
            employeeId = seededEmployeeId,
            date = "2024-04-01",
            hours = 7.4m,
            agreementCode = "AC",
            okVersion = "OK24"
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, $"{BackendUrl}/api/time-entries")
        {
            Content = JsonContent.Create(registerRequest)
        };
        WithAuth(postRequest);

        var registerResponse = await _client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{BackendUrl}/api/time-entries/{seededEmployeeId}");
        WithAuth(getRequest);

        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var entries = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(entries.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Backend_RegisterTimeEntry_UnknownTarget_Forbidden()
    {
        // OrgScopeValidator deny-before-GLOBAL (S64 census): even a GlobalAdmin is denied a
        // POST against a target employee that does not exist — ValidateEmployeeAccessAsync
        // resolves the target user and returns "Target employee not found" (403) BEFORE the
        // GLOBAL scope short-circuit. SMOKE002 is never seeded, so this pins the 403 contract.
        var registerRequest = new
        {
            employeeId = "SMOKE002",
            date = "2024-04-01",
            hours = 7.4m,
            agreementCode = "AC",
            okVersion = "OK24"
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, $"{BackendUrl}/api/time-entries")
        {
            Content = JsonContent.Create(registerRequest)
        };
        WithAuth(postRequest);

        var registerResponse = await _client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Forbidden, registerResponse.StatusCode);
    }

    [Fact]
    public async Task MockPayroll_ReceivesExport()
    {
        var payload = new { employeeId = "SMOKE003", wageType = "SLS_0110", hours = 37.0m };
        var response = await _client.PostAsJsonAsync($"{MockPayrollUrl}/api/payroll/receive", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
    }

    // ── S73 / TASK-7303 (R6) — the composed-stack backend→rule-engine hop probe ──
    //
    // THE test that would have caught the 2026-06-13 incident: the Backend's rule-engine
    // calls carried no bearer in the composed stack, so the rule engine answered 401 and
    // the Backend mapped that to a fail-closed 503 on the user's valid registration (and,
    // silently, on the compliance warnings — they just never loaded). TASK-7300 wired the
    // forwarding handler; this smoke test is the standing proof that the hop stays alive in
    // the COMPOSED stack at the real ports (the unit/regression suites stub the rule engine
    // and therefore could never see this wiring drift).
    //
    // THREE assertions, in a deliberate order:
    //   (1) HOP PROOF — a valid PARTIAL ferie (VACATION 3h, below the 7.4h day norm; VACATION
    //       is NOT full-day-only) persists 200 and is retrievable via the month GET. This leg
    //       and ONLY this leg discriminates the hop: VACATION routes through the rule engine's
    //       /api/rules/validate-entitlement, and the incident-verified fail-closed mapping
    //       turns a rule-engine non-2xx into a 503 here — so a 200 PROVES the rule engine
    //       answered 2xx through the forwarded bearer (R6 / Reviewer N4).
    //   (2) FULL-DAY RULE — a PARTIAL CARE_DAY (3.7h < the 7.4h norm) is rejected 422
    //       `absence_full_day_only` (TASK-7301). This is a BACKEND-LOCAL guard; it does NOT
    //       touch the rule engine and does NOT discriminate the hop (stated so the test is
    //       never misread or weakened by leg reordering).
    //   (3) COMPLIANCE HOP — the EU-WTD compliance-warnings endpoint (the SILENT half of the
    //       incident: warnings never loaded, no user-visible error) answers NON-503, i.e. its
    //       backend→rule-engine /api/rules/check-compliance hop is alive.
    //
    // SELF-ISOLATION (R6 / Step-0b B4 + Step-7a B3): the dev stack is lived-in and this test
    // re-runs. The absence save APPENDS an event-sourced projection row + increments the VACATION
    // `used` balance per save (it is NOT an upsert keyed on (employee,type,date) — verified
    // against AbsenceProjectionRepository.InsertAsync, keyed on a fresh event_id), and the VACATION
    // quota is per FERIEÅR. A prior implementation walked 10 fixed dates within a SINGLE far-future
    // year, so reruns shared that one year's 25-day quota and eventually depleted it (and a
    // same-minute rerun reused a date). The fix isolates by FERIEÅR: each run derives its
    // far-future YEAR from a MONOTONIC clock source — UtcNow whole-DAYS mapped into the
    // [2100, 8999] window — so the year only repeats after ~6900 DAYS (~19 years) of wall-clock,
    // and any two reruns within that span hit a DIFFERENT ferieår (replacing the Guid-hash-modulo-800
    // whose ~50% birthday collision after ~34 runs shared dates+quota — S73 Step-7a cycle-2 B3; and
    // the cycle-2 per-SECOND derivation whose modulo wrapped every ~6900 s ≈ 1h55m — cycle-3).
    // Same-day reruns share a year+date but only APPEND ~0.4 day each against the FRESH 25-day
    // ferieår quota (the save is append-not-upsert), so dozens of same-day reruns stay green; no
    // deletion of real data.
    [Fact]
    public async Task Backend_RuleEngineHop_AbsenceAndCompliance_ComposedStack()
    {
        // emp001 (Jesper Andersen, STY01, AC, OK24) — init.sql-seeded; the EmployeeProfileSeeder
        // backfills a full-time live profile (weekly_norm 37.0 / fraction 1.0 ⇒ 7.4h day norm)
        // covering all dates, so the partial-ferie and full-day legs are well-defined. (emp005
        // is deliberately avoided — it carries 2026-06-13 incident probe residue.)
        const string employeeId = "emp001";

        // Unique far-future YEAR per run (NOT a date within one shared year): each run hits a
        // DISTINCT future ferieår, so the 25-day VACATION quota never accumulates across runs and
        // the test is genuinely self-isolating on a lived-in stack (R6). Both the ferie and the
        // care-day rows live in the SAME month so a single month GET retrieves the ferie row.
        var (year, month, ferieDay, careDay) = PickUniqueFutureWeekdays();

        // ── (1) HOP PROOF: a valid partial ferie persists 200 + is retrievable ──
        var ferieSave = new
        {
            year,
            month,
            absences = new[] { new { date = $"{year:D4}-{month:D2}-{ferieDay:D2}", absenceType = "VACATION", hours = 3.0m } }
        };
        var ferieRequest = new HttpRequestMessage(HttpMethod.Post, $"{BackendUrl}/api/skema/{employeeId}/save")
        {
            Content = JsonContent.Create(ferieSave)
        };
        WithAuth(ferieRequest);
        var ferieResponse = await _client.SendAsync(ferieRequest);
        // 200 ⇒ the backend→rule-engine validate-entitlement hop answered 2xx through the
        // forwarded bearer (a broken hop fail-closes to 503 here — THE incident signature).
        Assert.Equal(HttpStatusCode.OK, ferieResponse.StatusCode);

        var monthRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{BackendUrl}/api/skema/{employeeId}/month?year={year}&month={month}");
        WithAuth(monthRequest);
        var monthResponse = await _client.SendAsync(monthRequest);
        Assert.Equal(HttpStatusCode.OK, monthResponse.StatusCode);

        var monthBody = await monthResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ferieDate = $"{year:D4}-{month:D2}-{ferieDay:D2}";
        var ferieRetrieved = monthBody.GetProperty("absences").EnumerateArray().Any(a =>
            a.GetProperty("absenceType").GetString() == "VACATION"
            && (a.GetProperty("date").GetString() ?? string.Empty).StartsWith(ferieDate, StringComparison.Ordinal));
        Assert.True(ferieRetrieved, $"Saved VACATION absence on {ferieDate} was not retrievable via the month GET.");

        // ── (2) FULL-DAY RULE (backend-local; does NOT discriminate the hop): a partial
        //         CARE_DAY (3.7h < the 7.4h day norm) is rejected 422 `absence_full_day_only`. ──
        var careSave = new
        {
            year,
            month,
            absences = new[] { new { date = $"{year:D4}-{month:D2}-{careDay:D2}", absenceType = "CARE_DAY", hours = 3.7m } }
        };
        var careRequest = new HttpRequestMessage(HttpMethod.Post, $"{BackendUrl}/api/skema/{employeeId}/save")
        {
            Content = JsonContent.Create(careSave)
        };
        WithAuth(careRequest);
        var careResponse = await _client.SendAsync(careRequest);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, careResponse.StatusCode);
        var careBody = await careResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_full_day_only", careBody.GetProperty("error").GetString());

        // ── (3) COMPLIANCE HOP (the SILENT half of the incident): the EU-WTD compliance
        //         endpoint's backend→rule-engine check-compliance hop is alive ⇒ NON-503. ──
        var complianceRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{BackendUrl}/api/compliance/{employeeId}/period?year={year}&month={month}");
        WithAuth(complianceRequest);
        var complianceResponse = await _client.SendAsync(complianceRequest);
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, complianceResponse.StatusCode);
    }

    /// <summary>
    /// Picks a UNIQUE-per-run far-future YEAR (so every run hits a distinct VACATION ferieår whose
    /// 25-day quota is fresh — no cross-run accumulation, no same-minute date collision; the R6/B3
    /// self-isolation contract) and two distinct WEEKDAYS in the SAME early month of that year (the
    /// ferie + care-day legs, retrievable via one month GET). Both days are guaranteed Mon–Fri
    /// (VACATION on a weekend would be a non-working-day 422, and a weekend's 0h norm would not
    /// trigger the full-day rule).
    /// </summary>
    private static (int Year, int Month, int FerieDay, int CareDay) PickUniqueFutureWeekdays()
    {
        // S73 Step-7a cycle-2 B3 — MONOTONIC year derivation, replacing the prior
        // Guid-hash-modulo-800 (which had only 800 buckets ⇒ ~50% birthday-paradox collision after
        // ~34 runs; colliding runs shared dates AND ferieår quota). DateTime.UtcNow.Ticks advances
        // ~10M/second, so any two SEQUENTIAL reruns more than one second apart map to a STRICTLY
        // INCREASING second-count and therefore a DIFFERENT year — consecutive runs always advance,
        // never collide. Quantising to whole seconds keeps the source coarse enough to be stable
        // within a single run while fine enough that no realistic rerun cadence repeats. The window
        // is 6900 wide ([2100, 9000) — inside DateOnly's year≤9999 limit and far beyond any dev
        // data). Per-DAY ticks ⇒ the year only repeats after ~6900 DAYS (~19 years) of wall-clock,
        // so no two reruns within ~19 years share a ferieår.
        const int WindowStart = 2100;
        const int WindowSpan = 6900; // 2100..8999 inclusive
        var dayTick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerDay;
        var year = WindowStart + (int)((ulong)dayTick % (ulong)WindowSpan);

        // Anchor on the 1st of January of that year, then advance to the first weekday. January 1
        // sits early enough that +0..+1 weekdays stay inside January (same month — one GET).
        var anchor = new DateOnly(year, 1, 1);
        var ferie = AddWeekdays(anchor, 0); // first weekday on/after Jan 1
        var care = AddWeekdays(ferie, 1);   // the next weekday (distinct date, same month)

        return (ferie.Year, ferie.Month, ferie.Day, care.Day);
    }

    private static DateOnly AddWeekdays(DateOnly start, int weekdays)
    {
        var d = start;
        var added = 0;
        while (added < weekdays)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                added++;
        }
        // Ensure the start itself is a weekday (the anchor is, but a +0 caller relies on it).
        while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
            d = d.AddDays(1);
        return d;
    }
}
