using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// SEC-033 — server-side range validation on the three admin config write surfaces. Before this
/// fix, money/compliance-adjacent config numbers could be set to corrupting values (the flagship:
/// <c>minimum_rest_hours = 0</c> DISABLES the daily-rest compliance check). These tests are the
/// endpoint-level (400-wiring) proof that each newly-guarded field is now rejected before persist,
/// that boundary/legitimate values still succeed, and that a representative fully-valid config on
/// each surface is not locked out. The pure per-field logic is additionally unit-tested without a
/// container in <c>StatsTid.Tests.Unit.Validation.*ValidatorTests</c>.
///
/// <para><b>Bounds are non-negativity + domain sets ONLY (owner ruling):</b> no invented upper
/// ceilings — the pre-existing <c>WeeklyNormHours &lt;= 50</c> is the only ceiling, kept as-is.</para>
///
/// <para><b>RED-before-green:</b> every <c>*_Returns400</c> case returned 2xx before the src
/// validation landed. Docker-backed (a fresh testcontainer per test, the established harness);
/// CI-deferred where no Docker daemon is present. Natural keys are <c>SEC033*</c>/<c>OKSEC033</c>,
/// disjoint from the boot seeders and every other suite.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SEC033AdminConfigValidationTests : IAsyncLifetime
{
    private const string ActorId = "sec033_gadmin";
    private const string JwtOrg = "SEC033M";
    private const string OkVersion = "OKSEC033";
    private const string PositionCode = "SEC033_POS"; // seeded positions FK target for the PO positive control

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders

        // FK target for the position-override positive control (negatives fail before the FK).
        await ExecAsync(
            """
            INSERT INTO positions (position_code, display_label, agreement_code)
            VALUES ('SEC033_POS', 'SEC033 Testposition', 'AC')
            ON CONFLICT DO NOTHING
            """);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    // ════════════════════════════════════════════════════════════════════════════════
    //  AGREEMENT-CONFIG  (POST /api/agreement-configs)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact] // flagship corruption: 0 disables the daily-rest check
    public async Task AgreementConfig_MinimumRestHoursZero_Returns400()
        => await AssertAgreement400(AgreementJson(minimumRestHours: "0"), "MinimumRestHours");

    [Fact]
    public async Task AgreementConfig_MaxDailyHoursZero_Returns400()
        => await AssertAgreement400(AgreementJson(maxDailyHours: "0"), "MaxDailyHours");

    [Fact]
    public async Task AgreementConfig_AnnualNormHoursZero_Returns400()
        => await AssertAgreement400(AgreementJson(annualNormHours: "0"), "AnnualNormHours");

    [Fact]
    public async Task AgreementConfig_NormPeriodWeeksSeven_Returns400()
        => await AssertAgreement400(AgreementJson(normPeriodWeeks: "7"), "NormPeriodWeeks");

    [Fact] // boundary + positive control: MinimumRestHours = 1 and NormPeriodWeeks = 4 both succeed
    public async Task AgreementConfig_FullyValid_BoundaryValues_Succeeds()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/agreement-configs",
            AgreementJson(minimumRestHours: "1", normPeriodWeeks: "4")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 201, $"expected 201, got {(int)response.StatusCode}: {body}");
    }

    private async Task AssertAgreement400(string json, string expectedErrorFragment)
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/agreement-configs", json));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 400, $"expected 400, got {(int)response.StatusCode}: {body}");
        AssertErrorMentions(body, expectedErrorFragment);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ENTITLEMENT-CONFIG  (POST /api/admin/entitlement-configs)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Entitlement_NegativeAnnualQuota_Returns400()
        => await AssertEntitlement400(EntitlementJson(annualQuota: "-1"), "AnnualQuota");

    [Fact] // non-VACATION reset_month must be 1..12 (a 400 range error, distinct from VACATION's 422)
    public async Task Entitlement_ResetMonthThirteen_NonVacation_Returns400()
        => await AssertEntitlement400(EntitlementJson(resetMonth: "13"), "ResetMonth");

    [Fact]
    public async Task Entitlement_NegativeMinAge_Returns400()
        => await AssertEntitlement400(EntitlementJson(minAge: "-1"), "MinAge");

    [Fact] // CarryoverMax = 0 is legitimate and must NOT be rejected
    public async Task Entitlement_CarryoverMaxZero_Succeeds()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/entitlement-configs", EntitlementJson(carryoverMax: "0")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 201, $"expected 201, got {(int)response.StatusCode}: {body}");
    }

    private async Task AssertEntitlement400(string json, string expectedErrorFragment)
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/entitlement-configs", json));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 400, $"expected 400, got {(int)response.StatusCode}: {body}");
        AssertErrorMentions(body, expectedErrorFragment);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  POSITION-OVERRIDE  (POST /api/admin/position-overrides)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PositionOverride_WeeklyNormHoursZero_Returns400()
        => await AssertPositionOverride400(PositionOverrideJson(weeklyNormHours: "0"), "WeeklyNormHours");

    [Fact]
    public async Task PositionOverride_NormPeriodWeeksSeven_Returns400()
        => await AssertPositionOverride400(PositionOverrideJson(normPeriodWeeks: "7"), "NormPeriodWeeks");

    [Fact]
    public async Task PositionOverride_NegativeMaxFlexBalance_Returns400()
        => await AssertPositionOverride400(PositionOverrideJson(maxFlexBalance: "-1"), "MaxFlexBalance");

    [Fact] // all-null numeric fields ("don't override") is legitimate and must NOT be rejected
    public async Task PositionOverride_AllNullNumericFields_Succeeds()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides",
            PositionOverrideJson(maxFlexBalance: "null", flexCarryoverMax: "null",
                normPeriodWeeks: "null", weeklyNormHours: "null")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 201, $"expected 201, got {(int)response.StatusCode}: {body}");
    }

    [Fact] // positive control incl. flex = 200 (a real seeded value → NO upper ceiling)
    public async Task PositionOverride_FullyValid_NoUpperCeiling_Succeeds()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides",
            PositionOverrideJson(maxFlexBalance: "200.0", normPeriodWeeks: "4", weeklyNormHours: "37.0")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 201, $"expected 201, got {(int)response.StatusCode}: {body}");
    }

    private async Task AssertPositionOverride400(string json, string expectedErrorFragment)
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/position-overrides", json));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 400, $"expected 400, got {(int)response.StatusCode}: {body}");
        AssertErrorMentions(body, expectedErrorFragment);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static void AssertErrorMentions(string body, string fragment)
    {
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString();
        Assert.Contains(fragment, error);
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── request bodies (raw JSON tokens for range values) ───────────────────────────────

    /// <summary>Full AgreementConfigRequest body; the fields under test are raw JSON tokens so
    /// out-of-range values (e.g. "0", "7") go on the wire verbatim. Unlisted fields are valid.</summary>
    private static string AgreementJson(
        string weeklyNormHours = "37.0", string normPeriodWeeks = "1", string annualNormHours = "1924.0",
        string maxDailyHours = "13.0", string minimumRestHours = "11.0")
        => $$"""
           {
             "agreementCode": "SEC033A", "okVersion": "{{OkVersion}}",
             "description": "SEC-033 validation", "normModel": "WEEKLY_HOURS",
             "weeklyNormHours": {{weeklyNormHours}}, "normPeriodWeeks": {{normPeriodWeeks}},
             "annualNormHours": {{annualNormHours}},
             "maxFlexBalance": 74.0, "flexCarryoverMax": 37.0,
             "hasOvertime": true, "hasMerarbejde": false,
             "overtimeThreshold50": 37.0, "overtimeThreshold100": 44.0,
             "eveningSupplementEnabled": true, "nightSupplementEnabled": true,
             "weekendSupplementEnabled": true, "holidaySupplementEnabled": true,
             "eveningStart": 17, "eveningEnd": 23, "nightStart": 23, "nightEnd": 6,
             "eveningRate": 0.25, "nightRate": 0.50, "weekendSaturdayRate": 0.50,
             "weekendSundayRate": 1.00, "holidayRate": 1.00,
             "onCallDutyEnabled": true, "onCallDutyRate": 0.25,
             "callInWorkEnabled": true, "callInMinimumHours": 3.0, "callInRate": 1.50,
             "travelTimeEnabled": true, "workingTravelRate": 1.00, "nonWorkingTravelRate": 0.50,
             "maxDailyHours": {{maxDailyHours}}, "minimumRestHours": {{minimumRestHours}}
           }
           """;

    /// <summary>CHILD_SICK entitlement body (not full-day-only, so no 422 day-shape guard;
    /// not VACATION, so the generic reset_month 1..12 range applies). effectiveFrom omitted ⇒ today.</summary>
    private static string EntitlementJson(
        string annualQuota = "3.0", string carryoverMax = "0", string resetMonth = "1", string minAge = "null")
        => $$"""
           { "entitlementType": "CHILD_SICK", "agreementCode": "SEC033E", "okVersion": "{{OkVersion}}",
             "annualQuota": {{annualQuota}}, "accrualModel": "IMMEDIATE", "resetMonth": {{resetMonth}},
             "carryoverMax": {{carryoverMax}}, "proRateByPartTime": false, "isPerEpisode": true,
             "minAge": {{minAge}}, "description": "SEC-033 validation", "fullDayOnly": false }
           """;

    /// <summary>Position-override body; all four numeric fields are raw JSON tokens so "null"
    /// (don't-override) and literals both go on the wire verbatim.</summary>
    private static string PositionOverrideJson(
        string maxFlexBalance = "120.5", string flexCarryoverMax = "40.25",
        string normPeriodWeeks = "4", string weeklyNormHours = "37.0")
        => $$"""
           { "agreementCode": "SEC033P", "okVersion": "{{OkVersion}}", "positionCode": "{{PositionCode}}",
             "maxFlexBalance": {{maxFlexBalance}}, "flexCarryoverMax": {{flexCarryoverMax}},
             "normPeriodWeeks": {{normPeriodWeeks}}, "weeklyNormHours": {{weeklyNormHours}},
             "description": "SEC-033 validation" }
           """;
}
