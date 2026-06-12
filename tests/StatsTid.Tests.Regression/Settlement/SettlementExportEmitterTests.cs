using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S69 / TASK-6907 (ADR-033 slice 1b) — Docker-gated D-tests for the
/// <see cref="StatsTid.Integrations.Payroll.Services.SettlementExportEmitter"/>: the §24
/// auto-payout settlement-export staging pipeline's exactly-once / money-free / fail-closed /
/// replay-stable invariants (Step-0b B1–B5 / C2-B1 / C2-B2 / C3-B1 / C4-B1 / W4).
///
/// <para>
/// The emitter consumes <c>VacationAutoPaidOut</c> from the canonical <c>events</c> table and stages a
/// durable <c>settlement_export_lines</c> row + a <c>settlement_payroll_inbox</c> checkpoint in ONE
/// advisory-locked tx, EXACTLY ONCE, with NO external emission this sprint. Each test writes the event
/// via the production <see cref="StatsTid.Infrastructure.PostgresEventStore"/> and drives EXACTLY ONE
/// real <c>DrainOnceAsync</c> per "process" via <see cref="SettlementEmitterFixture.ProcessOnceAsync"/>
/// (the BackgroundService runs one drain immediately on start, before its 30s delay).
/// </para>
///
/// <para>Harness + FAIL-002 protocol: see <see cref="SettlementEmitterFixture"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementExportEmitterTests : IAsyncLifetime
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
    // Scenario 1 — exactly-once / idempotent claim (B1, after-restart redelivery).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Processing one <c>VacationAutoPaidOut</c> stages EXACTLY ONE line + one PROCESSED inbox
    /// row. Then drives a TRUE duplicate claim of the SAME, STILL-SELECTABLE event (the inbox checkpoint
    /// is removed so the poll re-selects it while the staged line persists) — exercising the line-UNIQUE
    /// conflict + the same-<c>source_event_id</c> <see cref="LineInsertOutcome.BenignRedelivery"/> branch
    /// (NOT merely the poll's terminal-filter): still EXACTLY ONE line, inbox back to PROCESSED, and the
    /// persisted line fields are BYTE-IDENTICAL across the two claim attempts.</summary>
    [Fact]
    public async Task ProcessOnce_StagesExactlyOneLine_AndInboxProcessed_DuplicateClaimIsBenignRedelivery()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(Factory, emp, payoutDays: 5m);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
        var firstLine = await SettlementEmitterFixture.ReadLineAsync(Cs, emp);
        Assert.NotNull(firstLine);

        // TRUE duplicate claim: drop ONLY the inbox checkpoint (the staged line stays). The event now has
        // no terminal inbox row ⇒ the poll RE-SELECTS it and a second real drain re-runs the full claim
        // path. InsertLineAsync hits the line-UNIQUE conflict, sees the SAME source_event_id ⇒
        // BenignRedelivery ⇒ the inbox is re-promoted to PROCESSED. This drives the production dedup
        // branch (not just the poll filter), proving a redelivered/replayed event never double-stages.
        await SettlementEmitterFixture.DeleteInboxRowAsync(Cs, eventId);

        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        // Still EXACTLY one line, inbox PROCESSED again, and the line is BYTE-IDENTICAL (the immutable
        // event yields the same payload, and the UNIQUE refused a second insert — the row is untouched).
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        var secondLine = await SettlementEmitterFixture.ReadLineAsync(Cs, emp);
        Assert.NotNull(secondLine);
        Assert.Equal(firstLine!.Value, secondLine!.Value); // record-struct value equality ⇒ all fields byte-identical
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 2 — money-free (B5/D1): hours = PayoutDays, amount = 0, no rate column.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The staged line is money-free: <c>hours = PayoutDays</c>, <c>amount = 0</c>, the wage-key
    /// columns come from the snapshot, and the table has NO salary/rate column (a schema assertion).</summary>
    [Fact]
    public async Task StagedLine_IsMoneyFree_HoursEqualPayoutDays_AmountZero_NoRateColumn()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 3.5m, agreementCode: "AC", okVersion: "OK24");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.LineCountAsync(Cs, emp) == 1);

        var line = await SettlementEmitterFixture.ReadLineAsync(Cs, emp);
        Assert.NotNull(line);
        Assert.Equal(3.5m, line!.Value.Hours);           // hours = PayoutDays (day-count, not kroner)
        Assert.Equal(0m, line.Value.Amount);              // money-free (CHECK amount = 0)
        Assert.Equal(SettlementEmitterFixture.SentinelWageType, line.Value.WageType);
        Assert.Equal("OK24", line.Value.OkVersion);
        Assert.Equal("AC", line.Value.AgreementCode);
        Assert.Equal(eventId, line.Value.SourceEventId);

        // No salary/rate/amount-bearing money column exists on the line (only the day-count `hours` +
        // the CHECK-pinned `amount = 0`). Assert the absence of a rate/salary column at the schema level.
        Assert.False(await ColumnExistsAsync("settlement_export_lines", "rate"));
        Assert.False(await ColumnExistsAsync("settlement_export_lines", "salary"));
        Assert.False(await ColumnExistsAsync("settlement_export_lines", "kroner"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 3 — reconciled-skip (mutual exclusion A / B2): SKIPPED_RECONCILED, no line.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>When the operator has ALREADY reconciled the §24 bucket (<c>payout_reconciled_at</c>
    /// set on the settlement row), the emitter stages NO line and writes a terminal
    /// <c>SKIPPED_RECONCILED</c> checkpoint.</summary>
    [Fact]
    public async Task ReconciledBucket_Skips_WritesSkippedReconciled_NoLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        // A SETTLED row already reconciled by an operator (payout_reconciled_at NOT NULL).
        await SettlementEmitterFixture.SeedSettledPayoutRowAsync(Cs, emp, payoutDays: 5m, reconciled: true);
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(Factory, emp, payoutDays: 5m);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "SKIPPED_RECONCILED");

        Assert.Equal("SKIPPED_RECONCILED", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 5 — retry → success promotion (C4-B1): RETRY_PENDING then PROCESSED + line.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A deterministic failure (no §24 mapping for the snapshot's agreement) leaves a
    /// <c>RETRY_PENDING</c> inbox row with <c>attempts &gt;= 1</c> and NO line. Once the mapping is
    /// seeded, a re-drain PROMOTES the inbox to <c>PROCESSED</c> (not left stuck non-terminal) and stages
    /// the line. Proves the success-path conditional promotion (a bare DO NOTHING would leave it stuck).</summary>
    [Fact]
    public async Task RetryThenSuccess_PromotesInboxToProcessed_AndStagesLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        // An agreement code with NO §24 wage_type_mapping seeded (the seed covers {AC,HK,PROSA,...}).
        const string unmappedAgreement = "ZZ_NOMAP";
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: unmappedAgreement, okVersion: "OK24");

        // (1) First drain — null GetByKeyAtAsync ⇒ deterministic fail-closed ⇒ RETRY_PENDING, no line.
        var emitter1 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter1,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "RETRY_PENDING");

        Assert.Equal("RETRY_PENDING", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.True((await SettlementEmitterFixture.InboxAttemptsAsync(Cs, eventId) ?? 0) >= 1);
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));

        // (2) Seed the missing §24 mapping, then re-drain ⇒ the RETRY_PENDING row is PROMOTED to
        //     PROCESSED + the line is staged.
        await Insert24MappingAsync(unmappedAgreement, "OK24", SettlementEmitterFixture.SentinelWageType, new DateOnly(2020, 1, 1));

        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 6 — retry → DEAD_LETTER on budget (C2-B1 / B5): no line, parked.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>With the §24 mapping permanently missing, repeated drains keep the row
    /// <c>RETRY_PENDING</c> (bumping <c>attempts</c>) until the durable retry budget (MaxAttempts=10) is
    /// reached, then it transitions to terminal <c>DEAD_LETTER</c> with NO line ever staged — no live or
    /// hard-coded fallback.</summary>
    [Fact]
    public async Task RetryBudgetExhausted_DeadLetters_NoLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        const string unmappedAgreement = "ZZ_NOMAP";
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: unmappedAgreement, okVersion: "OK24");

        // Drive drains until DEAD_LETTER (each drain = one attempt; budget = 10). A generous cap on
        // iterations guards against an infinite loop if the lifecycle were wrong (the test would FAIL
        // the terminal assertion rather than hang).
        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        for (var i = 0; i < 15; i++)
        {
            var beforeAttempts = await SettlementEmitterFixture.InboxAttemptsAsync(Cs, eventId) ?? 0;
            await SettlementEmitterFixture.ProcessOnceAsync(emitter,
                until: async () =>
                {
                    var status = await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId);
                    if (status == "DEAD_LETTER") return true;
                    return (await SettlementEmitterFixture.InboxAttemptsAsync(Cs, eventId) ?? 0) > beforeAttempts;
                });
            if (await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "DEAD_LETTER")
                break;
        }

        Assert.Equal("DEAD_LETTER", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 7 — terminal re-check / no clobber (Step-5a FIX 1 / C3-B1).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A pre-existing TERMINAL inbox row (DEAD_LETTER) for the event must never be overwritten,
    /// and no line is staged: the under-lock terminal re-check (the select→lock TOCTOU guard) skips the
    /// event. The DEAD_LETTER status survives the drain (no clobber to RETRY_PENDING/PROCESSED, no
    /// line).
    ///
    /// <para>
    /// SCOPE (Step-7a FIX 2 — explicit limitation): this verifies the PRE-CHECK / poll-filter logic (the
    /// poll excludes a terminal row, and the in-lock terminal re-check + the terminal-aware PROMOTE
    /// <c>WHERE … = 'RETRY_PENDING'</c> guards refuse to clobber) using a SEQUENTIAL pre-set-then-drain
    /// shape — it does NOT spin up a second concurrent worker to race the lock. That is INTENTIONAL and
    /// single-emitter-instance-MOOT for slice 1b: the emitter is ONE BackgroundService instance, so the
    /// true under-lock terminal-race never arises in production. The shared employee advisory lock + the
    /// SQL terminal-aware guards (verified here + by the Step-5a/Step-7a dual-lens code review) are the
    /// production protection should a second instance ever be introduced.
    /// </para></summary>
    [Fact]
    public async Task PreexistingTerminalInbox_IsNotClobbered_NoLineStaged()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        // A perfectly RESOLVABLE event (AC/OK24 mapping exists) — so if the terminal re-check were
        // absent, the claim path WOULD stage a line. The pre-set terminal row must suppress that.
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: "AC", okVersion: "OK24");

        // Pre-set the inbox row terminal (DEAD_LETTER) for this event, BEFORE any drain.
        await PreInsertTerminalInboxAsync(eventId, emp, "DEAD_LETTER");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        // The poll filters terminal rows ⇒ the event is not re-selected; drive a bounded drain and
        // assert NOTHING changed. (Even under the select→lock race, the in-lock terminal re-check skips.)
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.LineCountAsync(Cs, emp) >= 1, // never true
            settleWait: TimeSpan.FromSeconds(2));

        Assert.Equal("DEAD_LETTER", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId)); // not clobbered
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));                  // no line
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 8 — replay-stable / delayed-first-consumption (B4).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The §24 lønart resolves off the snapshot's <c>SettlementBoundaryDate</c> via the dated
    /// natural key. If the <c>wage_type_mappings</c> row is SUPERSEDED with a NEW row effective AFTER the
    /// boundary, a DELAYED FIRST consumption still stages the HISTORICAL (as-of-boundary) mapping — NOT
    /// the superseded one. This proves the snapshot-keyed dated lookup, not mere dedup (the event is
    /// consumed for the FIRST time AFTER the supersession).</summary>
    [Fact]
    public async Task DelayedFirstConsumption_UsesHistoricalMapping_NotSuperseded()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);

        // Use a dedicated agreement so we control its §24 mapping history without touching the seed.
        const string agreement = "ZZ_HIST";
        var boundary = SettlementEmitterFixture.BoundaryDate; // 2025-12-31
        var supersedeDate = new DateOnly(2026, 6, 1);         // AFTER the boundary

        // Historical row effective [2020-01-01, 2026-06-01) — the one in force AT the boundary.
        await Insert24MappingAsync(agreement, "OK24", "SLS_TBD_S24", new DateOnly(2020, 1, 1), effectiveTo: supersedeDate);
        // Superseding row effective [2026-06-01, ∞) — a DIFFERENT lønart, in force only AFTER the boundary.
        await Insert24MappingAsync(agreement, "OK24", "SLS_TBD_S24_V2", supersedeDate, effectiveTo: null);

        // Write the event NOW (its snapshot boundary is 2025-12-31). FIRST consumption happens below,
        // AFTER both mapping rows already exist — so a non-dated lookup would pick the open V2 row.
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: agreement, okVersion: "OK24", boundaryDate: boundary);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        var line = await SettlementEmitterFixture.ReadLineAsync(Cs, emp);
        Assert.NotNull(line);
        Assert.Equal("SLS_TBD_S24", line!.Value.WageType);        // the as-of-boundary historical row
        Assert.NotEqual("SLS_TBD_S24_V2", line.Value.WageType);   // NOT the superseded (later) row
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 9 — non-identical line collision (C2-B2): DEAD_LETTER, existing line untouched.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>If a <c>settlement_export_lines</c> row already holds the bucket but came from a DIFFERENT
    /// <c>source_event_id</c>, processing an event whose identity maps to the same business key
    /// DEAD_LETTERs (the collision is reported, not silently swallowed as benign) and the pre-existing
    /// line is left untouched.</summary>
    [Fact]
    public async Task NonIdenticalLineCollision_DeadLetters_ExistingLineUntouched()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);

        // Pre-existing line for the bucket from a DIFFERENT source event (a foreign event_id).
        var foreignEventId = Guid.NewGuid();
        await PreInsertLineAsync(emp, foreignEventId, wageType: "SLS_TBD_S24", hours: 9m);

        // Now process a real VacationAutoPaidOut whose identity maps to the SAME bucket but a different
        // source event ⇒ the verify-on-conflict sees a foreign origin ⇒ collision.
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: "AC", okVersion: "OK24");
        Assert.NotEqual(foreignEventId, eventId);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "DEAD_LETTER");

        Assert.Equal("DEAD_LETTER", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));

        // Exactly the ONE pre-existing line remains, and it is the foreign one (untouched).
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
        var line = await SettlementEmitterFixture.ReadLineAsync(Cs, emp);
        Assert.Equal(foreignEventId, line!.Value.SourceEventId);
        Assert.Equal(9m, line.Value.Hours);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 10 — poison event (un-deserializable) dead-letters (Step-7a FIX 1 / BLOCKER).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A <c>VacationAutoPaidOut</c> row whose payload cannot be deserialized (poison) must be
    /// terminally <c>DEAD_LETTER</c>ed at <c>(source_event_id, '_EVENT')</c> — no recoverable identity ⇒
    /// no advisory lock, identity columns NULL; since the S71 R7 composite-key migration the row carries
    /// the event-level <c>'_EVENT'</c> sentinel bucket instead of the pre-S71 NULL (<c>bucket</c> is now
    /// NOT NULL) — so the poll stops re-selecting it every interval forever and the consumer is
    /// unstalled. No line is staged, and a SECOND drain does NOT re-select it (terminal).
    /// Regression for the Step-7a BLOCKER: the prior log-and-return wrote NO checkpoint.</summary>
    [Fact]
    public async Task PoisonEvent_CannotDeserialize_DeadLetters_KeyedBySourceEventId_NoLine_NotReselected()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        // A real event whose data JSONB is then corrupted so EventSerializer.Deserialize throws.
        var eventId = await SettlementEmitterFixture.WritePoisonAutoPaidOutEventAsync(Factory, emp);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "DEAD_LETTER");

        // Dead-lettered at the '_EVENT' sentinel, no line, identity columns are NULL (only permitted
        // on the poison path).
        Assert.Equal("DEAD_LETTER", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal("DEAD_LETTER", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.EventLevelBucket)); // keyed at (event, '_EVENT')
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
        Assert.Null(await InboxEmployeeIdAsync(eventId)); // identity NULL (no recoverable identity)
        Assert.NotNull(await InboxLastErrorAsync(eventId)); // the deserialize error is recorded

        // A second drain must NOT re-select the (now terminal) poison event — the consumer is unstalled.
        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => false, // never; just let one drain pass over a terminal row
            settleWait: TimeSpan.FromSeconds(2));
        Assert.Equal("DEAD_LETTER", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 11 (S71 TASK-7105) — R9 reversal-awareness retrofit / the R12 emitter-vs-reversal race.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>SPRINT-71 R9/R12 — reversal-commits-BEFORE-§24-consumption must NOT stage an orphan
    /// line: the settlement row at the event's exact sequence is already REVERSED when the emitter
    /// consumes the <c>VacationAutoPaidOut</c>, so the under-lock re-read stages NOTHING and writes a
    /// terminal <c>SKIPPED_VOIDED</c> checkpoint at the §24 bucket. Without the retrofit the emitter
    /// would stage a live line the <c>SettlementReversed</c> consumer can never compensate (its
    /// targets derive from lines staged BEFORE the reversal) — the orphan the R12 race names. A
    /// second drain does not re-select (terminal).</summary>
    [Fact]
    public async Task ReversedSettlement_AutoPaidOutEvent_SkippedVoided_NoOrphanLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs);
        // A zero-transfer auto-partition row that EMITTED VacationAutoPaidOut (payout > 0) and was
        // then reversed by the operator (D-A makes exactly these rows reversible).
        await SettlementEmitterFixture.SeedSettlementRowAsync(
            Cs, emp, trigger: "YEAR_END", state: "SETTLED", crystallizedDays: null, payoutDays: 5m);
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(Factory, emp, payoutDays: 5m);
        await SettlementEmitterFixture.MarkSettlementReversedAsync(Cs, emp); // the reversal wins the race

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.AutoPayoutBucket) == "SKIPPED_VOIDED");

        Assert.Equal("SKIPPED_VOIDED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.AutoPayoutBucket));
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp)); // orphan PREVENTED

        // Terminal: a second drain leaves everything untouched.
        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => false, settleWait: TimeSpan.FromSeconds(2));
        Assert.Equal("SKIPPED_VOIDED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.AutoPayoutBucket));
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 12 — snapshot capture fail-closed (TASK-6901): no agreement/profile ⇒ throw, no write.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The slice-1a snapshot CAPTURE (the source of the emitter's replay-deterministic key) is
    /// fail-closed: an employee whose dated <c>user_agreement_codes</c> / <c>employee_profiles</c> rows
    /// do NOT cover the ferieår start makes <c>VacationSettlementService.SettleAsync</c> THROW, and NO
    /// snapshot/settlement row is written. Proves a settlement never captures a live/empty wage-mapping
    /// key (B3/B5 at the capture site).</summary>
    [Fact]
    public async Task SnapshotCapture_NoDatedAgreementCoveringFerieaarStart_FailsClosed_NoSettlement()
    {
        // VACATION ferieår 2024 (reset_month 9) ⇒ ferieår start 2024-09-01. Seed the employee's
        // agreement/profile EFFECTIVE ONLY from 2025-01-01 — AFTER the ferieår start — so the strict
        // dated read at 2024-09-01 finds no row and capture fails closed.
        var emp = "emp_s69_failclosed_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            Cs, emp, SettlementEmitterFixture.OrgId, "AC", "OK24",
            effectiveFrom: new DateOnly(2025, 1, 1));

        await using var factory = new StatsTidWebApplicationFactory(Cs);
        _ = factory.CreateClient(); // boots seeders (VACATION entitlement config) the capture reads
        var svc = factory.Services.GetRequiredService<VacationSettlementService>();

        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Capture fails closed: no dated agreement/profile covers 2024-09-01 ⇒ InvalidOperationException.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await svc.SettleAsync(emp, SettlementEmitterFixture.VacationType, 2024, "YEAR_END", conn, tx));
        await tx.RollbackAsync();

        // No settlement row was written (fail-closed ⇒ no partial settlement).
        Assert.Equal(0L, await CountSettlementsAsync(emp));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = @t AND column_name = @c)
            """, conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task Insert24MappingAsync(
        string agreementCode, string okVersion, string wageType, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings
                (time_type, wage_type, ok_version, agreement_code, position, description, effective_from, effective_to)
            VALUES
                (@tt, @wt, @ok, @ac, '', 'test §24 mapping', @from, @to)
            """, conn);
        cmd.Parameters.AddWithValue("tt", SettlementEmitterFixture.SettlementTimeType);
        cmd.Parameters.AddWithValue("wt", wageType);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreInsertTerminalInboxAsync(Guid eventId, string employeeId, string terminalStatus)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_payroll_inbox
                (source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket,
                 processing_status, attempts, processed_at)
            VALUES
                (@id, @e, @t, 2024, 1, @b, @status, 0, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("id", eventId);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", SettlementEmitterFixture.VacationType);
        cmd.Parameters.AddWithValue("b", SettlementEmitterFixture.AutoPayoutBucket);
        cmd.Parameters.AddWithValue("status", terminalStatus);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreInsertLineAsync(string employeeId, Guid sourceEventId, string wageType, decimal hours)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_export_lines
                (employee_id, entitlement_type, entitlement_year, sequence, bucket,
                 wage_type, hours, amount, ok_version, agreement_code, position,
                 period_start, period_end, source_event_id, created_by)
            VALUES
                (@e, @t, 2024, 1, @b, @wt, @hours, 0, 'OK24', 'AC', '',
                 @d, @d, @src, 'pre-existing-test-line')
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", SettlementEmitterFixture.VacationType);
        cmd.Parameters.AddWithValue("b", SettlementEmitterFixture.AutoPayoutBucket);
        cmd.Parameters.AddWithValue("wt", wageType);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("d", SettlementEmitterFixture.BoundaryDate);
        cmd.Parameters.AddWithValue("src", sourceEventId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> CountSettlementsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The inbox row's <c>employee_id</c> (NULL on a poison DEAD_LETTER row), or null when absent.</summary>
    private async Task<string?> InboxEmployeeIdAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT employee_id FROM settlement_payroll_inbox WHERE source_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var v = await cmd.ExecuteScalarAsync();
        return v as string; // DBNull or absent ⇒ null
    }

    /// <summary>The inbox row's <c>last_error</c> (the recorded poison/diagnostics cause), or null.</summary>
    private async Task<string?> InboxLastErrorAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT last_error FROM settlement_payroll_inbox WHERE source_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }
}
