using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.UserAgreementCode;

/// <summary>
/// S34 / TASK-3414 — HTTP-surface determinism D-tests for past-period consumers
/// (refinement cycle 1 convergent BLOCKER 1 absorption — the gap is not just
/// PCS-replay; Balance/Skema/Overtime also read <c>users.agreement_code</c>
/// live for past-period queries and have the same class of bug).
///
/// <para>
/// <b>Test shape</b>. Each test seeds a backfilled <c>user_agreement_codes</c>
/// row at <c>effective_from='0001-01-01'</c> (via the
/// <c>UserAgreementCodeBackfillSeeder</c> at WAF startup), flips the user's
/// agreement_code from <c>AC</c> to <c>HK</c> today via the admin PUT path
/// (Case C cross-day supersession), then queries a past-month endpoint and
/// asserts the response is keyed on <c>AC</c> (the predecessor) — NOT the
/// live <c>HK</c>. The discriminator differs per endpoint:
/// <list type="bullet">
///   <item><b>Balance</b> — response carries <c>agreementCode</c> +
///     <c>hasMerarbejde</c> directly. AC.HasMerarbejde=true,
///     HK.HasMerarbejde=false; AC.HasOvertime=false, HK.HasOvertime=true.</item>
///   <item><b>Skema</b> — TimeEntryRegistered + AbsenceRegistered events
///     stamp the period-effective <c>AgreementCode</c>. After flip-to-HK,
///     a past-month save must emit events stamped <c>AgreementCode='AC'</c>
///     so payroll export effective-date lookup (ADR-018 D14) resolves cleanly.</item>
///   <item><b>Overtime</b> — past-period compensation-choice PUT exercises
///     a strong response-status discriminator. AC has
///     <c>EmployeeCompensationChoice=false</c>; HK has
///     <c>EmployeeCompensationChoice=true</c>. With AC effective at the
///     past-year boundary, the endpoint rejects 400 BadRequest with the
///     "Employee compensation choice is not enabled for this agreement"
///     literal at <c>OvertimeEndpoints.cs:566</c>. If the dated-lookup
///     cutover at <c>OvertimeEndpoints.cs:511-513</c> regressed to live
///     <c>user.AgreementCode</c> (HK after the flip), the response would be
///     200 OK — directly distinguishable from the AC path. The test uses a
///     dual-token-leg approach: the existing GlobalAdmin token flips AC→HK
///     via PUT <c>/api/admin/users/{userId}</c> (with TASK-3506
///     ETag/If-Match), then a freshly-minted Employee token (sub=emp001,
///     role=Employee, scope=org:STY01) acts as the employee for the
///     compensation-choice PUT under the endpoint's
///     <c>employeeId != actor.ActorId → 403</c> self-only gate.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>ADR-023 D3 graceful fallback</b>. All 3 consumers fall back to the live
/// cache when the dated lookup returns null (defensive — shouldn't happen
/// post-backfill but possible for users created after the period). The
/// fallback path is exercised when the test specifies a far-future query
/// date; here all 3 tests use past dates within the predecessor window.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementCodeHttpDeterminismTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey =
        "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Balance — past-month summary GET (TASK-3410)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// emp001 is seeded at <c>agreement_code='AC'</c> with the backfill seeder
    /// row at <c>effective_from='0001-01-01'</c>. Flip to <c>HK</c> today via
    /// PUT (Case C cross-day supersession). Past-month balance summary GET
    /// must surface <c>agreementCode='AC'</c> + <c>hasMerarbejde=true</c>
    /// (AC's values) — NOT <c>'HK'</c> + <c>false</c> (today's live cache).
    ///
    /// <para>
    /// Query a month two months prior to today so the predecessor's window
    /// ['0001-01-01', today) covers <c>monthStart</c> and the dated lookup
    /// returns the AC predecessor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Balance_PastMonthSummary_UsesPeriodEffectiveAgreementCode_NotLive()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Flip agreement code to HK today (Case C supersession).
        var flipRsp = await client.PutAsJsonAsync($"/api/admin/users/{userId}", new
        {
            agreementCode = "HK",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        });
        Assert.Equal(HttpStatusCode.OK, flipRsp.StatusCode);

        // Sanity: users.agreement_code cache is now HK; the live user_agreement_codes
        // row is HK; the closed predecessor is AC and covers monthStart in the past.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cacheCmd = new NpgsqlCommand(
                "SELECT agreement_code FROM users WHERE user_id = @id", conn);
            cacheCmd.Parameters.AddWithValue("id", userId);
            Assert.Equal("HK", (string?)await cacheCmd.ExecuteScalarAsync());
        }

        // Query a past month (two months ago).
        var pastMonth = today.AddMonths(-2);
        var rsp = await client.GetAsync(
            $"/api/balance/{userId}/summary?year={pastMonth.Year}&month={pastMonth.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        // The response's agreementCode field is the dated lookup result —
        // AC for past months (predecessor window covers them).
        Assert.Equal("AC", body.GetProperty("agreementCode").GetString());
        // AC.HasMerarbejde = true (CentralAgreementConfigs). HK.HasMerarbejde = false.
        Assert.True(body.GetProperty("hasMerarbejde").GetBoolean(),
            "Past-month balance summary must use AC's HasMerarbejde=true, not HK's false.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Skema — past-month POST save (TASK-3411)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// emp001 backfilled at AC. Flip to HK today. Past-month Skema POST save
    /// for the same employee must emit <c>TimeEntryRegistered</c> events
    /// stamped <c>AgreementCode='AC'</c> (the period-effective agreement,
    /// sourced via the dated lookup at <c>monthStart</c>) — NOT <c>HK</c>
    /// (today's live cache). This is the load-bearing payroll-export
    /// effective-date lookup determinism gap (ADR-018 D14).
    ///
    /// <para>
    /// The POST emits events on the <c>employee-{employeeId}</c> stream
    /// (SkemaEndpoints.cs:444 <c>$"employee-{employeeId}"</c>); we read back
    /// the latest <c>TimeEntryRegistered</c> event and assert its payload's
    /// <c>agreementCode</c> is <c>'AC'</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Skema_PastMonthSave_UsesPeriodEffectiveAgreementCode_NotLive()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Flip to HK today.
        var flipRsp = await client.PutAsJsonAsync($"/api/admin/users/{userId}", new
        {
            agreementCode = "HK",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        });
        Assert.Equal(HttpStatusCode.OK, flipRsp.StatusCode);

        // POST a save for a past month (two months ago).
        var pastMonthStart = new DateOnly(today.AddMonths(-2).Year, today.AddMonths(-2).Month, 1);
        // Pick a weekday within the past month so the entry stamps cleanly. Day-15
        // is always in the month and always a weekday (with most calendars; the
        // event stamping path doesn't care about weekday-ness — we just need a
        // valid date inside the past-month window).
        var entryDate = new DateOnly(pastMonthStart.Year, pastMonthStart.Month, 15);

        var saveRsp = await client.PostAsJsonAsync($"/api/skema/{userId}/save", new
        {
            year = pastMonthStart.Year,
            month = pastMonthStart.Month,
            entries = new[]
            {
                new
                {
                    date = entryDate.ToString("yyyy-MM-dd"),
                    projectCode = "GENERAL",
                    hours = 4.0m,
                },
            },
            absences = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.OK, saveRsp.StatusCode);

        // Read back the latest TimeEntryRegistered outbox event on the
        // employee-{employeeId} stream. Its payload's agreementCode must be
        // 'AC' (the predecessor period-effective value), NOT 'HK' (today's live).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'TimeEntryRegistered'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("streamId", $"employee-{userId}");
        var rawPayload = (string?)await cmd.ExecuteScalarAsync();
        Assert.False(string.IsNullOrEmpty(rawPayload),
            "POST /api/skema/{employeeId}/save must emit at least one TimeEntryRegistered event.");
        using var payloadDoc = JsonDocument.Parse(rawPayload!);
        Assert.Equal("AC",
            payloadDoc.RootElement.GetProperty("agreementCode").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // Overtime — past-period compensation-choice GET (TASK-3412)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// emp001 backfilled at AC. Flip to HK today (with TASK-3506 admin-strict
    /// ETag/If-Match flow). Then act as <c>emp001</c> (Employee role + matching
    /// org scope) and PUT a compensation-choice for a past year. The endpoint
    /// resolves the agreement at <c>periodYearStart = new DateOnly(periodYear,
    /// 1, 1)</c> via <c>userAgreementCodeRepo.GetByUserIdAtAsync</c>
    /// (OvertimeEndpoints.cs:559-562) — the dated lookup returns AC (the
    /// predecessor covers Jan 1 of last year), so the endpoint reads AC's
    /// config which has <c>EmployeeCompensationChoice=false</c>, and rejects
    /// the request with 400 BadRequest at line 566.
    ///
    /// <para>
    /// <b>Strong response-status discriminator</b>. If the S34 dated-lookup
    /// cutover at <c>OvertimeEndpoints.cs:511-513</c> (read leg) and its
    /// PUT-side symmetry at L559-562 regressed to <c>user.AgreementCode</c>
    /// (today's live HK after the flip), the endpoint would read HK's config
    /// which has <c>EmployeeCompensationChoice=true</c> and return 200 OK with
    /// the new balance row stamped. AC=400 vs HK=200 is a directly
    /// distinguishable HTTP status, NOT a side-channel — that's the strong
    /// discriminator this test pins.
    /// </para>
    ///
    /// <para>
    /// <b>Dual-token-leg approach</b>. The endpoint at
    /// <c>OvertimeEndpoints.cs:541-543</c> enforces
    /// <c>if (employeeId != actor.ActorId) return 403</c> with no admin
    /// bypass, so the global-admin token used for the AC→HK flip cannot be
    /// reused for the compensation-choice PUT. A fresh Employee token is
    /// minted via <see cref="MintEmployeeToken"/> with <c>sub=emp001</c>,
    /// <c>role=Employee</c>, and a scope covering org STY01 (emp001's
    /// primary_org_id per docker/postgres/init.sql L833).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Overtime_PastPeriodCompensationChoice_RejectsForACWithStrongDiscriminator()
    {
        var adminClient = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) Admin GET to capture ETag (TASK-3506 added the GET endpoint with
        //     ETag header stamped from the same atomic snapshot as the row).
        var getRsp = await adminClient.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // (2) Admin PUT flip AC→HK with If-Match (TASK-3506: admin-strict
        //     If-Match required; missing header would yield 428 not 200).
        var flipReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        flipReq.Headers.IfMatch.Add(etag!);
        var flipRsp = await adminClient.SendAsync(flipReq);
        Assert.Equal(HttpStatusCode.OK, flipRsp.StatusCode);

        // (3) Act as emp001 (Employee role + matching org) — PUT
        //     compensation-choice for a past year. STY01 is emp001's primary
        //     org per docker/postgres/init.sql L833.
        var employeeClient = AuthorizedClientFor(userId, "STY01");
        var pastYear = today.Year - 1;
        var rsp = await employeeClient.PutAsJsonAsync(
            $"/api/overtime/{userId}/compensation-choice",
            new { periodYear = pastYear, compensationModel = "UDBETALING" });

        // (4) Strong discriminator: AC has EmployeeCompensationChoice=false →
        //     400 BadRequest with the literal at OvertimeEndpoints.cs:566.
        //     If the cutover regressed to live HK (EmployeeCompensationChoice
        //     =true), the endpoint would return 200 OK with a new balance row.
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Employee compensation choice is not enabled for this agreement",
            body.GetProperty("error").GetString());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static string MintGlobalAdminToken()
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
            employeeId: "ADMIN_S34_QA",
            name: "S34 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    /// <summary>
    /// Mints an Employee-role JWT keyed on <paramref name="employeeId"/> with
    /// an ORG_ONLY scope covering <paramref name="orgId"/>. Used by
    /// <see cref="Overtime_PastPeriodCompensationChoice_RejectsForACWithStrongDiscriminator"/>
    /// (TASK-3508) to act as a real employee so the
    /// <c>employeeId != actor.ActorId → 403</c> self-only gate at
    /// <c>OvertimeEndpoints.cs:541-543</c> is satisfied (no admin bypass on
    /// that endpoint). Mirrors <see cref="MintGlobalAdminToken"/> shape;
    /// signs with the same <see cref="DevFallbackSigningKey"/>.
    /// </summary>
    private static string MintEmployeeToken(string employeeId, string orgId)
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
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private HttpClient AuthorizedClientFor(string employeeId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        return client;
    }
}
