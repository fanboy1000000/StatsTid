using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S27 / TASK-2710 Slot 4 — the marquee architectural-fix proof for Phase 4c.6.
///
/// <para>
/// Pins the load-bearing invariant of the S27 sync-in-tx projection design: a Time POST
/// followed by an immediate Time GET must see the just-written entry — even with the
/// <see cref="StatsTid.Infrastructure.Outbox.OutboxPublisher"/> stopped (i.e. the
/// <c>events</c> table is still empty for the write because the publisher has not drained
/// the outbox row to canonical events). The post-S26 reverted-atomic baseline FAILS this
/// test (the GET would return 0 entries because the event-stream-backed read sees nothing
/// until the publisher drain catches up). The post-S27 projection-table design PASSES
/// because the GET reads from <c>time_entries_projection</c>, which committed in the same
/// transaction as the outbox enqueue.
/// </para>
///
/// <para>
/// Cycle-3 tightened assertions (verbatim per refinement Approach 8):
/// <list type="number">
///   <item>GET response contains the just-written entry</item>
///   <item><c>events</c> table has zero matching <c>TimeEntryRegistered</c> rows for that
///   employee — proves the publisher has not drained yet</item>
///   <item><c>outbox_events</c> row exists with <c>published_at IS NULL</c> matching the
///   write — proves the outbox write happened</item>
///   <item>projection row's <c>outbox_id</c> matches the unpublished outbox row's
///   <c>outbox_id</c> — proves the per-event ordering invariant
///   (enqueue FIRST → projection SECOND consuming outbox_id)</item>
/// </list>
/// </para>
///
/// <para>
/// JWT minting: dev-fallback signing key per <c>JwtValidationSetup.DevFallbackSigningKey</c>.
/// The <see cref="WebApplicationFactory{Program}"/>'s default Hosting environment is
/// <c>Development</c> so the fallback fires. The token carries claims matching the
/// <c>Employee</c> role + matching <c>employee_id</c>, which short-circuits
/// <c>OrgScopeValidator</c> per <c>TimeEndpoints.cs:36-44</c> (Employee accessing own
/// data bypasses the scope check). No <c>org_id</c> / <c>scopes</c> claim required.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PublisherStallReadYourWriteTests : IAsyncLifetime
{
    // Verbatim from JwtValidationSetup.DevFallbackSigningKey — the dev-fallback key fires
    // when the Hosting env is Development and no Jwt:SigningKey is configured. The
    // WebApplicationFactory<Program> defaults to Development and we don't override the
    // signing key, so this is the resolved key the host validates against.
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Full Backend.Api schema (init.sql) — required so Program.cs seeders + the
        // projection tables + the outbox tables are all in place before the host boots.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Time entry RYW under publisher stall. POST /api/time-entries with the publisher
    /// stopped, then immediate GET /api/time-entries/{employeeId}; assert the four
    /// cycle-3 invariants (response contains the entry; events empty; unpublished outbox
    /// row present; projection.outbox_id == outbox row.outbox_id).
    /// </summary>
    [Fact]
    public async Task TimeEntry_PostThenGet_PublisherStopped_ProjectionServesReadYourWrite()
    {
        // Stop the publisher BEFORE issuing the POST so we know the publisher cannot
        // race the GET. (Stopping after POST would leave a window where the publisher
        // could drain to events before the assertion runs, weakening the test.)
        var client = _factory.CreateClient();
        await _factory.StopPublisherAsync();

        var employeeId = "EMP_RYW_T_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var token = MintEmployeeToken(employeeId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var entryDate = new DateOnly(2026, 5, 7);
        var postBody = new
        {
            employeeId,
            date = entryDate.ToString("yyyy-MM-dd"),
            hours = 7.4m,
            taskId = "PROJ-RYW-1",
            activityType = "NORMAL",
            agreementCode = "HK",
        };
        var postRsp = await client.PostAsJsonAsync("/api/time-entries", postBody);
        Assert.Equal(HttpStatusCode.Created, postRsp.StatusCode);

        // (1) GET response contains the just-written entry.
        var getRsp = await client.GetAsync($"/api/time-entries/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var entries = await getRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(entries.GetArrayLength() >= 1,
            "GET /api/time-entries returned no entries — projection-backed read failed under publisher stall.");

        var matched = entries.EnumerateArray().FirstOrDefault(e =>
        {
            var eid = e.TryGetProperty("employeeId", out var p) ? p.GetString() : null;
            return string.Equals(eid, employeeId, StringComparison.Ordinal);
        });
        Assert.NotEqual(default, matched);
        Assert.Equal(7.4m, matched.GetProperty("hours").GetDecimal());

        // (2) events table has ZERO matching TimeEntryRegistered rows for the employee.
        // The publisher is stopped — the outbox row is committed but events is unchanged.
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();

        var streamId = $"employee-{employeeId}";
        await using (var eventsCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE stream_id = @s AND event_type = 'TimeEntryRegistered'",
            verifyConn))
        {
            eventsCmd.Parameters.AddWithValue("s", streamId);
            var eventCount = Convert.ToInt64(await eventsCmd.ExecuteScalarAsync());
            Assert.Equal(0L, eventCount);
        }

        // (3) outbox_events row exists with published_at IS NULL matching the write.
        long? outboxId = null;
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT outbox_id FROM outbox_events
            WHERE stream_id = @s AND event_type = 'TimeEntryRegistered' AND published_at IS NULL
            """, verifyConn))
        {
            outboxCmd.Parameters.AddWithValue("s", streamId);
            await using var reader = await outboxCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "No unpublished outbox_events row found for the write — atomic write contract failed.");
            outboxId = reader.GetInt64(0);
        }
        Assert.NotNull(outboxId);

        // (4) projection row's outbox_id matches the unpublished outbox row's outbox_id.
        await using (var projCmd = new NpgsqlCommand(
            "SELECT outbox_id FROM time_entries_projection WHERE employee_id = @id",
            verifyConn))
        {
            projCmd.Parameters.AddWithValue("id", employeeId);
            await using var reader = await projCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "No time_entries_projection row found for the write — projection write was not made.");
            var projOutboxId = reader.GetInt64(0);
            Assert.Equal(outboxId.Value, projOutboxId);
        }
    }

    /// <summary>
    /// Absence RYW under publisher stall. Same shape as the time-entry test but for
    /// POST /api/absences → GET /api/absences/{employeeId}. Pins the projection-backed
    /// read for the absences_projection table.
    /// </summary>
    [Fact]
    public async Task Absence_PostThenGet_PublisherStopped_ProjectionServesReadYourWrite()
    {
        var client = _factory.CreateClient();
        await _factory.StopPublisherAsync();

        var employeeId = "EMP_RYW_A_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var token = MintEmployeeToken(employeeId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var absenceDate = new DateOnly(2026, 5, 7);
        var postBody = new
        {
            employeeId,
            date = absenceDate.ToString("yyyy-MM-dd"),
            absenceType = "VACATION",
            hours = 7.4m,
            agreementCode = "HK",
        };
        var postRsp = await client.PostAsJsonAsync("/api/absences", postBody);
        Assert.Equal(HttpStatusCode.Created, postRsp.StatusCode);

        // (1) GET response contains the just-written absence.
        var getRsp = await client.GetAsync($"/api/absences/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var absences = await getRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(absences.GetArrayLength() >= 1,
            "GET /api/absences returned no absences — projection-backed read failed under publisher stall.");

        var matched = absences.EnumerateArray().FirstOrDefault(a =>
        {
            var eid = a.TryGetProperty("employeeId", out var p) ? p.GetString() : null;
            return string.Equals(eid, employeeId, StringComparison.Ordinal);
        });
        Assert.NotEqual(default, matched);
        Assert.Equal("VACATION", matched.GetProperty("absenceType").GetString());

        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();

        var streamId = $"employee-{employeeId}";
        // (2) events empty for AbsenceRegistered on this stream.
        await using (var eventsCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE stream_id = @s AND event_type = 'AbsenceRegistered'",
            verifyConn))
        {
            eventsCmd.Parameters.AddWithValue("s", streamId);
            var eventCount = Convert.ToInt64(await eventsCmd.ExecuteScalarAsync());
            Assert.Equal(0L, eventCount);
        }

        // (3) unpublished outbox row exists.
        long? outboxId = null;
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT outbox_id FROM outbox_events
            WHERE stream_id = @s AND event_type = 'AbsenceRegistered' AND published_at IS NULL
            """, verifyConn))
        {
            outboxCmd.Parameters.AddWithValue("s", streamId);
            await using var reader = await outboxCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "No unpublished outbox_events row found for the absence write.");
            outboxId = reader.GetInt64(0);
        }

        // (4) projection.outbox_id == outbox row.outbox_id.
        await using (var projCmd = new NpgsqlCommand(
            "SELECT outbox_id FROM absences_projection WHERE employee_id = @id",
            verifyConn))
        {
            projCmd.Parameters.AddWithValue("id", employeeId);
            await using var reader = await projCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "No absences_projection row found for the absence write.");
            var projOutboxId = reader.GetInt64(0);
            Assert.Equal(outboxId!.Value, projOutboxId);
        }
    }

    /// <summary>
    /// Mints a JWT signed with the dev-fallback key, claiming the Employee role + the
    /// supplied <paramref name="employeeId"/> as both <c>sub</c> and the
    /// <c>employee_id</c> claim. <see cref="JwtTokenService"/> is the production helper —
    /// we instantiate it directly with a matching <see cref="JwtSettings"/> rather than
    /// resolving via DI so the test does not depend on <see cref="WebApplicationFactory{Program}"/>'s
    /// service-provider lifetime. Issuer / Audience / Key MUST match the values
    /// <see cref="JwtValidationSetup.AddStatsTidJwtAuth"/> uses to validate inbound tokens
    /// (<c>"statstid"</c> for both, dev-fallback signing key).
    /// </summary>
    private static string MintEmployeeToken(string employeeId)
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
            agreementCode: "HK");
    }
}
