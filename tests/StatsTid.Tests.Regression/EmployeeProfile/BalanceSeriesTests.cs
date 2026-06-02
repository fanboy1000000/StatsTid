using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S61 / TASK-6102 / ADR-030 — HTTP-level regression for the read-only accrual-curve endpoint
/// <c>GET /api/balance/{employeeId}/series?year={int}&amp;month={int}</c> (compute-on-read).
///
/// <para>Uses the WAF&lt;Program&gt; in-process harness (<see cref="StatsTidWebApplicationFactory"/>)
/// + per-test Postgres testcontainer, the same shape as
/// <see cref="EmployeeProfileLifecycleTests"/>. Unlike the Skema POST guards, the <c>/series</c>
/// endpoint never hops to the rule engine — it only reads <c>entitlement_configs</c>, the live
/// <c>users</c> row, the dated <c>user_agreement_codes</c>, and the dated
/// <c>employee_profiles</c> via the resolver — so NO rule-engine stub is required.</para>
///
/// <para>Pins (per the TASK-6106 validation criteria):</para>
/// <list type="bullet">
///   <item><description><b>Shape</b> — series contains ONLY VACATION + SPECIAL_HOLIDAY (the two
///   MONTHLY_ACCRUAL types); no IMMEDIATE types (CARE_DAY/CHILD_SICK/SENIOR_DAY). Each entry has
///   12 month-end points across the September ferieår; <c>annualQuota</c> /
///   <c>entitlementYear</c> / <c>ferieaarStart</c> correct.</description></item>
///   <item><description><b>Server-derived ferieår + month-end from params</b> — the curve depends
///   only on the <c>(year, month)</c> query (server-derived ferieår, server-built month-ends),
///   NOT on the wall clock. A request for a past month produces a past ferieår.</description></item>
///   <item><description><b>Reconciliation</b> — the point whose month == the requested month is
///   byte-identical to <c>/summary</c>'s <c>earned</c> for the same key.</description></item>
///   <item><description><b>Single-fraction (current-terms) projection — monotonic</b> (S61
///   Step-7a fix) — the curve is MONOTONIC non-decreasing, and a mid-ferieår part-time
///   supersession is projected with the SELECTED month's fraction across ALL 12 points (NOT a
///   per-point fraction). A per-point fraction made the curve DROP when the fraction fell
///   mid-ferieår (<c>AccrualMath.EarnedToDate</c> is single-fraction:
///   <c>quota × fraction × monthsElapsed / 12</c>); accrued vacation must never decrease, and the
///   series stays consistent with <c>/summary</c> + the Skema quota guard, which both use ONE
///   fraction. Piecewise month-by-month accrual is intentionally out of scope.</description></item>
///   <item><description><b>Auth</b> — an Employee reading ANOTHER employee's series ⇒ 403; a
///   leader out of org scope ⇒ 403; in-scope ⇒ 200.</description></item>
///   <item><description><b>Profile-less graceful</b> — an employee with no employee_profiles row
///   ⇒ 200 (fraction-1.0 fallback), no 500 (ADR-023 D3).</description></item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class BalanceSeriesTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Seeded employee (init.sql): emp001, STY01 (/MIN01/STY01/), AC, OK24. The EmployeeProfileSeeder
    // + UserAgreementCodeBackfillSeeder backfill a live profile + agreement row at CreateClient time.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    // VACATION + SPECIAL_HOLIDAY reset in September (reset_month 9). A request for Oct 2025 (>= 9)
    // resolves to ferieår 2025 (1 Sep 2025); a request for Mar 2026 (< 9) also resolves to 2025.
    // We query a fully-PAST ferieår so all 12 month-ends are historical and wall-clock-independent.
    private const int QueryYear = 2025;
    private const int QueryMonth = 10; // October 2025 → ferieår 2025

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build → seeders backfill emp001's live profile +
        // agreement-code rows.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Shape — MONTHLY_ACCRUAL-only, 12 September-ferieår month-end points.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The series contains EXACTLY the two MONTHLY_ACCRUAL types (VACATION + SPECIAL_HOLIDAY) and
    /// no IMMEDIATE types. Each entry carries 12 points whose <c>monthEnd</c> dates are the
    /// month-ends of the September 2025 ferieår (Sep 2025 … Aug 2026), with the correct
    /// <c>annualQuota</c> / <c>entitlementYear</c> / <c>ferieaarStart</c>.
    /// </summary>
    [Fact]
    public async Task Series_ContainsOnlyMonthlyAccrualTypes_With12SeptemberFerieaarPoints()
    {
        var client = EmployeeClient(Emp001);

        var body = await GetSeriesAsync(client, Emp001, QueryYear, QueryMonth);
        var series = body.GetProperty("series").EnumerateArray().ToList();

        var types = series.Select(s => s.GetProperty("type").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "VACATION", "SPECIAL_HOLIDAY" }, types);
        // Explicitly assert the IMMEDIATE types are absent.
        Assert.DoesNotContain("CARE_DAY", types);
        Assert.DoesNotContain("CHILD_SICK", types);
        Assert.DoesNotContain("SENIOR_DAY", types);

        foreach (var entry in series)
        {
            // ferieår 2025: reset month 9 → ferieaarStart 2025-09-01, entitlementYear 2025.
            Assert.Equal("2025-09-01", entry.GetProperty("ferieaarStart").GetString());
            Assert.Equal(2025, entry.GetProperty("entitlementYear").GetInt32());

            var expectedQuota = entry.GetProperty("type").GetString() == "VACATION" ? 25m : 5m;
            Assert.Equal(expectedQuota, entry.GetProperty("annualQuota").GetDecimal());

            var points = entry.GetProperty("points").EnumerateArray().ToList();
            Assert.Equal(12, points.Count);

            // The 12 month-ends are exactly the ferieår months Sep..Aug, each on its true last day.
            var expectedMonthEnds = Enumerable.Range(0, 12)
                .Select(i => new DateOnly(2025, 9, 1).AddMonths(i))
                .Select(d => (string?)new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month))
                    .ToString("yyyy-MM-dd"))
                .ToList();
            var actualMonthEnds = points.Select(p => p.GetProperty("monthEnd").GetString()).ToList();
            Assert.Equal(expectedMonthEnds, actualMonthEnds);

            // Exactly one point is the selected ("now") point — the requested month (October).
            var selected = points.Where(p => p.GetProperty("isSelected").GetBoolean()).ToList();
            Assert.Single(selected);
            Assert.Equal("2025-10-31", selected[0].GetProperty("monthEnd").GetString());
        }
    }

    /// <summary>
    /// The curve is a pure function of the <c>(year, month)</c> query, NOT the wall clock. Two
    /// requests for two different past months that both resolve to the SAME September ferieår
    /// (Oct 2025 and Mar 2026 both → ferieår 2025) return identical <c>earned</c> values per
    /// month-end — only the <c>isSelected</c> flag moves. This is the determinism property
    /// (priority #2/#4): re-deriving the curve never depends on "today".
    /// </summary>
    [Fact]
    public async Task Series_DerivedFromQueryParams_NotWallClock_SameFerieaarSameCurve()
    {
        var client = EmployeeClient(Emp001);

        var octBody = await GetSeriesAsync(client, Emp001, 2025, 10); // ferieår 2025, now = Oct
        var marBody = await GetSeriesAsync(client, Emp001, 2026, 3);  // ferieår 2025, now = Mar

        var octVac = VacationPoints(octBody);
        var marVac = VacationPoints(marBody);

        // Same ferieår window ⇒ same 12 month-ends and same earned values.
        Assert.Equal(
            octVac.Select(p => p.GetProperty("monthEnd").GetString()),
            marVac.Select(p => p.GetProperty("monthEnd").GetString()));
        Assert.Equal(
            octVac.Select(p => p.GetProperty("earned").GetDecimal()),
            marVac.Select(p => p.GetProperty("earned").GetDecimal()));

        // Only the selected point differs (Oct vs Mar).
        Assert.Equal("2025-10-31",
            octVac.Single(p => p.GetProperty("isSelected").GetBoolean()).GetProperty("monthEnd").GetString());
        Assert.Equal("2026-03-31",
            marVac.Single(p => p.GetProperty("isSelected").GetBoolean()).GetProperty("monthEnd").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Reconciliation — the selected point == /summary's earned for the same key.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The series point at the requested month is byte-identical to <c>/summary</c>'s VACATION
    /// <c>earned</c> for the same <c>(employeeId, year, month)</c> — the two seams share the same
    /// month-end as-of, the same dated profile, the same <see cref="AccrualMath"/> call, and the
    /// same <c>Math.Round(., 2)</c>. We call BOTH endpoints and assert equality.
    /// </summary>
    [Fact]
    public async Task Series_SelectedPoint_ReconcilesWith_SummaryEarned()
    {
        var client = EmployeeClient(Emp001);

        var seriesBody = await GetSeriesAsync(client, Emp001, QueryYear, QueryMonth);
        var selectedEarned = VacationPoints(seriesBody)
            .Single(p => p.GetProperty("isSelected").GetBoolean())
            .GetProperty("earned").GetDecimal();

        var summaryRsp = await client.GetAsync(
            $"/api/balance/{Emp001}/summary?year={QueryYear}&month={QueryMonth}");
        summaryRsp.EnsureSuccessStatusCode();
        var summaryBody = await summaryRsp.Content.ReadFromJsonAsync<JsonElement>();
        var summaryVacationEarned = summaryBody.GetProperty("entitlements").EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "VACATION")
            .GetProperty("earned").GetDecimal();

        Assert.Equal(summaryVacationEarned, selectedEarned);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Single-fraction (current-terms) projection — monotonic; selected month's
    // fraction drives the WHOLE curve (S61 Step-7a fix; was the per-point "bend").
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seed a fresh employee with a DATED part-time supersession mid-ferieår: full-time
    /// (fraction 1.0) for [ferieår start, 1 Jan 2026), then half-time (0.5) from 1 Jan 2026. The
    /// endpoint resolves the part-time fraction ONCE — at the REQUESTED month's month-end — and
    /// projects THAT single fraction across all 12 points (consistent with <c>/summary</c> + the
    /// Skema quota guard, both single-fraction). This is the S61 Step-7a fix: a per-point fraction
    /// made the curve NON-MONOTONIC (it DROPPED at the boundary where the fraction fell, since
    /// <c>AccrualMath.EarnedToDate</c> is single-fraction and smears each point's month-end
    /// fraction across all of that point's elapsed months). Accrued vacation must never decrease.
    ///
    /// <para>We assert BOTH regimes by selecting a month on each side of the supersession:</para>
    /// <list type="bullet">
    ///   <item><description>Selecting <b>October 2025</b> (month-end 2025-10-31, in the full-time
    ///   window) ⇒ the WHOLE curve uses fraction 1.0: earned[i] = 25 × 1.0 × (i+1)/12.</description></item>
    ///   <item><description>Selecting <b>March 2026</b> (month-end 2026-03-31, in the half-time
    ///   window) ⇒ the WHOLE curve uses fraction 0.5: earned[i] = 25 × 0.5 × (i+1)/12.</description></item>
    /// </list>
    /// <para>Both curves are MONOTONIC non-decreasing; neither bends mid-ferieår. Per-month
    /// fraction history (piecewise accrual) is intentionally out of scope.</para>
    /// </summary>
    [Fact]
    public async Task Series_MidFerieaarPartTimeChange_UsesSelectedMonthFraction_Monotonic()
    {
        // Fresh employee in emp001's org (STY01, AC, OK24) with NO seeded profile/agreement rows.
        var employeeId = await CreateUserAsync(orgId: Emp001OrgId, agreementCode: "AC");

        // Dated agreement code covering the WHOLE ferieår (so the resolver does not fail-loud on a
        // missing user_agreement_codes row — it would otherwise throw EmployeeProfileNotFoundException
        // and the endpoint would gracefully fall back to fraction 1.0, masking the seeded fraction).
        await SeedAgreementCodeAsync(employeeId, "AC", effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: null);

        // Predecessor profile: full-time [0001-01-01, 2026-01-01).
        await SeedProfileRowAsync(employeeId, fraction: 1.000m,
            effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: new DateOnly(2026, 1, 1), version: 1);
        // Successor (live) profile: half-time [2026-01-01, NULL).
        await SeedProfileRowAsync(employeeId, fraction: 0.500m,
            effectiveFrom: new DateOnly(2026, 1, 1), effectiveTo: null, version: 2);

        var client = EmployeeClient(employeeId);

        // The single-fraction trajectory for a given fraction (rounded 2dp as the endpoint does).
        static List<decimal> SingleFractionTrajectory(decimal fraction) =>
            Enumerable.Range(0, 12)
                .Select(i => Math.Round(25m * fraction * (i + 1) / 12m, 2))
                .ToList();

        // ── Selecting October 2025 (month-end in the full-time window) ⇒ whole curve fraction 1.0 ──
        var octBody = await GetSeriesAsync(client, employeeId, 2025, 10);
        var octEarned = VacationPoints(octBody)
            .Select(p => p.GetProperty("earned").GetDecimal()).ToList();
        Assert.Equal(12, octEarned.Count);
        Assert.Equal(SingleFractionTrajectory(1.0m), octEarned);
        AssertMonotonicNonDecreasing(octEarned);

        // ── Selecting March 2026 (month-end in the half-time window) ⇒ whole curve fraction 0.5 ──
        var marBody = await GetSeriesAsync(client, employeeId, 2026, 3);
        var marEarned = VacationPoints(marBody)
            .Select(p => p.GetProperty("earned").GetDecimal()).ToList();
        Assert.Equal(12, marEarned.Count);
        Assert.Equal(SingleFractionTrajectory(0.5m), marEarned);
        AssertMonotonicNonDecreasing(marEarned);

        // The whole curve scales with the SELECTED month's fraction — no mid-ferieår bend. The
        // half-time selection is exactly half the full-time selection at every point (single
        // fraction, not a per-point smear that would have made either curve drop).
        for (var i = 0; i < 12; i++)
            Assert.Equal(Math.Round(octEarned[i] / 2m, 2), marEarned[i]);
    }

    /// <summary>
    /// Regression guard for the S61 Step-7a BLOCKER: the accrual curve must be MONOTONIC
    /// non-decreasing for EVERY MONTHLY_ACCRUAL series, regardless of any mid-ferieår part-time
    /// change. The original per-point-fraction implementation produced a DROP (e.g. full-time
    /// Sep–Dec then 0.5 from Jan: Dec ≈ 8.33 then Jan ≈ 5.21). We seed exactly that supersession
    /// and assert no point is below its predecessor for every type in the series.
    /// </summary>
    [Fact]
    public async Task Series_PartTimeDropMidFerieaar_CurveIsMonotonicNonDecreasing()
    {
        var employeeId = await CreateUserAsync(orgId: Emp001OrgId, agreementCode: "AC");
        await SeedAgreementCodeAsync(employeeId, "AC", effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: null);
        // Full-time [.., 2026-01-01), then HALF-time [2026-01-01, NULL) — a mid-ferieår DROP in
        // terms that the old per-point model turned into a dropping (non-monotonic) curve.
        await SeedProfileRowAsync(employeeId, fraction: 1.000m,
            effectiveFrom: new DateOnly(1, 1, 1), effectiveTo: new DateOnly(2026, 1, 1), version: 1);
        await SeedProfileRowAsync(employeeId, fraction: 0.500m,
            effectiveFrom: new DateOnly(2026, 1, 1), effectiveTo: null, version: 2);

        var client = EmployeeClient(employeeId);
        // Request a month-end in the LATER (half-time) regime — the side on which the old model
        // produced the drop relative to the earlier full-time months.
        var body = await GetSeriesAsync(client, employeeId, 2026, 3);

        foreach (var entry in body.GetProperty("series").EnumerateArray())
        {
            var earned = entry.GetProperty("points").EnumerateArray()
                .Select(p => p.GetProperty("earned").GetDecimal()).ToList();
            Assert.Equal(12, earned.Count);
            AssertMonotonicNonDecreasing(earned);
        }
    }

    /// <summary>Asserts <c>points[i+1] &gt;= points[i]</c> for the whole curve (accrued vacation
    /// must never decrease — the S61 Step-7a invariant).</summary>
    private static void AssertMonotonicNonDecreasing(IReadOnlyList<decimal> earned)
    {
        for (var i = 1; i < earned.Count; i++)
            Assert.True(earned[i] >= earned[i - 1],
                $"Curve must be monotonic non-decreasing, but earned[{i}]={earned[i]} < earned[{i - 1}]={earned[i - 1]}.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Auth — self-only for Employee; org-scope for leaders.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>An Employee reading ANOTHER employee's series ⇒ 403 (employee-self equality).</summary>
    [Fact]
    public async Task Series_EmployeeReadingAnotherEmployee_Forbidden403()
    {
        // emp002 token reading emp001's series.
        var otherEmployee = EmployeeClient("emp002");
        var rsp = await otherEmployee.GetAsync(
            $"/api/balance/{Emp001}/series?year={QueryYear}&month={QueryMonth}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// A leader scoped to a DISJOINT org subtree (STY05 = /MIN02/STY05/) reading emp001
    /// (/MIN01/STY01/) ⇒ 403 (OrgScopeValidator: scope does not cover target org).
    /// </summary>
    [Fact]
    public async Task Series_LeaderOutOfOrgScope_Forbidden403()
    {
        var outOfScopeLeader = LeaderClient("mgr_oos", scopeOrg: "STY05");
        var rsp = await outOfScopeLeader.GetAsync(
            $"/api/balance/{Emp001}/series?year={QueryYear}&month={QueryMonth}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// A leader scoped to STY01 (= emp001's org, /MIN01/STY01/) with ORG_AND_DESCENDANTS ⇒ 200,
    /// and gets the same MONTHLY_ACCRUAL-only shape.
    /// </summary>
    [Fact]
    public async Task Series_LeaderInOrgScope_Allowed200()
    {
        var inScopeLeader = LeaderClient("mgr_is", scopeOrg: Emp001OrgId);
        var rsp = await inScopeLeader.GetAsync(
            $"/api/balance/{Emp001}/series?year={QueryYear}&month={QueryMonth}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var types = body.GetProperty("series").EnumerateArray()
            .Select(s => s.GetProperty("type").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "VACATION", "SPECIAL_HOLIDAY" }, types);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Profile-less graceful — no employee_profiles row ⇒ 200 (fraction 1.0), no 500.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An employee with NO <c>employee_profiles</c> row at all: the resolver returns null at every
    /// point and the endpoint falls back to fraction 1.0 (ADR-023 D3 graceful), returning 200 with
    /// a full-time curve — never a 500. The selected point equals the full-time earned-to-date.
    /// </summary>
    [Fact]
    public async Task Series_ProfilelessEmployee_FallsBackToFractionOne_Returns200()
    {
        // Fresh employee with NO profile row (and no agreement-code row either — the resolver
        // returns null on the missing employee_profiles JOIN BEFORE the agreement-code lookup,
        // so this is the clean "no dated profile" graceful path, fraction 1.0).
        var employeeId = await CreateUserAsync(orgId: Emp001OrgId, agreementCode: "AC");

        var client = EmployeeClient(employeeId);
        var rsp = await client.GetAsync(
            $"/api/balance/{employeeId}/series?year={QueryYear}&month={QueryMonth}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var selectedEarned = VacationPoints(body)
            .Single(p => p.GetProperty("isSelected").GetBoolean())
            .GetProperty("earned").GetDecimal();

        // October = month 2 of the ferieår ⇒ full-time earned = 25 × 1.0 × 2/12, rounded to 2dp.
        Assert.Equal(Math.Round(25m * 2 / 12m, 2), selectedEarned);
    }

    // ── HTTP helpers ──

    private static async Task<JsonElement> GetSeriesAsync(
        HttpClient client, string employeeId, int year, int month)
    {
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/series?year={year}&month={month}");
        rsp.EnsureSuccessStatusCode();
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The VACATION entry's 12 points (the curve under most assertions).</summary>
    private static List<JsonElement> VacationPoints(JsonElement seriesBody) =>
        seriesBody.GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("type").GetString() == "VACATION")
            .GetProperty("points").EnumerateArray().ToList();

    private HttpClient EmployeeClient(string employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, Emp001OrgId));
        return client;
    }

    private HttpClient LeaderClient(string leaderId, string scopeOrg)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintLeaderToken(leaderId, scopeOrg));
        return client;
    }

    // ── DB seeding helpers (direct access via the harness) ──

    /// <summary>
    /// Inserts a brand-new user via direct DB insert (NOT through AdminEndpoints POST, which would
    /// also create a profile row). The new user has NO employee_profiles / user_agreement_codes
    /// rows unless explicitly seeded by the caller. Returns the generated user id.
    /// </summary>
    private async Task<string> CreateUserAsync(string orgId, string agreementCode)
    {
        var userId = "emp_s61_series_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S61 Series Test User', NULL, @org, @ac, 'OK24', TRUE)
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

    private static string MintLeaderToken(string actorId, string scopeOrgId)
    {
        var tokenService = new JwtTokenService(DevSettings());
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.LocalLeader,
            agreementCode: "AC",
            orgId: scopeOrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, scopeOrgId, "ORG_AND_DESCENDANTS") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };
}
