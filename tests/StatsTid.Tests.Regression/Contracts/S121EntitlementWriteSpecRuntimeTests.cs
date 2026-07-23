using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S121 / TASK-12102 — the success-path proof for the ENTITLEMENT write flows, the
/// S118-deferred prod defect #2 (the child-entitlement create AND update dead-ends: the FE
/// omitted <c>fullDayOnly</c> on both verbs and <c>FullDayOnlyGuard</c> sits on both, so every
/// CARE_DAY/SENIOR_DAY child write 422'd in prod) plus the ruling #1/#3 structural pins on the
/// child and primary surfaces.
///
/// <para><b>What this class pins (owner rulings 2026-07-23):</b></para>
/// <list type="bullet">
///   <item><description><b>Child create + update SUCCESS (the repaired dead-ends):</b> a
///     CARE_DAY child (guard-forced <c>fullDayOnly: true</c>) POSTs 201 and PUTs 200 — both
///     bodies OMIT <c>effectiveFrom</c> (ruling #1: the server owns today) and both carry the
///     now binder-required flag (ruling #3). A non-forced type (CHILD_SICK) round-trips its
///     explicit <c>false</c> through the update preserve path.</description></item>
///   <item><description><b>Ruling #3 structural closure:</b> OMITTING <c>fullDayOnly</c> is a
///     400 at the BINDER on the child POST and PUT — the request-side lie detector now covers
///     the silent-omission trap that shipped this defect; nothing persists.</description></item>
///   <item><description><b>The D-A rule intact behind the binder:</b> an EXPLICIT
///     <c>false</c> on a forced type still draws the guard's 422 (rule semantics untouched —
///     the sprint's P4 exclusion).</description></item>
///   <item><description><b>Ruling #1 on the THIRD PUT of the family:</b> the primary
///     admin entitlement-config PUT succeeds with <c>effectiveFrom</c> OMITTED (the FE editor
///     drops its client-computed today under the S121 alignment).</description></item>
/// </list>
///
/// <para><b>Seed discipline:</b> a FRESH testcontainer per test (the established S118 harness
/// conventions; matcher + Support consumed AS-IS). Child-surface parents are
/// (<c>S121AGE_*</c>, <c>OKS121</c>); the primary surface uses (CARE_DAY, <c>S121EC</c>,
/// <c>OKS121_*</c>) — DISJOINT from the boot seeders (AC/HK/PROSA × OK24/OK26), the S118 gate
/// families (<c>S118AGC_*</c>/<c>S118AGE_*</c>/<c>S118EC</c>/<c>OKS118*</c>), and every
/// pre-existing EntitlementConfig suite (<c>OK_S30POST_*</c>/<c>OK_S68RESET_*</c>/
/// <c>OK_S73FDO_*</c>/<c>OK_CASEA_*</c>/<c>OK_CASEC_*</c>/<c>S73SUB_*</c>). Every row is
/// created through the REAL endpoints.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S121EntitlementWriteSpecRuntimeTests : IAsyncLifetime
{
    private const string ActorId = "s121e_gadmin";
    private const string JwtOrg = "S121EM"; // JWT claim only — entitlement-config audit rows are GLOBAL (no org FK)
    private const string OkVersion = "OKS121";
    private const string PrimaryAgreementCode = "S121EC";

    /// <summary>The 16-member shared child record (incl. <c>fullDayOnly</c>).</summary>
    private static readonly string[] ChildEntitlementKeys =
    {
        "configId", "entitlementType", "agreementCode", "okVersion",
        "annualQuota", "accrualModel", "resetMonth", "carryoverMax",
        "proRateByPartTime", "isPerEpisode", "minAge", "description",
        "fullDayOnly", "effectiveFrom", "effectiveTo", "version",
    };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The repaired child dead-ends — CARE_DAY create 201 + update 200, effectiveFrom
    //  OMITTED on BOTH verbs.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The S118 defect-#2 repair proof: a CARE_DAY child (forced <c>true</c>) POSTs
    /// 201 and PUTs 200 with bodies shaped EXACTLY as the graduated FE now sends them —
    /// <c>fullDayOnly</c> present (ruling #3), <c>effectiveFrom</c> OMITTED on both verbs
    /// (ruling #1; the server stamps today). Matcher + exact key set on both; same-day Case C
    /// in-place on the PUT (same configId, version 2); the forced flag round-trips TRUE.</summary>
    [Fact]
    public async Task Child_CareDay_Post201ThenPut200_EffectiveFromOmittedOnBothVerbs_ForcedTrueRoundTrips()
    {
        using var admin = Admin();
        var parentId = await CreateParentAsync(admin, "S121AGE_CD");
        var today = Today();

        // POST — 201 exact + matcher; effectiveFrom omitted ⇒ server-stamped today.
        using var created = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentId}/entitlements",
            ChildJson("CARE_DAY", annualQuota: "2.0", fullDayOnly: "true")));
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.Equal(201, (int)created.StatusCode);
        var postTruth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/agreement-configs/{configId}/entitlements", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, postTruth, 201, createdBody,
            "POST /api/agreement-configs/{configId}/entitlements (201, S121 repaired create)");
        var createdRoot = JsonDocument.Parse(createdBody).RootElement;
        S118ContractAssert.AssertExactKeySet(createdRoot, ChildEntitlementKeys, "child POST 201 (S121)");
        var childId = createdRoot.GetProperty("configId").GetGuid();
        Assert.True(createdRoot.GetProperty("fullDayOnly").GetBoolean());
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            createdRoot.GetProperty("effectiveFrom").GetString()); // server-defaulted today
        var etag = S118ContractAssert.EtagVersion(created);
        Assert.Equal(1L, etag);

        // PUT — effectiveFrom OMITTED (the ruling #1 pin on the child PUT), quota 2.0 → 3.0.
        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
                $"/api/agreement-configs/{parentId}/entitlements/{childId}",
                ChildJson("CARE_DAY", annualQuota: "3.0", fullDayOnly: "true"), ifMatchVersion: etag),
            "/api/agreement-configs/{configId}/entitlements/{entitlementConfigId}", "put");
        var putRoot = JsonDocument.Parse(putBody).RootElement;
        S118ContractAssert.AssertExactKeySet(putRoot, ChildEntitlementKeys, "child PUT 200 (S121)");
        Assert.Equal(childId, putRoot.GetProperty("configId").GetGuid()); // same-day Case C: the SAME row
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());
        Assert.Equal(3.0m, putRoot.GetProperty("annualQuota").GetDecimal());
        Assert.True(putRoot.GetProperty("fullDayOnly").GetBoolean());     // forced TRUE round-trips
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            putRoot.GetProperty("effectiveFrom").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Non-forced type — the preserve round-trip.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The FE's preserve semantics for a NON-forced type (ruling #2: the row's
    /// current value round-trips — no editable control): a CHILD_SICK child created with an
    /// explicit <c>false</c> keeps <c>false</c> through an update that round-trips the row
    /// value. Both bodies omit <c>effectiveFrom</c>.</summary>
    [Fact]
    public async Task Child_NonForcedType_Put200_PreservesExplicitFalseRoundTrip()
    {
        using var admin = Admin();
        var parentId = await CreateParentAsync(admin, "S121AGE_CS");

        using var created = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentId}/entitlements",
            ChildJson("CHILD_SICK", annualQuota: "1.0", fullDayOnly: "false", isPerEpisode: "true")));
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.Equal(201, (int)created.StatusCode);
        var createdRoot = JsonDocument.Parse(createdBody).RootElement;
        Assert.False(createdRoot.GetProperty("fullDayOnly").GetBoolean());
        var childId = createdRoot.GetProperty("configId").GetGuid();
        var etag = S118ContractAssert.EtagVersion(created);

        // Update round-tripping the row's fullDayOnly (false) — the preserve path.
        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
                $"/api/agreement-configs/{parentId}/entitlements/{childId}",
                ChildJson("CHILD_SICK", annualQuota: "2.0", fullDayOnly: "false", isPerEpisode: "true"),
                ifMatchVersion: etag),
            "/api/agreement-configs/{configId}/entitlements/{entitlementConfigId}", "put");
        var putRoot = JsonDocument.Parse(putBody).RootElement;
        Assert.Equal(childId, putRoot.GetProperty("configId").GetGuid());
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());
        Assert.False(putRoot.GetProperty("fullDayOnly").GetBoolean()); // the preserved value, unchanged
        Assert.Equal(2.0m, putRoot.GetProperty("annualQuota").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ruling #3 — omission is a binder-400 on BOTH child verbs; nothing persists.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The structural-closure pin: <c>fullDayOnly</c> is binder-REQUIRED, so a body
    /// WITHOUT it never reaches the guard — 400 at the binder on the child POST (nothing
    /// persisted) AND on the child PUT (the existing row untouched at version 1). This is the
    /// exact omission shape the pre-S121 FE sent — the defect would now be caught at the
    /// contract, loudly, instead of dead-ending as a guard-422.</summary>
    [Fact]
    public async Task Child_FullDayOnlyOmitted_Returns400AtBinder_BothVerbs_NothingPersisted()
    {
        using var admin = Admin();
        var parentId = await CreateParentAsync(admin, "S121AGE_BND");

        // POST without the member → binder-400, no row.
        using (var post = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentId}/entitlements",
            ChildJsonOmittingFullDayOnly("CARE_DAY"))))
        {
            Assert.Equal(400, (int)post.StatusCode);
        }
        Assert.Equal(0L, await CountChildRowsAsync("S121AGE_BND"));

        // Seed a VALID child, then PUT without the member → binder-400, row untouched.
        using var created = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentId}/entitlements",
            ChildJson("CARE_DAY", annualQuota: "2.0", fullDayOnly: "true")));
        Assert.Equal(201, (int)created.StatusCode);
        var childId = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("configId").GetGuid();
        var etag = S118ContractAssert.EtagVersion(created);

        using (var put = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/agreement-configs/{parentId}/entitlements/{childId}",
            ChildJsonOmittingFullDayOnly("CARE_DAY"), ifMatchVersion: etag)))
        {
            Assert.Equal(400, (int)put.StatusCode);
        }
        // The row is untouched: still version 1, flag still TRUE.
        Assert.Equal(1L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM entitlement_configs
            WHERE config_id = @id AND version = 1 AND full_day_only = TRUE
            """, ("id", childId)));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The D-A guard behind the binder — explicit false on a forced type still 422s.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Rule semantics untouched (the sprint's P4 exclusion): an EXPLICIT
    /// <c>false</c> on CARE_DAY passes the binder and draws the shared guard's 422 with the
    /// structured <c>suppliedFullDayOnly</c> error; nothing persists.</summary>
    [Fact]
    public async Task Child_ExplicitFalseOnForcedType_Returns422_GuardIntactBehindTheBinder()
    {
        using var admin = Admin();
        var parentId = await CreateParentAsync(admin, "S121AGE_GRD");

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentId}/entitlements",
            ChildJson("CARE_DAY", annualQuota: "2.0", fullDayOnly: "false")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(422, (int)response.StatusCode);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("CARE_DAY", root.GetProperty("entitlementType").GetString());
        Assert.False(root.GetProperty("suppliedFullDayOnly").GetBoolean());
        Assert.Equal(0L, await CountChildRowsAsync("S121AGE_GRD"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ruling #1 on the THIRD PUT — the primary admin entitlement-config surface.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The family-completeness pin (the Reviewer-W1 framing: ruling only the two
    /// dead PUTs would leave the midnight race live here): the primary admin PUT succeeds
    /// with <c>effectiveFrom</c> OMITTED — same-day Case C in-place, matcher-asserted 200,
    /// version 2 on the same row, the binder-required <c>fullDayOnly</c> present.</summary>
    [Fact]
    public async Task Primary_Put200_EffectiveFromOmitted_SameDayInPlace()
    {
        using var admin = Admin();

        using var created = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/entitlement-configs", PrimaryCreateJson("OKS121_PPUT")));
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.Equal(201, (int)created.StatusCode);
        var configId = JsonDocument.Parse(createdBody).RootElement.GetProperty("configId").GetGuid();
        var etag = S118ContractAssert.EtagVersion(created);
        Assert.Equal(1L, etag);

        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
                $"/api/admin/entitlement-configs/{configId}",
                PrimaryPutJsonOmittingEffectiveFrom("OKS121_PPUT"), ifMatchVersion: etag),
            "/api/admin/entitlement-configs/{configId}", "put");

        var putRoot = JsonDocument.Parse(putBody).RootElement;
        S118ContractAssert.AssertExactKeySet(putRoot, ChildEntitlementKeys, "primary PUT 200 (S121, effectiveFrom omitted)");
        Assert.Equal(configId, putRoot.GetProperty("configId").GetGuid()); // Case C: the SAME row
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());
        Assert.Equal(3.0m, putRoot.GetProperty("annualQuota").GetDecimal());
        Assert.True(putRoot.GetProperty("fullDayOnly").GetBoolean());
        Assert.Equal(Today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            putRoot.GetProperty("effectiveFrom").GetString()); // server-owned today
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    /// <summary>Create a DRAFT parent agreement config through the REAL endpoint; returns its
    /// configId. A single config per (code, okVersion) keeps the child surface editable.</summary>
    private async Task<Guid> CreateParentAsync(HttpClient client, string agreementCode)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/agreement-configs", ParentConfigJson(agreementCode)));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Parent config create for {agreementCode} returned {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("configId").GetGuid();
    }

    private Task<long> CountChildRowsAsync(string parentAgreementCode)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE agreement_code = @ac",
            ("ac", parentAgreementCode));

    private async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // ─────────────────────────────── request bodies (invariant JSON) ───────────────────────────────

    /// <summary>The full AgreementConfigRequest body (all C#-required members) for the
    /// child-surface parent.</summary>
    private static string ParentConfigJson(string agreementCode)
        => $$"""
           {
             "agreementCode": "{{agreementCode}}", "okVersion": "{{OkVersion}}",
             "description": "S121 kontrakttest", "normModel": "WEEKLY_HOURS",
             "weeklyNormHours": 37.0, "normPeriodWeeks": 4, "annualNormHours": 1924.0,
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
             "travelTimeEnabled": true, "workingTravelRate": 1.00, "nonWorkingTravelRate": 0.50
           }
           """;

    /// <summary>Child body — <c>fullDayOnly</c> PRESENT (ruling #3), <c>effectiveFrom</c>
    /// OMITTED (ruling #1). resetMonth/accrualModel invariant (the immutability guard).</summary>
    private static string ChildJson(
        string entitlementType, string annualQuota, string fullDayOnly, string isPerEpisode = "false")
        => $$"""
           { "entitlementType": "{{entitlementType}}", "annualQuota": {{annualQuota}},
             "accrualModel": "IMMEDIATE", "resetMonth": 1, "carryoverMax": 0,
             "proRateByPartTime": true, "isPerEpisode": {{isPerEpisode}},
             "description": "S121 kontrakttest", "fullDayOnly": {{fullDayOnly}} }
           """;

    /// <summary>The pre-S121 FE's exact defective shape: NO <c>fullDayOnly</c> (and no
    /// <c>effectiveFrom</c>) — must die at the binder, not the guard.</summary>
    private static string ChildJsonOmittingFullDayOnly(string entitlementType)
        => $$"""
           { "entitlementType": "{{entitlementType}}", "annualQuota": 2.0,
             "accrualModel": "IMMEDIATE", "resetMonth": 1, "carryoverMax": 0,
             "proRateByPartTime": true, "isPerEpisode": false,
             "description": "S121 udeladt flag" }
           """;

    /// <summary>Primary POST body — CARE_DAY (forced true); effectiveFrom omitted ⇒ today.</summary>
    private static string PrimaryCreateJson(string okVersion)
        => $$"""
           { "entitlementType": "CARE_DAY", "agreementCode": "{{PrimaryAgreementCode}}",
             "okVersion": "{{okVersion}}", "annualQuota": 2.0, "accrualModel": "IMMEDIATE",
             "resetMonth": 1, "carryoverMax": 0, "proRateByPartTime": true, "isPerEpisode": false,
             "description": "S121 omsorgsdage", "fullDayOnly": true }
           """;

    /// <summary>Primary PUT body — <c>effectiveFrom</c> OMITTED (ruling #1 on the third PUT);
    /// quota edited 2.0 → 3.0; the binder-required flag present.</summary>
    private static string PrimaryPutJsonOmittingEffectiveFrom(string okVersion)
        => $$"""
           { "entitlementType": "CARE_DAY", "agreementCode": "{{PrimaryAgreementCode}}",
             "okVersion": "{{okVersion}}", "annualQuota": 3.0, "accrualModel": "IMMEDIATE",
             "resetMonth": 1, "carryoverMax": 0, "proRateByPartTime": true, "isPerEpisode": false,
             "description": "S121 omsorgsdage (redigeret)", "fullDayOnly": true }
           """;
}
