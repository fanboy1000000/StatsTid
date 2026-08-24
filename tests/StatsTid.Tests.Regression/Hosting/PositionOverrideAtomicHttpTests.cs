using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13312 (QUAL-016) — WIRE-DRIVEN conversions of the position-override atomic-outbox
/// proofs (create, update, deactivate, activate). Replaces the rollback methods of the legacy
/// hand-mirror <c>Outbox.PositionOverrideAtomicTests</c> (now retired), which re-typed the
/// endpoint's save-orchestration in the test body. Each test here drives the REAL route through a
/// throwing-outbox host (<see cref="StatsTidWebApplicationFactory.WithThrowingOutbox"/>) and pins
/// that the whole state+audit+outbox+projection write rolls back (PAT-019).
///
/// <para><b>Auth/seed cost:</b> the position-override WRITE endpoints are policy
/// <c>GlobalAdminOnly</c> (SEC-032; <c>requireOrgScope:false</c>) → a bare <c>role=GlobalAdmin</c>
/// token admits with no scope. The <c>position_code</c> FK parent is the init.sql-seeded
/// <c>DEPARTMENT_HEAD</c> row. Each override carries a test-UNIQUE <c>agreement_code</c> (a plain
/// TEXT column, not an FK) so the create witness has a distinctive key; update/activate/deactivate
/// self-seed through the REAL create endpoint on the base, non-throwing host.</para>
///
/// <para><b>Witness keying:</b> the create's override_id is server-generated inside the rolled-back
/// tx and never returned, so its witnesses key on the test-unique agreement_code (payload/new_data
/// <c>::text LIKE</c>), per PAT-019. Update/activate/deactivate know the seeded override_id from
/// the seed's 201 body, so they key state/audit on override_id and the outbox/event on
/// stream+event_type (the seed's genuine CREATED/DEACTIVATED events share the stream).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverrideAtomicHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OkVersion = "OK24";
    private const string PositionCode = "DEPARTMENT_HEAD"; // init.sql-seeded positions FK parent

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
    /// <c>POST /api/admin/position-overrides</c>. Falsifiability: the handler runs
    /// <c>CreateReturningAsync(conn,tx)</c> + CREATED audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c>
    /// + audit-projection in ONE tx, then <c>CommitAsync</c>. The throwing outbox faults the enqueue
    /// before the commit → the tx disposes without committing → nothing for this unique
    /// agreement_code survives, and the 5xx surfaces. Goes RED if the route is deleted, if
    /// <c>CommitAsync</c> moves before the enqueue, or if the INSERT self-manages its connection.
    /// </summary>
    [Fact]
    public async Task Create_RealEndpoint_OutboxThrows_RollsBackWholeCreate()
    {
        var uniqueAc = "FR_PO_C_" + Rand(); // unique agreement_code = the absence-witness

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var rsp = await client.PostAsJsonAsync("/api/admin/position-overrides",
            OverrideBody(uniqueAc, maxFlex: 200m));

        AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"agreement_code = '{uniqueAc}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"action = 'CREATED' AND new_data::text LIKE '%{uniqueAc}%'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"event_type = 'PositionOverrideCreated' AND event_payload::text LIKE '%{uniqueAc}%'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"event_type = 'PositionOverrideCreated' AND data::text LIKE '%{uniqueAc}%'");
    }

    // ───────────────────────────────────────── UPDATE ────────────────────────────────────────

    /// <summary>
    /// <c>PUT /api/admin/position-overrides/{id}</c> (admin-strict If-Match). Falsifiability: the
    /// handler runs <c>UpdateAsync(conn,tx,id,expectedVersion)</c> + UPDATED audit +
    /// <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection under <c>catch { Rollback; throw }</c>.
    /// The throwing outbox faults the enqueue; the max_flex mutation (200→250) never commits and the
    /// 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c> moves before the
    /// enqueue, or if the UPDATE self-manages its connection.
    /// </summary>
    [Fact]
    public async Task Update_RealEndpoint_OutboxThrows_RollsBackWholeUpdate()
    {
        var baseClient = BaseAdminClient();
        var uniqueAc = "FR_PO_U_" + Rand();
        var (overrideId, etag) = await CreateOverrideAsync(baseClient, uniqueAc, maxFlex: 200m);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        // SEC-034: the identity triple (agreement_code, ok_version, position_code) must match the
        // stored row — carry the same uniqueAc so the PUT is a value-only edit (max_flex 200→250).
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}")
        {
            Content = JsonContent.Create(OverrideBody(uniqueAc, maxFlex: 250m)),
        };
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"override_id = '{overrideId}' AND max_flex_balance = 250");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{overrideId}' AND action = 'UPDATED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'position-override-{overrideId}' AND event_type = 'PositionOverrideUpdated'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'position-override-{overrideId}' AND event_type = 'PositionOverrideUpdated'");
    }

    // ─────────────────────────────────────── DEACTIVATE ──────────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/position-overrides/{id}/deactivate</c> (admin-strict If-Match).
    /// Falsifiability: the handler runs <c>DeactivateAsync(conn,tx,id,expectedVersion)</c> +
    /// DEACTIVATED audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection under
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue; the row stays
    /// ACTIVE and the 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c> moves
    /// before the enqueue, or if the state UPDATE self-manages its connection.
    /// </summary>
    [Fact]
    public async Task Deactivate_RealEndpoint_OutboxThrows_RollsBackWholeDeactivate()
    {
        var baseClient = BaseAdminClient();
        var uniqueAc = "FR_PO_DA_" + Rand();
        var (overrideId, etag) = await CreateOverrideAsync(baseClient, uniqueAc, maxFlex: 200m);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate");
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"override_id = '{overrideId}' AND status = 'INACTIVE'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{overrideId}' AND action = 'DEACTIVATED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'position-override-{overrideId}' AND event_type = 'PositionOverrideDeactivated'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'position-override-{overrideId}' AND event_type = 'PositionOverrideDeactivated'");
    }

    // ──────────────────────────────────────── ACTIVATE ───────────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/position-overrides/{id}/activate</c> (admin-strict If-Match) against an
    /// INACTIVE row. Falsifiability: the handler runs <c>ActivateAsync(conn,tx,id,expectedVersion)</c>
    /// + ACTIVATED audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection under
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue; the row stays
    /// INACTIVE and the 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c> moves
    /// before the enqueue, or if the state UPDATE self-manages its connection.
    /// </summary>
    [Fact]
    public async Task Activate_RealEndpoint_OutboxThrows_RollsBackWholeActivate()
    {
        var baseClient = BaseAdminClient();
        var uniqueAc = "FR_PO_AC_" + Rand();
        var (overrideId, createEtag) = await CreateOverrideAsync(baseClient, uniqueAc, maxFlex: 200m);

        // Deactivate on the base host so the activate acts on an INACTIVE row; capture the new ETag.
        var deReq = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate");
        deReq.Headers.IfMatch.Add(createEtag);
        var deRsp = await baseClient.SendAsync(deReq);
        Assert.Equal(HttpStatusCode.OK, deRsp.StatusCode);
        Assert.NotNull(deRsp.Headers.ETag);
        var inactiveEtag = deRsp.Headers.ETag!;

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/activate");
        req.Headers.IfMatch.Add(inactiveEtag);
        var rsp = await client.SendAsync(req);

        AssertServerError(rsp);

        // Row stayed INACTIVE (a row at status=ACTIVE is the absence-witness).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "position_override_configs",
            $"override_id = '{overrideId}' AND status = 'ACTIVE'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "position_override_config_audit",
            $"override_id = '{overrideId}' AND action = 'ACTIVATED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'position-override-{overrideId}' AND event_type = 'PositionOverrideActivated'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'position-override-{overrideId}' AND event_type = 'PositionOverrideActivated'");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static string Rand() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private static void AssertServerError(HttpResponseMessage rsp) =>
        Assert.True((int)rsp.StatusCode >= 500,
            $"expected a 5xx from the escaped outbox throw, got {(int)rsp.StatusCode}");

    private static object OverrideBody(string agreementCode, decimal maxFlex) => new
    {
        agreementCode,
        okVersion = OkVersion,
        positionCode = PositionCode,
        maxFlexBalance = maxFlex,
        flexCarryoverMax = (decimal?)null,
        normPeriodWeeks = 4,
        weeklyNormHours = (decimal?)null,
        description = "QUAL-016 position-override forced-rollback",
    };

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

    /// <summary>Creates an ACTIVE override via the REAL create endpoint; returns its server id + ETag.</summary>
    private async Task<(Guid OverrideId, EntityTagHeaderValue Etag)> CreateOverrideAsync(
        HttpClient client, string agreementCode, decimal maxFlex)
    {
        var rsp = await client.PostAsJsonAsync("/api/admin/position-overrides",
            OverrideBody(agreementCode, maxFlex));
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var overrideId = body.GetProperty("overrideId").GetGuid();
        Assert.NotNull(rsp.Headers.ETag);
        return (overrideId, rsp.Headers.ETag!);
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
