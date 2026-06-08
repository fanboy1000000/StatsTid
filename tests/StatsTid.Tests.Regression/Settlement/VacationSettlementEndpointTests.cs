using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S68 / TASK-6808 (ADR-033 D8/D10) — Docker-gated wire-contract tests for
/// <see cref="StatsTid.Backend.Api.Endpoints.VacationSettlementEndpoints"/>: the §21 transfer-
/// agreement write (POST/PUT), the D10 resolve (FORFEIT/DEFER), and the §24 payout-pending list +
/// reconcile. WAF&lt;Program&gt; harness; JWT minting + the HROrAbove RBAC matrix + cross-org binding
/// mirror <see cref="Config.EmployeeProfileEndpointTests"/> (same policy + OrgScopeValidator shape).
///
/// <para><b>Today-dependence (no wall-clock EXPECTED values; the Adr032RevaluationTests convention).</b>
/// The §21 deadline is the Copenhagen business clock (a private endpoint helper, NOT the injectable
/// TimeProvider), so deadline/future-date guards are real-today-anchored. We use ferieår
/// <see cref="RecordableYear"/> whose §21 deadline (31 Dec E+1) is in the future relative to the real
/// clock, and derive agreement dates from <see cref="Today"/> — asserting the guard OUTCOMES
/// (200/403/409/412/422) and the invariants, never a wall-clock-dependent literal.</para>
///
/// <para>The PENDING_REVIEW fixtures (for the D10 resolve + reconcile tests) are written DIRECTLY into
/// <c>vacation_settlements</c> with a hand-built snapshot — the slice-1a poller only settles a year
/// whose boundary has passed, and seeding the row directly keeps the resolve/reconcile CAS tests
/// independent of the boundary clock.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class VacationSettlementEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";        // emp001's org (/MIN01/STY01/)
    private const string DisjointOrg = "STY05";  // /MIN02/STY05/ — disjoint from STY01
    private const string CoveringOrg = "MIN01";  // covers STY01 via ORG_AND_DESCENDANTS
    private const string VacationType = "VACATION";

    // A ferieår whose §21 deadline (31 Dec of E+1) is in the FUTURE relative to the real clock
    // (today 2026), so the §21 write happy-path/guards are recordable. E=2025 ⇒ deadline 31 Dec 2026.
    private const int RecordableYear = 2025;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (VACATION config quota 25 / carryover_max 5)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ════════════════════════════════════════════════════════════════════════
    // §21 transfer-agreement write — happy path + the legal/state guards (scenario 8).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>POST a valid §21 agreement (VACATION, transfer 3 ≤ cap 5, agreement_date today ≤
    /// deadline) → 201 with an ETag; the row persists with the agreed days.</summary>
    [Fact]
    public async Task Post21Agreement_Valid_Returns201_WithEtag()
    {
        var employeeId = await SeedEmployeeAsync();
        var rsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, transferDays: 3m, agreementDate: Today);

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3m, body.GetProperty("transferDays").GetDecimal());
        Assert.Equal(1L, body.GetProperty("version").GetInt64());
    }

    /// <summary>HROrAbove RBAC: an Employee token → 403; a LocalLeader token (not in the policy's
    /// allowed set) → 403. (GlobalAdmin/LocalAdmin/LocalHR are admitted, exercised by the happy path.)</summary>
    [Fact]
    public async Task Post21Agreement_LowerRoles_Return403()
    {
        var employeeId = await SeedEmployeeAsync();

        var empRsp = await PostTransferAgreementAsync(
            ClientWith(EmployeeToken(employeeId, OrgId)), employeeId, RecordableYear, VacationType, 3m, Today);
        Assert.Equal(HttpStatusCode.Forbidden, empRsp.StatusCode);

        var leaderRsp = await PostTransferAgreementAsync(
            ClientWith(LeaderToken("leader_qa", OrgId)), employeeId, RecordableYear, VacationType, 3m, Today);
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
    }

    /// <summary>Cross-org binding (FAIL-001): an HR token scoped to a DISJOINT org subtree (STY05)
    /// recording an agreement for an STY01 employee → 403 (the OrgScopeValidator binds the actor to
    /// the target's org; HROrAbove alone is not enough).</summary>
    [Fact]
    public async Task Post21Agreement_CrossOrgHr_Returns403()
    {
        var employeeId = await SeedEmployeeAsync();
        var rsp = await PostTransferAgreementAsync(HrClient(DisjointOrg), employeeId,
            RecordableYear, VacationType, 3m, Today);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>VACATION-only: a non-VACATION entitlement_type → 422 (§21 stk.2 is the &gt;4-week
    /// VACATION tranche only).</summary>
    [Fact]
    public async Task Post21Agreement_NonVacationType_Returns422()
    {
        var employeeId = await SeedEmployeeAsync();
        var rsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, "SPECIAL_HOLIDAY", transferDays: 1m, agreementDate: Today);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>transfer_days &gt; carryover_max (the statutory cap 5 for VACATION) → 422.</summary>
    [Fact]
    public async Task Post21Agreement_OverCap_Returns422()
    {
        var employeeId = await SeedEmployeeAsync();
        var rsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, transferDays: 6m, agreementDate: Today); // cap is 5
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>A FUTURE agreement_date (after the Copenhagen business clock) → 422 (cannot record an
    /// agreement dated in the future).</summary>
    [Fact]
    public async Task Post21Agreement_FutureAgreementDate_Returns422()
    {
        var employeeId = await SeedEmployeeAsync();
        var rsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, transferDays: 3m, agreementDate: Today.AddDays(7));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>agreement_date AFTER the §21 deadline (31 Dec E+1) → 422. Uses a ferieår whose
    /// deadline is in the PAST (E=2020 ⇒ deadline 31 Dec 2021) so any non-future agreement_date
    /// exceeds it; we send a date past that deadline but not in the future (a 2024 date), which
    /// trips the "after the deadline" / "deadline has passed" 422 branch.</summary>
    [Fact]
    public async Task Post21Agreement_AfterDeadline_Returns422()
    {
        var employeeId = await SeedEmployeeAsync();
        // Past-deadline ferieår 2020 (deadline 31 Dec 2021). An agreement dated 2024-01-01 is both
        // after the deadline AND not in the future ⇒ the deadline guard (not the future guard) fires.
        var rsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            entitlementYear: 2020, VacationType, transferDays: 3m, agreementDate: new DateOnly(2024, 1, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>An active settlement already exists for the year → 409 (a §21 transfer cannot be
    /// agreed for an already-settled ferieår). We seed a SETTLED row directly, then POST.</summary>
    [Fact]
    public async Task Post21Agreement_ActiveSettlementExists_Returns409()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "SETTLED",
            transfer: 0m, payout: 5m, forfeit: 0m, version: 1, reviewDisposition: null);

        var rsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, transferDays: 3m, agreementDate: Today);
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
    }

    /// <summary>PUT edit-in-place requires admin-strict If-Match: a stale token → 412. POST creates
    /// at version 1; PUT with If-Match "0" (stale) → 412 with expected/actual versions.</summary>
    [Fact]
    public async Task Put21Agreement_StaleIfMatch_Returns412()
    {
        var employeeId = await SeedEmployeeAsync();
        var createRsp = await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, 3m, Today);
        Assert.Equal(HttpStatusCode.Created, createRsp.StatusCode);

        var rsp = await PutTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, transferDays: 4m, agreementDate: Today, ifMatch: "\"0\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
    }

    /// <summary>PUT with a missing If-Match → 428 (admin-strict; the precondition is required).</summary>
    [Fact]
    public async Task Put21Agreement_MissingIfMatch_Returns428()
    {
        var employeeId = await SeedEmployeeAsync();
        await PostTransferAgreementAsync(HrClient(CoveringOrg), employeeId, RecordableYear, VacationType, 3m, Today);

        var rsp = await PutTransferAgreementAsync(HrClient(CoveringOrg), employeeId,
            RecordableYear, VacationType, transferDays: 4m, agreementDate: Today, ifMatch: null);
        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // D10 resolve — FORFEIT / DEFER (scenario 7).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>FORFEIT a PENDING_REVIEW settlement → CAS PENDING_REVIEW→SETTLED, review_disposition
    /// FORFEIT, the row's forfeit_days stands, and exactly ONE <c>VacationForfeitedToFeriefond</c>
    /// event is emitted with a matching audit_projection row. A second FORFEIT (now SETTLED) → 409,
    /// no double-emit.</summary>
    [Fact]
    public async Task Resolve_Forfeit_SettlesAndEmitsOnce_SecondIs409()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m, version: 1, reviewDisposition: null);

        var rsp = await ResolveAsync(HrClient(CoveringOrg), employeeId, RecordableYear,
            disposition: "FORFEIT", forfeitDays: null, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SETTLED", body.GetProperty("settlementState").GetString());
        Assert.Equal("FORFEIT", body.GetProperty("reviewDisposition").GetString());
        Assert.True(body.GetProperty("resolved").GetBoolean());

        // Exactly one forfeit event + one audit_projection row.
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
        Assert.True(await CountAuditProjectionByTypeAsync("VacationForfeitedToFeriefond") >= 1);

        // Re-FORFEIT (now SETTLED, version bumped to 2) → 409 (not PENDING_REVIEW), no double-emit.
        var rsp2 = await ResolveAsync(HrClient(CoveringOrg), employeeId, RecordableYear,
            disposition: "FORFEIT", forfeitDays: null, ifMatch: "\"2\"");
        Assert.Equal(HttpStatusCode.Conflict, rsp2.StatusCode);
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond")); // still once
    }

    /// <summary>A stale If-Match on FORFEIT → 412 (the CAS loser), no settlement transition, no
    /// forfeit event.</summary>
    [Fact]
    public async Task Resolve_Forfeit_StaleIfMatch_Returns412_NoEmit()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m, version: 1, reviewDisposition: null);

        var rsp = await ResolveAsync(HrClient(CoveringOrg), employeeId, RecordableYear,
            disposition: "FORFEIT", forfeitDays: null, ifMatch: "\"0\""); // stale (current = 1)
        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));

        // The row is untouched — still PENDING_REVIEW at version 1.
        var row = await ReadSettlementAsync(employeeId, RecordableYear);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
    }

    /// <summary>FORFEIT with an explicit forfeitDays NOT equal to the flagged §34 remainder → 422
    /// (FORFEIT forfeits the WHOLE remainder; a partial is DEFER's job, slice 4). The flagged
    /// remainder is 20; sending 10 → 422, no transition.</summary>
    [Fact]
    public async Task Resolve_Forfeit_MismatchedForfeitDays_Returns422()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m, version: 1, reviewDisposition: null);

        var rsp = await ResolveAsync(HrClient(CoveringOrg), employeeId, RecordableYear,
            disposition: "FORFEIT", forfeitDays: 10m, ifMatch: "\"1\""); // flagged remainder is 20
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
    }

    /// <summary>DEFER a PENDING_REVIEW settlement → the row STAYS PENDING_REVIEW with
    /// review_disposition DEFER and a bumped version; NO forfeit event (impediment modeling is slice
    /// 4). resolved=false.</summary>
    [Fact]
    public async Task Resolve_Defer_StaysPendingReview_NoEmit()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m, version: 1, reviewDisposition: null);

        var rsp = await ResolveAsync(HrClient(CoveringOrg), employeeId, RecordableYear,
            disposition: "DEFER", forfeitDays: null, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDING_REVIEW", body.GetProperty("settlementState").GetString());
        Assert.Equal("DEFER", body.GetProperty("reviewDisposition").GetString());
        Assert.False(body.GetProperty("resolved").GetBoolean());

        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
        var row = await ReadSettlementAsync(employeeId, RecordableYear);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal("DEFER", row.Value.ReviewDisposition);
        Assert.Equal(2L, row.Value.Version); // version bumped
    }

    /// <summary>Resolve RBAC: a lower role (Employee) → 403.</summary>
    [Fact]
    public async Task Resolve_LowerRole_Returns403()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "PENDING_REVIEW",
            transfer: 0m, payout: 5m, forfeit: 20m, version: 1, reviewDisposition: null);

        var rsp = await ResolveAsync(ClientWith(EmployeeToken(employeeId, OrgId)), employeeId,
            RecordableYear, disposition: "FORFEIT", forfeitDays: null, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // §24 payout-pending list + reconcile (scenario 9).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The payout-pending list returns SETTLED rows with payout_days &gt; 0 not yet
    /// reconciled, org-scoped. A SETTLED+payout employee appears for an in-scope HR; reconciling it
    /// (CAS) sets payout_reconciled_* and removes it from the list; a stale-If-Match reconcile loser
    /// → 412.</summary>
    [Fact]
    public async Task PayoutPending_ListsAndReconciles_OrgScoped()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "SETTLED",
            transfer: 0m, payout: 5m, forfeit: 0m, version: 1, reviewDisposition: null);

        // In-scope HR sees the row in the payout-pending list.
        var listRsp = await HrClient(CoveringOrg).GetAsync("/api/vacation-settlements/payout-pending");
        Assert.Equal(HttpStatusCode.OK, listRsp.StatusCode);
        var list = await listRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.GetProperty("items").EnumerateArray(),
            it => it.GetProperty("employeeId").GetString() == employeeId
                  && it.GetProperty("payoutDays").GetDecimal() == 5m);

        // A DISJOINT-org HR does NOT see it (org-scope filter).
        var oosList = await (await HrClient(DisjointOrg).GetAsync("/api/vacation-settlements/payout-pending"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(oosList.GetProperty("items").EnumerateArray(),
            it => it.GetProperty("employeeId").GetString() == employeeId);

        // Reconcile (CAS) — sets payout_reconciled_* + bumps version.
        var recRsp = await ReconcilePayoutAsync(HrClient(CoveringOrg), employeeId, RecordableYear, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, recRsp.StatusCode);
        var recBody = await recRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2L, recBody.GetProperty("version").GetInt64());
        Assert.False(string.IsNullOrEmpty(recBody.GetProperty("payoutReconciledBy").GetString()));

        // Now it is gone from the pending list (reconciled).
        var afterList = await (await HrClient(CoveringOrg).GetAsync("/api/vacation-settlements/payout-pending"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(afterList.GetProperty("items").EnumerateArray(),
            it => it.GetProperty("employeeId").GetString() == employeeId);

        // A second reconcile (now already-reconciled) → 409. The endpoint's already-reconciled
        // guard ("Payout already reconciled.") precedes the If-Match version check, so the duplicate
        // is reported as 409 Conflict regardless of the supplied If-Match — the more informative
        // outcome for a double-reconcile (a stale token on a STILL-un-reconciled row would 412, but
        // here the row is already reconciled so the conflict guard wins).
        var dupeRsp = await ReconcilePayoutAsync(HrClient(CoveringOrg), employeeId, RecordableYear, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, dupeRsp.StatusCode);
    }

    /// <summary>Reconciling a settlement with NO §24 payout bucket (payout_days = 0) → 409 (nothing
    /// to reconcile).</summary>
    [Fact]
    public async Task ReconcilePayout_NoPayoutBucket_Returns409()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, RecordableYear, state: "SETTLED",
            transfer: 5m, payout: 0m, forfeit: 0m, version: 1, reviewDisposition: null);

        var rsp = await ReconcilePayoutAsync(HrClient(CoveringOrg), employeeId, RecordableYear, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static async Task<HttpResponseMessage> PostTransferAgreementAsync(
        HttpClient client, string employeeId, int entitlementYear, string entitlementType,
        decimal transferDays, DateOnly agreementDate)
        => await client.PostAsJsonAsync($"/api/vacation-transfer-agreements/{employeeId}", new
        {
            entitlementYear,
            entitlementType,
            transferDays,
            agreementDate = agreementDate.ToString("yyyy-MM-dd"),
        });

    private static async Task<HttpResponseMessage> PutTransferAgreementAsync(
        HttpClient client, string employeeId, int entitlementYear, string entitlementType,
        decimal transferDays, DateOnly agreementDate, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/vacation-transfer-agreements/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                entitlementYear,
                entitlementType,
                transferDays,
                agreementDate = agreementDate.ToString("yyyy-MM-dd"),
            }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> ResolveAsync(
        HttpClient client, string employeeId, int year, string disposition, decimal? forfeitDays, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/resolve")
        {
            Content = JsonContent.Create(new { disposition, forfeitDays }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> ReconcilePayoutAsync(
        HttpClient client, string employeeId, int year, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/reconcile-payout")
        {
            Content = JsonContent.Create(new { }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ─────────────────────────────── clients / tokens ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient HrClient(string scopeOrgId) =>
        ClientWith(HrToken("hr_s68_qa", scopeOrgId));

    private static string HrToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_AND_DESCENDANTS") });
    }

    private static string EmployeeToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string LeaderToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalLeader,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_AND_DESCENDANTS") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s68_ep_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Seeds a <c>vacation_settlements</c> row directly with a minimal valid snapshot JSON
    /// (the resolve/reconcile paths deserialize it for the forfeit event; an empty-but-valid object
    /// round-trips). payout_reconciled_* stay null (the paired-nullable CHECK holds).</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string state,
        decimal transfer, decimal payout, decimal forfeit, long version, string? reviewDisposition)
    {
        // A minimal valid snapshot (matches VacationSettlementSnapshot camelCase shape; all value
        // defaults + an empty recordedAbsences list). The resolve path deserializes it for the event.
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            transferAgreementDays = transfer,
            isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, @y, 1, @state, 'YEAR_END', @snapshot::jsonb, @transfer, @payout, @forfeit,
                 @disp, @version)
            ON CONFLICT (employee_id, entitlement_type, entitlement_year, sequence)
                DO UPDATE SET settlement_state = EXCLUDED.settlement_state,
                              transfer_days = EXCLUDED.transfer_days,
                              payout_days = EXCLUDED.payout_days,
                              forfeit_days = EXCLUDED.forfeit_days,
                              review_disposition = EXCLUDED.review_disposition,
                              version = EXCLUDED.version
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transfer);
        cmd.Parameters.AddWithValue("payout", payout);
        cmd.Parameters.AddWithValue("forfeit", forfeit);
        cmd.Parameters.AddWithValue("disp", (object?)reviewDisposition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, string? ReviewDisposition, long Version, decimal Forfeit)?>
        ReadSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, review_disposition, version, forfeit_days
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt64(2), reader.GetDecimal(3));
    }

    private async Task<long> CountOutboxByTypeAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountAuditProjectionByTypeAsync(string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = @t", conn);
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
