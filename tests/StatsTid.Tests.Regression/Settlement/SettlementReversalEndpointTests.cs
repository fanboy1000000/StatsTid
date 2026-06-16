using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7102 (ADR-033 D4/D5; SPRINT-71 R4/R12 + owner D-A/D-B) — the reversal ENDPOINT,
/// <c>POST /api/admin/employees/{employeeId}/settlement-reversal</c>, driving the TASK-7104
/// <see cref="SettlementReversalService"/> end-to-end over HTTP:
///
/// <list type="bullet">
///   <item><description><b>Bare happy path</b> on the DORMANT (no go-live) host: REVERSED +
///   the R3 marker (the durable poll-suppression service effect), request VOIDed, the OPERATOR
///   actor from the JWT on the event — and the D13 proof that BARE stays allowed
///   dormant;</description></item>
///   <item><description><b>Unconfigured-go-live supersession refusal:</b> REVERSE_AND_SUPERSEDE
///   on the dormant host → 409 BEFORE any DB mutation (D13: nothing settles
///   pre-go-live);</description></item>
///   <item><description><b>Supersede happy path</b> on the go-live host with the DUAL If-Match
///   (settlement version in the header + <c>expectedUserVersion</c> in the body): corrected end
///   date applied via the shared lifecycle writer, successor at the R1 sequence 3, SUPERSEDED
///   kind;</description></item>
///   <item><description><b>Self-target 403</b> for ANY end-date-correcting mode (active AND
///   terminated actor variants — the S70 W1 hole stays closed through this second
///   writer);</description></item>
///   <item><description><b>The 7104 contract-delta 409s end-to-end:</b> SequenceMismatch (the
///   ABA — equal versions across generations, refused on the sequence with the actual sequence
///   in the body) and AffectedSpanConflict (the B2/R13 in-tx span guard, blockers NAMED);
///   plus the D-A zero-bucket and R4 reconciled-row 409s;</description></item>
///   <item><description><b>412/428/422 matrix:</b> stale settlement If-Match → 412
///   (CasConflict), stale expectedUserVersion → 412 (UserVersionConflict, full rollback),
///   missing If-Match / missing expectedUserVersion-with-correction → 428, body-shape 422s,
///   no-active-row 404; non-HR / out-of-subtree 403 denial rows.</description></item>
/// </list>
///
/// <para>Fixed clock 2026-03-05; end date 2026-02-28 ⇒ ferieår 2025, crystallized 12.5; go-live
/// host floor 2025-01-01 (the SettlementReversalTests NarrowGoLive anchor set).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementReversalEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string DisjointOrg = "STY05";
    private const string CoveringOrg = "MIN01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";
    private const string HrActorId = "hr_s71_rev_ep";

    private static readonly DateOnly Clock = new(2026, 3, 5);
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int EndDateFerieaar = 2025;
    private static readonly DateOnly GoLive = new(2025, 1, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    /// <summary>The DORMANT fixed-clock host (no go-live): bare-mode + guard-matrix surface.</summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _dormant = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dormant = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock))));
        _ = _dormant.CreateClient(); // boot seeders
    }

    public async Task DisposeAsync()
    {
        _dormant?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // BARE happy path on the DORMANT host (the D13 bare-still-allowed proof): REVERSED +
    // the R3 marker (poll suppression via the durable service effect), request VOIDed,
    // the OPERATOR actor from the JWT on the SettlementReversed event.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Bare_HappyPath_OnDormantHost_200_Marker_RequestVoided_OperatorActor()
    {
        var employeeId = await SeedSettledTerminationAsync(_dormant);
        await SeedOpenRequestAsync(employeeId, EndDateFerieaar, 1);

        var rsp = await PostReversalAsync(HrClient(_dormant, CoveringOrg), employeeId,
            new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "BARE" },
            ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BARE", body.GetProperty("reversalKind").GetString());
        Assert.Equal(1, body.GetProperty("reversedSequence").GetInt32());
        Assert.Equal(2L, body.GetProperty("reversedVersion").GetInt64());
        Assert.True(body.GetProperty("bareReversalNotDue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("successor").ValueKind);
        Assert.Equal(1, body.GetProperty("voidedRequestIds").GetArrayLength());

        // The durable service effects: REVERSED + the R3 marker (the poll-suppression fact the
        // 7104 suite pins against BOTH enumeration branches) — state-only, snapshot preserved.
        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("REVERSED", row.State);
        Assert.True(row.BareMarker);
        Assert.Equal(2L, row.Version);

        // R6/D-E — the bound OPEN request was VOIDed in the same tx.
        Assert.Equal(1L, await CountAsync(
            "termination_payout_requests",
            "employee_id = @e AND state = 'VOIDED_BY_REVERSAL'", ("e", employeeId)));

        // R10 — the OPERATOR (JWT actor) on the event, never a system actor.
        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "SettlementReversed");
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        Assert.Equal("BARE", doc.RootElement.GetProperty("reversalKind").GetString());
        Assert.Equal(HrActorId, doc.RootElement.GetProperty("actorId").GetString());
        Assert.Equal(12.5m, doc.RootElement.GetProperty("crystallizedDays").GetDecimal());

        // S71 Step-7a c2 pin — the ADR-026 audit row attributes the OPERATOR's org (the covering
        // MIN01 actor minted above), NOT the target employee's (STY01); the target org stays the
        // employee's. The shared EmitAsync would conflate the two for this operator-driven event
        // without the explicit actorOrgId (the close-service system-actor sites keep employee-org
        // for both — the S68 convention).
        Assert.Equal(1L, await CountAsync(
            "audit_projection",
            "event_type = 'SettlementReversed' AND target_resource_id = @r " +
            "AND actor_primary_org_id = @actorOrg AND target_org_id = @targetOrg",
            ("r", employeeId), ("actorOrg", CoveringOrg), ("targetOrg", OrgId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // The D13 go-live gate: a supersession on the DORMANT host refuses 409 BEFORE any DB
    // mutation; the SAME tuple then bare-reverses fine (bare-still-allowed, same test).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Supersede_DormantGoLive_409_NothingMutated_ThenBareStillAllowed()
    {
        var employeeId = await SeedSettledTerminationAsync(_dormant);
        var client = HrClient(_dormant, CoveringOrg);

        var refused = await PostReversalAsync(client, employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = new DateOnly(2025, 12, 31),
            expectedUserVersion = 1,
        }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var refusedBody = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SupersedeGoLiveDormant", refusedBody.GetProperty("failure").GetString());
        Assert.Contains("D13", refusedBody.GetProperty("error").GetString());

        // NOTHING mutated — the refusal fired at the endpoint, before any DB work.
        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("SETTLED", row.State);
        Assert.Equal(1L, row.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));

        // BARE stays allowed on the dormant host (D13: a bare reversal settles nothing).
        var bare = await PostReversalAsync(client, employeeId,
            new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "BARE" },
            ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, bare.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Supersede happy path (go-live host) — the R4 dual If-Match shape: settlement version in
    // the HEADER + expectedUserVersion in the BODY; corrected end date applied via the shared
    // lifecycle writer; successor at the R1 next-generation sequence 3; SUPERSEDED kind.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Supersede_HappyPath_DualIfMatch_SuccessorAtSequenceThree()
    {
        var app = BootGoLiveHost();
        var employeeId = await SeedSettledTerminationAsync(app);
        var correctedEndDate = new DateOnly(2025, 12, 31); // same ferieår 2025, still passed

        var rsp = await PostReversalAsync(HrClient(app, CoveringOrg), employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate,
            expectedUserVersion = 1,
        }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SUPERSEDED", body.GetProperty("reversalKind").GetString());
        Assert.Equal(1, body.GetProperty("reversedSequence").GetInt32());
        Assert.False(body.GetProperty("bareReversalNotDue").GetBoolean());
        var successor = body.GetProperty("successor");
        Assert.Equal(3, successor.GetProperty("sequence").GetInt32());      // R1: gen 2 ⇒ 2·2−1
        Assert.Equal("SETTLED", successor.GetProperty("settlementState").GetString());
        Assert.Equal(Termination, successor.GetProperty("trigger").GetString());
        Assert.Equal(2L, body.GetProperty("userVersionAfter").GetInt64());
        Assert.False(body.GetProperty("userIsActiveAfter").GetBoolean());   // still-passed date

        // The corrected user tuple (the SHARED lifecycle writer's versioned write).
        var user = await ReadUserTupleAsync(employeeId);
        Assert.Equal(correctedEndDate, user.EndDate);
        Assert.False(user.IsActive);
        Assert.Equal(2L, user.Version);

        // Old row REVERSED (no marker — a successor exists); the SUPERSEDED event names it.
        Assert.Equal("REVERSED", (await ReadRowAsync(employeeId, EndDateFerieaar, 1)).State);
        Assert.False((await ReadRowAsync(employeeId, EndDateFerieaar, 1)).BareMarker);
        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "SettlementReversed");
        using var doc = JsonDocument.Parse(payload!);
        Assert.Equal("SUPERSEDED", doc.RootElement.GetProperty("reversalKind").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("successorSequence").GetInt32());
        // The lifecycle write rode the SAME tx and carries the OPERATOR actor (R4/R10).
        var endDateEvent = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "EmployeeEmploymentEndDateSet");
        Assert.NotNull(endDateEvent);
        using var lifecycleDoc = JsonDocument.Parse(endDateEvent!);
        Assert.Equal(HrActorId, lifecycleDoc.RootElement.GetProperty("actorId").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 self-target exclusion — ANY end-date-correcting mode 403s actor == employee,
    // BEFORE any DB work; active AND terminated actor variants (the S70 W1 choreography).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SelfTarget_EndDateCorrectingMode_ActiveActor_403_NothingMutated()
    {
        var app = BootGoLiveHost();
        var hrActor = await SeedSettledTerminationAsync(app);
        // Reactivate so the actor variant is ACTIVE (the rule is actor-state-independent).
        await ExecAsync("UPDATE users SET is_active = TRUE WHERE user_id = @id", ("id", hrActor));
        var selfClient = ClientWith(app, HrToken(hrActor, CoveringOrg));

        var rsp = await PostReversalAsync(selfClient, hrActor, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = (DateOnly?)null, // clear — the self-reinstatement shape
            expectedUserVersion = 1,
        }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("second administrator", body.GetProperty("reason").GetString());
        Assert.Equal("SETTLED", (await ReadRowAsync(hrActor, EndDateFerieaar, 1)).State);
        Assert.Equal(0L, await CountOutboxByTypeAsync(hrActor, "SettlementReversed"));
    }

    /// <summary>The W1 attack choreography through the SECOND writer: a lifecycle-deactivated
    /// HR actor's still-valid JWT reverse+supersedes its OWN tuple with correction=CLEAR —
    /// pre-guard this would reactivate via R1(c). Must 403; the user stays deactivated.</summary>
    [Fact]
    public async Task SelfTarget_EndDateCorrectingMode_TerminatedActor_CannotSelfReinstate_403()
    {
        var app = BootGoLiveHost();
        var hrActor = await SeedSettledTerminationAsync(app); // deactivated leaver
        var selfClient = ClientWith(app, HrToken(hrActor, CoveringOrg));

        var rsp = await PostReversalAsync(selfClient, hrActor, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = (DateOnly?)null,
            expectedUserVersion = 1,
        }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var user = await ReadUserTupleAsync(hrActor);
        Assert.False(user.IsActive);          // NOT reinstated
        Assert.Equal(EndDate, user.EndDate);  // end date intact
        Assert.Equal(1L, user.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // The 7104 contract-delta 409s, end-to-end over HTTP.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>SequenceMismatch (B1/R2 — the ABA): after a supersession the gen-2 row is
    /// (sequence 3, version 1) — a stale command's version MATCHES; only the sequence
    /// discriminates → 409 with the actual sequence.</summary>
    [Fact]
    public async Task SequenceMismatch_AfterSupersession_409_CarriesActualSequence()
    {
        var app = BootGoLiveHost();
        var employeeId = await SeedSettledTerminationAsync(app);
        var client = HrClient(app, CoveringOrg);
        var supersede = await PostReversalAsync(client, employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = new DateOnly(2025, 12, 31),
            expectedUserVersion = 1,
        }, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, supersede.StatusCode);

        // The stale operator: built against gen-1 (sequence 1, version 1) — the gen-2 active
        // row is (sequence 3, version 1), so If-Match "1" alone would ABA-match.
        var stale = await PostReversalAsync(client, employeeId,
            new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "BARE" },
            ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SequenceMismatch", body.GetProperty("failure").GetString());
        Assert.Equal(3, body.GetProperty("actualSettlementSequence").GetInt32());
        Assert.Equal("SETTLED", (await ReadRowAsync(employeeId, EndDateFerieaar, 3)).State); // untouched
    }

    /// <summary>AffectedSpanConflict (B2/the R13 analog): reversing the 2024 deferred row with
    /// a correction whose span covers 2025 — where an ACTIVE TERMINATION row stands — 409s with
    /// the blocker NAMED; FULL rollback.</summary>
    [Fact]
    public async Task AffectedSpanConflict_409_BlockerNamed_FullRollback()
    {
        var app = BootGoLiveHost();
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SettleInOwnTxAsync(app, employeeId, 2024, YearEnd, GoLive);       // deferred PENDING_REVIEW
        await SettleInOwnTxAsync(app, employeeId, EndDateFerieaar, Termination); // the 2025 blocker

        var rsp = await PostReversalAsync(HrClient(app, CoveringOrg), employeeId, new
        {
            entitlementYear = 2024,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = new DateOnly(2025, 6, 30), // ferieår 2024; old 2026-02-28 ⇒ span [2024..2025]
            expectedUserVersion = 1,
        }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AffectedSpanConflict", body.GetProperty("failure").GetString());
        Assert.Contains("VACATION/2025 sequence 1 (SETTLED)", body.GetProperty("error").GetString());

        // FULL rollback — target still active, user untouched.
        Assert.Equal("PENDING_REVIEW", (await ReadRowAsync(employeeId, 2024, 1)).State);
        Assert.Equal(1L, (await ReadUserTupleAsync(employeeId)).Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
    }

    /// <summary>D-A zero-bucket (carryover-writing row) + the R4 reconciled-row exclusion —
    /// both 409 with the machine-readable discriminator, rows untouched (dormant host, bare).</summary>
    [Fact]
    public async Task ZeroBucketAndReconciled_Refusals_409()
    {
        var client = HrClient(_dormant, CoveringOrg);

        // D-A — the §21 carryover writer refuses.
        var carryEmp = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(carryEmp, 2024, transferDays: 3m);
        await SettleInOwnTxAsync(_dormant, carryEmp, 2024, YearEnd);
        var carry = await PostReversalAsync(client, carryEmp,
            new { entitlementYear = 2024, expectedSettlementSequence = 1, mode = "BARE" }, "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, carry.StatusCode);
        Assert.Equal("CarryoverWritingRow",
            (await carry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("failure").GetString());
        Assert.NotEqual("REVERSED", (await ReadRowAsync(carryEmp, 2024, 1)).State);

        // R4 — the operator-reconciled §24 disposition refuses.
        var reconEmp = await SeedEmployeeAsync();
        await SettleInOwnTxAsync(_dormant, reconEmp, 2024, YearEnd); // transfer 0 ⇒ D-A passes
        await ExecAsync(
            """
            UPDATE vacation_settlements
               SET payout_reconciled_at = NOW(), payout_reconciled_by = 'hr_s69_reconciler'
             WHERE employee_id = @e AND entitlement_year = 2024 AND sequence = 1
            """, ("e", reconEmp));
        var recon = await PostReversalAsync(client, reconEmp,
            new { entitlementYear = 2024, expectedSettlementSequence = 1, mode = "BARE" }, "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, recon.StatusCode);
        Assert.Equal("ReconciledRow",
            (await recon.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("failure").GetString());
        Assert.NotEqual("REVERSED", (await ReadRowAsync(reconEmp, 2024, 1)).State);
    }

    /// <summary>S79 R7 — a §22 FERIEHINDRING carryover-writing row (feriehindring_transfer_days > 0)
    /// is ALSO refused by the D-A zero-bucket guard (irreversible until the compensating-reversal
    /// follow-up), exactly like the §21 transfer_days > 0 row. Row untouched.</summary>
    [Fact]
    public async Task FeriehindringCarryoverWritingRow_Refused_409()
    {
        var client = HrClient(_dormant, CoveringOrg);
        var feriEmp = await SeedEmployeeAsync();
        // Seed a complete SETTLED §22-resolved row directly (feriehindring_transfer_days > 0 — the
        // carryover-writing bucket the R7 guard reads). A direct INSERT of the final valid shape, so
        // the test does not depend on the automated close producing a particular pre-state.
        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state,
                 trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, feriehindring_transfer_days, feriehindring_reason, version)
            VALUES
                (@e, 'VACATION', 2024, 1, 'SETTLED', 'YEAR_END',
                 '{"annualQuota":25,"carryoverMax":5,"resetMonth":9,"okVersion":"OK24"}'::jsonb,
                 0, 0, 0, 'FERIEHINDRING', 10, 'sygdom hele ferieafholdelsesperioden', 1)
            """, ("e", feriEmp));

        var feri = await PostReversalAsync(client, feriEmp,
            new { entitlementYear = 2024, expectedSettlementSequence = 1, mode = "BARE" }, "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, feri.StatusCode);
        Assert.Equal("CarryoverWritingRow",
            (await feri.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("failure").GetString());
        Assert.NotEqual("REVERSED", (await ReadRowAsync(feriEmp, 2024, 1)).State);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 412 / 428 / 422 / 404 matrix.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PreconditionMatrix_412_428_422_404()
    {
        var employeeId = await SeedSettledTerminationAsync(_dormant);
        var client = HrClient(_dormant, CoveringOrg);

        // 412 — stale settlement If-Match (CasConflict) with the actual version.
        var stale = await PostReversalAsync(client, employeeId,
            new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "BARE" }, "\"99\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CasConflict", staleBody.GetProperty("failure").GetString());
        Assert.Equal(1L, staleBody.GetProperty("actualVersion").GetInt64());

        // 428 — missing If-Match (admin-strict).
        var missing = await PostReversalAsync(client, employeeId,
            new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "BARE" }, ifMatch: null);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        // 428 — an end-date-correcting mode without expectedUserVersion (the R4 two-aggregate
        // precondition's second half is REQUIRED).
        var noUserVersion = await PostReversalAsync(client, employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = new DateOnly(2025, 12, 31),
        }, "\"1\"");
        Assert.Equal((HttpStatusCode)428, noUserVersion.StatusCode);

        // 422 — unknown mode / bare+correction / correctedEndDate without the flag / missing sequence.
        var badMode = await PostReversalAsync(client, employeeId,
            new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "REVERSE" }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badMode.StatusCode);
        var bareCorrection = await PostReversalAsync(client, employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "BARE",
            hasEndDateCorrection = true,
            expectedUserVersion = 1,
        }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bareCorrection.StatusCode);
        var flagless = await PostReversalAsync(client, employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            correctedEndDate = new DateOnly(2025, 12, 31),
            expectedUserVersion = 1,
        }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, flagless.StatusCode);
        var noSeq = await PostReversalAsync(client, employeeId,
            new { entitlementYear = EndDateFerieaar, mode = "BARE" }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noSeq.StatusCode);

        // 404 — no active row for the tuple.
        var noRow = await PostReversalAsync(client, employeeId,
            new { entitlementYear = 2020, expectedSettlementSequence = 1, mode = "BARE" }, "\"1\"");
        Assert.Equal(HttpStatusCode.NotFound, noRow.StatusCode);

        // Nothing mutated by the whole refused barrage.
        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("SETTLED", row.State);
        Assert.Equal(1L, row.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
    }

    /// <summary>412 — stale expectedUserVersion (UserVersionConflict, the two-aggregate second
    /// half): the settlement CAS had already applied in-tx, so the FULL rollback is the proof.</summary>
    [Fact]
    public async Task StaleUserVersion_412_FullRollback()
    {
        var app = BootGoLiveHost();
        var employeeId = await SeedSettledTerminationAsync(app);

        var rsp = await PostReversalAsync(HrClient(app, CoveringOrg), employeeId, new
        {
            entitlementYear = EndDateFerieaar,
            expectedSettlementSequence = 1,
            mode = "REVERSE_AND_SUPERSEDE",
            hasEndDateCorrection = true,
            correctedEndDate = new DateOnly(2025, 12, 31),
            expectedUserVersion = 99,
        }, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UserVersionConflict", body.GetProperty("failure").GetString());
        Assert.Equal(1L, body.GetProperty("actualUserVersion").GetInt64());

        // FULL rollback — the settlement CAS (which ran before the user precondition) rolled back.
        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("SETTLED", row.State);
        Assert.Equal(1L, row.Version);
        Assert.Equal(1L, (await ReadUserTupleAsync(employeeId)).Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Denial rows — non-HR 403, out-of-subtree HR 403 (the R9e family on this 3b surface).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DenialMatrix_NonHr_And_OutOfSubtree_403()
    {
        var employeeId = await SeedSettledTerminationAsync(_dormant);
        var bareBody = new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1, mode = "BARE" };

        var emp = await PostReversalAsync(ClientWith(_dormant, EmployeeToken("emp_s71_rev", OrgId)),
            employeeId, bareBody, "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, emp.StatusCode);

        var leader = await PostReversalAsync(ClientWith(_dormant, LeaderToken("ldr_s71_rev", OrgId)),
            employeeId, bareBody, "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, leader.StatusCode);

        var disjoint = await PostReversalAsync(HrClient(_dormant, DisjointOrg), employeeId, bareBody, "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, disjoint.StatusCode);

        Assert.Equal("SETTLED", (await ReadRowAsync(employeeId, EndDateFerieaar, 1)).State);
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static string ReversalUrl(string employeeId) =>
        $"/api/admin/employees/{employeeId}/settlement-reversal";

    private static async Task<HttpResponseMessage> PostReversalAsync(
        HttpClient client, string employeeId, object body, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, ReversalUrl(employeeId))
        {
            Content = JsonContent.Create(body),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ─────────────────────────────── hosts / clients / tokens ───────────────────────────────

    /// <summary>A fixed-clock host WITH the go-live config (floor 2025-01-01) — the
    /// supersession surface. Booted BEFORE seeding the test's employees, so its immediate boot
    /// poll sees nothing of this test's state (PollInterval 5min ⇒ no later poll lands inside
    /// the test); year-scoped assertions tolerate any leaver-deferred rows a poll might add for
    /// PRIOR tests' employees.</summary>
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

    private HttpClient ClientWith(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string bearer)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient HrClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string scopeOrgId)
        => ClientWith(app, HrToken(HrActorId, scopeOrgId));

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

    // ─────────────────────────────── seeding / drives ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_revep_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task<string> SeedSettledTerminationAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        var outcome = await SettleInOwnTxAsync(app, employeeId, EndDateFerieaar, Termination);
        Assert.True(outcome.DidSettle, "the TERMINATION settle drive must produce the settled row");
        return employeeId;
    }

    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId), ("endDate", endDate));
    }

    private async Task SeedOpenRequestAsync(string employeeId, int year, int settlementSequence)
    {
        await ExecAsync(
            """
            INSERT INTO termination_payout_requests
                (employee_id, entitlement_type, entitlement_year, settlement_sequence,
                 state, request_date, recorded_by, version)
            VALUES (@e, @t, @y, @s, 'OPEN', @d, 'test_s71_hr', 1)
            """,
            ("e", employeeId), ("t", VacationType), ("y", year), ("s", settlementSequence),
            ("d", new DateOnly(2026, 3, 1)));
    }

    private async Task SeedTransferAgreementAsync(string employeeId, int year, decimal transferDays)
    {
        await ExecAsync(
            """
            INSERT INTO vacation_transfer_agreements
                (employee_id, entitlement_year, entitlement_type, transfer_days, agreement_date, recorded_by, version)
            VALUES (@e, @y, @t, @days, @date, 'test_s71_hr', 1)
            """,
            ("e", employeeId), ("y", year), ("t", VacationType), ("days", transferDays),
            ("date", new DateOnly(year + 1, 6, 30)));
    }

    private async Task<SettlementOutcome> SettleInOwnTxAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app,
        string employeeId, int year, string trigger, DateOnly? goLiveFloor = null)
    {
        var service = app.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = app.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(
                employeeId, VacationType, year, trigger, conn, tx, leaverGoLiveFloor: goLiveFloor);
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

    // ─────────────────────────────── reads ───────────────────────────────

    private sealed record RowState(string State, bool BareMarker, long Version);

    private async Task<RowState> ReadRowAsync(string employeeId, int year, int sequence)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, bare_reversal_not_due, version
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND sequence = @s
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("s", sequence);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(),
            $"settlement row expected for {employeeId}/{year}/seq {sequence}");
        return new RowState(reader.GetString(0), reader.GetBoolean(1), reader.GetInt64(2));
    }

    private async Task<(DateOnly? EndDate, bool IsActive, long Version)> ReadUserTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT employment_end_date, is_active, version FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"user row expected for {employeeId}");
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetInt64(2));
    }

    private async Task<string?> ReadLatestOutboxPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId AND event_type = @eventType
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        return (string?)await cmd.ExecuteScalarAsync();
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

    private async Task<long> CountAsync(string table, string whereClause, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table} WHERE {whereClause}", conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
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
