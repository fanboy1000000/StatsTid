using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.UserAgreementCode;

/// <summary>
/// S34 / TASK-3414 — AdminEndpoints HTTP-level D-tests for TASK-3407
/// (PUT + POST <c>/api/admin/users</c>) versioned-history routing:
///
/// <list type="bullet">
///   <item><b>POST Case A</b> — new user → 6-way atomic INSERT including
///     <c>user_agreement_codes</c> Case A row + <c>UserAgreementCodeSeeded</c>
///     outbox event; login JWT works and
///     <see cref="EmploymentProfileResolver.GetByEmployeeIdAtAsync"/>(today)
///     returns the seeded code (Codex BLOCKER 2 absorption).</item>
///   <item><b>PUT validator backdated / future-dated</b> — both reject with 422
///     when <c>agreementCode</c> mutates (ADR-023 D8 same-day-only narrowing,
///     mirrors S33 employee-profile PUT validator).</item>
///   <item><b>PUT dual-emission Case C</b> — cross-day agreement_code change
///     emits BOTH <c>UserAgreementCodeChanged</c> AND
///     <c>UserAgreementCodeSuperseded</c> on the <c>user-{userId}</c> stream,
///     audit row carries <c>action='SUPERSEDED'</c> with populated
///     <c>version_before</c>/<c>version_after</c> columns (refinement cycle 1
///     Reviewer WARNING 4 + S25 publish-supersession precedent).</item>
/// </list>
///
/// <para>
/// HTTP-level via <see cref="StatsTidWebApplicationFactory"/> + <c>CreateClient()</c>
/// per S27 precedent. JWT signing uses the dev-fallback key per
/// <see cref="StatsTid.Tests.Regression.EmployeeProfile.EmployeeProfileLifecycleTests"/>
/// shape.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminEndpointsAgreementCodeTests : IAsyncLifetime
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
        // CreateClient triggers Program.cs host build → seeders run (including
        // UserAgreementCodeBackfillSeeder backfilling at effective_from='0001-01-01').
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // POST /api/admin/users — Case A INSERT + Seeded event (Codex BLOCKER 2)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Net-new admin-created user gets BOTH a <c>users</c> row AND a
    /// <c>user_agreement_codes</c> Case A INSERT row atomically; the POST emits
    /// <c>UserAgreementCodeSeeded</c> on the <c>user-{userId}</c> stream
    /// (matching the backfill seeder semantic — no predecessor). The seeded row's
    /// <c>effective_from</c> is today (admin-POST today-stamp convention per
    /// AdminEndpoints.cs:432) — strictly NOT '0001-01-01' which is the seeder
    /// bootstrap-only convention. Subsequent
    /// <see cref="EmploymentProfileResolver.GetByEmployeeIdAtAsync"/>(today)
    /// succeeds — proving the new row is reachable through the dated lookup
    /// path consumed by PCS / Compliance / etc.
    /// </summary>
    [Fact]
    public async Task AdminPostUser_NewUserGetsBothUsersRowAndUserAgreementCodesCaseAInsert_EmitsSeededEvent()
    {
        var client = AuthorizedClient();
        var newUserId = "emp_s34_post_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S34 Post Case A Test User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) users row landed.
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", newUserId);
            var cacheCode = (string?)await usersCmd.ExecuteScalarAsync();
            Assert.Equal("AC", cacheCode);
        }

        // (2) user_agreement_codes Case A row landed — version=1, today-stamp.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var uacCmd = new NpgsqlCommand(
            """
            SELECT agreement_code, effective_from, effective_to, version
            FROM user_agreement_codes
            WHERE user_id = @userId
            """, conn))
        {
            uacCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await uacCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "POST must create a user_agreement_codes row.");
            Assert.Equal("AC", reader.GetString(0));
            Assert.Equal(today, reader.GetFieldValue<DateOnly>(1));
            Assert.True(reader.IsDBNull(2), "Case A insert must leave effective_to NULL.");
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.False(await reader.ReadAsync(), "Exactly one user_agreement_codes row expected.");
        }

        // (3) user_agreement_codes_audit CREATED row landed.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM user_agreement_codes_audit
            WHERE user_id = @userId
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "POST must emit a CREATED audit row.");
            Assert.Equal("CREATED", reader.GetString(0));
            Assert.True(reader.IsDBNull(1), "CREATED audit must have NULL version_before.");
            Assert.Equal(1L, reader.GetInt64(2));
        }

        // (4) UserAgreementCodeSeeded outbox event landed on user-{userId}.
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserAgreementCodeSeeded'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            outboxCmd.Parameters.AddWithValue("streamId", $"user-{newUserId}");
            var rawPayload = (string?)await outboxCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrEmpty(rawPayload),
                "POST must emit a UserAgreementCodeSeeded event matching the backfill semantic.");
            using var payloadDoc = JsonDocument.Parse(rawPayload!);
            Assert.Equal(newUserId, payloadDoc.RootElement.GetProperty("userId").GetString());
            Assert.Equal("AC", payloadDoc.RootElement.GetProperty("agreementCode").GetString());
            Assert.Equal(today.ToString("yyyy-MM-dd"),
                payloadDoc.RootElement.GetProperty("effectiveFrom").GetString());
            Assert.Equal(1L, payloadDoc.RootElement.GetProperty("rowVersion").GetInt64());
        }

        // (5) EmploymentProfileResolver.GetByEmployeeIdAtAsync(today) returns the
        // seeded code — proving the row is reachable through the dated lookup path
        // (PCS / Compliance / Balance / Skema / Overtime consumption sites).
        var repo = new UserAgreementCodeRepository(_harness.Factory);
        var resolver = new EmploymentProfileResolver(_harness.Factory, repo);
        // employee_profiles row was also INSERTed atomically by the POST (4-way → 6-way
        // atomicity per TASK-3407), so the resolver's JOIN should succeed.
        var resolved = await resolver.GetByEmployeeIdAtAsync(newUserId, today);
        Assert.NotNull(resolved);
        Assert.Equal("AC", resolved!.AgreementCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // PUT validator — backdated + future-dated EffectiveFrom (ADR-023 D8)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PUT with <c>EffectiveFrom = yesterday</c> AND a mutating <c>agreementCode</c>
    /// returns 422 with a structured body naming the <c>provided</c> + <c>expected</c>
    /// dates. The validator only fires when agreement_code mutates (no-op edits
    /// without agreement_code change continue to ignore EffectiveFrom — preserves
    /// S33 PUT path semantics).
    /// </summary>
    [Fact]
    public async Task PUT_BackdatedEffectiveFrom_Returns422()
    {
        var client = AuthorizedClient();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        // S35 / TASK-3506 — admin-strict If-Match required on PUT. Capture
        // ETag via GET first so the validator can run (without If-Match the
        // endpoint returns 428 before reaching the EffectiveFrom check).
        var getRsp = await client.GetAsync("/api/admin/users/emp001");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // emp001 seeded at agreement_code='AC' — mutate to 'HK' with backdated
        // EffectiveFrom to exercise the validator.
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/users/emp001")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = yesterday.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(yesterday.ToString("yyyy-MM-dd"),
            body.GetProperty("provided").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            body.GetProperty("expected").GetString());
    }

    /// <summary>
    /// PUT with <c>EffectiveFrom = tomorrow</c> AND a mutating <c>agreementCode</c>
    /// returns 422 (same validator branch as backdated). Pins the symmetric
    /// rejection per ADR-023 D8 same-day-only-edit narrowing — two-sided,
    /// not one-sided.
    /// </summary>
    [Fact]
    public async Task PUT_FutureDatedEffectiveFrom_Returns422()
    {
        var client = AuthorizedClient();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // S35 / TASK-3506 — admin-strict If-Match required (see backdated test
        // above for rationale).
        var getRsp = await client.GetAsync("/api/admin/users/emp001");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/users/emp001")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = tomorrow.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // PUT dual-emission ordering — Case C cross-day supersession
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Case C cross-day PUT (predecessor seeded at '0001-01-01', request
    /// EffectiveFrom=today) emits BOTH:
    /// <list type="bullet">
    ///   <item><c>UserAgreementCodeChanged</c> — narrow signal, always emits
    ///     when agreement_code mutated (preserved S33 contract).</item>
    ///   <item><c>UserAgreementCodeSuperseded</c> — Case C lifecycle event,
    ///     emitted ADDITIONALLY (dual emission per S25 publish-supersession
    ///     precedent).</item>
    /// </list>
    /// The audit row carries <c>action='SUPERSEDED'</c> with populated
    /// <c>version_before</c>/<c>version_after</c> (S33 EmployeeProfile precedent).
    /// Stream-id is <c>user-{userId}</c> for both events.
    ///
    /// <para>
    /// <b>Consumer dedupe contract</b> (refinement cycle 2 Reviewer WARNING 2):
    /// downstream consumers MUST dedupe Changed + Superseded on Case C — the
    /// narrow Changed signal is for steady-state replay-data trail walkers
    /// while Superseded carries the predecessor close + successor open
    /// lifecycle transition. Consumers selecting on
    /// <c>event_type='UserAgreementCodeChanged'</c> alone see every change;
    /// consumers selecting on Superseded see only cross-day transitions.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminPutUserCrossDayAgreementCodeChange_EmitsBothChangedAndSupersededEvents_AndAuditActionSUPERSEDED()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // S35 / TASK-3506 — admin-strict If-Match required on PUT. Capture
        // ETag via GET first; the new GET endpoint stamps ETag: "<version>".
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // emp001's user_agreement_codes row was backfilled by the seeder at
        // effective_from='0001-01-01' < today → Case C routing on this PUT.
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Both events present on the user-{userId} stream.
        var streamId = $"user-{userId}";
        await using (var bothCmd = new NpgsqlCommand(
            """
            SELECT event_type FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type IN ('UserAgreementCodeChanged', 'UserAgreementCodeSuperseded')
            ORDER BY outbox_id ASC
            """, conn))
        {
            bothCmd.Parameters.AddWithValue("streamId", streamId);
            var types = new List<string>();
            await using var reader = await bothCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                types.Add(reader.GetString(0));
            Assert.Contains("UserAgreementCodeChanged", types);
            Assert.Contains("UserAgreementCodeSuperseded", types);
        }

        // Superseded payload carries the predecessor + successor identities.
        await using (var supersededCmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserAgreementCodeSuperseded'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            supersededCmd.Parameters.AddWithValue("streamId", streamId);
            var rawPayload = (string?)await supersededCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrEmpty(rawPayload));
            using var payloadDoc = JsonDocument.Parse(rawPayload!);
            Assert.Equal(userId, payloadDoc.RootElement.GetProperty("userId").GetString());
            Assert.Equal("AC", payloadDoc.RootElement.GetProperty("oldAgreementCode").GetString());
            Assert.Equal("HK", payloadDoc.RootElement.GetProperty("newAgreementCode").GetString());
            // End-exclusive convention: predecessorEffectiveTo == newEffectiveFrom == today.
            Assert.Equal(today.ToString("yyyy-MM-dd"),
                payloadDoc.RootElement.GetProperty("predecessorEffectiveTo").GetString());
            Assert.Equal(today.ToString("yyyy-MM-dd"),
                payloadDoc.RootElement.GetProperty("newEffectiveFrom").GetString());
            // S33 Step 7a P1 absorption: successor version = predecessor.Version + 1.
            var versionBefore = payloadDoc.RootElement.GetProperty("versionBefore").GetInt64();
            var versionAfter = payloadDoc.RootElement.GetProperty("versionAfter").GetInt64();
            Assert.Equal(versionBefore + 1, versionAfter);
        }

        // Audit row: action='SUPERSEDED' with populated version_before/version_after.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after FROM user_agreement_codes_audit
            WHERE user_id = @userId
              AND action IN ('UPDATED', 'SUPERSEDED')
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "PUT must emit a UPDATED or SUPERSEDED audit row.");
            Assert.Equal("SUPERSEDED", reader.GetString(0));
            Assert.False(reader.IsDBNull(1), "Case C audit must have non-null version_before.");
            Assert.False(reader.IsDBNull(2), "Case C audit must have non-null version_after.");
            Assert.Equal(reader.GetInt64(1) + 1, reader.GetInt64(2));
        }
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
}
