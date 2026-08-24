using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13312 (QUAL-016) — the WIRE-DRIVEN conversions of the agreement-config
/// lifecycle atomic-outbox proofs (clone, update, publish ×2, archive). Companion to the spike
/// template <see cref="AgreementConfigCreateAtomicHttpTests"/> (which already covers CREATE);
/// together the six methods here + there replace every rollback method of the legacy
/// hand-mirror <c>Outbox.AgreementConfigAtomicTests</c> (now retired).
///
/// <para><b>What this replaces and why (plain-language):</b> the legacy
/// <c>AgreementConfigAtomicTests</c> HAND-TYPED each endpoint's save sequence in the test body
/// (open conn → begin tx → repo <c>(conn,tx)</c> overload → append audit → throwing enqueue →
/// commit) and asserted the hand-typed copy rolled back — so it proved only "my copy rolls back",
/// never that the SHIPPED endpoint does. If the real handler drifted (committed before the
/// enqueue, opened its own connection, or was deleted) the legacy test stayed green. Each test
/// here drives the REAL route over authenticated HTTP through a host whose
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/> throws
/// (<see cref="StatsTidWebApplicationFactory.WithThrowingOutbox"/> for the single-emit legs;
/// <see cref="StatsTidWebApplicationFactory.WithThrowOnSecondEnqueueOutbox"/> for the DUAL-EMIT
/// supersede publish), so it pins the wiring the legacy test could not (PAT-019).</para>
///
/// <para><b>Auth/seed cost:</b> all endpoints are policy <c>GlobalAdminOnly</c>
/// (<c>requireOrgScope:false</c>) — a bare <c>role=GlobalAdmin</c> token admits with NO org scope
/// and NO org/user seed. Publish/archive/update self-seed their prior config through the REAL
/// create endpoint (on the base, non-throwing host) first, so the seeded row is genuine product
/// state; the throwing host is derived only for the mutation under test.</para>
///
/// <para><b>Witness keying:</b> because the seed create emits a genuine
/// <c>AgreementConfigCreated</c> event on the SAME <c>agreement-config-{id}</c> stream, the
/// outbox/event absence-witnesses are keyed on the MUTATION's own event_type (Updated / Published /
/// Archived), not on the stream alone — otherwise the legitimate seed CREATED row would make the
/// witness fail. Server-generated ids that are never returned (the rolled-back create leg of clone)
/// are witnessed on a test-unique payload field via a <c>::text LIKE</c> over the JSONB, per PAT-019.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementConfigLifecycleAtomicHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot the BASE host once so its idempotent startup seeders run against the empty DB HERE —
        // before any throwing host derives from it (the S63/S65 boot-order lesson, PAT-019 gotcha).
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────────────────────────────── CLONE ─────────────────────────────────────────

    /// <summary>
    /// <c>POST /api/agreement-configs/{id}/clone</c>. Falsifiability: the clone handler runs
    /// <c>CreateReturningAsync(conn,tx)</c> + CLONED audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c>
    /// + audit-projection in ONE tx, and only THEN <c>CommitAsync</c>. The throwing outbox faults
    /// the enqueue before the commit → the tx disposes without committing → the clone row, its
    /// CLONED audit, its outbox row + audit-projection row all roll back, and the escaped throw
    /// surfaces as 5xx. Goes RED if the route is deleted (404/405), if <c>CommitAsync</c> moves
    /// before the enqueue, or if the clone INSERT switches to a self-connection overload.
    /// </summary>
    [Fact]
    public async Task Clone_RealEndpoint_OutboxThrows_RollsBackWholeClone()
    {
        var baseClient = BaseAdminClient();
        var (sourceId, _) = await CreateConfigAsync(baseClient, "FR_SRC_" + Rand());

        // A test-unique agreement_code on the CLONE is the absence-witness (the clone's config_id
        // is server-generated inside the rolled-back tx and never returned).
        var cloneCode = "FR_CLONE_" + Rand();

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var rsp = await client.PostAsync(
            $"/api/agreement-configs/{sourceId}/clone?agreementCode={cloneCode}&okVersion=OK24", null);

        await AssertServerError(rsp);

        // (1) no clone state row …
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs", $"agreement_code = '{cloneCode}'");
        // (2) no CLONED audit row (its new_data JSON carries the source id) …
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"action = 'CLONED' AND new_data::text LIKE '%{sourceId}%'");
        // (3) no outbox row (payload carries the clone code) …
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"event_type = 'AgreementConfigCloned' AND event_payload::text LIKE '%{cloneCode}%'");
        // (4) no canonical event row.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"event_type = 'AgreementConfigCloned' AND data::text LIKE '%{cloneCode}%'");
    }

    // ───────────────────────────────────────── UPDATE ────────────────────────────────────────

    /// <summary>
    /// <c>PUT /api/agreement-configs/{id}</c> (admin-strict If-Match). Falsifiability: the update
    /// handler runs <c>UpdateDraftAsync(conn,tx,id,expectedVersion)</c> + UPDATED audit +
    /// <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection in ONE tx under its
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue; the weekly-norm
    /// mutation (37→38) never commits and the 5xx surfaces. Goes RED if the route is deleted, if
    /// <c>CommitAsync</c> moves before the enqueue, or if the UPDATE runs on a self-managed connection.
    /// </summary>
    [Fact]
    public async Task UpdateDraft_RealEndpoint_OutboxThrows_RollsBackWholeUpdate()
    {
        var baseClient = BaseAdminClient();
        var code = "FR_UPD_" + Rand();
        var (configId, etag) = await CreateConfigAsync(baseClient, code, weeklyNorm: 37m);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/agreement-configs/{configId}")
        {
            // Sentinel: weekly_norm 37 → 38 (a row at 38 is the absence-witness).
            Content = JsonContent.Create(FullConfigBody(code, weeklyNorm: 38m)),
        };
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        await AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configId}' AND weekly_norm_hours = 38");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configId}' AND action = 'UPDATED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'agreement-config-{configId}' AND event_type = 'AgreementConfigUpdated'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'agreement-config-{configId}' AND event_type = 'AgreementConfigUpdated'");
    }

    // ─────────────────────────────────────── PUBLISH (single) ────────────────────────────────

    /// <summary>
    /// <c>POST /api/agreement-configs/{id}/publish</c> with NO prior ACTIVE for the (unique)
    /// agreement_code → the SINGLE-emit publish leg (only the PUBLISHED event is enqueued; no
    /// supersede/ARCHIVED leg). Falsifiability: the publish handler runs
    /// <c>PublishAsync(conn,tx,id,expectedVersion)</c> (DRAFT→ACTIVE) + PUBLISHED audit +
    /// <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection in ONE tx under
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the ONLY enqueue; the config
    /// stays DRAFT and the 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c>
    /// moves before the enqueue, or if the state UPDATE runs on a self-managed connection.
    /// </summary>
    [Fact]
    public async Task Publish_RealEndpoint_SingleEmit_OutboxThrows_RollsBackWholePublish()
    {
        var baseClient = BaseAdminClient();
        // Unique agreement_code ⇒ no prior ACTIVE ⇒ single-emit publish (no ARCHIVED supersede leg).
        var (configId, etag) = await CreateConfigAsync(baseClient, "FR_PUB1_" + Rand());

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/agreement-configs/{configId}/publish");
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        await AssertServerError(rsp);

        // Config stayed DRAFT (a row at status=ACTIVE is the absence-witness).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configId}' AND status = 'ACTIVE'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configId}' AND action = 'PUBLISHED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'agreement-config-{configId}' AND event_type = 'AgreementConfigPublished'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'agreement-config-{configId}' AND event_type = 'AgreementConfigPublished'");
    }

    // ─────────────────────────────────── PUBLISH (dual-emit supersede) ───────────────────────

    /// <summary>
    /// <c>POST /api/agreement-configs/{id}/publish</c> that SUPERSEDES a prior ACTIVE for the same
    /// (agreement_code, ok_version) → the DUAL-EMIT leg (ADR-019 D1): the handler emits PUBLISHED
    /// (enqueue #1) then, because a prior ACTIVE was archived, ARCHIVED (enqueue #2), both in one
    /// tx. This is THE dual-emit conversion — it uses
    /// <see cref="StatsTidWebApplicationFactory.WithThrowOnSecondEnqueueOutbox"/>: enqueue #1
    /// genuinely lands the PUBLISHED outbox row in-tx, enqueue #2 (ARCHIVED) throws. A fault AFTER
    /// the archive leg must roll back the WHOLE tx INCLUDING the successfully-inserted #1 row.
    ///
    /// <para>Falsifiability: goes RED if the ARCHIVED supersede leg is ever moved OUT of the publish
    /// tx (its outbox row would survive on config B's stream), if the archive of A is switched to a
    /// self-connection overload (A would commit as ARCHIVED), or if <c>CommitAsync</c> moves before
    /// enqueue #2. The load-bearing witness is B's PUBLISHED outbox row — really inserted by
    /// enqueue #1 — being absent after rollback.</para>
    /// </summary>
    [Fact]
    public async Task Publish_RealEndpoint_SupersedeLeg_DualEmit_ThrowsOnSecondEnqueue_RollsBackWholeTx()
    {
        var baseClient = BaseAdminClient();
        var sharedCode = "FR_PUB2_" + Rand();

        // Prior ACTIVE (config A): create then genuinely publish on the base (non-throwing) host.
        var (configIdA, etagA) = await CreateConfigAsync(baseClient, sharedCode);
        var pubA = new HttpRequestMessage(
            HttpMethod.Post, $"/api/agreement-configs/{configIdA}/publish");
        pubA.Headers.IfMatch.Add(etagA);
        var pubARsp = await baseClient.SendAsync(pubA);
        Assert.Equal(System.Net.HttpStatusCode.OK, pubARsp.StatusCode);

        // New DRAFT (config B) for the SAME (agreement_code, ok_version) — publishing it supersedes A.
        var (configIdB, etagB) = await CreateConfigAsync(baseClient, sharedCode);

        using var throwingHost = _factory.WithThrowOnSecondEnqueueOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/agreement-configs/{configIdB}/publish");
        req.Headers.IfMatch.Add(etagB);
        var rsp = await client.SendAsync(req);

        await AssertServerError(rsp);

        // B stayed DRAFT; A stayed ACTIVE (neither state transition committed).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configIdB}' AND status = 'ACTIVE'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configIdA}' AND status = 'ARCHIVED'");
        // No PUBLISHED audit for B, no ARCHIVED audit for A.
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configIdB}' AND action = 'PUBLISHED'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configIdA}' AND action = 'ARCHIVED'");
        // LOAD-BEARING: B's PUBLISHED outbox row (enqueue #1 REALLY inserted it) rolled back.
        // (B's CREATED event from the seed is legitimately present, hence the event_type filter.)
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'agreement-config-{configIdB}' AND event_type = 'AgreementConfigPublished'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'agreement-config-{configIdB}' AND event_type = 'AgreementConfigPublished'");
        // No ARCHIVED event for A (A's own PUBLISHED event from its successful base-host publish
        // is legitimate; only the ARCHIVED supersede event must be absent).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'agreement-config-{configIdA}' AND event_type = 'AgreementConfigArchived'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'agreement-config-{configIdA}' AND event_type = 'AgreementConfigArchived'");
    }

    // ───────────────────────────────────────── ARCHIVE ───────────────────────────────────────

    /// <summary>
    /// <c>POST /api/agreement-configs/{id}/archive</c> (admin-strict If-Match). Single-emit.
    /// Falsifiability: the archive handler runs <c>ArchiveAsync(conn,tx,id,expectedVersion)</c> +
    /// ARCHIVED audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c> + audit-projection in ONE tx under
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue; the config stays
    /// DRAFT and the 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c> moves
    /// before the enqueue, or if the state UPDATE runs on a self-managed connection.
    /// </summary>
    [Fact]
    public async Task Archive_RealEndpoint_OutboxThrows_RollsBackWholeArchive()
    {
        var baseClient = BaseAdminClient();
        var (configId, etag) = await CreateConfigAsync(baseClient, "FR_ARC_" + Rand());

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/agreement-configs/{configId}/archive");
        req.Headers.IfMatch.Add(etag);
        var rsp = await client.SendAsync(req);

        await AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configId}' AND status = 'ARCHIVED'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configId}' AND action = 'ARCHIVED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"stream_id = 'agreement-config-{configId}' AND event_type = 'AgreementConfigArchived'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"stream_id = 'agreement-config-{configId}' AND event_type = 'AgreementConfigArchived'");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static string Rand() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private static async Task AssertServerError(HttpResponseMessage rsp)
    {
        Assert.True((int)rsp.StatusCode >= 500,
            $"expected a 5xx from the escaped outbox throw, got {(int)rsp.StatusCode}");
        await Task.CompletedTask;
    }

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

    /// <summary>Creates a DRAFT config via the REAL create endpoint; returns its server id + ETag.</summary>
    private async Task<(Guid ConfigId, EntityTagHeaderValue Etag)> CreateConfigAsync(
        HttpClient client, string agreementCode, decimal weeklyNorm = 37m)
    {
        var rsp = await client.PostAsJsonAsync("/api/agreement-configs", FullConfigBody(agreementCode, weeklyNorm));
        Assert.Equal(System.Net.HttpStatusCode.Created, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var configId = body.GetProperty("configId").GetGuid();
        Assert.NotNull(rsp.Headers.ETag);
        return (configId, rsp.Headers.ETag!);
    }

    /// <summary>
    /// A JWT signed with the dev-fallback key claiming <c>role=GlobalAdmin</c>. The
    /// <c>GlobalAdminOnly</c> policy's <c>ScopeRequirement</c> has <c>requireOrgScope:false</c>, so
    /// the bare role claim admits — no orgId, no scopes, no seeded org/user required.
    /// </summary>
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

    /// <summary>
    /// A fully-populated, validation-passing agreement-config body (mirrors the spike template's
    /// <c>NewRequestBody</c>). Every <c>required</c> DTO member is present so model-binding does not
    /// 400 before the handler; the numeric values satisfy <c>ValidateRequest</c>.
    /// </summary>
    private static object FullConfigBody(string agreementCode, decimal weeklyNorm) => new
    {
        agreementCode,
        okVersion = "OK24",
        description = "QUAL-016 lifecycle forced-rollback",
        normModel = "WEEKLY_HOURS",
        weeklyNormHours = weeklyNorm,
        normPeriodWeeks = 1,
        annualNormHours = 1924m,
        maxFlexBalance = 100m,
        flexCarryoverMax = 50m,
        hasOvertime = true,
        hasMerarbejde = false,
        overtimeThreshold50 = 37m,
        overtimeThreshold100 = 40m,
        eveningSupplementEnabled = false,
        nightSupplementEnabled = false,
        weekendSupplementEnabled = false,
        holidaySupplementEnabled = false,
        eveningStart = 17,
        eveningEnd = 23,
        nightStart = 23,
        nightEnd = 6,
        eveningRate = 1.25m,
        nightRate = 1.5m,
        weekendSaturdayRate = 1.5m,
        weekendSundayRate = 2m,
        holidayRate = 2m,
        onCallDutyEnabled = false,
        onCallDutyRate = 0.33m,
        callInWorkEnabled = false,
        callInMinimumHours = 3m,
        callInRate = 1m,
        travelTimeEnabled = false,
        workingTravelRate = 1m,
        nonWorkingTravelRate = 0.5m,
        maxDailyHours = 13m,
        minimumRestHours = 11m,
        restPeriodDerogationAllowed = false,
        weeklyMaxHoursReferencePeriod = 17,
        voluntaryUnsocialHoursAllowed = true,
    };
}
