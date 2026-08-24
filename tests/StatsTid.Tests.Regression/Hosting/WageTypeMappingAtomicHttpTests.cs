using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13312 (QUAL-016) — WIRE-DRIVEN conversions of the wage-type-mapping atomic-outbox
/// proofs (create, update, delete). Replaces the rollback methods of the legacy hand-mirror
/// <c>Outbox.WageTypeMappingAtomicTests</c> (now retired), which re-typed the endpoint's
/// save-orchestration in the test body and so proved only the copy. Each test here drives the REAL
/// route through a host whose <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/> throws
/// (<see cref="StatsTidWebApplicationFactory.WithThrowingOutbox"/>) and pins that the whole
/// state+audit+outbox+projection write rolls back (PAT-019).
///
/// <para><b>Auth/seed cost:</b> policy <c>GlobalAdminOnly</c> (<c>requireOrgScope:false</c>) → a
/// bare <c>role=GlobalAdmin</c> token admits with no scope. Update/delete self-seed a mapping
/// through the REAL create endpoint (Case A fresh insert) on the base, non-throwing host first.</para>
///
/// <para><b>Witness keying:</b> the natural key (time_type/ok_version/agreement_code/position) is
/// test-chosen, so the outbox/event stream <c>wage-type-mapping-{ac}-{ok}-{tt}</c> is known. Create
/// has nothing legitimately on the stream (its only op rolled back) → stream-keyed asserts.
/// Update/delete share the stream with the seed's genuine <c>WageTypeMappingCreated</c> event, so
/// their outbox/event witnesses filter on the MUTATION's own event_type (Updated / Deleted).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingAtomicHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot base-host seeders before any throwing host derives
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────────────────────────────── CREATE ────────────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/wage-type-mappings</c> (Case A — no closed-today predecessor → fresh
    /// INSERT). Falsifiability: the handler locks the natural key, INSERTs, appends the CREATED
    /// audit, calls <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection, then
    /// <c>CommitAsync</c>, all under <c>catch { Rollback; throw }</c>. The throwing outbox faults
    /// the enqueue; nothing commits and the 5xx surfaces. Goes RED if the route is deleted, if
    /// <c>CommitAsync</c> moves before the enqueue, or if the INSERT runs on a self-managed connection.
    /// </summary>
    [Fact]
    public async Task Create_RealEndpoint_OutboxThrows_RollsBackWholeCreate()
    {
        var timeType = "FR_WTM_C_HTTP_" + Rand();
        var streamId = StreamId(timeType);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var rsp = await client.PostAsJsonAsync("/api/admin/wage-type-mappings", new
        {
            timeType,
            wageType = "SLS_0110",
            okVersion = OkVersion,
            agreementCode = AgreementCode,
        });

        AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "wage_type_mappings", $"time_type = '{timeType}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "wage_type_mapping_audit",
            $"time_type = '{timeType}' AND action = 'CREATED'");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
    }

    // ───────────────────────────────────────── UPDATE ────────────────────────────────────────

    /// <summary>
    /// <c>PUT /api/admin/wage-type-mappings</c> (admin-strict If-Match; same-day in-place UPDATE).
    /// Falsifiability: the handler routes through <c>SupersedeAndCreateAsync(conn,tx,…)</c> + UPDATED
    /// audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection under
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue; the wage_type
    /// mutation (SLS_0110→SLS_9999) never commits and the 5xx surfaces. Goes RED if the route is
    /// deleted, if <c>CommitAsync</c> moves before the enqueue, or if the UPDATE self-manages its connection.
    /// </summary>
    [Fact]
    public async Task Update_RealEndpoint_OutboxThrows_RollsBackWholeUpdate()
    {
        var baseClient = BaseAdminClient();
        var timeType = "FR_WTM_U_HTTP_" + Rand();
        var etag = await SeedMappingAsync(baseClient, timeType, wageType: "SLS_0110");
        var streamId = StreamId(timeType);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/wage-type-mappings")
        {
            Content = JsonContent.Create(new
            {
                timeType,
                wageType = "SLS_9999", // sentinel (a row at SLS_9999 is the absence-witness)
                okVersion = OkVersion,
                agreementCode = AgreementCode,
            }),
        };
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "wage_type_mappings",
            $"time_type = '{timeType}' AND wage_type = 'SLS_9999'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "wage_type_mapping_audit",
            $"time_type = '{timeType}' AND action = 'UPDATED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = '{streamId}' AND event_type = 'WageTypeMappingUpdated'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = '{streamId}' AND event_type = 'WageTypeMappingUpdated'");
    }

    // ───────────────────────────────────────── DELETE ────────────────────────────────────────

    /// <summary>
    /// <c>DELETE /api/admin/wage-type-mappings</c> (admin-strict If-Match; soft-delete via
    /// effective_to=today). Falsifiability: the handler runs <c>SoftDeleteAsync(conn,tx,…)</c> +
    /// DELETED audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection under
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue; the soft-close
    /// never commits (the row stays OPEN, effective_to IS NULL) and the 5xx surfaces. Goes RED if
    /// the route is deleted, if <c>CommitAsync</c> moves before the enqueue, or if the soft-delete
    /// self-manages its connection.
    /// </summary>
    [Fact]
    public async Task Delete_RealEndpoint_OutboxThrows_RollsBackWholeDelete()
    {
        var baseClient = BaseAdminClient();
        var timeType = "FR_WTM_D_HTTP_" + Rand();
        var etag = await SeedMappingAsync(baseClient, timeType, wageType: "SLS_0110");
        var streamId = StreamId(timeType);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/admin/wage-type-mappings?timeType={timeType}&okVersion={OkVersion}&agreementCode={AgreementCode}");
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        AssertServerError(rsp);

        // The soft-close did NOT commit: the row stays OPEN (a closed row — effective_to set — is
        // the absence-witness).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "wage_type_mappings",
            $"time_type = '{timeType}' AND effective_to IS NOT NULL");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "wage_type_mapping_audit",
            $"time_type = '{timeType}' AND action = 'DELETED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = '{streamId}' AND event_type = 'WageTypeMappingDeleted'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = '{streamId}' AND event_type = 'WageTypeMappingDeleted'");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static string Rand() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private static string StreamId(string timeType) =>
        $"wage-type-mapping-{AgreementCode}-{OkVersion}-{timeType}";

    private static void AssertServerError(HttpResponseMessage rsp) =>
        Assert.True((int)rsp.StatusCode >= 500,
            $"expected a 5xx from the escaped outbox throw, got {(int)rsp.StatusCode}");

    private HttpClient BaseAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken("admin_qual016_seed"));
        return client;
    }

    private static HttpClient ThrowingClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken("admin_qual016"));
        return client;
    }

    /// <summary>Seeds an open mapping (Case A fresh insert) via the REAL create endpoint; returns its ETag.</summary>
    private static async Task<EntityTagHeaderValue> SeedMappingAsync(
        HttpClient client, string timeType, string wageType)
    {
        var rsp = await client.PostAsJsonAsync("/api/admin/wage-type-mappings", new
        {
            timeType,
            wageType,
            okVersion = OkVersion,
            agreementCode = AgreementCode,
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        return rsp.Headers.ETag!;
    }

    private static string MintGlobalAdminToken(string actorId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC");
    }
}
