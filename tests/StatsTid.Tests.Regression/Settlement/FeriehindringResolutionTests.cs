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
/// S79 / TASK-7901 (ADR-033 slice 4; SPRINT-79 R1/R3/R8/R12) — Docker-gated tests for the
/// FERIEHINDRING resolution verb on the manual-resolve surface: the §22 impediment RESCUE of an
/// impeded tranche from the §34 forfeiture bucket into next year's <c>carryover_in</c>, a sibling
/// disposition to FORFEIT/DEFER/WAIVED (NOT a DEFER variant). The legal-sensitive core:
///
/// <list type="bullet">
///   <item><description><b>Partial rescue (R3):</b> flagged forfeit=15, impeded=10 →
///   feriehindring_transfer_days=10, forfeit_days=5 (§22 FIRST, §34 the residual — the same day
///   never both transfers and forfeits), carryover_in(next year)=§21+10, ONE
///   VacationForfeitedToFeriefond(5) + ONE FeriehindringTransferred(10, reason), SETTLED.</description></item>
///   <item><description><b>Full rescue (R8):</b> impeded == forfeit → forfeit_days=0, NO forfeit
///   event, carryover composed, SETTLED.</description></item>
///   <item><description><b>Bounds (R3, clean 422 not a raw CHECK):</b> impeded &gt; ForfeitDays;
///   AND (with ForfeitDays &gt; 20) impeded &gt; 20 — both 422, no mutation, still PENDING_REVIEW.</description></item>
///   <item><description><b>Source-keyed carryover (R1 / W3 discriminator):</b> a settlement with
///   transfer_days &gt; 0 AND a FERIEHINDRING resolve → next-year carryover is the §21+§22 SUM,
///   never just §22, never just §21.</description></item>
///   <item><description><b>Reason required (R3):</b> missing / empty reason → 422.</description></item>
///   <item><description><b>TERMINATION re-pin (R3):</b> FERIEHINDRING on a TERMINATION row → 422 (it
///   carries a §7 claim flag, not a §34 candidate).</description></item>
///   <item><description><b>Concurrency (R12):</b> two FERIEHINDRING resolves, or FERIEHINDRING vs
///   FORFEIT → one wins, the other 409 (If-Match), no double-emit.</description></item>
///   <item><description><b>Replay:</b> re-running the audit projection over the emitted events
///   reproduces the same audit rows (idempotent event_id).</description></item>
///   <item><description><b>Audit actor-org (the request-endpoint convention):</b> the ADR-026 row
///   records the OPERATOR's org + the EMPLOYEE's resolved org (a parent-org HR actor over a
///   child-org employee — the discriminating pin).</description></item>
/// </list>
///
/// <para>Harness/JWT/seeding conventions mirror <see cref="WaiverResolutionTests"/> (Docker harness,
/// direct settlement-row seeding, HR LocalHR token for the resolve surface). The rows here are
/// YEAR_END (FERIEHINDRING resolves a §34-candidate remainder, never a TERMINATION §7 claim).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FeriehindringResolutionTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";
    private const int Ferieaar = 2025;
    private const string Reason = "Langtidssygdom hele ferieåret (§22 feriehindring)";

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
    // R3 marquee — the PARTIAL split: flagged forfeit=15, impeded=10 → §22=10 / §34=5,
    // carryover=§21+10, one forfeit(5) + one feriehindring(10), SETTLED.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PartialRescue_SplitsSection22First_Section34Residual_EventsCarryover()
    {
        var employeeId = await SeedEmployeeAsync();
        // §21 transfer of 3 already persisted at close; §34-candidate remainder flagged = 15.
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 3m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 10m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("resolved").GetBoolean());
        Assert.Equal("SETTLED", body.GetProperty("settlementState").GetString());
        Assert.Equal("FERIEHINDRING", body.GetProperty("reviewDisposition").GetString());
        Assert.Equal(10m, body.GetProperty("feriehindringTransferDays").GetDecimal());
        Assert.Equal(Reason, body.GetProperty("feriehindringReason").GetString());
        Assert.Equal(5m, body.GetProperty("forfeitDays").GetDecimal());     // the §34 residual
        Assert.Equal(13m, body.GetProperty("carryoverIn").GetDecimal());    // §21 (3) + §22 (10)

        // Durable row state — §22 FIRST, §34 residual, reason recorded, SETTLED.
        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal("FERIEHINDRING", row.Value.ReviewDisposition);
        Assert.Equal(10m, row.Value.FeriehindringTransfer);
        Assert.Equal(Reason, row.Value.FeriehindringReason);
        Assert.Equal(5m, row.Value.Forfeit);
        Assert.Equal(3m, row.Value.Transfer);
        Assert.Equal(2L, row.Value.Version);

        // Source-keyed carryover (R1): next-year carryover_in = §21 (3) + §22 (10) = 13.
        Assert.Equal(13m, await ReadCarryoverInAsync(employeeId, Ferieaar + 1));

        // Exactly ONE FeriehindringTransferred(10, reason) + ONE VacationForfeitedToFeriefond(5).
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
        using var fhPayload = JsonDocument.Parse(
            (await ReadOutboxPayloadAsync(employeeId, "FeriehindringTransferred"))!);
        Assert.Equal(10m, fhPayload.RootElement.GetProperty("transferDays").GetDecimal());
        Assert.Equal(Reason, fhPayload.RootElement.GetProperty("feriehindringReason").GetString());
        using var ffPayload = JsonDocument.Parse(
            (await ReadOutboxPayloadAsync(employeeId, "VacationForfeitedToFeriefond"))!);
        Assert.Equal(5m, ffPayload.RootElement.GetProperty("forfeitDays").GetDecimal());

        // ADR-026 audit rows: one FeriehindringTransferred + one VacationForfeitedToFeriefond.
        Assert.Equal(1L, await CountAsync("audit_projection",
            "event_type = 'FeriehindringTransferred' AND target_resource_id = @r", ("r", employeeId)));
        Assert.Equal(1L, await CountAsync("audit_projection",
            "event_type = 'VacationForfeitedToFeriefond' AND target_resource_id = @r", ("r", employeeId)));
        Assert.True(await CountAsync("vacation_settlement_audit",
            "employee_id = @e AND action = 'UPDATED'", ("e", employeeId)) >= 1);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R8 — the FULL rescue: impeded == forfeit → forfeit_days=0, NO forfeit event, carryover composed.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullRescue_NoResidual_NoForfeitEvent_CarryoverComposed()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 12m, transferDays: 2m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 12m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal("FERIEHINDRING", row.Value.ReviewDisposition);
        Assert.Equal(12m, row.Value.FeriehindringTransfer);
        Assert.Equal(0m, row.Value.Forfeit); // FULL rescue — residual is zero

        // Carryover composed = §21 (2) + §22 (12) = 14.
        Assert.Equal(14m, await ReadCarryoverInAsync(employeeId, Ferieaar + 1));

        // One feriehindring event; NO forfeit event at all (full rescue).
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
        Assert.Equal(0L, await CountAsync("audit_projection",
            "event_type = 'VacationForfeitedToFeriefond' AND target_resource_id = @r", ("r", employeeId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1 / W3 discriminator — the carryover is the §21+§22 SUM (NOT just §22, NOT just §21). A
    // §21-only check (transfer 5, impeded 4) → carryover 9; the §22 component alone would be 4 and
    // the §21 component alone 5, so 9 discriminates the source-keyed compose.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SourceKeyedCarryover_IsSection21PlusSection22Sum()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 10m, transferDays: 5m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 4m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // 5 (§21) + 4 (§22) = 9 — neither 4 (§22 alone) nor 5 (§21 alone).
        Assert.Equal(9m, await ReadCarryoverInAsync(employeeId, Ferieaar + 1));
        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal(6m, row!.Value.Forfeit); // 10 − 4 residual
        Assert.Equal(4m, row.Value.FeriehindringTransfer);
        Assert.Equal(5m, row.Value.Transfer);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R3 bounds — clean 422 (not a raw CHECK), no mutation, still PENDING_REVIEW. BOTH bounds:
    //   (a) impeded > ForfeitDays (with ForfeitDays < 20)
    //   (b) impeded > 20 (with ForfeitDays > 20 so the §34 ceiling is not the binding one)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImpededExceedsForfeitDays_422_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 3m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 16m, reason: Reason, ifMatch: "\"1\""); // 16 > forfeit 15
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(15m, row.Value.Forfeit);
        Assert.Equal(0m, row.Value.FeriehindringTransfer);
        Assert.Null(row.Value.ReviewDisposition);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
        Assert.Null(await ReadCarryoverInAsync(employeeId, Ferieaar + 1));
    }

    [Fact]
    public async Task ImpededExceedsSection22Cap20_422_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        // Forfeit 25 (> 20) so the §22 statutory cap (20), not the §34 remainder, is the binding bound.
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 25m, transferDays: 0m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 21m, reason: Reason, ifMatch: "\"1\""); // 21 <= forfeit 25 BUT > §22 cap 20
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(25m, row.Value.Forfeit);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
    }

    [Fact]
    public async Task ImpededZeroOrNegative_422()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);

        var zero = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 0m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, zero.StatusCode);

        var neg = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: -2m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, neg.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R3 — reason required + non-empty.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReasonMissingOrEmpty_422_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);

        // Missing reason entirely.
        var missing = await ResolveRawAsync(HrClient(), employeeId, Ferieaar,
            new { disposition = "FERIEHINDRING", impededDays = 5m }, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

        // Whitespace-only reason.
        var blank = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 5m, reason: "   ", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R3 re-pin — a TERMINATION row is not FERIEHINDRING-resolvable (its forfeit_days is a §7 claim
    // flag, not a §34 candidate).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FeriehindringOnTerminationRow_422()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, new DateOnly(2026, 2, 28));
        await SeedSettlementRowAsync(employeeId, Ferieaar, Termination, "PENDING_REVIEW",
            forfeitDays: 2.92m, transferDays: 0m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 2m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Step-7a BLOCKER 1 — carryover-finality guard. A 2024 PENDING_REVIEW row resolved AFTER the
    // close poll has SETTLED 2025 must NOT overwrite 2025's finalized carryover_in. FERIEHINDRING of
    // 2024 → 409 NextYearAlreadySettled; the 2024 row stays PENDING_REVIEW, the 2025 carryover_in is
    // unchanged, nothing is emitted. Mirrors the superseding-carryover finality guard.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NextYearAlreadySettled_409_NoOverwrite_RowUntouched_NothingEmitted()
    {
        var employeeId = await SeedEmployeeAsync();
        // 2024 PENDING_REVIEW row (the one we attempt to FERIEHINDRING-resolve), §34-candidate flag = 15.
        await SeedSettlementRowAsync(employeeId, Ferieaar - 1, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 3m);
        // 2025 ALREADY SETTLED (the close poll got there first) — its carryover_in is final.
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "SETTLED",
            forfeitDays: 0m, transferDays: 4m);
        // Seed 2025's finalized carryover_in (the value FERIEHINDRING of 2024 must NOT clobber).
        await SeedCarryoverInAsync(employeeId, Ferieaar, carryoverDays: 4m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar - 1,
            impededDays: 10m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NextYearAlreadySettled", body.GetProperty("failure").GetString());
        Assert.Equal(Ferieaar, body.GetProperty("nextYear").GetInt32());

        // The 2024 row is untouched (still PENDING_REVIEW, version 1, no FERIEHINDRING applied).
        var row2024 = await ReadSettlementAsync(employeeId, Ferieaar - 1);
        Assert.Equal("PENDING_REVIEW", row2024!.Value.State);
        Assert.Equal(15m, row2024.Value.Forfeit);
        Assert.Equal(0m, row2024.Value.FeriehindringTransfer);
        Assert.Null(row2024.Value.ReviewDisposition);
        Assert.Equal(1L, row2024.Value.Version);

        // 2025's finalized carryover_in is unchanged (NOT overwritten by 3 + 10).
        Assert.Equal(4m, await ReadCarryoverInAsync(employeeId, Ferieaar));

        // Nothing emitted.
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Step-7a BLOCKER 2 — a leaver's deferred-disposition YEAR_END row (forfeit_days = the FULL
    // disposable as a FLAG + the snapshot DeferredDisposition marker, written by
    // SettleLeaverDeferredDispositionAsync) is NOT a §34 candidate → FERIEHINDRING refuses 422, the
    // row is untouched, nothing emitted. (FORFEIT/DEFER remain its valid resolutions.)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LeaverDeferredDispositionRow_422_RowUntouched_NothingEmitted()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, new DateOnly(2026, 2, 28));
        // Mirror the SettleLeaverDeferredDispositionAsync shape: trigger=YEAR_END, PENDING_REVIEW,
        // forfeit_days = the full disposable, the DeferredDisposition marker set in the snapshot.
        await SeedDeferredDispositionRowAsync(employeeId, Ferieaar, disposable: 12m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 5m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal("YEAR_END", row.Value.Trigger);
        Assert.Equal(12m, row.Value.Forfeit);
        Assert.Equal(0m, row.Value.FeriehindringTransfer);
        Assert.Null(row.Value.ReviewDisposition);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationForfeitedToFeriefond"));
        Assert.Null(await ReadCarryoverInAsync(employeeId, Ferieaar + 1));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Guards — stale If-Match (412), already-SETTLED (409).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StaleIfMatch_412_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 5m, reason: Reason, ifMatch: "\"9\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);
        Assert.Equal(1L, row.Value.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
    }

    [Fact]
    public async Task AlreadySettledRow_409()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "SETTLED",
            forfeitDays: 0m, transferDays: 5m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 1m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal(1L, row!.Value.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 concurrency — two FERIEHINDRING resolves park on the employee advisory lock; the loser
    // re-reads SETTLED under the lock and returns 409, no second event. (FERIEHINDRING vs FORFEIT is
    // the same CAS-loser path; this covers the double-FERIEHINDRING case via the parked-lock probe.)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Race_ParksOnAdvisoryLock_LoserGets409_NoSecondApplication()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);

        await using var foreignConn = new NpgsqlConnection(_harness.ConnectionString);
        await foreignConn.OpenAsync();
        await using var foreignTx = await foreignConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @id))", foreignConn, foreignTx))
        {
            lockCmd.Parameters.AddWithValue("id", employeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        var hr = HrClient();
        var postTask = Task.Run(() => ResolveFeriehindringAsync(hr, employeeId, Ferieaar,
            impededDays: 10m, reason: Reason, ifMatch: "\"1\""));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (postTask.IsCompleted)
            {
                var early = await postTask;
                Assert.Fail($"the FERIEHINDRING POST completed early ({(int)early.StatusCode}) without " +
                            "parking on the advisory lock — the R12 lock-first contract was not exercised.");
            }
            parked = await HasUngrantedAdvisoryWaitAsync();
            if (!parked) await Task.Delay(100);
        }
        Assert.True(parked, "the FERIEHINDRING POST never parked on the employee advisory lock within 30s.");

        // The competing winner commits a FERIEHINDRING-equivalent state on the foreign tx (the lock
        // holder), releasing the lock and waking the parked request into its in-lock re-read.
        await using (var winCmd = new NpgsqlCommand(
            """
            UPDATE vacation_settlements SET
                settlement_state = 'SETTLED', review_disposition = 'FERIEHINDRING',
                feriehindring_transfer_days = 10, feriehindring_reason = 'competing winner',
                forfeit_days = forfeit_days - 10,
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

        // The loser re-reads under the lock, sees SETTLED, returns 409 — never a double application.
        var rsp = await postTask;
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal("FERIEHINDRING", row.Value.ReviewDisposition);
        Assert.Equal(2L, row.Value.Version); // exactly the winner's bump
        // The SQL winner emitted nothing and the HTTP loser must not have either.
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
    }

    // Step-5a Codex WARNING — two REAL HTTP contenders (no synthetic SQL winner): exactly one wins
    // the advisory lock + the If-Match("1") CAS and fully resolves (emit + SETTLED + version→2); the
    // other parks, wakes, sees SETTLED / a stale version → conflicts. Discriminates real double-emission.
    [Fact]
    public async Task Race_TwoRealContenders_ExactlyOneWins_SingleEmission_NoDoubleApply()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);

        var taskA = ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar, impededDays: 10m, reason: "A", ifMatch: "\"1\"");
        var taskB = ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar, impededDays: 7m, reason: "B", ifMatch: "\"1\"");
        var responses = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed));

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("SETTLED", row!.Value.State);
        Assert.Equal("FERIEHINDRING", row.Value.ReviewDisposition);
        Assert.Equal(2L, row.Value.Version);                                                  // exactly ONE bump
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred")); // single emission
    }

    // Step-5a Codex BLOCKER — scale adversary: 0.001 passes the range guard (0 < 0.001 ≤ 15) but
    // exceeds the NUMERIC(6,2) day-count scale → it would round to 0.00 in the persisted bucket while
    // the event/carryover compose from the unrounded value. Must 422, row untouched, nothing emitted.
    [Fact]
    public async Task ImpededOverScale_422_RowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);
        var hr = HrClient();

        var rsp = await ResolveFeriehindringAsync(hr, employeeId, Ferieaar,
            impededDays: 0.001m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var row = await ReadSettlementAsync(employeeId, Ferieaar);
        Assert.Equal("PENDING_REVIEW", row!.Value.State);  // untouched
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "FeriehindringTransferred"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Replay — re-running the audit projection over the emitted FeriehindringTransferred +
    // VacationForfeitedToFeriefond events reproduces the SAME audit rows (idempotent event_id;
    // ON CONFLICT DO NOTHING — the re-insert is a no-op).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Replay_ReinsertingProjectionRows_IsIdempotent()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 3m);

        var rsp = await ResolveFeriehindringAsync(HrClient(), employeeId, Ferieaar,
            impededDays: 10m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var beforeFh = await CountAsync("audit_projection",
            "event_type = 'FeriehindringTransferred' AND target_resource_id = @r", ("r", employeeId));
        var beforeFf = await CountAsync("audit_projection",
            "event_type = 'VacationForfeitedToFeriefond' AND target_resource_id = @r", ("r", employeeId));
        Assert.Equal(1L, beforeFh);
        Assert.Equal(1L, beforeFf);

        // Re-insert each projection row with the SAME event_id (the replay shape; the unique event_id
        // + ON CONFLICT DO NOTHING make the re-run a no-op — counts unchanged, rows byte-identical).
        await ReinsertProjectionRowByEventIdAsync(employeeId, "FeriehindringTransferred");
        await ReinsertProjectionRowByEventIdAsync(employeeId, "VacationForfeitedToFeriefond");

        Assert.Equal(beforeFh, await CountAsync("audit_projection",
            "event_type = 'FeriehindringTransferred' AND target_resource_id = @r", ("r", employeeId)));
        Assert.Equal(beforeFf, await CountAsync("audit_projection",
            "event_type = 'VacationForfeitedToFeriefond' AND target_resource_id = @r", ("r", employeeId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Audit actor-org provenance — the ADR-026 rows record the OPERATOR's org + the EMPLOYEE's
    // resolved org. A same-org fixture cannot discriminate; this uses a parent-org HR actor over a
    // child-org employee (ORG_AND_DESCENDANTS subtree admission).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ParentOrgHrActor_AuditRowsCarryActorOrgNotTargetOrg()
    {
        const string parentOrg = "STY79FP";
        const string childOrg = "STY79FC";
        await SeedOrgPairAsync(parentOrg, childOrg);

        var employeeId = "emp_s79_fh_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, childOrg, "AC", "OK24");
        await SeedSettlementRowAsync(employeeId, Ferieaar, YearEnd, "PENDING_REVIEW",
            forfeitDays: 15m, transferDays: 0m);

        var rsp = await ResolveFeriehindringAsync(HrClientFor(parentOrg), employeeId, Ferieaar,
            impededDays: 10m, reason: Reason, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Both emitted events' audit rows: actor org = parent, target org = child.
        Assert.Equal(1L, await CountAsync("audit_projection",
            "event_type = 'FeriehindringTransferred' AND target_resource_id = @r " +
            "AND actor_primary_org_id = @actorOrg AND target_org_id = @targetOrg",
            ("r", employeeId), ("actorOrg", parentOrg), ("targetOrg", childOrg)));
        Assert.Equal(1L, await CountAsync("audit_projection",
            "event_type = 'VacationForfeitedToFeriefond' AND target_resource_id = @r " +
            "AND actor_primary_org_id = @actorOrg AND target_org_id = @targetOrg",
            ("r", employeeId), ("actorOrg", parentOrg), ("targetOrg", childOrg)));
    }

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

    private HttpClient HrClientFor(string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        var token = svc.GenerateToken(
            employeeId: "hr_s79_fh", name: "hr_s79_fh", role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_AND_DESCENDANTS") });
        return ClientWith(token);
    }

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
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

    private static Task<HttpResponseMessage> ResolveFeriehindringAsync(
        HttpClient client, string employeeId, int year, decimal impededDays, string reason, string ifMatch)
        => ResolveRawAsync(client, employeeId, year,
            new { disposition = "FERIEHINDRING", impededDays, reason }, ifMatch);

    private static async Task<HttpResponseMessage> ResolveRawAsync(
        HttpClient client, string employeeId, int year, object payload, string ifMatch)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/vacation-settlements/{employeeId}/{VacationType}/{year}/resolve")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s79_fh_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

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

    /// <summary>Direct settlement-row seed (the WaiverResolutionTests convention) with a minimal
    /// valid YEAR_END/TERMINATION-shaped snapshot carrying annualQuota 25 (the seedQuota the
    /// source-keyed carryover writes).</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string trigger, string state, decimal forfeitDays, decimal transferDays)
    {
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
            agreementCode = "AC",
            transferAgreementDays = transferDays,
            isFeriehindret = false,
            terminationDate = trigger == Termination ? "2026-02-28" : null,
            crystallizationBasis = trigger == Termination ? "S26_WHOLE_MONTH" : null,
            crystallizedDays = 0m,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, version)
            VALUES
                (@e, @t, @y, 1, @state, @trigger, @snapshot::jsonb, @transfer, 0, @forfeit, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transferDays);
        cmd.Parameters.AddWithValue("forfeit", forfeitDays);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Seeds a leaver's deferred-disposition YEAR_END row exactly as
    /// SettleLeaverDeferredDispositionAsync (VacationSettlementService.cs:657, R4) writes it:
    /// trigger=YEAR_END, PENDING_REVIEW, forfeit_days = the FULL disposable (a FLAG, not a §34
    /// bucket), transfer_days = payout_days = 0, and the snapshot DeferredDisposition marker set
    /// (VacationSettlementSnapshot.cs:192).</summary>
    private async Task SeedDeferredDispositionRowAsync(string employeeId, int year, decimal disposable)
    {
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
            agreementCode = "AC",
            transferAgreementDays = 0m,
            isFeriehindret = false,
            terminationDate = "2026-02-28",
            deferredDisposition = true,   // the R4 marker — this is a leaver's other-ferieår flag row
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, version)
            VALUES
                (@e, @t, @y, 1, 'PENDING_REVIEW', 'YEAR_END', @snapshot::jsonb, 0, 0, @forfeit, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("forfeit", disposable);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Seeds a finalized next-year carryover_in row (mirrors WriteCarryoverInAsync's
    /// zero-state seed: total_quota 25, used/planned 0, carryover_in = the given value) — the value
    /// BLOCKER 1 must refuse to overwrite once the next year is settled.</summary>
    private async Task SeedCarryoverInAsync(string employeeId, int year, decimal carryoverDays)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year,
                 total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @e, @t, @y, 25, 0, 0, @carryover, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
            DO UPDATE SET carryover_in = @carryover, updated_at = NOW()
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("carryover", carryoverDays);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, string Trigger, decimal Forfeit, decimal Transfer,
            string? ReviewDisposition, decimal FeriehindringTransfer, string? FeriehindringReason, long Version)?>
        ReadSettlementAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, trigger, forfeit_days, transfer_days, review_disposition,
                   feriehindring_transfer_days, feriehindring_reason, version
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt64(7));
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

    /// <summary>Re-inserts the audit_projection row for the latest event of the given type using its
    /// SAME event_id (the replay shape — the ON CONFLICT (event_id) DO NOTHING makes this a no-op so
    /// the projection is idempotent under re-run).</summary>
    private async Task ReinsertProjectionRowByEventIdAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO audit_projection
                (event_id, outbox_id, event_type, visibility_scope, target_org_id,
                 target_resource_id, actor_id, actor_primary_org_id, correlation_id, occurred_at, details)
            SELECT event_id, outbox_id, event_type, visibility_scope, target_org_id,
                   target_resource_id, actor_id, actor_primary_org_id, correlation_id, occurred_at, details
            FROM audit_projection
            WHERE event_type = @t AND target_resource_id = @r
            ON CONFLICT (event_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("r", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }
}
