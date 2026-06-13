using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S73 / TASK-7301 — the ADMIN construction-enforcement pins for the full-day-only flag
/// (SPRINT-73 R2, the S68-B1 uniform-by-construction lesson; owner ruling D-A 2026-06-13).
/// WAF&lt;Program&gt; + admin-token pattern from <see cref="EntitlementConfigEndpointTests"/>.
///
/// <list type="bullet">
///   <item><b>POST/PUT guard</b> — a CARE_DAY/SENIOR_DAY config write whose
///     <c>fullDayOnly</c> is false OR ABSENT → 422 (the second enforcement layer on TOP of
///     the DB CHECK); nothing persists. Type-scoped: a CHILD_SICK write without the flag is
///     untouched by the guard (CHILD_SICK stays hours-based per D-A).</item>
///   <item><b>Version-survival (the BACKEND pin)</b> — an admin PUT editing an UNRELATED
///     field (description) produces a Case-B successor whose <c>full_day_only</c> persists
///     TRUE, end-to-end: response DTO, the new live DB row, AND the
///     <c>EntitlementConfigCreated</c> / <c>EntitlementConfigSuperseded</c> outbox payloads
///     (the R2 additive-nullable event extension).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EntitlementConfigFullDayOnlyAdminTests : IAsyncLifetime
{
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

    // ════════════════════════════════════════════════════════════════════════
    // POST — flag false / absent → 422; flag true → 201 with the flag everywhere.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_CareDay_FlagFalse_Returns422_NothingPersisted()
    {
        var client = AdminClient();
        var fakeOk = "OK_S73FDO_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var rsp = await client.PostAsJsonAsync("/api/admin/entitlement-configs",
            CareDayPostBody(fakeOk, fullDayOnly: false));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CARE_DAY", body.GetProperty("entitlementType").GetString());
        Assert.False(body.GetProperty("suppliedFullDayOnly").GetBoolean());
        Assert.Equal(0, await CountRowsAsync(fakeOk));
    }

    [Fact]
    public async Task Post_SeniorDay_FlagAbsent_Returns422_NothingPersisted()
    {
        var client = AdminClient();
        var fakeOk = "OK_S73FDO_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // The flag is deliberately ABSENT from the body — the DTO defaults it to false.
        var rsp = await client.PostAsJsonAsync("/api/admin/entitlement-configs", new
        {
            entitlementType = "SENIOR_DAY",
            agreementCode = "AC",
            okVersion = fakeOk,
            annualQuota = 2m,
            accrualModel = "IMMEDIATE",
            resetMonth = 1,
            carryoverMax = 0m,
            proRateByPartTime = false,
            isPerEpisode = false,
            minAge = (int?)62,
            description = "s73-absent-flag",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.Equal(0, await CountRowsAsync(fakeOk));
    }

    [Fact]
    public async Task Post_CareDay_FlagTrue_Created_FlagOnRowResponseAndEventPayload()
    {
        var client = AdminClient();
        var fakeOk = "OK_S73FDO_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var rsp = await client.PostAsJsonAsync("/api/admin/entitlement-configs",
            CareDayPostBody(fakeOk, fullDayOnly: true));

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("fullDayOnly").GetBoolean()); // the response DTO serves it

        // The DB row carries TRUE.
        Assert.Equal(true, await ScalarAsync(
            "SELECT full_day_only FROM entitlement_configs WHERE ok_version = @p0", fakeOk));

        // The EntitlementConfigCreated outbox payload carries the additive-nullable field.
        var payloadFlag = await ScalarAsync(
            """
            SELECT event_payload ->> 'fullDayOnly' FROM outbox_events
            WHERE stream_id = @p0 AND event_type = 'EntitlementConfigCreated'
            """, $"entitlement-config-CARE_DAY-AC-{fakeOk}");
        Assert.Equal("true", (string?)payloadFlag);
    }

    /// <summary>Type-scoped: CHILD_SICK stays hours-based per D-A — a POST WITHOUT the flag
    /// is not touched by the guard (201, row FALSE).</summary>
    [Fact]
    public async Task Post_ChildSick_FlagAbsent_NotGuarded_CreatedWithFalse()
    {
        var client = AdminClient();
        var fakeOk = "OK_S73FDO_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var rsp = await client.PostAsJsonAsync("/api/admin/entitlement-configs", new
        {
            entitlementType = "CHILD_SICK",
            agreementCode = "AC",
            okVersion = fakeOk,
            annualQuota = 1m,
            accrualModel = "IMMEDIATE",
            resetMonth = 1,
            carryoverMax = 0m,
            proRateByPartTime = false,
            isPerEpisode = true,
            minAge = (int?)null,
            description = "s73-child-sick-unguarded",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("fullDayOnly").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════
    // PUT — flag false/absent → 422; the unrelated-field VERSION-SURVIVAL pin.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_CareDay_FlagFalse_Returns422_LiveRowUnchanged()
    {
        var client = AdminClient();
        var (configId, version) = await ReadSeededConfigAsync(client, "CARE_DAY", "PROSA", "OK26");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var rsp = await PutAsync(client, configId, "CARE_DAY", "PROSA", "OK26",
            annualQuota: 2m, description: "s73-unrule-attempt",
            effectiveFrom: today, ifMatch: version, fullDayOnly: false, minAge: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CARE_DAY", body.GetProperty("entitlementType").GetString());

        // The live seed row is untouched (still one open row, flag TRUE).
        Assert.Equal(true, await ScalarAsync(
            """
            SELECT full_day_only FROM entitlement_configs
            WHERE entitlement_type = 'CARE_DAY' AND agreement_code = 'PROSA'
              AND ok_version = 'OK26' AND effective_to IS NULL
            """));
    }

    /// <summary>
    /// The R2 BACKEND version-survival pin: an admin PUT editing an UNRELATED field
    /// (description only) on the seeded SENIOR_DAY/AC/OK24 row — with the editor's
    /// round-tripped <c>fullDayOnly: true</c> — produces a Case-B successor whose flag
    /// persists TRUE on the response, the new live row, AND both event payloads.
    /// </summary>
    [Fact]
    public async Task Put_UnrelatedFieldEdit_SuccessorKeepsFlagTrue_EndToEnd()
    {
        var client = AdminClient();
        var (configId, version) = await ReadSeededConfigAsync(client, "SENIOR_DAY", "AC", "OK24");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var rsp = await PutAsync(client, configId, "SENIOR_DAY", "AC", "OK24",
            annualQuota: 2m, description: "s73-unrelated-field-edit",
            effectiveFrom: today, ifMatch: version, fullDayOnly: true, minAge: 62);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("fullDayOnly").GetBoolean());
        Assert.Equal("s73-unrelated-field-edit", body.GetProperty("description").GetString());

        // The successor (live) row carries TRUE; the closed predecessor stays TRUE.
        Assert.Equal(true, await ScalarAsync(
            """
            SELECT full_day_only FROM entitlement_configs
            WHERE entitlement_type = 'SENIOR_DAY' AND agreement_code = 'AC'
              AND ok_version = 'OK24' AND effective_to IS NULL
            """));
        Assert.Equal(true, await ScalarAsync(
            """
            SELECT full_day_only FROM entitlement_configs
            WHERE entitlement_type = 'SENIOR_DAY' AND agreement_code = 'AC'
              AND ok_version = 'OK24' AND effective_to IS NOT NULL
            """));

        // Both dual-emission payloads carry the additive-nullable field (R2).
        var streamId = "entitlement-config-SENIOR_DAY-AC-OK24";
        Assert.Equal("true", (string?)await ScalarAsync(
            """
            SELECT event_payload ->> 'fullDayOnly' FROM outbox_events
            WHERE stream_id = @p0 AND event_type = 'EntitlementConfigCreated'
            ORDER BY outbox_id DESC LIMIT 1
            """, streamId));
        Assert.Equal("true", (string?)await ScalarAsync(
            """
            SELECT event_payload ->> 'fullDayOnly' FROM outbox_events
            WHERE stream_id = @p0 AND event_type = 'EntitlementConfigSuperseded'
            ORDER BY outbox_id DESC LIMIT 1
            """, streamId));
    }

    // ════════════════════════════════════════════════════════════════════════
    // M4(a) — the SECOND admin surface: /api/agreement-configs/{id}/entitlements
    //   (AgreementEntitlementEndpoints) shares the SAME FullDayOnlyGuard predicate.
    //   The two-surface invariant is pinned so a guard regression on one surface
    //   cannot pass green on the other (the wiring-drift class this sprint closes).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Second surface POST — a CARE_DAY child entitlement with the flag FALSE is
    /// rejected by the shared guard (which fires before any natural-key lock) → 422.</summary>
    [Fact]
    public async Task SubResource_Post_CareDay_FlagFalse_Returns422()
    {
        var client = AdminClient();
        var parentId = await SeedAgreementConfigAsync();

        var rsp = await client.PostAsJsonAsync(
            $"/api/agreement-configs/{parentId}/entitlements",
            ChildEntitlementBody("CARE_DAY", fullDayOnly: false));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CARE_DAY", body.GetProperty("entitlementType").GetString());
        Assert.False(body.GetProperty("suppliedFullDayOnly").GetBoolean());
    }

    /// <summary>Second surface POST — a SENIOR_DAY child entitlement with the flag ABSENT (the
    /// DTO defaults it to false) is rejected → 422.</summary>
    [Fact]
    public async Task SubResource_Post_SeniorDay_FlagAbsent_Returns422()
    {
        var client = AdminClient();
        var parentId = await SeedAgreementConfigAsync();

        var rsp = await client.PostAsJsonAsync(
            $"/api/agreement-configs/{parentId}/entitlements", new
            {
                entitlementType = "SENIOR_DAY",
                annualQuota = 2m,
                accrualModel = "IMMEDIATE",
                resetMonth = 1,
                carryoverMax = 0m,
                proRateByPartTime = false,
                isPerEpisode = false,
                minAge = (int?)62,
                description = "s73-subresource-absent-flag",
                // fullDayOnly deliberately ABSENT.
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>Second surface POST — a CARE_DAY child entitlement with the flag TRUE is
    /// created (201) and the live row carries full_day_only TRUE.</summary>
    [Fact]
    public async Task SubResource_Post_CareDay_FlagTrue_Created_RowCarriesTrue()
    {
        var client = AdminClient();
        var (parentId, code, version) = await SeedAgreementConfigWithCodeAsync();

        var rsp = await client.PostAsJsonAsync(
            $"/api/agreement-configs/{parentId}/entitlements",
            ChildEntitlementBody("CARE_DAY", fullDayOnly: true));

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("fullDayOnly").GetBoolean());

        Assert.Equal(true, await ScalarAsync(
            """
            SELECT full_day_only FROM entitlement_configs
            WHERE entitlement_type = 'CARE_DAY' AND agreement_code = @p0
              AND ok_version = @p1 AND effective_to IS NULL
            """, code, version));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static object ChildEntitlementBody(string entitlementType, bool fullDayOnly)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new
        {
            entitlementType,
            annualQuota = 2m,
            accrualModel = "IMMEDIATE",
            resetMonth = 1,
            carryoverMax = 0m,
            proRateByPartTime = false,
            isPerEpisode = false,
            minAge = (int?)null,
            description = "s73-subresource-test",
            fullDayOnly,
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        };
    }

    /// <summary>Seeds a single ACTIVE agreement config with a unique code (so siblingConfigs
    /// count is 1 — entitlements are editable) and returns its parent configId.</summary>
    private async Task<Guid> SeedAgreementConfigAsync()
        => (await SeedAgreementConfigWithCodeAsync()).ParentId;

    private async Task<(Guid ParentId, string Code, string OkVersion)> SeedAgreementConfigWithCodeAsync()
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var code = "S73SUB_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var entity = NewAgreementConfig(code);
        var parentId = await repo.CreateAsync(entity, "ACTIVE");
        return (parentId, code, entity.OkVersion);
    }

    private static AgreementConfigEntity NewAgreementConfig(string code) => new()
    {
        ConfigId = Guid.Empty,
        AgreementCode = code,
        OkVersion = "OK24",
        Status = AgreementConfigStatus.ACTIVE,
        WeeklyNormHours = 37m,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 100m,
        FlexCarryoverMax = 50m,
        HasOvertime = true,
        HasMerarbejde = false,
        OvertimeThreshold50 = 37m,
        OvertimeThreshold100 = 40m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.5m,
        WeekendSaturdayRate = 1.5m,
        WeekendSundayRate = 2m,
        HolidayRate = 2m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.33m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 3m,
        CallInRate = 1m,
        TravelTimeEnabled = false,
        WorkingTravelRate = 1m,
        NonWorkingTravelRate = 0.5m,
        MaxDailyHours = 13m,
        MinimumRestHours = 11m,
        RestPeriodDerogationAllowed = false,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
        DefaultCompensationModel = "UDBETALING",
        EmployeeCompensationChoice = false,
        MaxOvertimeHoursPerPeriod = 0m,
        OvertimeRequiresPreApproval = false,
        CreatedBy = "s73-qa",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "s73-subresource-parent",
    };

    private static object CareDayPostBody(string fakeOk, bool fullDayOnly)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new
        {
            entitlementType = "CARE_DAY",
            agreementCode = "AC",
            okVersion = fakeOk,
            annualQuota = 2m,
            accrualModel = "IMMEDIATE",
            resetMonth = 1,
            carryoverMax = 0m,
            proRateByPartTime = false,
            isPerEpisode = false,
            minAge = (int?)null,
            description = "s73-full-day-only-admin-test",
            fullDayOnly,
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        };
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, Guid configId, string entitlementType, string agreementCode,
        string okVersion, decimal annualQuota, string? description, DateOnly effectiveFrom,
        long ifMatch, bool fullDayOnly, int? minAge)
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
                accrualModel = "IMMEDIATE",
                resetMonth = 1,
                carryoverMax = 0m,
                proRateByPartTime = false,
                isPerEpisode = false,
                minAge,
                description,
                fullDayOnly,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        return await client.SendAsync(req);
    }

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
                // The list response itself serves the flag (R3 admin surface) — assert TRUE
                // for the D-A types right where we read the seed row.
                Assert.True(item.GetProperty("fullDayOnly").GetBoolean());
                return (item.GetProperty("configId").GetGuid(),
                        item.GetProperty("version").GetInt64());
            }
        }
        throw new InvalidOperationException(
            $"Could not find seeded config ({entitlementType}, {agreementCode}, {okVersion}).");
    }

    private async Task<int> CountRowsAsync(string okVersion)
    {
        return Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM entitlement_configs WHERE ok_version = @p0", okVersion));
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

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());
        return client;
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
            employeeId: "ADMIN_S73_QA",
            name: "S73 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC");
    }
}
