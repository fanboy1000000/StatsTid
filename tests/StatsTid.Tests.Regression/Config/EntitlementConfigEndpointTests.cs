using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S30 / TASK-3010 D-tests — admin-CRUD HTTP-level wire contract for
/// <see cref="EntitlementConfigEndpoints"/>. WAF&lt;Program&gt;-based per TASK-3001b unblock
/// (mirrors S29 <see cref="WageTypeMappingEndpointTests"/> pattern).
///
/// <list type="bullet">
///   <item><b>GET list</b>: returns all 30 live seed configs.</item>
///   <item><b>GET by id</b>: returns one config with <c>ETag: "&lt;version&gt;"</c> header.</item>
///   <item><b>POST</b>: creates a new natural-key row (Case A); 201 + ETag header; outbox +
///   audit row emitted in the same atomic tx (ADR-018 D3).</item>
///   <item><b>PUT stale If-Match</b>: 412.</item>
///   <item><b>PUT missing If-Match</b>: 428.</item>
///   <item><b>PUT changing reset_month</b>: 422 with structured immutable-fields error body
///   (Q1 sub-fork (i) freeze).</item>
///   <item><b>PUT backdated effective_from</b>: 422 (cycle-3 same-day-only-edit validator).</item>
///   <item><b>DELETE</b>: 204 + soft-close (effective_to=today) + audit DELETED + outbox
///   SoftDeleted event.</item>
/// </list>
///
/// <para>
/// JWT minting follows the dev-fallback signing key pattern verbatim from
/// <see cref="WageTypeMappingEndpointTests"/>. The <c>GlobalAdminOnly</c> policy requires the
/// GlobalAdmin role on the JWT — no org-scope required (admin-strict per ADR-019).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EntitlementConfigEndpointTests : IAsyncLifetime
{
    // Verbatim from JwtValidationSetup.DevFallbackSigningKey — same approach as
    // WageTypeMappingEndpointTests. Hosting env defaults to Development under
    // WebApplicationFactory<Program>, so the dev-fallback signing key fires.
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #1 — GET /api/admin/entitlement-configs lists all live (open) configs.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Get_List_ReturnsAllLiveSeedConfigs()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var rsp = await client.GetAsync("/api/admin/entitlement-configs");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var arr = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        // S37/TASK-3701 (3eea4f5): AC_RESEARCH + AC_TEACHING variants added (+20 rows).
        // 5 entitlement_types × 5 agreement_codes × 2 ok_versions = 50 live seed rows.
        var count = arr.EnumerateArray().Count();
        Assert.Equal(50, count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #2 — GET by id returns one config + ETag header.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Get_ById_ReturnsConfigWithETagHeader()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Read one configId from the list endpoint.
        var listRsp = await client.GetAsync("/api/admin/entitlement-configs");
        Assert.Equal(HttpStatusCode.OK, listRsp.StatusCode);
        var arr = await listRsp.Content.ReadFromJsonAsync<JsonElement>();
        var first = arr.EnumerateArray().First();
        var configIdString = first.GetProperty("configId").GetString();
        Assert.False(string.IsNullOrEmpty(configIdString));

        // GET by id.
        var rsp = await client.GetAsync($"/api/admin/entitlement-configs/{configIdString}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        // Seed rows start at version=1 → ETag "1".
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #3 — POST creates new config; 201 + ETag + audit row + outbox event.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Post_CreatesNewConfig_WithETagAndAuditAndOutboxEvent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Use a synthetic natural key that doesn't exist in the seed.
        var fakeOk = "OK_S30POST_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var body = new
        {
            entitlementType = "VACATION",
            agreementCode = "AC",
            okVersion = fakeOk,
            annualQuota = 22m,
            accrualModel = "IMMEDIATE",
            resetMonth = 9,
            carryoverMax = 5m,
            proRateByPartTime = true,
            isPerEpisode = false,
            minAge = (int?)null,
            description = "post-test",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        };
        var rsp = await client.PostAsJsonAsync("/api/admin/entitlement-configs", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);

        // Verify DB row + audit row + outbox event with the natural-key stream.
        var streamId = $"entitlement-config-VACATION-AC-{fakeOk}";

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // 1 row in entitlement_configs for this natural key.
        await using (var dbCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM entitlement_configs
            WHERE entitlement_type = 'VACATION' AND agreement_code = 'AC' AND ok_version = @ok
            """, conn))
        {
            dbCmd.Parameters.AddWithValue("ok", fakeOk);
            Assert.Equal(1L, Convert.ToInt64(await dbCmd.ExecuteScalarAsync()));
        }

        // 1 CREATED audit row.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM entitlement_config_audit
            WHERE entitlement_type = 'VACATION' AND agreement_code = 'AC' AND ok_version = @ok
              AND action = 'CREATED'
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("ok", fakeOk);
            Assert.Equal(1L, Convert.ToInt64(await auditCmd.ExecuteScalarAsync()));
        }

        // 1 EntitlementConfigCreated outbox event on the natural-key stream.
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @sid AND event_type = 'EntitlementConfigCreated'
            """, conn))
        {
            outboxCmd.Parameters.AddWithValue("sid", streamId);
            Assert.Equal(1L, Convert.ToInt64(await outboxCmd.ExecuteScalarAsync()));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #3b — POST a VACATION config with a non-9 reset_month → 422 (S68 Step-7a
    // Codex c2 B1): the statutory 1-Sep ferieår pins VACATION reset_month to 9, on which
    // the §21/§24 settlement boundary depends. The endpoint rejects with a friendly 422
    // (the DB CHECK is the data-layer backstop), so the close poller can never read a
    // VACATION reset_month that diverges from the dated-snapshot valuation.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Post_VacationConfig_NonNineResetMonth_Returns422()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var fakeOk = "OK_S68RESET_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var body = new
        {
            entitlementType = "VACATION",
            agreementCode = "AC",
            okVersion = fakeOk,
            annualQuota = 25m,
            accrualModel = "MONTHLY_ACCRUAL",
            resetMonth = 1, // illegal for VACATION (the calendar-year reset belongs to CARE_DAY/SENIOR_DAY).
            carryoverMax = 5m,
            proRateByPartTime = false,
            isPerEpisode = false,
            minAge = (int?)null,
            description = "illegal-reset-month",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        };
        var rsp = await client.PostAsJsonAsync("/api/admin/entitlement-configs", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        // No row persisted (neither the endpoint guard nor the DB CHECK let it through).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var dbCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM entitlement_configs WHERE ok_version = @ok", conn);
        dbCmd.Parameters.AddWithValue("ok", fakeOk);
        Assert.Equal(0L, Convert.ToInt64(await dbCmd.ExecuteScalarAsync()));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #4 — PUT with stale If-Match → 412.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Put_StaleIfMatch_Returns412()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Read a seeded config: SENIOR_DAY/AC/OK24 (annual_quota=0; min_age=60).
        var (configId, version) = await ReadSeededConfigAsync(client, "SENIOR_DAY", "AC", "OK24");
        Assert.Equal(1L, version);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // PUT with stale If-Match (version - 1) = "0".
        var stale = version - 1;
        var rsp = await PutAsync(client, configId,
            entitlementType: "SENIOR_DAY",
            agreementCode: "AC",
            okVersion: "OK24",
            annualQuota: 1m,
            accrualModel: "IMMEDIATE",
            resetMonth: 1,
            carryoverMax: 0m,
            proRateByPartTime: false,
            isPerEpisode: false,
            minAge: 60,
            description: "stale-test",
            effectiveFrom: today,
            ifMatchValue: stale.ToString());

        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(stale, json.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(version, json.GetProperty("actualVersion").GetInt64());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #5 — PUT without If-Match → 428.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Put_MissingIfMatch_Returns428()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var (configId, _) = await ReadSeededConfigAsync(client, "VACATION", "HK", "OK26");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var rsp = await PutAsync(client, configId,
            entitlementType: "VACATION",
            agreementCode: "HK",
            okVersion: "OK26",
            annualQuota: 26m,
            accrualModel: "IMMEDIATE",
            resetMonth: 9,
            carryoverMax: 5m,
            proRateByPartTime: true,
            isPerEpisode: false,
            minAge: null,
            description: "no-precondition",
            effectiveFrom: today,
            ifMatchValue: null);
        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #6 — PUT changing reset_month → 422 with structured immutable-fields body.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Put_ChangesResetMonth_Returns422_WithImmutableErrorBody()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // VACATION/AC/OK26 has reset_month=9; try to change to 1 → 422.
        var (configId, version) = await ReadSeededConfigAsync(client, "VACATION", "AC", "OK26");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var rsp = await PutAsync(client, configId,
            entitlementType: "VACATION",
            agreementCode: "AC",
            okVersion: "OK26",
            annualQuota: 25m,
            accrualModel: "IMMEDIATE",
            resetMonth: 1, // ← changed from 9 → 1
            carryoverMax: 5m,
            proRateByPartTime: true,
            isPerEpisode: false,
            minAge: null,
            description: "reset-month-edit-attempt",
            effectiveFrom: today,
            ifMatchValue: version.ToString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        // Endpoint body shape: { error, supplied: { reset_month, accrual_model }, immutable: [...] }
        Assert.True(json.TryGetProperty("error", out _));
        Assert.True(json.TryGetProperty("supplied", out var supplied));
        Assert.Equal(1, supplied.GetProperty("reset_month").GetInt32());
        Assert.True(json.TryGetProperty("immutable", out var immutable));
        var immutableFields = immutable.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("reset_month", immutableFields);
        Assert.Contains("accrual_model", immutableFields);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #7 — PUT with backdated effective_from → 422 (same-day-only-edit validator).
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Put_BackdatedEffectiveFrom_Returns422_WithSuppliedAndTodayInBody()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var (configId, version) = await ReadSeededConfigAsync(client, "CARE_DAY", "HK", "OK24");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var backdated = today.AddDays(-7);

        var rsp = await PutAsync(client, configId,
            entitlementType: "CARE_DAY",
            agreementCode: "HK",
            okVersion: "OK24",
            annualQuota: 2m,
            accrualModel: "IMMEDIATE",
            resetMonth: 1,
            carryoverMax: 0m,
            proRateByPartTime: false,
            isPerEpisode: false,
            minAge: null,
            description: "backdated-attempt",
            effectiveFrom: backdated,
            ifMatchValue: version.ToString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(backdated.ToString("yyyy-MM-dd"),
            json.GetProperty("suppliedEffectiveFrom").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"),
            json.GetProperty("today").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #8 — DELETE soft-deletes; 204 + audit DELETED + outbox SoftDeleted.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Delete_SoftDeletes_Returns204_AuditAndOutboxEvent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // CHILD_SICK/PROSA/OK26 (annual_quota=3; min_age=null).
        var (configId, version) = await ReadSeededConfigAsync(client, "CHILD_SICK", "PROSA", "OK26");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/admin/entitlement-configs/{configId}");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var rsp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        // Verify soft-close (effective_to=today) + audit DELETED + outbox SoftDeleted.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var rowCmd = new NpgsqlCommand(
            "SELECT effective_to FROM entitlement_configs WHERE config_id = @id", conn))
        {
            rowCmd.Parameters.AddWithValue("id", configId);
            var raw = await rowCmd.ExecuteScalarAsync();
            Assert.NotNull(raw);
            Assert.NotEqual(DBNull.Value, raw);
            var actualClose = DateOnly.FromDateTime((DateTime)raw!);
            Assert.Equal(today, actualClose);
        }

        await using (var auditCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM entitlement_config_audit WHERE config_id = @id AND action = 'DELETED'", conn))
        {
            auditCmd.Parameters.AddWithValue("id", configId);
            Assert.Equal(1L, Convert.ToInt64(await auditCmd.ExecuteScalarAsync()));
        }

        var streamId = "entitlement-config-CHILD_SICK-PROSA-OK26";
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @sid AND event_type = 'EntitlementConfigSoftDeleted'
            """, conn))
        {
            outboxCmd.Parameters.AddWithValue("sid", streamId);
            Assert.Equal(1L, Convert.ToInt64(await outboxCmd.ExecuteScalarAsync()));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #9 — Step 7a P1 fix: after admin Case-B supersession of
    // VACATION/AC/OK24, GET balance summary returns EXACTLY ONE entitlement
    // entry of type VACATION — not two. Locks down the `EffectiveTo IS NULL`
    // filter at BalanceEndpoints.cs that S30 missed; pre-fix this test fails
    // with vacationCount == 2 (closed predecessor row leaks into the loop).
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task BalanceSummary_AfterSupersession_ReturnsExactlyOneVacationEntitlement()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Seed VACATION/AC/OK24 has effective_from='0001-01-01'. PUT today triggers
        // ADR-020 D2 Case B: predecessor closes at today-1, new row opens at today.
        var (configId, version) = await ReadSeededConfigAsync(client, "VACATION", "AC", "OK24");
        Assert.Equal(1L, version);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // ADR-030/S60 + S62 accrual guard; S64 F4-5 ruling. The seeded VACATION/AC/OK24 row
        // flipped to accrual_model='MONTHLY_ACCRUAL' in S60 (ADR-030 samtidighedsferie). The PUT
        // immutability guard (EntitlementConfigEndpoints reset_month/accrual_model freeze, Q1
        // sub-fork (i)) returns 422 if the body's accrual_model differs from the predecessor's.
        // The original 'IMMEDIATE' now diverges from the MONTHLY_ACCRUAL seed → 422. Match the
        // predecessor's accrual_model (and the unchanged reset_month=9) so the supersession is
        // valid; the test's INTENT (exactly one live VACATION after Case-B supersession) is
        // preserved — the annual_quota edit (25→27) still forces a new live row.
        var putRsp = await PutAsync(client, configId,
            entitlementType: "VACATION",
            agreementCode: "AC",
            okVersion: "OK24",
            annualQuota: 27m, // change from 25 — still forces a Case-B supersession
            accrualModel: "MONTHLY_ACCRUAL", // must match the S60 seed (immutable field)
            resetMonth: 9,
            carryoverMax: 5m,
            proRateByPartTime: true,
            isPerEpisode: false,
            minAge: null,
            description: "step7a-fix-test",
            effectiveFrom: today,
            ifMatchValue: version.ToString());
        Assert.Equal(HttpStatusCode.OK, putRsp.StatusCode);

        // Verify 2 rows now exist for the natural key (closed predecessor + new live).
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cnt = new NpgsqlCommand(
                "SELECT COUNT(*) FROM entitlement_configs WHERE entitlement_type='VACATION' AND agreement_code='AC' AND ok_version='OK24'",
                conn);
            Assert.Equal(2L, Convert.ToInt64(await cnt.ExecuteScalarAsync()));
        }

        // Switch to employee token. emp001 is seeded with agreement_code='AC', ok_version='OK24'.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintEmployeeToken("emp001", "AC"));

        var balanceRsp = await client.GetAsync(
            $"/api/balance/emp001/summary?year={today.Year}&month={today.Month}");
        Assert.Equal(HttpStatusCode.OK, balanceRsp.StatusCode);

        var json = await balanceRsp.Content.ReadFromJsonAsync<JsonElement>();
        var entitlements = json.GetProperty("entitlements");

        var vacationCount = 0;
        foreach (var ent in entitlements.EnumerateArray())
        {
            if (ent.GetProperty("type").GetString() == "VACATION")
                vacationCount++;
        }
        Assert.Equal(1, vacationCount);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a seeded config by natural key via the list endpoint and returns
    /// <c>(configId, version)</c>. Filters the list response (vs. issuing a fresh DB read)
    /// because the list endpoint is part of the SUT.
    /// </summary>
    private static async Task<(Guid ConfigId, long Version)> ReadSeededConfigAsync(
        HttpClient client, string entitlementType, string agreementCode, string okVersion)
    {
        var rsp = await client.GetAsync("/api/admin/entitlement-configs");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var arr = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.GetProperty("entitlementType").GetString() == entitlementType
                && item.GetProperty("agreementCode").GetString() == agreementCode
                && item.GetProperty("okVersion").GetString() == okVersion)
            {
                return (item.GetProperty("configId").GetGuid(),
                        item.GetProperty("version").GetInt64());
            }
        }
        throw new InvalidOperationException(
            $"Could not find seeded config ({entitlementType}, {agreementCode}, {okVersion}).");
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, Guid configId,
        string entitlementType, string agreementCode, string okVersion,
        decimal annualQuota, string accrualModel, int resetMonth, decimal carryoverMax,
        bool proRateByPartTime, bool isPerEpisode, int? minAge, string? description,
        DateOnly effectiveFrom, string? ifMatchValue)
    {
        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/entitlement-configs/{configId}")
        {
            Content = JsonContent.Create(new
            {
                entitlementType,
                agreementCode,
                okVersion,
                annualQuota,
                accrualModel,
                resetMonth,
                carryoverMax,
                proRateByPartTime,
                isPerEpisode,
                minAge,
                description,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            }),
        };
        if (ifMatchValue is not null)
        {
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchValue}\"");
        }
        return await client.SendAsync(req);
    }

    private static string MintAdminToken()
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: "ADMIN_S30_QA",
            name: "S30 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC");
    }

    private static string MintEmployeeToken(string employeeId, string agreementCode)
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: employeeId,
            name: employeeId,
            role: StatsTidRoles.Employee,
            agreementCode: agreementCode);
    }
}
