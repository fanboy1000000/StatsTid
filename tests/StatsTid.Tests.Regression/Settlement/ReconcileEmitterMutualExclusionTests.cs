using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S69 / TASK-6907 (ADR-033 slice 1b, Step-0b B2 / TASK-6905) — Docker-gated D-tests for the
/// claim/reconcile MUTUAL EXCLUSION (reconcile XOR emitter-claim) across the Backend reconcile-payout
/// endpoint and the Payroll <see cref="StatsTid.Integrations.Payroll.Services.SettlementExportEmitter"/>.
/// Both writers acquire the SAME employee advisory lock; exactly one disposition per
/// <c>(identity, sequence, bucket)</c>. Tested in BOTH winner orderings (scenario 4).
///
/// <para>
/// The reconcile endpoint is driven over the real WAF&lt;Program&gt; with a JWT-minted HR token (the
/// same RBAC/OrgScope shape as <see cref="VacationSettlementEndpointTests"/>); the emitter is driven
/// via <see cref="SettlementEmitterFixture.ProcessOnceAsync"/> (one real drain). A FRESH employee per
/// test (unique GUID). FAIL-002 protocol per <see cref="SettlementEmitterFixture"/>.
/// </para>
///
/// <para>
/// SCOPE (Step-7a FIX 2 — explicit limitation). These tests run the two writers SEQUENTIALLY in each
/// winner ordering (reconcile-then-claim and claim-then-reconcile) and assert the loser observes the
/// winner's committed state and refuses (the 409 pre-check / the SKIPPED_RECONCILED in-lock probe). They
/// verify the PRE-CHECK / committed-state logic — they do NOT spin two workers to contend for the shared
/// advisory lock simultaneously. That is INTENTIONAL and single-emitter-instance-MOOT for slice 1b: the
/// emitter is ONE BackgroundService instance and reconcile is an operator action, so a truly simultaneous
/// in-lock collision does not arise in production. The true under-lock mutual exclusion (both writers take
/// the SAME <c>pg_advisory_xact_lock('employee-'||id)</c>, so the second to acquire it observes the
/// first's committed disposition) is the production protection, verified by the Step-5a/Step-7a dual-lens
/// code review; a full two-worker coordinated lock-race harness is out of scope for this fixture.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReconcileEmitterMutualExclusionTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string CoveringOrg = "STY01";  // S93 flat role-scope: covers STY01 by exact ORG_ONLY match (a MAO no longer covers a child)
    private const int Year = 2024;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private string Cs => _harness.ConnectionString;
    private DbConnectionFactory Factory => new(Cs);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
        _factory = new StatsTidWebApplicationFactory(Cs);
        _ = _factory.CreateClient(); // boot seeders
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Ordering A — the EMITTER claims first ⇒ a later reconcile is refused (409).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The emitter stages the §24 line first (PROCESSED); a subsequent operator
    /// reconcile-payout for the same settlement is refused with 409 (the line/checkpoint-absence
    /// pre-check under the shared lock) — no double disposition.</summary>
    [Fact]
    public async Task EmitterClaimsFirst_ThenReconcile_Returns409_NoDoublePay()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s69_xorA_");
        // A SETTLED, un-reconciled §24 payout row (version 1) + its source event.
        await SettlementEmitterFixture.SeedSettledPayoutRowAsync(Cs, emp, payoutDays: 5m, reconciled: false);
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(Factory, emp, payoutDays: 5m);

        // Emitter claims the bucket first.
        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));

        // The operator's reconcile now collides ⇒ 409 (reconcile XOR machine-claim).
        var rsp = await ReconcileAsync(emp, Year, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);

        // No double disposition: still exactly one line, and the settlement stays un-reconciled.
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
        Assert.False(await PayoutReconciledAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Ordering B — the OPERATOR reconciles first ⇒ the emitter skips (no line).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The operator reconciles the §24 payout first (200, marker set); the emitter then sees
    /// <c>payout_reconciled_at</c> and SKIPS — terminal SKIPPED_RECONCILED, NO line. No double
    /// disposition.</summary>
    [Fact]
    public async Task ReconcileFirst_ThenEmitter_Skips_NoLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s69_xorB_");
        await SettlementEmitterFixture.SeedSettledPayoutRowAsync(Cs, emp, payoutDays: 5m, reconciled: false);
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(Factory, emp, payoutDays: 5m);

        // Operator reconciles first ⇒ 200 (marker set).
        var rsp = await ReconcileAsync(emp, Year, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.True(await PayoutReconciledAsync(emp));

        // Emitter then skips: SKIPPED_RECONCILED, no line.
        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "SKIPPED_RECONCILED");

        Assert.Equal("SKIPPED_RECONCILED", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
    }

    // ─────────────────────────────── reconcile HTTP ───────────────────────────────

    private async Task<HttpResponseMessage> ReconcileAsync(string employeeId, int year, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{SettlementEmitterFixture.VacationType}/{year}/reconcile-payout")
        {
            Content = JsonContent.Create(new { }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await HrClient(CoveringOrg).SendAsync(req);
    }

    private HttpClient HrClient(string scopeOrgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HrToken("hr_s69_qa", scopeOrgId));
        return client;
    }

    private static string HrToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });
    }

    private async Task<bool> PayoutReconciledAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT payout_reconciled_at IS NOT NULL FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND sequence = 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", SettlementEmitterFixture.VacationType);
        cmd.Parameters.AddWithValue("y", Year);
        var v = await cmd.ExecuteScalarAsync();
        return v is true;
    }
}
