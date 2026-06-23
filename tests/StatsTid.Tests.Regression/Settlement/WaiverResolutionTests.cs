using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7103 (ADR-033 slice 3b; SPRINT-71 R5/R12 + slice Step-0 gate (i)) — Docker-gated
/// tests for the WAIVED resolution verb on the manual-resolve surface plus the R5 settled-year
/// reader changes:
///
/// <list type="bullet">
///   <item><description><b>Happy path (R5):</b> a negative-pre-clamp TERMINATION PENDING_REVIEW
///   row (the S70 shape — the §7-shaped claim is the forfeit-FLAG on <c>forfeit_days</c>) waives
///   in ONE atomic tx: PENDING_REVIEW → SETTLED, <c>review_disposition=WAIVED</c>,
///   <c>claim_disposition_days</c> = the flagged quantity (from the ROW), <c>forfeit_days → 0</c>
///   (the claim never reads as §34 forfeiture), <c>TerminationClaimWaived</c> emitted (outbox) +
///   the ADR-026 audit row + the settlement-table audit row.</description></item>
///   <item><description><b>Guard matrix:</b> WAIVED on a YEAR_END row → 422; on a SETTLED row →
///   409 (the existing not-PENDING_REVIEW convention); on a flagless TERMINATION PENDING_REVIEW
///   row → 422; with a supplied body quantity → 422; MODREGNING → the dedicated parked-pending-
///   SLS-dialogue 422 (gate (i)); FORFEIT/DEFER on a TERMINATION row → still 422 (re-pin);
///   stale If-Match → 412.</description></item>
///   <item><description><b>Invariant re-pin (R5):</b> NO resolve disposition writes
///   <c>carryover_in</c> — now including WAIVED; no export line, no §21/§24 events.</description></item>
///   <item><description><b>Readers (R5):</b> a WAIVED row renders its claim disposition DISTINCTLY
///   on both the <c>/summary</c> settled-year reader and the year-overview closed-ferieår reader
///   (<c>reviewDisposition</c> + <c>claimDispositionDays</c>, remaining/expiring 0, forfeitDays 0)
///   — never as forfeiture and never as a positive pending remainder; PENDING_REVIEW and clean
///   SETTLED rows keep their S68/S70 rendering with the two new fields null.</description></item>
///   <item><description><b>R12 race:</b> the waiver PARKS on the employee advisory lock (lock
///   FIRST), and the in-lock re-read makes the CAS loser a clean 409 after a competing waiver
///   committed mid-window (the parked-lock choreography precedent).</description></item>
/// </list>
///
/// <para>Harness/JWT/seeding conventions mirror <see cref="TerminationSettlementTests"/> +
/// <see cref="SettledYearReaderTests"/> (Docker harness, direct seeding, employee tokens for the
/// reader surfaces, PAT-008 FixedTimeProvider for the year-overview). Reader tests use ACTIVE
/// employees (the /summary body read is is_active-filtered and NOT R9c-allowlisted — a terminated
/// target 404s there by design); the waiver happy path uses a genuinely deactivated leaver (the
/// resolve endpoint IS allowlisted, SPRINT-70 R9c).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WaiverResolutionTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";

    /// <summary>End date 2026-02-28 ⇒ R6 ferieår 2025 (the TerminationSettlementTests anchor);
    /// /summary?year=2026&amp;month=3 maps to the same entitlement_year 2025 (month &lt; 9).</summary>
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int Ferieaar = 2025;

    /// <summary>The S70 negative-pre-clamp flag quantity used throughout (|round2(2.0833 − 5)|).</summary>
    private const decimal ClaimDays = 2.92m;

    // Fixed today for the year-overview determinism (mirrors SettledYearReaderTests).
    private static readonly DateOnly FixedToday = new(2026, 6, 15);

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

    // ════════════════════════════════════════════════════════════════════════
    // R5 — the marquee: waive-in-full on a deactivated leaver's negative-pre-clamp TERMINATION
    // PENDING_REVIEW row. Row transition + flag-clear + event + both audit rows, one tx.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Waive_NegativePreClampTerminationRow_HappyPath_RowEventAudit()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // the resolve endpoint is R9c-allowlisted
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag);

        // Response body — the resolved row.
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("resolved").GetBoolean());
        Assert.Equal("SETTLED", body.GetProperty("settlementState").GetString());
        Assert.Equal("WAIVED", body.GetProperty("reviewDisposition").GetString());
        Assert.Equal(ClaimDays, body.GetProperty("claimDispositionDays").GetDecimal());
        Assert.Equal(0m, body.GetProperty("forfeitDays").GetDecimal()); // the cleared transient flag
        Assert.Equal(1, body.GetProperty("sequence").GetInt32());
        Assert.Equal(2L, body.GetProperty("version").GetInt64());

        // Durable row state — quantity moved to claim_disposition_days, flag CLEARED.
        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal("WAIVED", row.Value.ReviewDisposition);
        Assert.Equal(ClaimDays, row.Value.ClaimDispositionDays);
        Assert.Equal(0m, row.Value.Forfeit);
        Assert.Equal(2L, row.Value.Version);

        // TerminationClaimWaived emitted exactly once, with the 7101 payload shape (the quantity
        // from the ROW + the settlement-row sequence, R2).
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "TerminationClaimWaived"));
        using var payload = JsonDocument.Parse(
            (await ReadOutboxPayloadAsync(employeeId, "TerminationClaimWaived"))!);
        Assert.Equal(ClaimDays, payload.RootElement.GetProperty("waivedDays").GetDecimal());
        Assert.Equal(1, payload.RootElement.GetProperty("settlementSequence").GetInt32());
        Assert.Equal(VacationType, payload.RootElement.GetProperty("entitlementType").GetString());

        // ADR-026 audit_projection row + the settlement-table UPDATED audit row, same tx.
        Assert.Equal(1L, await CountAsync(
            "audit_projection", "event_type = 'TerminationClaimWaived' AND target_resource_id = @r",
            ("r", employeeId)));
        Assert.True(await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND action = 'UPDATED'", ("e", employeeId)) >= 1);

        // NEVER as §34 forfeiture: no forfeit event of any kind.
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 invariant re-pin — NO resolve disposition writes carryover_in (now including WAIVED);
    // and the waiver stages NOTHING toward Payroll (no line, no §21/§24 events — R9).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Waive_WritesNoCarryover_NoExportLine_NoPayrollEvents()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The restated 3a invariant, re-pinned for the 3b verb: carryover untouched (no balance
        // row materializes for the next ferieår), no export line, no §21/§24 events.
        Assert.Null(await ReadCarryoverInAsync(employeeId, Ferieaar + 1));
        Assert.Equal(0L, await CountAsync("settlement_export_lines", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationAutoPaidOut"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // S71 Step-7a W1 pin — audit actor-org provenance: the ADR-026 row records the OPERATOR's
    // org in actor_primary_org_id and the EMPLOYEE's resolved org in target_org_id (the
    // request-endpoint / lifecycle-writer convention). The same-org facts above cannot
    // discriminate the two columns — this fact uses an HR actor whose PRIMARY org differs from
    // the target leaver's org. S93 flat role-scope: the actor holds an exact ORG_ONLY scope on the
    // leaver's (child) Organisation while its token's primary org is a DIFFERENT org — so the audit
    // records the primary org, not the scope/target org.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Waive_ParentOrgHrActor_AuditRowCarriesActorOrgNotTargetOrg()
    {
        const string actorOrg = "STY71WP";   // the actor's PRIMARY org (recorded in actor_primary_org_id)
        const string childOrg = "STY71WC";   // the leaver's org (the access-granting scope + target_org_id)
        await SeedOrgPairAsync(actorOrg, childOrg);

        var employeeId = "emp_s71_waiver_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, childOrg, "AC", "OK24");
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        // Actor: primary org = actorOrg, access-granting scope = the child org the leaver sits on.
        var rsp = await ResolveAsync(HrClientFor(actorOrg, childOrg), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(1L, await CountAsync(
            "audit_projection",
            "event_type = 'TerminationClaimWaived' AND target_resource_id = @r " +
            "AND actor_primary_org_id = @actorOrg AND target_org_id = @targetOrg",
            ("r", employeeId), ("actorOrg", actorOrg), ("targetOrg", childOrg)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Guard matrix — trigger / state / flag / body-quantity / parked-§7 / FORFEIT-DEFER re-pin /
    // stale CAS.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Waive_YearEndRow_422_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        // A leaver-deferred-shaped YEAR_END PENDING_REVIEW row (full-disposable flag) — NOT waivable.
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW", forfeitDays: 25m);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(25m, row.Value.Forfeit);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationClaimWaived"));
    }

    [Fact]
    public async Task Waive_SettledTerminationRow_409()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        // A clean SETTLED TERMINATION row (zero buckets, crystallized in the snapshot) — already complete.
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "SETTLED", forfeitDays: 0m);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal(1L, row!.Value.Version); // untouched
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationClaimWaived"));
    }

    /// <summary>Defense-in-depth: a flagless TERMINATION PENDING_REVIEW row should not exist (R5
    /// pins PENDING_REVIEW iff negative pre-clamp) — fail closed, never invent a zero-quantity
    /// waiver record.</summary>
    [Fact]
    public async Task Waive_FlaglessTerminationPendingRow_422()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: 0m);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
    }

    /// <summary>The waived quantity comes from the ROW, never the caller — any supplied body
    /// quantity is rejected outright (even the exactly-matching one; fail-closed, no override
    /// channel exists for WAIVED).</summary>
    [Fact]
    public async Task Waive_SuppliedForfeitDays_422_EvenWhenExact()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"",
            forfeitDays: ClaimDays);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
    }

    /// <summary>Gate (i) — the §7 MODREGNING verb is PARKED behind the SLS-dialogue task: a
    /// §7-shaped attempt gets the dedicated 422 naming the parking, even on the exactly-right
    /// row shape, and touches nothing.</summary>
    [Fact]
    public async Task Modregning_422_ParkedPendingSlsDialogue_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "MODREGNING", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadAsStringAsync();
        Assert.Contains("PARKED", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SLS", body, StringComparison.Ordinal);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(ClaimDays, row.Value.Forfeit);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationClaimWaived"));
    }

    /// <summary>Re-pin (SPRINT-71 R5): FORFEIT and DEFER keep their TERMINATION 422 — the 3b
    /// waiver verb did not reopen the materially-false-§34 path.</summary>
    [Fact]
    public async Task ForfeitAndDefer_OnTerminationRow_Still422()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var forfeit = await ResolveAsync(HrClient(), employeeId, Ferieaar, "FORFEIT", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, forfeit.StatusCode);

        var defer = await ResolveAsync(HrClient(), employeeId, Ferieaar, "DEFER", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, defer.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
    }

    [Fact]
    public async Task Waive_StaleIfMatch_412_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var rsp = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"7\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, body.GetProperty("actualVersion").GetInt64());

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(ClaimDays, row.Value.Forfeit);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationClaimWaived"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 readers — /summary: a WAIVED row renders the claim disposition DISTINCTLY (end-to-end
    // through the live waiver verb); PENDING_REVIEW keeps the S70 positive-remainder rendering
    // with null claim fields; a clean SETTLED YEAR_END row keeps its S68 rendering with null
    // claim fields.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>End-to-end: HR waives an ACTIVE employee's negative-pre-clamp TERMINATION row
    /// (reader surfaces are NOT R9c-allowlisted — a terminated target 404s on /summary by design,
    /// so the reader pin uses an active employee; the row shape alone drives the reader).</summary>
    [Fact]
    public async Task Summary_WaivedTerminationRow_ClaimDistinct_NotForfeiture_NotPending()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);
        var waive = await ResolveAsync(HrClient(), employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, waive.StatusCode);

        var vacation = await GetSummaryCategoryAsync(
            ClientWith(EmployeeToken(employeeId)), employeeId, year: 2026, month: 3, VacationType);

        // Never a positive pending remainder; never §34 forfeiture.
        Assert.Equal(0m, vacation.GetProperty("remaining").GetDecimal());
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());
        Assert.Equal(0m, settlement.GetProperty("forfeitDays").GetDecimal());
        Assert.False(settlement.GetProperty("forfeitPending").GetBoolean());
        // The DISTINCT claim rendering (the R5 reader shape: reviewDisposition + claimDispositionDays).
        Assert.Equal("WAIVED", settlement.GetProperty("reviewDisposition").GetString());
        Assert.Equal(ClaimDays, settlement.GetProperty("claimDispositionDays").GetDecimal());
    }

    [Fact]
    public async Task Summary_PendingReviewTerminationRow_PositiveRemainder_NullClaimFields()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        var vacation = await GetSummaryCategoryAsync(
            ClientWith(EmployeeToken(employeeId)), employeeId, year: 2026, month: 3, VacationType);

        // The S70 pinned rendering, unchanged: the flag shows as a POSITIVE pending remainder.
        Assert.Equal(ClaimDays, vacation.GetProperty("remaining").GetDecimal());
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal("PENDING_REVIEW", settlement.GetProperty("state").GetString());
        Assert.True(settlement.GetProperty("forfeitPending").GetBoolean());
        Assert.Equal(ClaimDays, settlement.GetProperty("forfeitDays").GetDecimal());
        // The new claim fields are present-but-null (no claim disposition exists yet).
        Assert.Equal(JsonValueKind.Null, settlement.GetProperty("reviewDisposition").ValueKind);
        Assert.Equal(JsonValueKind.Null, settlement.GetProperty("claimDispositionDays").ValueKind);
    }

    /// <summary>No-regression: an unwaived SETTLED YEAR_END row keeps the S68 zero-remaining
    /// full-partition rendering — forfeitDays still carries the recorded §34 bucket; the two new
    /// claim fields are null.</summary>
    [Fact]
    public async Task Summary_SettledCleanYearEndRow_S68RenderingKept_NullClaimFields()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "SETTLED",
            forfeitDays: 20m, transferDays: 5m);

        var vacation = await GetSummaryCategoryAsync(
            ClientWith(EmployeeToken(employeeId)), employeeId, year: 2026, month: 3, VacationType);

        Assert.Equal(0m, vacation.GetProperty("remaining").GetDecimal());
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());
        Assert.Equal(5m, settlement.GetProperty("transferDays").GetDecimal());
        Assert.Equal(20m, settlement.GetProperty("forfeitDays").GetDecimal()); // genuine §34 — unchanged
        Assert.False(settlement.GetProperty("forfeitPending").GetBoolean());
        Assert.Equal(JsonValueKind.Null, settlement.GetProperty("reviewDisposition").ValueKind);
        Assert.Equal(JsonValueKind.Null, settlement.GetProperty("claimDispositionDays").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5 readers — year-overview: the closed-ferieår disposition carries the same distinct claim
    // shape; `expiring` reads 0 for a WAIVED row (the cleared flag — never §34).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task YearOverview_WaivedRow_ExpiringZero_ClaimDistinct()
    {
        var employeeId = await SeedEmployeeAsync();
        // The already-waived durable shape (state SETTLED / WAIVED / claim recorded / flag cleared).
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "SETTLED",
            forfeitDays: 0m, reviewDisposition: "WAIVED", claimDispositionDays: ClaimDays);

        var client = MakeFixedTodayClient(EmployeeToken(employeeId));
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/year-overview?year=2026");
        rsp.EnsureSuccessStatusCode();
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var vacation = body.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == VacationType);

        Assert.Equal(0m, vacation.GetProperty("expiring").GetDecimal()); // never §34 forfeiture
        var settlement = vacation.GetProperty("settlement");
        Assert.Equal("SETTLED", settlement.GetProperty("state").GetString());
        Assert.Equal(0m, settlement.GetProperty("forfeitDays").GetDecimal());
        Assert.False(settlement.GetProperty("forfeitPending").GetBoolean());
        Assert.Equal("WAIVED", settlement.GetProperty("reviewDisposition").GetString());
        Assert.Equal(ClaimDays, settlement.GetProperty("claimDispositionDays").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 — the waiver PARKS on the employee advisory lock (lock FIRST, before the row read), and
    // the in-lock re-read turns the CAS loser into a clean 409 when a competing waiver committed
    // mid-window. Choreography (the S70 parked-lock precedent): a foreign tx holds the lock →
    // the POST provably parks (pg_locks ungranted advisory wait) → the competing waiver commits
    // on the foreign tx (releasing the lock) → the parked request resumes, re-reads SETTLED → 409.
    // Without the lock the POST would complete instantly and never park — the park assertion is
    // the discriminator.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Waive_Race_ParksOnAdvisoryLock_LoserGets409_NoSecondApplication()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW", forfeitDays: ClaimDays);

        await using var foreignConn = new NpgsqlConnection(_harness.ConnectionString);
        await foreignConn.OpenAsync();
        await using var foreignTx = await foreignConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @id))", foreignConn, foreignTx))
        {
            lockCmd.Parameters.AddWithValue("id", employeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        // Fire the waiver POST — it must PARK on the advisory lock (lock precedes the row read).
        var hr = HrClient();
        var postTask = Task.Run(() => ResolveAsync(hr, employeeId, Ferieaar, "WAIVED", ifMatch: "\"1\""));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (postTask.IsCompleted)
            {
                var early = await postTask;
                Assert.Fail($"the waiver POST completed early ({(int)early.StatusCode}) without parking " +
                            "on the advisory lock — the R12 lock-first contract was not exercised.");
            }
            parked = await HasUngrantedAdvisoryWaitAsync();
            if (!parked)
                await Task.Delay(100);
        }
        Assert.True(parked, "the waiver POST never parked on the employee advisory lock within 30s.");

        // The competing waiver wins on the foreign tx (the lock holder), then commits — releasing
        // the lock and waking the parked request into its in-lock re-read.
        await using (var winCmd = new NpgsqlCommand(
            """
            UPDATE vacation_settlements SET
                settlement_state = 'SETTLED', review_disposition = 'WAIVED',
                claim_disposition_days = forfeit_days, forfeit_days = 0,
                version = version + 1, updated_at = NOW()
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND sequence = 1
            """, foreignConn, foreignTx))
        {
            winCmd.Parameters.AddWithValue("e", employeeId);
            winCmd.Parameters.AddWithValue("t", VacationType);
            winCmd.Parameters.AddWithValue("y", Ferieaar);
            await winCmd.ExecuteNonQueryAsync();
        }
        await foreignTx.CommitAsync();

        // The loser re-reads under the lock, sees SETTLED, and returns the clean state 409 —
        // never a double application, never a second event.
        var rsp = await postTask;
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal("WAIVED", row.Value.ReviewDisposition);
        Assert.Equal(ClaimDays, row.Value.ClaimDispositionDays);
        Assert.Equal(0m, row.Value.Forfeit);
        Assert.Equal(2L, row.Value.Version); // exactly the winner's bump
        // The SQL winner emitted nothing and the HTTP loser must not have either.
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationClaimWaived"));
    }

    /// <summary>True when some backend on this database is waiting on an UNGRANTED advisory lock —
    /// the deterministic the-POST-is-parked probe (the polling backend itself never waits).</summary>
    private async Task<bool> HasUngrantedAdvisoryWaitAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ─────────────────────────────── clients / endpoints ───────────────────────────────

    private HttpClient HrClient() => HrClientFor(OrgId);

    // S93 flat role-scope: the HR actor's access-granting scope is exact ORG_ONLY membership. The
    // single-arg form keys the scope on the same org as the actor's primary org (the common case);
    // the two-arg form lets a test give the actor a DIFFERENT primary org than its scope org (used
    // to discriminate actor_primary_org_id from the access-granting scope org).
    private HttpClient HrClientFor(string orgId) => HrClientFor(orgId, orgId);

    private HttpClient HrClientFor(string primaryOrgId, string scopeOrgId)
    {
        var svc = new JwtTokenService(DevSettings());
        var token = svc.GenerateToken(
            employeeId: "hr_s71_waiver", name: "hr_s71_waiver", role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: primaryOrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, scopeOrgId, "ORG_ONLY") });
        return ClientWith(token);
    }

    private static string EmployeeToken(string actorId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient MakeFixedTodayClient(string bearer)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedToday));
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    private static async Task<HttpResponseMessage> ResolveAsync(
        HttpClient client, string employeeId, int year, string disposition, string ifMatch,
        decimal? forfeitDays = null)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/resolve")
        {
            Content = forfeitDays is null
                ? JsonContent.Create(new { disposition })
                : JsonContent.Create(new { disposition, forfeitDays }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<JsonElement> GetSummaryCategoryAsync(
        HttpClient client, string employeeId, int year, int month, string type)
    {
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/summary?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("entitlements").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == type);
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_waiver_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>A MAO + child-ORGANISATION org pair. S93 flat role-scope: the actor scopes
    /// directly on the child Organisation (exact ORG_ONLY); the MAO parent only serves as a
    /// DIFFERENT primary-org label so the audit row's actor_primary_org_id can be discriminated
    /// from target_org_id. Idempotent; pre-empts the seeder's flat ensureOrg.</summary>
    private async Task SeedOrgPairAsync(string parentOrg, string childOrg)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path)
            VALUES (@p, @p, 'MAO', NULL, '/' || @p || '/'),
                   (@c, @c, 'ORGANISATION', @p, '/' || @p || '/' || @c || '/')
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("p", parentOrg);
        cmd.Parameters.AddWithValue("c", childOrg);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The post-Step-A leaver state (the TerminationSettlementTests convention): end date
    /// + lifecycle deactivation with provenance.</summary>
    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("endDate", endDate);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Direct settlement-row seed (the TerminationSettlementTests convention) with a
    /// minimal valid TERMINATION/YEAR_END-shaped snapshot; optionally pre-resolved
    /// (<paramref name="reviewDisposition"/> + <paramref name="claimDispositionDays"/> — the 7100
    /// pairing CHECK requires them together for WAIVED).</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string trigger, string state, decimal forfeitDays,
        decimal transferDays = 0m, string? reviewDisposition = null, decimal? claimDispositionDays = null)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 12.5m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            agreementCode = "AC",
            transferAgreementDays = transferDays,
            isFeriehindret = false,
            terminationDate = trigger == Termination ? "2026-02-28" : null,
            crystallizationBasis = trigger == Termination ? "S26_WHOLE_MONTH" : null,
            crystallizedDays = trigger == Termination && state == "SETTLED" ? 12.5m : 0m,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, claim_disposition_days, version)
            VALUES
                (@e, @t, @y, 1, @state, @trigger, @snapshot::jsonb, @transfer, 0, @forfeit,
                 @review, @claim, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transferDays);
        cmd.Parameters.AddWithValue("forfeit", forfeitDays);
        cmd.Parameters.AddWithValue("review", (object?)reviewDisposition ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("claim", NpgsqlTypes.NpgsqlDbType.Numeric)
        {
            Value = (object?)claimDispositionDays ?? DBNull.Value,
        });
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, string Trigger, decimal Forfeit, string? ReviewDisposition,
            decimal? ClaimDispositionDays, long Version)?>
        ReadSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, trigger, forfeit_days, review_disposition,
                   claim_disposition_days, version
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.GetInt64(5));
    }

    private async Task<decimal?> ReadCarryoverInAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT carryover_in FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (decimal)result;
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

    private async Task<string?> ReadOutboxPayloadAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @s AND event_type = @t
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("t", eventType);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string)result;
    }
}
