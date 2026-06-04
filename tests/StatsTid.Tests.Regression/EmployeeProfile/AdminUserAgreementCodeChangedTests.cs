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
/// S33 / TASK-3312 + TASK-3309 D-tests — admin PUT
/// <c>/api/admin/users/{userId}</c> emission of the new
/// <c>UserAgreementCodeChanged</c> event (TASK-3304 / ADR-023 D2). The event
/// rides the same atomic tx as the existing <c>UserUpdated</c> event per
/// ADR-018 D3; ordering on the per-user stream is <c>UserUpdated</c> then
/// <c>UserAgreementCodeChanged</c> (in append-order of the
/// <c>EnqueueAsync</c> calls at <c>AdminEndpoints.cs:537</c> then
/// <c>:559</c>).
///
/// <para>
/// <b>Predicate (TASK-3309 / refinement cycle 1 W1).</b> The endpoint
/// emits <c>UserAgreementCodeChanged</c> ONLY when:
/// <list type="number">
///   <item><description><c>request.AgreementCode is not null</c> (PUT body
///     explicitly set the field, distinguished from JSON omission).</description></item>
///   <item><description>AND <c>!string.Equals(request.AgreementCode,
///     existingUser.AgreementCode, StringComparison.Ordinal)</c> (the value
///     differs).</description></item>
/// </list>
/// Both branches are tested here — the positive predicate (emits) and the
/// null-branch negative (no-emit when body omits agreement_code).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminUserAgreementCodeChangedTests : IAsyncLifetime
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
        // Boot the host so the seeder + DI graph stand up.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Positive predicate: PUT changes <c>agreement_code</c> from "AC" to
    /// "HK". Outbox must carry BOTH <c>UserUpdated</c> AND
    /// <c>UserAgreementCodeChanged</c> on the <c>user-{userId}</c> stream
    /// (TASK-3309 wires both inside the same atomic tx). Payload checks:
    /// <c>oldAgreementCode == "AC"</c>, <c>newAgreementCode == "HK"</c>,
    /// <c>effectiveFrom == today (UTC)</c>.
    /// </summary>
    [Fact]
    public async Task AdminPutUserChangesAgreementCode_EmitsUserAgreementCodeChangedAndUserUpdated_BothInSameTx()
    {
        var client = AuthorizedClient();

        // emp001 in the seeded init.sql is agreement_code='AC'. PUT to "HK".
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // S35/TASK-3506 (a5e3ce0): /api/admin/users PUT is admin-strict If-Match
        // (ADR-019 D2) — 428 without the header. Capture the live ETag via GET, then
        // PUT with If-Match (same idiom as AdminUserVersioningTests).
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        putReq.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var streamId = $"user-{userId}";
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Both events present on the stream.
        await using (var bothCmd = new NpgsqlCommand(
            """
            SELECT event_type FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type IN ('UserUpdated', 'UserAgreementCodeChanged')
            ORDER BY outbox_id ASC
            """, conn))
        {
            bothCmd.Parameters.AddWithValue("streamId", streamId);
            var types = new List<string>();
            await using var reader = await bothCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                types.Add(reader.GetString(0));

            // Both must be present.
            Assert.Contains("UserUpdated", types);
            Assert.Contains("UserAgreementCodeChanged", types);

            // Order (TASK-3309 emits UserUpdated first at L537, then
            // UserAgreementCodeChanged at L559 — both inside the same tx).
            var lastUserUpdated = types.LastIndexOf("UserUpdated");
            var lastAgreement = types.LastIndexOf("UserAgreementCodeChanged");
            Assert.True(lastUserUpdated < lastAgreement,
                $"UserUpdated (idx={lastUserUpdated}) should precede UserAgreementCodeChanged (idx={lastAgreement}) on stream '{streamId}'.");
        }

        // UserAgreementCodeChanged payload.
        await using (var payloadCmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserAgreementCodeChanged'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            payloadCmd.Parameters.AddWithValue("streamId", streamId);
            var rawPayload = (string?)await payloadCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrEmpty(rawPayload));
            using var payloadDoc = JsonDocument.Parse(rawPayload!);
            Assert.Equal(userId,
                payloadDoc.RootElement.GetProperty("userId").GetString());
            Assert.Equal("AC",
                payloadDoc.RootElement.GetProperty("oldAgreementCode").GetString());
            Assert.Equal("HK",
                payloadDoc.RootElement.GetProperty("newAgreementCode").GetString());
            // EffectiveFrom is DateOnly per TASK-3304; serialized as
            // ISO-8601 yyyy-MM-dd. Today's UTC date (declared at method scope above).
            var effectiveFromStr = payloadDoc.RootElement
                .GetProperty("effectiveFrom").GetString();
            Assert.NotNull(effectiveFromStr);
            Assert.Equal(today.ToString("yyyy-MM-dd"), effectiveFromStr);
        }
    }

    /// <summary>
    /// Negative predicate: PUT body omits <c>agreementCode</c> entirely. The
    /// fallthrough at <c>AdminEndpoints.cs:519</c> still writes
    /// <c>request.AgreementCode ?? existingUser.AgreementCode</c> to the
    /// users row (no DB change), and <c>UserUpdated</c> still fires per the
    /// always-on emission contract. But the null-guard at L546 prevents
    /// <c>UserAgreementCodeChanged</c> from being enqueued — pinning that
    /// the narrow signal is genuinely narrow (no spurious emissions on
    /// non-agreement-code edits like display-name updates).
    /// </summary>
    [Fact]
    public async Task AdminPutUserOmitsAgreementCode_DoesNotEmitUserAgreementCodeChanged()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // S35/TASK-3506 (a5e3ce0): /api/admin/users PUT is admin-strict If-Match
        // (ADR-019 D2) — 428 without the header. GET the live ETag, then PUT with
        // If-Match (same idiom as AdminUserVersioningTests).
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // PUT body omits agreementCode (only display_name change).
        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "Updated Display Name S33",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        putReq.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // No UserAgreementCodeChanged on the stream.
        await using var agreementCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserAgreementCodeChanged'
            """, conn);
        agreementCmd.Parameters.AddWithValue("streamId", $"user-{userId}");
        var count = Convert.ToInt64(await agreementCmd.ExecuteScalarAsync());
        Assert.Equal(0L, count);

        // UserUpdated DID fire (defense in depth — proves the null-guard
        // sits below the always-on UserUpdated emission, not above it).
        await using var updatedCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserUpdated'
            """, conn);
        updatedCmd.Parameters.AddWithValue("streamId", $"user-{userId}");
        var updatedCount = Convert.ToInt64(await updatedCmd.ExecuteScalarAsync());
        Assert.True(updatedCount >= 1L,
            $"Expected at least one UserUpdated event on stream 'user-{userId}'.");
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
            employeeId: "ADMIN_S33_QA",
            name: "S33 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
