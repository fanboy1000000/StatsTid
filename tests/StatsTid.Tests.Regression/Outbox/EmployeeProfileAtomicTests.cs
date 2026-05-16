using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S31 / TASK-3110 marquee D-test — verifies the 3-way atomic contract on
/// <c>PUT /api/admin/employee-profiles/{employeeId}</c> per ADR-018 D3 atomic-outbox:
/// the live <c>employee_profiles</c> row UPDATE, the <c>employee_profile_audit</c>
/// UPDATED row (with ADR-019 D8 version_before/version_after), and the
/// <c>EmployeeProfileUpdated</c> outbox event all commit together in a single
/// transaction.
///
/// <para>
/// Pre-condition: <see cref="StatsTid.Infrastructure.EmployeeProfileSeeder"/> runs at
/// <see cref="StatsTidWebApplicationFactory"/> startup and backfills one live row per
/// seed user (admin01 / hr01 / mgr01 / ladm01 / emp001 / emp002 / emp003) with
/// defaults (<c>weekly_norm_hours=37.0</c>, <c>part_time_fraction=1.000</c>,
/// <c>position=NULL</c>, <c>version=1</c>). The test reads emp001's initial row via
/// GET (ETag <c>"1"</c>), then PUTs an edit with <c>If-Match: "1"</c> and pins all
/// three atomicity facets on the resulting DB state.
/// </para>
///
/// <para>
/// JWT minting follows the dev-fallback signing key pattern verbatim from
/// <see cref="StatsTid.Tests.Regression.Config.EntitlementConfigEndpointTests"/>.
/// emp001 is in <c>STY01</c>; we mint a GlobalAdmin token (covers all orgs) to focus
/// the marquee on the atomicity contract — the cross-org RBAC matrix is covered by
/// <see cref="StatsTid.Tests.Regression.Config.EmployeeProfileEndpointTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileAtomicTests : IAsyncLifetime
{
    // Verbatim from JwtValidationSetup.DevFallbackSigningKey — same approach as
    // EntitlementConfigEndpointTests. WebApplicationFactory<Program> defaults the
    // hosting environment to Development, so the dev-fallback signing key fires.
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
    // Marquee — 3-way atomic round-trip with versioned audit + outbox event.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task EmployeeProfileEdit_RoundTripsAtomically_WithVersionedAuditAndEvent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        const string employeeId = "emp001";

        // ── 1. Read the seeded live row → version=1, ETag "1".
        var getRsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        Assert.NotNull(getRsp.Headers.ETag);
        Assert.Equal("\"1\"", getRsp.Headers.ETag!.Tag);

        var initial = await getRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(employeeId, initial.GetProperty("employeeId").GetString());
        Assert.Equal(1L, initial.GetProperty("version").GetInt64());

        // ── 2. PUT with If-Match: "1" → 200 + new ETag "2".
        var putReq = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                weeklyNormHours = 30.0m,
                partTimeFraction = 0.75m,
                position = "Department Head",
            }),
        };
        putReq.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        var putRsp = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, putRsp.StatusCode);
        Assert.NotNull(putRsp.Headers.ETag);
        Assert.Equal("\"2\"", putRsp.Headers.ETag!.Tag);

        var putBody = await putRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2L, putBody.GetProperty("version").GetInt64());
        Assert.Equal(30.0m, putBody.GetProperty("weeklyNormHours").GetDecimal());
        Assert.Equal(0.75m, putBody.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("Department Head", putBody.GetProperty("position").GetString());
        Assert.True(putBody.GetProperty("isPartTime").GetBoolean());

        // ── 3. Three-way atomicity assertions against the database.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (a) Row update — live row reflects new values + version=2.
        await using (var rowCmd = new NpgsqlCommand(
            """
            SELECT version, weekly_norm_hours, part_time_fraction, position
            FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, conn))
        {
            rowCmd.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await rowCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "Expected exactly one live row for emp001 after the PUT.");
            Assert.Equal(2L, reader.GetInt64(0));
            Assert.Equal(30.0m, reader.GetDecimal(1));
            Assert.Equal(0.75m, reader.GetDecimal(2));
            Assert.Equal("Department Head", reader.GetString(3));
        }

        // (b) Audit row — UPDATED action with version_before=1, version_after=2.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT version_before, version_after, previous_data, new_data,
                   actor_id, actor_role
            FROM employee_profile_audit
            WHERE employee_id = @employeeId AND action = 'UPDATED'
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "Expected an UPDATED audit row for emp001 after the PUT.");
            Assert.Equal(1L, reader.GetInt64(0));      // version_before
            Assert.Equal(2L, reader.GetInt64(1));      // version_after

            // previous_data JSONB shows pre-PUT values (seeded defaults).
            var previousJson = reader.GetString(2);
            using var previousDoc = JsonDocument.Parse(previousJson);
            Assert.Equal(37.0m, previousDoc.RootElement.GetProperty("weeklyNormHours").GetDecimal());
            Assert.Equal(1.000m, previousDoc.RootElement.GetProperty("partTimeFraction").GetDecimal());
            // Position was NULL pre-update; the endpoint serializes the source EmploymentProfile's
            // null Position as JSON null.
            Assert.Equal(JsonValueKind.Null,
                previousDoc.RootElement.GetProperty("position").ValueKind);

            // new_data JSONB shows the PUT payload.
            var newJson = reader.GetString(3);
            using var newDoc = JsonDocument.Parse(newJson);
            Assert.Equal(30.0m, newDoc.RootElement.GetProperty("weeklyNormHours").GetDecimal());
            Assert.Equal(0.75m, newDoc.RootElement.GetProperty("partTimeFraction").GetDecimal());
            Assert.Equal("Department Head",
                newDoc.RootElement.GetProperty("position").GetString());

            // actor_id + actor_role populated from the JWT claims (the minted admin token
            // uses sub = "ADMIN_S31_QA"). Endpoint stamps GlobalAdmin role.
            Assert.False(string.IsNullOrEmpty(reader.GetString(4)));
            Assert.False(string.IsNullOrEmpty(reader.GetString(5)));
        }

        // (c) Outbox event — EmployeeProfileUpdated on stream employee-profile-emp001.
        // The seeder may have emitted EmployeeProfileCreated rows on the same stream
        // during host startup; assert presence of the UPDATED event specifically.
        var streamId = $"employee-profile-{employeeId}";
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'EmployeeProfileUpdated'
            """, conn))
        {
            outboxCmd.Parameters.AddWithValue("streamId", streamId);
            var updatedCount = Convert.ToInt64(await outboxCmd.ExecuteScalarAsync());
            Assert.True(updatedCount >= 1L,
                $"Expected at least one EmployeeProfileUpdated outbox event on stream " +
                $"'{streamId}', found {updatedCount}.");
        }

        // (c.1) The latest UPDATED outbox row's payload matches the PUT body — pins that
        // the event the endpoint enqueued carries the new field values, not stale ones.
        await using (var payloadCmd = new NpgsqlCommand(
            """
            SELECT event_payload
            FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'EmployeeProfileUpdated'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            payloadCmd.Parameters.AddWithValue("streamId", streamId);
            var rawPayload = (string?)await payloadCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrEmpty(rawPayload));
            using var payloadDoc = JsonDocument.Parse(rawPayload!);
            // EventSerializer uses JsonNamingPolicy.CamelCase — payload property names
            // are camelCase, not PascalCase.
            Assert.Equal(employeeId,
                payloadDoc.RootElement.GetProperty("employeeId").GetString());
            Assert.Equal(30.0m,
                payloadDoc.RootElement.GetProperty("weeklyNormHours").GetDecimal());
            Assert.Equal(0.75m,
                payloadDoc.RootElement.GetProperty("partTimeFraction").GetDecimal());
            Assert.Equal("Department Head",
                payloadDoc.RootElement.GetProperty("position").GetString());
            Assert.Equal(1L,
                payloadDoc.RootElement.GetProperty("versionBefore").GetInt64());
            Assert.Equal(2L,
                payloadDoc.RootElement.GetProperty("versionAfter").GetInt64());
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        // HROrAbove policy requires a scope claim (requireOrgScope: true). A GLOBAL-typed
        // RoleScope satisfies both the role check and the scope-presence check.
        return tokenService.GenerateToken(
            employeeId: "ADMIN_S31_QA",
            name: "S31 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
