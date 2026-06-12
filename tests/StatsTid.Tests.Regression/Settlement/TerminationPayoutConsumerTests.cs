using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7105 (ADR-033 slice 3b; SPRINT-71 R1/R2/R6/R11) — Docker-gated D-tests for the §26
/// <c>TerminationPayoutRequested</c> consumer in
/// <see cref="StatsTid.Integrations.Payroll.Services.SettlementExportEmitter"/>: the
/// exactly-once, money-free, fail-closed §26 termination-payout staging choreography.
///
/// <para>
/// The consumer polls the canonical <c>events</c> table, and per event in ONE advisory-locked tx
/// (R6/R12): re-reads the EXACT settlement row + the request row — request OPEN + settlement
/// SETTLED ⇒ stage the <c>SLS_TBD_S26</c> ORIGINAL line (<c>hours = the event's
/// CrystallizedDays</c>; export sequence = the ODD settlement-row sequence per R1/R2) + the
/// <c>(source_event_id, TERMINATION_PAYOUT_26)</c> checkpoint + the CONSUMER-authoritative
/// request <c>OPEN → LINE_STAGED</c> promotion; request VOIDED or settlement REVERSED ⇒ NO line
/// + a terminal <c>SKIPPED_VOIDED</c> checkpoint. The lønart resolves fail-closed off the
/// settlement ROW's immutable snapshot at <c>asOf = the event's SettlementBoundaryDate</c> (R11).
/// </para>
///
/// <para>Harness + FAIL-002 protocol: see <see cref="SettlementEmitterFixture"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TerminationPayoutConsumerTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private string Cs => _harness.ConnectionString;
    private DbConnectionFactory Factory => new(Cs);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Happy path — R6 choreography: line + checkpoint + LINE_STAGED promotion, one tx.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>An OPEN request against a SETTLED TERMINATION row stages EXACTLY ONE money-free
    /// <c>SLS_TBD_S26</c> ORIGINAL line (<c>hours = CrystallizedDays</c>, <c>amount = 0</c>, export
    /// sequence = the ODD settlement-row sequence — R1: originals carry <c>2g−1</c>), writes the
    /// terminal PROCESSED checkpoint at <c>(event, TERMINATION_PAYOUT_26)</c>, and promotes the
    /// request <c>OPEN → LINE_STAGED</c> (R6 — consumer promotion is authoritative), all visible
    /// atomically.</summary>
    [Fact]
    public async Task OpenRequest_SettledTermination_StagesS26Line_PromotesRequest()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, crystallizedDays: 12.5m);
        await SettlementEmitterFixture.SeedRequestRowAsync(Cs, emp, state: "OPEN");
        var eventId = await SettlementEmitterFixture.WriteTerminationPayoutRequestedEventAsync(
            Factory, emp, crystallizedDays: 12.5m);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "PROCESSED");

        // The checkpoint (composite key — the §26 bucket).
        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.TerminationBucket));

        // EXACTLY one line, at the ODD settlement-row sequence (R1/R2: original = 2g−1 = 1).
        Assert.Equal(1L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        var line = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 1, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.NotNull(line);
        Assert.Equal(SettlementEmitterFixture.TerminationSentinelWageType, line!.Value.WageType);
        Assert.Equal(12.5m, line.Value.Hours);          // hours = the EVENT's CrystallizedDays
        Assert.Equal(0m, line.Value.Amount);            // money-free (CHECK amount = 0)
        Assert.Equal("ORIGINAL", line.Value.LineKind);  // R8: an original, not a compensating line
        Assert.Null(line.Value.ReversesLineId);
        Assert.Equal("OK24", line.Value.OkVersion);     // wage key off the ROW snapshot
        Assert.Equal("AC", line.Value.AgreementCode);
        Assert.Equal(SettlementEmitterFixture.BoundaryDate, line.Value.PeriodStart); // asOf anchor as period
        Assert.Equal(eventId, line.Value.SourceEventId);

        // R6 — the consumer promoted the request in the same tx.
        Assert.Equal("LINE_STAGED", await SettlementEmitterFixture.RequestStateAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Exactly-once / replay parity on redelivery.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A TRUE redelivery (the inbox checkpoint removed; the staged line + the LINE_STAGED
    /// request persist) re-runs the full claim path: the line-UNIQUE conflict resolves as
    /// BenignRedelivery (same <c>source_event_id</c>), the request promotion no-ops (already
    /// LINE_STAGED — the conditional <c>WHERE state='OPEN'</c>), the checkpoint is re-promoted to
    /// PROCESSED, and the persisted line is BYTE-IDENTICAL across the two claims (replay parity).</summary>
    [Fact]
    public async Task Redelivery_IsBenign_LineByteIdentical_RequestStaysLineStaged()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, crystallizedDays: 7.25m);
        await SettlementEmitterFixture.SeedRequestRowAsync(Cs, emp, state: "OPEN");
        var eventId = await SettlementEmitterFixture.WriteTerminationPayoutRequestedEventAsync(
            Factory, emp, crystallizedDays: 7.25m);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "PROCESSED");
        var firstLine = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 1, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.NotNull(firstLine);

        // Drop ONLY the checkpoint ⇒ the poll re-selects the event ⇒ a second REAL claim runs.
        await SettlementEmitterFixture.DeleteInboxRowAsync(Cs, eventId);

        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "PROCESSED");

        Assert.Equal(1L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp)); // never double-staged
        var secondLine = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 1, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.Equal(firstLine!.Value, secondLine!.Value); // record-struct equality ⇒ byte-identical
        Assert.Equal("LINE_STAGED", await SettlementEmitterFixture.RequestStateAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6 — voided request ⇒ SKIPPED_VOIDED, no line.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>When EVERY request for the settlement row is <c>VOIDED_BY_REVERSAL</c> (the D-E
    /// fail-closed: the Backend reversal voids requests in its own tx; this event raced it), the
    /// consumer stages NOTHING and writes a terminal <c>SKIPPED_VOIDED</c> checkpoint. The voided
    /// request row is untouched.</summary>
    [Fact]
    public async Task VoidedRequest_SkippedVoided_NoLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, crystallizedDays: 12.5m);
        await SettlementEmitterFixture.SeedRequestRowAsync(Cs, emp, state: "VOIDED_BY_REVERSAL");
        var eventId = await SettlementEmitterFixture.WriteTerminationPayoutRequestedEventAsync(
            Factory, emp, crystallizedDays: 12.5m);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "SKIPPED_VOIDED");

        Assert.Equal("SKIPPED_VOIDED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.TerminationBucket));
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Equal("VOIDED_BY_REVERSAL", await SettlementEmitterFixture.RequestStateAsync(Cs, emp)); // untouched
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6/R9 — REVERSED settlement ⇒ SKIPPED_VOIDED, no line (the request-vs-reversal race).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>When the settlement row at the event's exact sequence is REVERSED at consumption
    /// time — even with the request still (incoherently) OPEN — the under-lock settlement re-read
    /// wins (R6: the consumer re-reads BOTH rows; the settlement state gates first): NO line, a
    /// terminal <c>SKIPPED_VOIDED</c> checkpoint, and the request is NOT promoted.</summary>
    [Fact]
    public async Task ReversedSettlement_SkippedVoided_NoLine_RequestNotPromoted()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, crystallizedDays: 12.5m);
        await SettlementEmitterFixture.SeedRequestRowAsync(Cs, emp, state: "OPEN");
        var eventId = await SettlementEmitterFixture.WriteTerminationPayoutRequestedEventAsync(
            Factory, emp, crystallizedDays: 12.5m);
        await SettlementEmitterFixture.MarkSettlementReversedAsync(Cs, emp); // the reversal wins the race

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "SKIPPED_VOIDED");

        Assert.Equal("SKIPPED_VOIDED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.TerminationBucket));
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Equal("OPEN", await SettlementEmitterFixture.RequestStateAsync(Cs, emp)); // not promoted
    }

    // ════════════════════════════════════════════════════════════════════════
    // R11 — lønart fail-closed: no §26 mapping ⇒ RETRY_PENDING at '_EVENT', no line.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A settlement-row snapshot whose agreement has NO §26 <c>wage_type_mappings</c> row
    /// fails CLOSED: no line, no fallback — a non-terminal RETRY_PENDING diagnostics row at the
    /// event-level <c>'_EVENT'</c> sentinel (R7: transient/deterministic diagnostics key at
    /// '_EVENT') with <c>attempts ≥ 1</c>. The request stays OPEN (nothing staged ⇒ nothing
    /// promoted).</summary>
    [Fact]
    public async Task MissingS26Mapping_FailsClosed_RetryPendingAtEventBucket_NoLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        // ZZ_NOMAP has no VACATION_TERMINATION_PAYOUT mapping (the seed covers {AC,HK,PROSA,…}).
        await SettlementEmitterFixture.SeedSettlementRowAsync(
            Cs, emp, crystallizedDays: 12.5m, agreementCode: "ZZ_NOMAP");
        await SettlementEmitterFixture.SeedRequestRowAsync(Cs, emp, state: "OPEN");
        var eventId = await SettlementEmitterFixture.WriteTerminationPayoutRequestedEventAsync(
            Factory, emp, crystallizedDays: 12.5m);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.EventLevelBucket) == "RETRY_PENDING");

        Assert.Equal("RETRY_PENDING", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.EventLevelBucket));
        Assert.True((await SettlementEmitterFixture.InboxAttemptsAsync(Cs, eventId) ?? 0) >= 1);
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Equal("OPEN", await SettlementEmitterFixture.RequestStateAsync(Cs, emp));
    }

    /// <summary>The retry→success promotion across the '_EVENT' diagnostics row — the
    /// diagnostics-FIRST-then-completion direction of the R7 event-level monotonic completion
    /// (Step-5a cycle-1 B1 pinned the contract as BOTH directions; the inverse
    /// completion-FIRST-then-late-diagnostics order is
    /// <see cref="LateDiagnostics_AfterCompetingCompletion_NoOps_NoStrandedNonTerminalEventRow"/>):
    /// after the missing §26 mapping is seeded, a re-drain stages the line, writes the real-bucket
    /// PROCESSED checkpoint AND promotes the prior '_EVENT' RETRY_PENDING row to PROCESSED. THE R7
    /// VALID SHAPE asserted here: a COMPLETED event leaves NO non-terminal inbox row at ANY bucket
    /// — a RETRY_PENDING row left behind a terminal row would be stranded FOREVER (the poll
    /// suppresses the event on the terminal row, so nothing would ever promote it).</summary>
    [Fact]
    public async Task RetryThenSuccess_PromotesEventLevelDiagnostics_AndStagesLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(
            Cs, emp, crystallizedDays: 4m, agreementCode: "ZZ_NOMAP");
        await SettlementEmitterFixture.SeedRequestRowAsync(Cs, emp, state: "OPEN");
        var eventId = await SettlementEmitterFixture.WriteTerminationPayoutRequestedEventAsync(
            Factory, emp, crystallizedDays: 4m);

        // (1) First drain — fail-closed ⇒ '_EVENT' RETRY_PENDING, no line.
        var emitter1 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter1,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.EventLevelBucket) == "RETRY_PENDING");
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));

        // (2) Seed the missing §26 mapping, re-drain ⇒ line + the R7 valid post-completion shape.
        await InsertS26MappingAsync("ZZ_NOMAP", "OK24");
        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "PROCESSED");

        Assert.Equal(1L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal(2, rows.Count); // exactly the real-bucket checkpoint + the promoted '_EVENT' row
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.TerminationBucket]);
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.EventLevelBucket]); // promoted, not dangling
        // THE R7 VALID-SHAPE INVARIANT (Step-5a cycle-1 B1): no non-terminal row survives a
        // completed event, at ANY bucket.
        Assert.DoesNotContain(rows, kvp => kvp.Value == "RETRY_PENDING");
        Assert.Equal("LINE_STAGED", await SettlementEmitterFixture.RequestStateAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R7/B1 — the two-worker race: completion FIRST, late diagnostics SECOND (Step-5a cycle-1 B1).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The Step-5a cycle-1 B1 race, exact choreography (the parked-lock pattern): Worker
    /// A's stage tx failed and ROLLED BACK (releasing the xact lock, leaving NO inbox row); Worker
    /// B then COMPLETES the event (real-bucket terminal PROCESSED commits); Worker A's LATE
    /// diagnostics write re-acquires the lock afterwards. Without the fix it would insert a fresh
    /// <c>'_EVENT'</c>/RETRY_PENDING row unchecked — the poll suppresses the event on the real
    /// terminal row, so that non-terminal diagnostics row would be stranded FOREVER (the R7
    /// exclusivity/monotonicity violation). With the fix the late diagnostics write, UNDER the
    /// lock, first sees a terminal row for the event (ANY bucket) and NO-OPs.
    ///
    /// <para>Choreography: a foreign tx holds the employee advisory lock → Worker A's
    /// <see cref="SettlementInboxLineRepository.WriteDiagnosticsAsync"/> provably PARKS on it
    /// (pg_locks ungranted-advisory probe — proves the diagnostics terminal pre-check runs
    /// IN-LOCK, not before it) → Worker B completes the event ON the foreign tx via the production
    /// <see cref="SettlementInboxLineRepository.PromoteToTerminalAsync"/> and commits (releasing
    /// the lock) → the parked diagnostics write resumes, observes the committed terminal row and
    /// NO-OPs. Valid final shape: EXACTLY the real-bucket PROCESSED row; NO '_EVENT' row at
    /// all.</para></summary>
    [Fact]
    public async Task LateDiagnostics_AfterCompetingCompletion_NoOps_NoStrandedNonTerminalEventRow()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_26_");
        var eventId = Guid.NewGuid();
        var repo = new SettlementInboxLineRepository(Factory);
        var identity = new SettlementIdentity(
            emp, SettlementEmitterFixture.VacationType, 2024, 1, SettlementEmitterFixture.TerminationBucket);

        // The foreign tx (Worker B's side of the interleaving) holds the employee advisory lock.
        await using var foreignConn = new NpgsqlConnection(Cs);
        await foreignConn.OpenAsync();
        await using var foreignTx = await foreignConn.BeginTransactionAsync();
        await SettlementInboxLineRepository.AcquireEmployeeLockAsync(
            foreignConn, foreignTx, emp, CancellationToken.None);

        // Worker A's LATE diagnostics fire (its stage tx already rolled back — no inbox row exists).
        // They must PARK on the advisory lock (the terminal pre-check is in-lock by contract).
        var diagnosticsTask = Task.Run(() => repo.WriteDiagnosticsAsync(
            eventId, identity, forceDeadLetter: false, budget: 10,
            "late diagnostics: worker A's stage tx rolled back before worker B completed the event",
            CancellationToken.None));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (diagnosticsTask.IsCompleted)
            {
                var early = await diagnosticsTask;
                Assert.Fail($"the late diagnostics write completed early (written={early}) without " +
                            "parking on the employee advisory lock — the in-lock B1 contract was not exercised.");
            }
            parked = await HasUngrantedAdvisoryWaitAsync();
            if (!parked)
                await Task.Delay(100);
        }
        Assert.True(parked, "the late diagnostics write never parked on the employee advisory lock within 30s.");

        // Worker B COMPLETES the event on the lock-holding tx — the PRODUCTION completion write —
        // then commits, releasing the lock and waking the parked diagnostics write.
        await repo.PromoteToTerminalAsync(foreignConn, foreignTx, eventId, identity, "PROCESSED", CancellationToken.None);
        await foreignTx.CommitAsync();

        // The late diagnostics write resumed, saw the terminal row under the lock, and NO-OP'd.
        var written = await diagnosticsTask;
        Assert.False(written);

        // THE VALID SHAPE (Step-5a cycle-1 B1): exactly the real-bucket terminal checkpoint;
        // NO '_EVENT' row at all — and therefore no stranded non-terminal row the poll would
        // suppress forever.
        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal(
            new[] { SettlementEmitterFixture.TerminationBucket },
            rows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.TerminationBucket]);
        Assert.DoesNotContain(rows, kvp => kvp.Value == "RETRY_PENDING");
    }

    /// <summary>True when some backend on this database is waiting on an UNGRANTED advisory lock —
    /// the deterministic the-write-is-parked probe (the WaiverResolutionTests parked-lock pattern;
    /// the polling backend itself never waits).</summary>
    private async Task<bool> HasUngrantedAdvisoryWaitAsync()
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task InsertS26MappingAsync(string agreementCode, string okVersion)
    {
        await using var conn = new Npgsql.NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings
                (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
            VALUES (@tt, @wt, @ok, @ac, '', 'test §26 mapping', '2020-01-01')
            """, conn);
        cmd.Parameters.AddWithValue("tt", SettlementEmitterFixture.TerminationTimeType);
        cmd.Parameters.AddWithValue("wt", SettlementEmitterFixture.TerminationSentinelWageType);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        await cmd.ExecuteNonQueryAsync();
    }
}
