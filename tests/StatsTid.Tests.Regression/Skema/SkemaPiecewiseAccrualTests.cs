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
/// S62 / TASK-6203 / TASK-6204 / ADR-030 <b>D8</b> — HTTP-level regression for the Skema POST seam
/// (<c>POST /api/skema/{id}/save</c>) AFTER the cumulative-piecewise accrual cutover. Sibling of
/// <see cref="StatsTid.Tests.Regression.Outbox.SkemaMonthlyAccrualGuardTests"/> (S60, single-fraction
/// cap); this file pins the two behaviours that the piecewise model ADDS / CHANGES:
///
/// <list type="bullet">
///   <item><description><b>Fail-closed (retained + extended)</b> — a VACATION (MONTHLY_ACCRUAL)
///   booking for an employee with NO dated employment profile at the absence anchor is rejected
///   <c>422 employment_profile_missing</c> (NEVER a silent fraction-1.0). S62 retained the anchor
///   profile-missing guard and added a belt-and-suspenders empty-fraction-window guard before the
///   piecewise sum — both surface the same 422.</description></item>
///   <item><description><b>Piecewise forskud cap moves with mid-ferieår terms</b> — the VACATION
///   forskud cap = <c>EarnedToDatePiecewise</c> evaluated at the ferieår's LAST day (whole-ferieår
///   accruable; manager approval IS the §7 forskudsferie agreement). For an employee FULL-TIME
///   early in the ferieår then dropping to part-time, the full-time months are summed in and are
///   NOT erased by the later part-time fraction — so the cap is HIGHER than a single-fraction
///   (current-terms) cap would give. A booking that piecewise ALLOWS but single-fraction-at-current
///   would REJECT is decided by the piecewise value; a booking beyond the piecewise cap still 422s.
///   </description></item>
/// </list>
///
/// <para>Determinism (priority #2/#4): a fully-PAST ferieår (2024: 1 Sep 2024 – 31 Aug 2025) and
/// fixed dated profile rows are seeded — never the wall clock. <c>employment_start_date</c> is left
/// NULL (full-ferieår assumption), so accrual starts at the ferieår start.</para>
///
/// <para><b>Rule-engine stub.</b> The in-process WAF&lt;Program&gt; harness has no rule-engine
/// container, so — exactly as <see cref="StatsTid.Tests.Regression.Outbox.SkemaMonthlyAccrualGuardTests"/>
/// does — <see cref="IHttpClientFactory"/> is replaced by a stub that drives the REAL
/// <see cref="EntitlementValidationRule.Evaluate"/> over the <c>/api/rules/validate-entitlement</c>
/// seam. The per-type carryover-inclusive <c>BookableLimit</c> the Backend computes from
/// <c>EarnedToDatePiecewise</c> is therefore exercised end-to-end (this is NOT a new harness — it
/// mirrors the established Skema-Docker precedent). DB facts are seeded directly; assertions read
/// <c>absences_projection</c> back from the DB.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaPiecewiseAccrualTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Fresh employees are created per-test in emp001's org (STY01, AC, OK24); a self-save means the
    // actor == the route id, so the bearer is minted for the same id we POST to.
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build (seeders backfill the init.sql emp001 rows,
        // which we do not use here — every test below creates its own bare employee).
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Fail-closed — VACATION booking with NO dated profile at the anchor ⇒ 422.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A VACATION (MONTHLY_ACCRUAL) booking for an employee with NO <c>employee_profiles</c> row at
    /// the absence anchor is rejected <c>422 employment_profile_missing</c> — the Skema seam
    /// fail-CLOSES rather than silently assuming a full-time 1.0 fraction (which would over-grant
    /// forskud). We seed a dated agreement code (so the resolver does not throw on a missing
    /// agreement row) but DELIBERATELY no profile row, isolating the missing-profile path. Nothing
    /// persists.
    /// </summary>
    [Fact]
    public async Task Vacation_NoDatedProfileAtAnchor_FailsClosed422_NothingPersisted()
    {
        var employeeId = await CreateUserAsync(OrgId, "AC");
        // Agreement code present (covers the anchor) but NO employee_profiles row at all.
        await SeedAgreementCodeAsync(employeeId, "AC",
            effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: null);

        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());
        var date = new DateOnly(2024, 11, 4); // ferieår 2024
        var rsp = await PostAbsencesAsync(client, employeeId, 2024, 11,
            new[] { (date, "VACATION", 7.4m) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("employment_profile_missing", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, date));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Piecewise forskud cap — full-time-then-part-time cap > single-fraction cap.
    // ════════════════════════════════════════════════════════════════════════
    //
    // Scenario (deterministic, fully-past ferieår 2024 = 1 Sep 2024 … 31 Aug 2025):
    //   profile FULL-TIME [.., 2025-01-01) then 0.5 [2025-01-01, NULL); employment_start NULL.
    // VACATION forskud cap = EarnedToDatePiecewise at the ferieår end (2025-08-31). Month anchors:
    //   Sep,Oct,Nov,Dec 2024 → 1.0 (4 months) ; Jan..Aug 2025 → 0.5 (8 months).
    //   Σfraction = 4×1.0 + 8×0.5 = 8  ⇒  cap = 25 × 8 / 12 = 16.666… days.
    // A single-fraction-at-CURRENT-terms cap (fraction 0.5) would be 25 × 0.5 = 12.5 days. So:
    //   • 15 days  → ALLOWED by piecewise (15 ≤ 16.67) but REJECTED by single-fraction (15 > 12.5).
    //   • 17 days  → REJECTED (17 > 16.67) — the piecewise cap is still bounded (no infinite forskud).

    /// <summary>
    /// 15 VACATION days for the full-time-then-half-time employee is ALLOWED: the piecewise forskud
    /// cap (≈ 16,67 d — full-time Sep–Dec summed in) exceeds 15, even though a single-fraction cap at
    /// the current 0,5 terms (12,5 d) would REJECT it. This is the load-bearing piecewise behaviour:
    /// earlier full-time months are NOT erased by the later part-time fraction. The booking persists.
    /// </summary>
    [Fact]
    public async Task Vacation_PiecewiseForskudCap_AllowsBookingASingleFractionCapWouldReject()
    {
        var employeeId = await SeedFullThenHalfTimeEmployeeAsync();

        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());
        // 15 distinct VACATION days in the 2024 ferieår (Nov 2024 ⇒ entitlement_year 2024).
        var dates = DistinctDays(new DateOnly(2024, 11, 4), 15);
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var rsp = await PostAbsencesAsync(client, employeeId, 2024, 11, absences);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);          // piecewise cap ≈16.67 ≥ 15
        Assert.Equal(1, await CountAbsenceRowsAsync(employeeId, dates[0]));
        Assert.Equal(15, await CountAbsenceRowsAsync(employeeId, dates));
    }

    /// <summary>
    /// 17 VACATION days for the same full-time-then-half-time employee is REJECTED <c>422</c>: 17
    /// exceeds the piecewise forskud cap (≈ 16,67 d), proving the cap — though raised by the
    /// full-time months — is still BOUNDED (no infinite borrowing). Nothing persists. Together with
    /// the 15-day allow, this brackets the piecewise cap on BOTH sides.
    /// </summary>
    [Fact]
    public async Task Vacation_BeyondPiecewiseForskudCap_Rejected422_NothingPersisted()
    {
        var employeeId = await SeedFullThenHalfTimeEmployeeAsync();

        var client = CreateEmployeeClient(employeeId, CreateRuleStubbedClient());
        var dates = DistinctDays(new DateOnly(2024, 11, 4), 17); // 17 > 16.67 piecewise cap
        var absences = dates.Select(d => (d, "VACATION", 7.4m)).ToArray();

        var rsp = await PostAbsencesAsync(client, employeeId, 2024, 11, absences);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entitlement quota exceeded", body.GetProperty("error").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId, dates[0]));
    }

    // ── Scenario seeding ──

    /// <summary>
    /// Fresh employee with the deterministic full-time-then-half-time history used by the forskud-cap
    /// tests: full-time <c>[0001-01-01, 2025-01-01)</c> then 0,5 <c>[2025-01-01, NULL)</c>, plus a
    /// dated AC agreement code covering all of history. <c>employment_start_date</c> stays NULL.
    /// </summary>
    private async Task<string> SeedFullThenHalfTimeEmployeeAsync()
    {
        var employeeId = await CreateUserAsync(OrgId, "AC");
        await SeedAgreementCodeAsync(employeeId, "AC",
            effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: null);
        await SeedProfileRowAsync(employeeId, fraction: 1.000m,
            effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: new DateOnly(2025, 1, 1), version: 1);
        await SeedProfileRowAsync(employeeId, fraction: 0.500m,
            effectiveFrom: new DateOnly(2025, 1, 1), effectiveTo: null, version: 2);
        return employeeId;
    }

    // ── HTTP helpers (mirror SkemaMonthlyAccrualGuardTests) ──

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

    /// <summary>
    /// N consecutive distinct calendar dates starting at <paramref name="start"/>. Each absence row
    /// must sit on its own date (the per-day norm cap rejects &gt;7,4h on one date), so "book K
    /// vacation days" is K rows on K distinct dates — 1 row of 7,4h = 1 day.
    /// </summary>
    private static DateOnly[] DistinctDays(DateOnly start, int count)
    {
        var days = new DateOnly[count];
        for (var i = 0; i < count; i++)
            days[i] = start.AddDays(i);
        return days;
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

    /// <summary>
    /// Inserts a brand-new user via direct DB insert (NOT through AdminEndpoints POST, which would
    /// also create a profile row). The new user has NO employee_profiles / user_agreement_codes
    /// rows and NULL employment_start_date unless explicitly seeded. Returns the generated user id.
    /// </summary>
    private async Task<string> CreateUserAsync(string orgId, string agreementCode)
    {
        var userId = "emp_s62_skema_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S62 Skema Piecewise Test User', NULL, @org, @ac, 'OK24', TRUE)
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
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

    /// <summary>Count of absence rows across the whole set of dates (proves the full batch persisted).</summary>
    private async Task<int> CountAbsenceRowsAsync(string employeeId, DateOnly[] dates)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e AND date = ANY(@d)", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", dates);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── Token minting (mirrors the sibling Skema/EmployeeProfile regression suites) ──

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
