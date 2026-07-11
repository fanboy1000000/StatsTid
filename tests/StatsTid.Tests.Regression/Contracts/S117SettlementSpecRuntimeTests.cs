using System.Data;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S117 / TASK-11703 — the per-route spec≡runtime gate for the SETTLEMENT bucket: the 6 ops
/// drained in retrofit Pass 4 (TASK-11701). Op 5 (<c>POST .../resolve</c>) is deliberately
/// ABSENT — it took the flag-and-defer rule's second firing (owner-ruled S117 OQ-2: 4 success
/// branches with genuinely different key sets) and remains untyped, so there is no declared
/// contract to gate.
///
/// <para><b>The headline assertion (the mechanism's first-new-consumer live proof):</b> op 2's
/// <c>successor</c> member is the S117 nullable-complex WRAPPER's first NEW consumer
/// (<c>type: object</c> + <c>allOf: [$ref]</c> + <c>nullable: true</c>, required — TASK-11700).
/// BOTH branches are proven against the REAL endpoint: null (a BARE reversal — admitted by the
/// wrapper-level <c>nullable: true</c>) AND object (a SUPERSEDED reversal — the matcher recurses
/// THROUGH the wrapper into the 4-field <c>SettlementSuccessor</c>, exercising
/// <c>settlementState</c>/<c>trigger</c> enum fidelity on live values). The RED-on-lie proof
/// corrupts the INNER wrapped schema's <c>required</c> set and demonstrates the matcher throws
/// from INSIDE the wrapper (the message carries the <c>.successor</c> context) — the Docker-level
/// recursion proof complementing 11700's unit RED.</para>
///
/// <para><b>State truth (never SQL-faked):</b> every settlement state is driven through the REAL
/// machinery. SETTLED YEAR_END rows come from <c>VacationSettlementService.SettleAsync</c> driven
/// the exact <c>SettlementCloseService</c> way (own ReadCommitted tx — the
/// <c>VacationSettlementServiceTests</c>/<c>SettlementReversalEndpointTests</c> drive); the §24
/// payout bucket arises from the legal partition itself (balance <c>used</c> is INPUT data seeded
/// so <c>disposable ≤ carryover_max</c> ⇒ SETTLED with <c>payout_days &gt; 0</c>). Leavers are
/// made through the REAL end-date PUT (<c>/api/admin/employees/{id}/employment-end-date</c> — the
/// lifecycle writer deactivates under the fixed clock), then TERMINATION-settled through the same
/// service; reversal/supersession/reconcile transitions all go through their REAL endpoints.
/// Anchor set mirrors the S71 suites: fixed clock 2026-03-05, end date 2026-02-28 ⇒ ferieår 2025,
/// crystallized 12.5; go-live floor 2025-01-01 for the supersede surface.</para>
///
/// <para><b>Seed discipline (FAIL-002 — the settlement contention epicenter):</b> a FRESH
/// testcontainer DB per test (the established harness), and ALL ids are S117-prefixed and
/// DISJOINT from every existing Settlement suite: own org tree <c>S117SMAO</c>/<c>S117SO01</c>
/// (vs their STY01/STY05), employees <c>s117s_*</c> (vs <c>emp_s68_ep_*</c>,
/// <c>emp_s69_*</c>/<c>emp_s69_xor*</c>, <c>emp_s71_req_*</c>/<c>emp_s71_revep_*</c>/
/// <c>emp_s71_waiver_*</c>, <c>emp_s79_fh_*</c>), actor <c>s117s_hr</c> (vs <c>hr_s68_qa</c>,
/// <c>hr_s69_qa</c>, <c>hr_s71_req/rev_ep/waiver</c>, <c>hr_s79_fh</c>). The shared
/// <c>SettlementEmitterFixture</c> is NOT referenced. Matcher + Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S117SettlementSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "S117SMAO";
    private const string Org = "S117SO01";
    private const string HrActorId = "s117s_hr";
    private const string VacationType = "VACATION";

    // The S71 anchor set (SettlementReversalEndpointTests / TerminationPayoutRequestEndpointTests):
    // fixed clock 2026-03-05; end date 2026-02-28 ⇒ ferieår 2025, crystallized 12.5 (whole-month).
    private static readonly DateOnly Clock = new(2026, 3, 5);
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int EndDateFerieaar = 2025;
    private static readonly DateOnly GoLive = new(2025, 1, 1);

    // A long-closed ferieår for the ACTIVE YEAR_END settles (the VacationSettlementServiceTests
    // anchor): ferieår 2021 = Sep 2021 .. Aug 2022 — firmly past under the fixed clock.
    private const int ClosedYear = 2021;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    /// <summary>The fixed-clock DORMANT host (no go-live): YEAR_END settles, BARE reversal,
    /// termination-payout-request, payout-pending, reconcile, transfer agreements.</summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _fixedHost = null!;

    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _fixedHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock))));
        _ = _fixedHost.CreateClient(); // boot seeders (org tree + entitlement configs)

        await SeedOrgTreeAsync();
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _fixedHost?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/vacation-settlements/payout-pending (BOTH envelope branches).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The EMPTY-SCOPE branch (the :1069 return site): an HR actor whose ONLY scope is a
    /// stale pre-S93 <c>ORG_AND_DESCENDANTS</c> HR scope — it clears the HROrAbove POLICY (an
    /// HR-role scope exists: ScopeAuthorizationHandler:56) but the S93-hardened accessible-org
    /// union DEFAULT-DENIES the removed scope type (only ORG_ONLY contributes) ⇒ an EMPTY
    /// accessible set — the endpoint returns the SAME envelope record with an empty typed list.
    /// The matcher proves the branch serves the declared <c>{items: [], count: 0}</c> shape (one
    /// wire contract, two return sites).</summary>
    [Fact]
    public async Task PayoutPending_Get200_EmptyScopeBranch_SameEnvelopeRecord()
    {
        using var noScopes = ActorClient(_fixedHost, "s117s_hr_stalescope", StatsTidRoles.LocalHR, Org,
            new RoleScope(StatsTidRoles.LocalHR, Org, "ORG_AND_DESCENDANTS")); // pre-S93 type ⇒ default-denied ⇒ empty union
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, noScopes,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/vacation-settlements/payout-pending"),
            "/api/vacation-settlements/payout-pending", "get");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind);
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
        Assert.Equal(0, root.GetProperty("count").GetInt32());
    }

    /// <summary>The POPULATED branch (the :1113 return site): a real §24 payout row driven through
    /// the REAL YEAR_END settle (balance used=20.75 ⇒ disposable 4.25 ≤ cap 5 ⇒ SETTLED with
    /// payout_days 4.25 — the partition itself produces the bucket, no SQL-faked state). The
    /// matcher proves the 8-field item; payoutDays decimal fidelity (4.25, copied verbatim from
    /// the NUMERIC(6,2) row); the item schema is enum-free by construction.</summary>
    [Fact]
    public async Task PayoutPending_Get200_PopulatedBranch_EightFieldItem_PayoutDaysDecimalFidelity()
    {
        var emp = await SeedEmployeeAsync("pp1");
        await SeedBalanceAsync(emp, ClosedYear, used: 20.75m);
        await SettleYearEndAsync(_fixedHost, emp, ClosedYear);

        using var hr = HrClient(_fixedHost);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, hr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/vacation-settlements/payout-pending"),
            "/api/vacation-settlements/payout-pending", "get");

        var root = JsonDocument.Parse(body).RootElement;
        var items = root.GetProperty("items");
        Assert.Equal(items.GetArrayLength(), root.GetProperty("count").GetInt32()); // count == items.length
        var row = FindByEmployee(items, emp);

        // The 8-field item, field by field (the exact shape-copy of the prior anonymous item).
        Assert.Equal(VacationType, row.GetProperty("entitlementType").GetString());
        Assert.Equal(ClosedYear, row.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, row.GetProperty("sequence").GetInt32());
        Assert.Equal(4.25m, row.GetProperty("payoutDays").GetDecimal()); // decimal fidelity — verbatim NUMERIC(6,2)
        Assert.Equal(1L, row.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.String, row.GetProperty("settledAt").ValueKind);
        Assert.Equal(Org, row.GetProperty("primaryOrgId").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — POST settlement-reversal 200: BOTH successor branches THROUGH the wrapper.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The NULL branch (a BARE reversal): the required <c>successor</c> member is served
    /// as JSON null and is admitted by the WRAPPER-level <c>nullable: true</c> — the matcher walks
    /// the real 200 through the new allOf path without touching the inner schema. Also exercises
    /// <c>reversalKind</c> enum fidelity on the live "BARE" value.</summary>
    [Fact]
    public async Task SettlementReversal_Bare_Post200_SuccessorNull_AdmittedByTheWrapperNullable()
    {
        var emp = await SeedSettledTerminationLeaverAsync(_fixedHost, "bare1");

        using var hr = HrClient(_fixedHost);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, hr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/admin/employees/{emp}/settlement-reversal",
                """{ "entitlementYear": 2025, "expectedSettlementSequence": 1, "mode": "BARE" }""",
                ifMatchVersion: 1),
            "/api/admin/employees/{employeeId}/settlement-reversal", "post");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("BARE", root.GetProperty("reversalKind").GetString());      // in the declared enum set
        Assert.Equal(JsonValueKind.Null, root.GetProperty("successor").ValueKind); // the wrapper's null branch, LIVE
        Assert.Equal(1, root.GetProperty("reversedSequence").GetInt32());
        Assert.Equal(2L, root.GetProperty("reversedVersion").GetInt64());
        Assert.True(root.GetProperty("bareReversalNotDue").GetBoolean());
        Assert.Equal(0, root.GetProperty("voidedRequestIds").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("userVersionAfter").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("userIsActiveAfter").ValueKind);
    }

    /// <summary>The OBJECT branch (a SUPERSEDED reversal) — <b>the sprint's headline assertion</b>:
    /// the matcher recurses THROUGH the new allOf wrapper into the 4-field
    /// <c>SettlementSuccessor</c> on the REAL response, exercising the inner
    /// <c>settlementState</c>/<c>trigger</c> enum fidelity on live values (SETTLED/TERMINATION)
    /// — the mechanism's first-new-consumer live proof. Driven on the go-live host (D13) with the
    /// R4 dual If-Match (settlement version in the header, expectedUserVersion in the body — the
    /// user version is 2 after the REAL end-date PUT).</summary>
    [Fact]
    public async Task SettlementReversal_Supersede_Post200_SuccessorObject_InnerEnumFidelityThroughTheWrapper()
    {
        var app = BootGoLiveHost();
        var emp = await SeedSettledTerminationLeaverAsync(app, "sup1");

        using var hr = HrClient(app);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, hr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/admin/employees/{emp}/settlement-reversal",
                """
                { "entitlementYear": 2025, "expectedSettlementSequence": 1, "mode": "REVERSE_AND_SUPERSEDE",
                  "hasEndDateCorrection": true, "correctedEndDate": "2025-12-31", "expectedUserVersion": 2 }
                """,
                ifMatchVersion: 1),
            "/api/admin/employees/{employeeId}/settlement-reversal", "post");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("SUPERSEDED", root.GetProperty("reversalKind").GetString());

        // The wrapped member, LIVE: the matcher already recursed through the wrapper and enforced
        // the inner required set + both enum sets; these pins document the live values it saw.
        var successor = root.GetProperty("successor");
        Assert.Equal(JsonValueKind.Object, successor.ValueKind);
        Assert.Equal(3, successor.GetProperty("sequence").GetInt32());                    // R1: gen 2 ⇒ 2·2−1
        Assert.Equal("SETTLED", successor.GetProperty("settlementState").GetString());    // in-set, live
        Assert.Equal("TERMINATION", successor.GetProperty("trigger").GetString());        // in-set, live
        Assert.Equal(1L, successor.GetProperty("version").GetInt64());

        Assert.Equal(3L, root.GetProperty("userVersionAfter").GetInt64()); // the corrected user tuple
        Assert.False(root.GetProperty("userIsActiveAfter").GetBoolean());  // still-passed corrected date
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — POST termination-payout-request: TRUE 201 + the 11-field body.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The §26 request against the REAL SETTLED TERMINATION row: the 201 status asserted
    /// EXACTLY (a 200 here is RED) + the 11-field body with <c>crystallizedDays</c> decimal
    /// fidelity (12.5 — the whole-month anchor, snapshot-COPIED) and <c>evidenceNote</c> in BOTH
    /// nullable states (populated on one employee's request, null on a second's).</summary>
    [Fact]
    public async Task TerminationPayoutRequest_Post201Exact_ElevenFieldBody_EvidenceNoteBothStates()
    {
        using var hr = HrClient(_fixedHost);

        // (a) evidenceNote POPULATED — the exact-201 assertion rides the raw response.
        var emp1 = await SeedSettledTerminationLeaverAsync(_fixedHost, "req1");
        using var response = await hr.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/employees/{emp1}/termination-payout-request",
            """{ "entitlementYear": 2025, "expectedSettlementSequence": 1, "requestDate": "2026-03-01", "evidenceNote": "S117 anmodning pr. mail" }""",
            ifMatchVersion: 1));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/admin/employees/{employeeId}/termination-payout-request", "post");
        Assert.Equal(201, truth.StatusCode);         // the committed contract declares exactly 201
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body,
            "POST /api/admin/employees/{employeeId}/termination-payout-request (201)");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.True(root.GetProperty("requestId").GetInt64() > 0);
        Assert.Equal(VacationType, root.GetProperty("entitlementType").GetString());
        Assert.Equal(EndDateFerieaar, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, root.GetProperty("settlementSequence").GetInt32());
        Assert.Equal("OPEN", root.GetProperty("state").GetString());                       // in the declared enum superset
        Assert.Equal("2026-03-01", root.GetProperty("requestDate").GetString());
        Assert.Equal("S117 anmodning pr. mail", root.GetProperty("evidenceNote").GetString());
        Assert.Equal(12.5m, root.GetProperty("crystallizedDays").GetDecimal());            // decimal fidelity — snapshot-copied
        Assert.Equal(EndDate.ToString("yyyy-MM-dd"), root.GetProperty("settlementBoundaryDate").GetString());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());

        // (b) evidenceNote NULL (omitted from the body) — the nullable state, matcher-asserted.
        var emp2 = await SeedSettledTerminationLeaverAsync(_fixedHost, "req2");
        var nullNoteBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, hr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/admin/employees/{emp2}/termination-payout-request",
                """{ "entitlementYear": 2025, "expectedSettlementSequence": 1, "requestDate": "2026-03-01" }""",
                ifMatchVersion: 1),
            "/api/admin/employees/{employeeId}/termination-payout-request", "post");
        var nullNoteRoot = JsonDocument.Parse(nullNoteBody).RootElement;
        Assert.Equal(JsonValueKind.Null, nullNoteRoot.GetProperty("evidenceNote").ValueKind);
        Assert.Equal(12.5m, nullNoteRoot.GetProperty("crystallizedDays").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — POST reconcile-payout: the If-Match flow exactly as a caller composes it.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The caller's real composition: settle a §24 payout row through the REAL machinery,
    /// READ its version off the payout-pending list, then POST reconcile with that version as
    /// If-Match (the op binds NO request DTO — route params + header only, the declared-bodyless
    /// list's 3rd member) → the matcher proves the 7-field 200 receipt; the row then leaves the
    /// pending list (the flow's observable end state).</summary>
    [Fact]
    public async Task ReconcilePayout_Post200_IfMatchFlowAsTheCallerComposesIt_SevenFieldReceipt()
    {
        var emp = await SeedEmployeeAsync("rec1");
        await SeedBalanceAsync(emp, ClosedYear, used: 20m); // disposable 5 ⇒ SETTLED, payout 5.00
        await SettleYearEndAsync(_fixedHost, emp, ClosedYear);

        using var hr = HrClient(_fixedHost);

        // Step 1 — the caller reads the version from the pending list (not a hard-coded token).
        var pending = JsonDocument.Parse(await GetOkAsync(hr, "/api/vacation-settlements/payout-pending")).RootElement;
        var row = FindByEmployee(pending.GetProperty("items"), emp);
        var version = row.GetProperty("version").GetInt64();
        Assert.Equal(1L, version);

        // Step 2 — POST with If-Match: "<version>" (no body — the declared-bodyless op).
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, hr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/vacation-settlements/{emp}/{VacationType}/{ClosedYear}/reconcile-payout",
                jsonBody: null, ifMatchVersion: version),
            "/api/vacation-settlements/{employeeId}/{entitlementType}/{entitlementYear}/reconcile-payout", "post");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(emp, root.GetProperty("employeeId").GetString());
        Assert.Equal(VacationType, root.GetProperty("entitlementType").GetString());
        Assert.Equal(ClosedYear, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, root.GetProperty("sequence").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("payoutReconciledAt").ValueKind);
        Assert.Equal(HrActorId, root.GetProperty("payoutReconciledBy").GetString());
        Assert.Equal(version + 1, root.GetProperty("version").GetInt64());

        // Step 3 — the flow's end state: the reconciled row has left the pending list.
        var after = JsonDocument.Parse(await GetOkAsync(hr, "/api/vacation-settlements/payout-pending")).RootElement;
        foreach (var item in after.GetProperty("items").EnumerateArray())
            Assert.NotEqual(emp, item.GetProperty("employeeId").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ops 6+7 — POST/PUT transfer-agreements: status-per-verb on the ONE shared record.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>POST creates (TRUE 201, asserted exactly) and PUT edits the SAME record (200,
    /// admin-strict If-Match) — ONE shared <c>TransferAgreementResponse</c> behind both verbs
    /// (separate single-status ops, not a multi-2xx set). <c>transferDays</c> decimal fidelity on
    /// both writes (2.5 → 4.25); the guard-forced <c>entitlementType</c> singleton enum
    /// ("VACATION") exercised live on both.</summary>
    [Fact]
    public async Task TransferAgreements_Post201Exact_ThenPut200_OnTheSameSharedRecord()
    {
        var emp = await SeedEmployeeAsync("ta1"); // stays ACTIVE — a §21 agreement precedes any settle
        using var hr = HrClient(_fixedHost);
        var url = $"/api/vacation-transfer-agreements/{emp}";

        // POST — create (no If-*): the exact-201 assertion + the matcher on the shared record.
        // Ferieår 2026 (reset-9 ⇒ deadline 31 Dec 2027 — the §21 guards read the REAL Copenhagen
        // clock); agreementDate in the past, ≤ deadline; transferDays ≤ the cap (5).
        using var created = await hr.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, url,
            """{ "entitlementYear": 2026, "entitlementType": "VACATION", "transferDays": 2.5, "agreementDate": "2026-03-01" }"""));
        var createdBody = await created.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)created.StatusCode); // the EXACT status per verb — a 200 here is RED

        var postTruth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/vacation-transfer-agreements/{employeeId}", "post");
        Assert.Equal(201, postTruth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, postTruth, 201, createdBody,
            "POST /api/vacation-transfer-agreements/{employeeId} (201)");

        var createdRoot = JsonDocument.Parse(createdBody).RootElement;
        Assert.Equal(emp, createdRoot.GetProperty("employeeId").GetString());
        Assert.Equal(2026, createdRoot.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(VacationType, createdRoot.GetProperty("entitlementType").GetString()); // the singleton enum, live
        Assert.Equal(2.5m, createdRoot.GetProperty("transferDays").GetDecimal());           // decimal fidelity
        Assert.Equal("2026-03-01", createdRoot.GetProperty("agreementDate").GetString());
        Assert.Equal(HrActorId, createdRoot.GetProperty("recordedBy").GetString());
        Assert.Equal(1L, createdRoot.GetProperty("version").GetInt64());

        // PUT — edit-in-place on the SAME record (admin-strict If-Match): the 200 sibling status.
        var updatedBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, hr,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, url,
                """{ "entitlementYear": 2026, "entitlementType": "VACATION", "transferDays": 4.25, "agreementDate": "2026-03-01" }""",
                ifMatchVersion: 1),
            "/api/vacation-transfer-agreements/{employeeId}", "put");

        var updatedRoot = JsonDocument.Parse(updatedBody).RootElement;
        Assert.Equal(4.25m, updatedRoot.GetProperty("transferDays").GetDecimal()); // decimal fidelity on the edit
        Assert.Equal(VacationType, updatedRoot.GetProperty("entitlementType").GetString());
        Assert.Equal(2L, updatedRoot.GetProperty("version").GetInt64());           // the SAME record, bumped
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The RED-on-lie proof THROUGH A WRAPPED MEMBER (the Docker-level recursion proof).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The REAL SUPERSEDED 200 is matched against a deliberately-CORRUPTED contract whose
    /// INNER wrapped schema (<c>SettlementSuccessor</c>) claims a required member the wire does not
    /// carry. The matcher MUST throw from INSIDE the wrapper — the failure context carries
    /// <c>.successor</c> and the inner member name, proving the full <c>Match()</c> recursion
    /// (required-fidelity, not a shallow kind check) executes THROUGH the allOf wrapper at the
    /// Docker level (complementing TASK-11700's unit RED). The TRUTH contract passes on the same
    /// response first.</summary>
    [Fact]
    public async Task Gate_IsRed_OnCorruptedInnerRequiredSet_ThrownFromInsideTheWrapper()
    {
        var app = BootGoLiveHost();
        var emp = await SeedSettledTerminationLeaverAsync(app, "red1");

        using var hr = HrClient(app);
        using var response = await hr.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/employees/{emp}/settlement-reversal",
            """
            { "entitlementYear": 2025, "expectedSettlementSequence": 1, "mode": "REVERSE_AND_SUPERSEDE",
              "hasEndDateCorrection": true, "correctedEndDate": "2025-12-31", "expectedUserVersion": 2 }
            """,
            ifMatchVersion: 1));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(JsonValueKind.Object,
            JsonDocument.Parse(body).RootElement.GetProperty("successor").ValueKind); // the wrapped member is LIVE

        const string path = "/api/admin/employees/{employeeId}/settlement-reversal";

        // The truth (the committed contract) passes on the real superseded response…
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, path, "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "truth");

        // …then the lie: the SAME spec with the INNER wrapped schema's required set corrupted —
        // SettlementSuccessor gains a phantom required member. Only a matcher that recurses
        // THROUGH the wrapper into the inner schema can see this lie.
        var lieNode = JsonNode.Parse(_spec.GetRawText())!;
        var successorSchema = lieNode["components"]!["schemas"]!["StatsTid.Backend.Api.Contracts.SettlementSuccessor"]!;
        ((JsonArray)successorSchema["required"]!).Add("s117PhantomInnerMember");
        var lieSpec = JsonDocument.Parse(lieNode.ToJsonString()).RootElement.Clone();

        var lieContract = SpecRuntimeMatcher.ResolveSuccessContract(lieSpec, path, "post");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(lieSpec, lieContract, 200, body, "injected-inner-required-lie"));

        Assert.Contains(".successor", ex.Message, StringComparison.Ordinal);        // thrown INSIDE the wrapper
        Assert.Contains("s117PhantomInnerMember", ex.Message, StringComparison.Ordinal);
        Assert.Contains("REQUIRED", ex.Message, StringComparison.Ordinal);           // the required-fidelity path, not a kind check
    }

    // ─────────────────────────────── hosts / clients ───────────────────────────────

    /// <summary>The fixed-clock host WITH the go-live config (floor 2025-01-01) — the supersession
    /// surface (D13). Booted BEFORE this test's employees are seeded, so its immediate boot poll
    /// sees nothing of this test's state (PollInterval 5min ⇒ no later poll lands inside the
    /// test) — the SettlementReversalEndpointTests choreography.</summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> BootGoLiveHost()
    {
        var derived = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Settlement:GoLiveDate"] = GoLive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }));
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock)));
        });
        _ = derived.CreateClient();
        return derived;
    }

    private HttpClient HrClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
        => ActorClient(app, HrActorId, StatsTidRoles.LocalHR, Org,
            new RoleScope(StatsTidRoles.LocalHR, Org, "ORG_ONLY"));

    /// <summary>A client for an arbitrary actor/role/scope set (the scoped HR, the empty-scopes
    /// actor). Mirrors the Support helper's JWT minting; Support itself consumed AS-IS.</summary>
    private static HttpClient ActorClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app,
        string actorId, string role, string orgId, params RoleScope[] scopes)
    {
        var client = app.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ─────────────────────────────── real-machinery drives ───────────────────────────────

    /// <summary>The full leaver recipe, REAL machinery end to end: seed the employee (input data),
    /// set the end date through the REAL end-date PUT (the lifecycle writer deactivates under the
    /// fixed clock — user version 1 → 2), then drive the TERMINATION settle through
    /// <c>VacationSettlementService.SettleAsync</c> exactly as the close service does ⇒ the
    /// SETTLED sequence-1 version-1 row with snapshot CrystallizedDays 12.5 (the S71 anchor).</summary>
    private async Task<string> SeedSettledTerminationLeaverAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string suffix)
    {
        var emp = await SeedEmployeeAsync(suffix);

        using (var hr = HrClient(app))
        {
            using var put = await hr.SendAsync(SpecRuntimeTestSupport.JsonRequest(
                HttpMethod.Put, $"/api/admin/employees/{emp}/employment-end-date",
                $$"""{ "employmentEndDate": "{{EndDate:yyyy-MM-dd}}" }""",
                ifMatchVersion: 1));
            var putBody = await put.Content.ReadAsStringAsync();
            if ((int)put.StatusCode != 200)
                throw new XunitException($"End-date PUT for {emp} returned {(int)put.StatusCode}: {putBody}");
            var putRoot = JsonDocument.Parse(putBody).RootElement;
            if (putRoot.GetProperty("isActive").GetBoolean())
                throw new XunitException($"End-date PUT for {emp} did not deactivate the leaver (fixed clock {Clock:yyyy-MM-dd}).");
        }

        var outcome = await SettleAsync(app, emp, EndDateFerieaar, "TERMINATION");
        if (!outcome.DidSettle)
            throw new XunitException($"The TERMINATION settle drive for {emp} did not settle.");
        return emp;
    }

    private async Task SettleYearEndAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string employeeId, int year)
    {
        var outcome = await SettleAsync(app, employeeId, year, "YEAR_END");
        if (!outcome.DidSettle || outcome.Row is null
            || !string.Equals(outcome.Row.SettlementState, "SETTLED", StringComparison.Ordinal))
            throw new XunitException(
                $"The YEAR_END settle drive for {employeeId}/{year} must produce a SETTLED row " +
                $"(got DidSettle={outcome.DidSettle}, state={outcome.Row?.SettlementState}).");
    }

    /// <summary>One settlement pass in its OWN ReadCommitted tx, committed — the exact
    /// SettlementCloseService shape (the established VacationSettlementServiceTests /
    /// SettlementReversalEndpointTests drive; the REAL settle machinery, never SQL-faked).</summary>
    private static async Task<SettlementOutcome> SettleAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app,
        string employeeId, int year, string trigger)
    {
        var service = app.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = app.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(employeeId, VacationType, year, trigger, conn, tx);
            await tx.CommitAsync();
            return outcome;
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync();
            throw;
        }
    }

    // ─────────────────────────────── HTTP / JSON support ───────────────────────────────

    private static async Task<string> GetOkAsync(HttpClient client, string url)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, url));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"GET {url} returned {(int)response.StatusCode}: {body}");
        return body;
    }

    private static JsonElement FindByEmployee(JsonElement array, string employeeId)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("employeeId").GetString(), employeeId, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a row for employee {employeeId} in: {array.GetRawText()}");
    }

    // ─────────────────────────────── input-data seeds (NOT settlement states) ───────────────────────────────

    /// <summary>The S117 org tree — DISJOINT from every Settlement suite's STY01/STY05 fixtures.</summary>
    private async Task SeedOrgTreeAsync()
    {
        await ExecAsync(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S117SMAO', 'S117 Settlement Ministerie', 'MAO',          NULL,       '/S117SMAO/',           'AC', 'OK24'),
                ('S117SO01', 'S117 Settlement Styrelse',   'ORGANISATION', 'S117SMAO', '/S117SMAO/S117SO01/', 'AC', 'OK24')
            ON CONFLICT DO NOTHING
            """);
    }

    /// <summary>Employee INPUT rows (users + employee_profiles + user_agreement_codes — the
    /// resolver triple) via the canonical <see cref="RegressionSeed"/>. Ids are s117s_-prefixed
    /// and live in the S117 org only.</summary>
    private async Task<string> SeedEmployeeAsync(string suffix)
    {
        var employeeId = "s117s_" + suffix;
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Consumption INPUT data (the closed year's <c>used</c>) — the operand the legal
    /// partition reads; the settlement STATE itself is always produced by the real settle pass.
    /// Mirrors the VacationSettlementServiceTests balance seed.</summary>
    private async Task SeedBalanceAsync(string employeeId, int year, decimal used)
    {
        await ExecAsync(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year,
                 total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @e, @t, @y, 25, @used, 0, 0, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
                DO UPDATE SET used = EXCLUDED.used, updated_at = NOW()
            """,
            ("e", employeeId), ("t", VacationType), ("y", year), ("used", used));
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
