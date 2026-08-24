using System.Net.Http.Headers;
using System.Net.Http.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13307 (QUAL-016 SPIKE) — the WIRE-DRIVEN conversion of the Pattern-B
/// (audit-emitting) atomic-outbox proof for <c>POST /api/agreement-configs</c>.
///
/// <para><b>What this replaces and why (plain-language):</b> the legacy
/// <c>Outbox.AgreementConfigAtomicTests.Create_OutboxFails_RollsBack</c> (now retired)
/// HAND-TYPES the endpoint's save sequence in the test body (open conn → begin tx → repo
/// <c>CreateAsync(conn,tx)</c> → append audit → throwing enqueue → commit) and asserts rollback.
/// Because it re-implements the sequence instead of calling the shipped endpoint, it proves only
/// "my hand-typed sequence rolls back" — if the REAL endpoint's orchestration drifted (committed
/// before the enqueue, opened its own connection, or was deleted) the legacy test would stay
/// green. This test drives the REAL endpoint over authenticated HTTP through a host whose
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/> throws
/// (<see cref="StatsTidWebApplicationFactory.WithThrowingOutbox"/>), so it pins the wiring the
/// legacy test could not.</para>
///
/// <para><b>Falsifiability (the guard this now pins):</b> the create handler runs
/// <c>CreateReturningAsync(conn,tx)</c> + audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c> +
/// audit-projection, all inside ONE transaction, and only THEN <c>tx.CommitAsync</c>. The
/// throwing outbox faults the enqueue BEFORE the commit; the transaction disposes without
/// committing, so PostgreSQL rolls back the config row, the CREATED audit row, the outbox row
/// and the audit-projection row together, and the escaped throw surfaces as a 5xx. This test
/// goes RED if the endpoint is deleted (route 404/405, not 5xx), if <c>CommitAsync</c> is moved
/// before the enqueue, or if any leg is switched to a self-managed (own-connection) overload —
/// each of which the hand-mirrored legacy test cannot detect.</para>
///
/// <para><b>Auth/seed cost (spike measurement):</b> the endpoint's policy is
/// <c>GlobalAdminOnly</c>, whose <c>ScopeRequirement</c> has <c>requireOrgScope:false</c> — a bare
/// <c>role=GlobalAdmin</c> token passes with NO org scope and NO org/user seed. The create path
/// inserts a self-contained <c>agreement_configs</c> row (no FK parents), so this conversion needs
/// ZERO row seeding — only the full <c>init.sql</c> schema the WAF already applies.</para>
///
/// <para><b>Witness keying (spike measurement):</b> the config_id is generated server-side
/// (<c>gen_random_uuid()</c>) INSIDE the rolled-back tx and is never returned (the response is a
/// 5xx with no id), so the outbox/event stream id <c>agreement-config-{configId}</c> is unknown to
/// the test. The four "no leakage" checks therefore key on the test-unique <c>agreementCode</c>
/// witness instead — reusing the generic <see cref="ForcedRollbackHarness.AssertNoStateMutationAsync"/>
/// / <see cref="ForcedRollbackHarness.AssertNoAuditRowAsync"/> count-filter helpers (state on the
/// code column; audit/outbox/event on a <c>::text LIKE</c> over the JSON payload, which carries the
/// code regardless of property-name casing). This unknown-server-id wrinkle is specific to
/// create/clone endpoints and is called out in the spike cost survey.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementConfigCreateAtomicHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        // Full Backend.Api schema — the create tx writes agreement_configs + agreement_config_audit
        // + outbox_events + events + audit_projection, all of which live in init.sql.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot the BASE host once so its idempotent startup seeders (agreement/entitlement/profile)
        // run against the empty DB HERE — before the throwing host derives from it. A seeder that
        // still had backfill to do would otherwise invoke the throwing outbox at startup.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task Create_RealEndpoint_OutboxThrows_RollsBackWholeCreate()
    {
        // A test-unique agreement_code is the absence-witness across all four tables.
        var agreementCode = "FR_HTTP_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = throwingHost.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken("admin_qual016"));

        var rsp = await client.PostAsJsonAsync("/api/agreement-configs", NewRequestBody(agreementCode));

        // The enqueue threw before commit; the escaped throw is surfaced as a 5xx (no commit).
        Assert.True((int)rsp.StatusCode >= 500,
            $"expected a 5xx from the escaped outbox throw, got {(int)rsp.StatusCode}");

        // Whole-create rollback — NOTHING for this agreement_code committed on a fresh connection:
        // (1) no state row …
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"agreement_code = '{agreementCode}'");
        // (2) no CREATED audit row (its new_data JSON carries the code) …
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"action = 'CREATED' AND new_data::text LIKE '%{agreementCode}%'");
        // (3) no outbox row (its event_payload JSON carries the code) …
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "outbox_events",
            $"event_type = 'AgreementConfigCreated' AND event_payload::text LIKE '%{agreementCode}%'");
        // (4) no canonical event row (the publisher never drains a rolled-back outbox row anyway).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "events",
            $"event_type = 'AgreementConfigCreated' AND data::text LIKE '%{agreementCode}%'");
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
    /// A fully-populated, validation-passing create body. Every <c>required</c> member of the
    /// endpoint's request DTO must be present or model-binding 400s before the handler runs; the
    /// numeric values satisfy <c>ValidateRequest</c> (weekly norm 0&lt;37≤50, norm-period-weeks in
    /// the valid set, positive annual/daily/rest knobs, threshold100 ≥ threshold50, hour windows
    /// 0–23). This full-DTO materialization is a per-endpoint cost the spike survey notes for the
    /// config-family (large request bodies) vs. the cheap two-field time/absence bodies.
    /// </summary>
    private static object NewRequestBody(string agreementCode) => new
    {
        agreementCode,
        okVersion = "OK24",
        description = "QUAL-016 spike forced-rollback",
        normModel = "WEEKLY_HOURS",
        weeklyNormHours = 37m,
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
