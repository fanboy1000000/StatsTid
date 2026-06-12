using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7105 (ADR-033 D4/D5; SPRINT-71 R1/R2/R7/R8/R9) — Docker-gated D-tests for the
/// <c>SettlementReversed</c> consumer in
/// <see cref="StatsTid.Integrations.Payroll.Services.SettlementExportEmitter"/>: the
/// compensating-line choreography.
///
/// <para>
/// Per event in ONE advisory-locked tx: the consumer derives its compensation TARGETS from the
/// Payroll-side staged ORIGINAL lines for the reversed settlement row (R9 — its OWN records,
/// never the payload, never a live re-derivation); per target it stages a compensating line that
/// COPIES the original's mapping/period/quantity with <c>line_kind = 'REVERSAL'</c> +
/// <c>reverses_line_id</c> (R8) at the R1 EVEN export sequence <c>2g</c>
/// (= settlement-row sequence + 1), plus one <c>(source_event_id, bucket)</c> checkpoint per
/// bucket — ALL buckets atomically (R7: no partial subset can commit). No staged lines ⇒ a
/// terminal no-op PROCESSED checkpoint at <c>(source_event_id, '_EVENT')</c>. Originals are
/// NEVER mutated or deleted.
/// </para>
///
/// <para>Harness + FAIL-002 protocol: see <see cref="SettlementEmitterFixture"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementReversalConsumerTests : IAsyncLifetime
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
    // Compensation — exactly the staged lines, R8 shape, R1 even export sequence.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>One staged §26 ORIGINAL line at settlement sequence 1 ⇒ exactly ONE compensating
    /// REVERSAL line: mapping/period/quantity COPIED from the original (R8), <c>line_kind =
    /// 'REVERSAL'</c>, <c>reverses_line_id</c> = the original's <c>line_id</c>, export sequence =
    /// 2 (R1: even <c>2g</c> for generation g = 1), <c>amount = 0</c>; a PROCESSED checkpoint at
    /// <c>(event, bucket)</c>; the ORIGINAL line is untouched (never mutated/deleted).</summary>
    [Fact]
    public async Task StagedOriginal_IsCompensated_R8Shape_EvenExportSequence_OriginalUntouched()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        var originalSource = Guid.NewGuid();
        var originalLineId = await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, originalSource, SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 1);

        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.TerminationBucket) == "PROCESSED");

        // The compensating line at the EVEN export sequence 2 (R1: 2g, g = (1+1)/2 = 1).
        var reversal = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.NotNull(reversal);
        Assert.Equal("REVERSAL", reversal!.Value.LineKind);
        Assert.Equal(originalLineId, reversal.Value.ReversesLineId);  // R8: the unambiguous FK reference
        Assert.Equal(SettlementEmitterFixture.TerminationSentinelWageType, reversal.Value.WageType); // mapping copied
        Assert.Equal(12.5m, reversal.Value.Hours);                    // quantity copied, POSITIVE (direction = line_kind)
        Assert.Equal(0m, reversal.Value.Amount);                      // money-free
        Assert.Equal(SettlementEmitterFixture.BoundaryDate, reversal.Value.PeriodStart); // period copied
        Assert.Equal(eventId, reversal.Value.SourceEventId);

        // The original is untouched (P3/R9 — compensation is purely additive).
        var original = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 1, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.NotNull(original);
        Assert.Equal("ORIGINAL", original!.Value.LineKind);
        Assert.Equal(originalSource, original.Value.SourceEventId);
        Assert.Equal(12.5m, original.Value.Hours);

        // Exactly the two lines (original + its compensation), nothing else.
        Assert.Equal(2L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.TerminationBucket));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Multi-bucket — one tx, complete set, per-bucket checkpoints (R7).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>TWO staged ORIGINAL buckets at the reversed sequence ⇒ BOTH compensated in ONE
    /// drain, each with its own <c>(event, bucket)</c> PROCESSED checkpoint (the composite-PK
    /// multi-row-per-event admission) — and no '_EVENT' row is left behind on the clean path.</summary>
    [Fact]
    public async Task MultiBucket_CompensatesAllBuckets_OneCheckpointPerBucket()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        var s24LineId = await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.AutoPayoutBucket,
            SettlementEmitterFixture.SentinelWageType, hours: 5m, sequence: 1);
        var s26LineId = await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 1);

        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => (await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId)).Count == 2);

        // Both buckets checkpointed PROCESSED under ONE source_event_id (the composite PK).
        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal(2, rows.Count);
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.AutoPayoutBucket]);
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.TerminationBucket]);

        // Both compensating lines exist at export sequence 2 with the right back-references.
        var rev24 = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.AutoPayoutBucket);
        var rev26 = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.Equal(s24LineId, rev24!.Value.ReversesLineId);
        Assert.Equal(s26LineId, rev26!.Value.ReversesLineId);
        Assert.Equal(5m, rev24.Value.Hours);
        Assert.Equal(12.5m, rev26.Value.Hours);
        Assert.Equal(4L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp)); // 2 originals + 2 compensations
    }

    /// <summary>R7 multi-bucket ATOMICITY under failure: when the SECOND bucket's compensating
    /// insert collides with a FOREIGN-source line (a different <c>source_event_id</c> already holds
    /// that (identity, even-sequence, bucket) slot), the WHOLE multi-bucket set rolls back — the
    /// FIRST bucket's compensating line and checkpoint must NOT survive (no partial subset, ever) —
    /// and the collision dead-letters the event at '_EVENT' (non-self-healing).</summary>
    [Fact]
    public async Task MultiBucket_SecondBucketCollision_FullRollback_NoPartialSubset_DeadLetters()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        // Targets ordered by bucket: AUTO_PAYOUT_24 < TERMINATION_PAYOUT_26 — the collision is
        // planted on the SECOND (TERMINATION) slot so the first compensation succeeds in-tx first.
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.AutoPayoutBucket,
            SettlementEmitterFixture.SentinelWageType, hours: 5m, sequence: 1);
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 1);
        // The foreign squatter on the SECOND bucket's even export slot (seq 2). A REVERSAL-shaped
        // row from an unrelated source event — the verify-on-conflict must classify it Collision.
        var foreignSource = Guid.NewGuid();
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, foreignSource, SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 9m, sequence: 2);

        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.EventLevelBucket) == "DEAD_LETTER");

        // Dead-lettered at '_EVENT' (collision ⇒ forceDeadLetter), and NOTHING from this event
        // survived: no first-bucket compensating line, no first-bucket checkpoint (full rollback).
        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal(
            new[] { SettlementEmitterFixture.EventLevelBucket },
            rows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("DEAD_LETTER", rows[SettlementEmitterFixture.EventLevelBucket]);

        var rev24 = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.AutoPayoutBucket);
        Assert.Null(rev24); // the FIRST bucket's compensation did NOT survive the rollback

        // The foreign squatter is untouched; total lines = 2 originals + the squatter.
        var squatter = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.Equal(foreignSource, squatter!.Value.SourceEventId);
        Assert.Equal(3L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
    }

    /// <summary>R8 / Step-5a cycle-1 B2 — a SAME-event conflict must validate the FULL immutable
    /// line shape, never classify on <c>source_event_id</c> alone: a pre-seeded colliding row at
    /// the even export slot carrying the SAME reversal event id but a WRONG
    /// <c>reverses_line_id</c> (it points at the §24 original instead of the §26 original — every
    /// OTHER field identical to what the consumer would stage) is a COLLISION ⇒ the event is
    /// dead-lettered with the mismatch ENUMERATED, NOT processed as benign; the wrong row is
    /// untouched; and the FIRST bucket's already-inserted compensation rolls back with the whole
    /// set (R7 — no partial subset).</summary>
    [Fact]
    public async Task SameEventConflict_WrongReversesLineId_DeadLetters_NotBenign_WrongRowUntouched()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        // Two staged originals at the reversed sequence (buckets ordered 24 < 26 — the collision
        // sits on the SECOND target so the first compensation succeeds in-tx before the conflict).
        var s24LineId = await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.AutoPayoutBucket,
            SettlementEmitterFixture.SentinelWageType, hours: 5m, sequence: 1);
        var s26LineId = await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 1);

        // The reversal event FIRST — its id seeds the colliding row's source_event_id, so the
        // pre-B2 source-only check would have called the conflict BenignRedelivery and PROCESSED it.
        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        // The colliding row at (seq 2, §26 bucket): SAME event id, IDENTICAL mapping/period/
        // quantity/kind — ONLY reverses_line_id is wrong (the §24 original). reverses_line_id is
        // therefore the SOLE discriminator, proving it participates in the validation (R8).
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, eventId, SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 2,
            lineKind: "REVERSAL", reversesLineId: s24LineId, // WRONG — must be s26LineId
            createdBy: "settlement-export-emitter"); // matches the consumer ⇒ reverses_line_id stays the SOLE discriminator

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.EventLevelBucket) == "DEAD_LETTER");

        // Dead-lettered at '_EVENT' (NOT benign, never PROCESSED) with the mismatch enumerated.
        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal(
            new[] { SettlementEmitterFixture.EventLevelBucket },
            rows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("DEAD_LETTER", rows[SettlementEmitterFixture.EventLevelBucket]);
        var lastError = await InboxLastErrorAsync(eventId);
        Assert.NotNull(lastError);
        Assert.Contains("reverses_line_id", lastError);

        // The wrong row is UNTOUCHED (still the wrong back-reference, same source event).
        var wrongRow = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.TerminationBucket);
        Assert.NotNull(wrongRow);
        Assert.Equal(s24LineId, wrongRow!.Value.ReversesLineId);
        Assert.Equal(eventId, wrongRow.Value.SourceEventId);

        // The FIRST bucket's compensation did NOT survive the full rollback (R7), the originals
        // stand: 2 originals + the wrong squatter = 3 lines total.
        Assert.Null(await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.AutoPayoutBucket));
        Assert.Equal(3L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Equal(s26LineId, (await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 1, bucket: SettlementEmitterFixture.TerminationBucket))!.Value.LineId);
    }

    /// <summary>The inbox row's <c>last_error</c> at the event-level '_EVENT' sentinel (the
    /// dead-letter report carrying the B2 enumerated mismatches), or null.</summary>
    private async Task<string?> InboxLastErrorAsync(Guid eventId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT last_error FROM settlement_payroll_inbox WHERE source_event_id = @id AND bucket = @b", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        cmd.Parameters.AddWithValue("b", SettlementEmitterFixture.EventLevelBucket);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }

    // ════════════════════════════════════════════════════════════════════════
    // No staged lines ⇒ terminal no-op checkpoint (the declared '_EVENT' PROCESSED).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A reversal of a row with NO staged lines (the common race-winner case — the
    /// §24/§26 consumers will SKIPPED_VOIDED their events per R9) consumes terminally with a no-op
    /// PROCESSED checkpoint at <c>(event, '_EVENT')</c> and stages NOTHING; a second drain does not
    /// re-select it.</summary>
    [Fact]
    public async Task NoStagedLines_NoOpCheckpointAtEventBucket_NothingStaged()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.EventLevelBucket) == "PROCESSED");

        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.EventLevelBucket));
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));

        // Terminal — not re-selected.
        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => false, settleWait: TimeSpan.FromSeconds(2));
        Assert.Equal(0L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Single(await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Idempotent redelivery — benign, byte-stable, exactly-once.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A TRUE redelivery (all checkpoints removed; the compensating lines persist)
    /// re-runs the full claim: every compensating insert resolves as BenignRedelivery (same
    /// <c>source_event_id</c>), the checkpoints are re-written, and the line population is
    /// unchanged (still exactly one REVERSAL line per bucket, byte-identical).</summary>
    [Fact]
    public async Task Redelivery_IsBenign_NoSecondCompensation()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.AutoPayoutBucket,
            SettlementEmitterFixture.SentinelWageType, hours: 5m, sequence: 1);
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 1);

        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => (await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId)).Count == 2);
        Assert.Equal(4L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        var firstRev24 = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.AutoPayoutBucket);

        // Drop ALL checkpoints ⇒ the event re-selects ⇒ a second REAL claim runs end-to-end.
        await SettlementEmitterFixture.DeleteInboxRowAsync(Cs, eventId);

        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => (await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId)).Count == 2);

        Assert.Equal(4L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp)); // no double compensation
        var secondRev24 = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, sequence: 2, bucket: SettlementEmitterFixture.AutoPayoutBucket);
        Assert.Equal(firstRev24!.Value, secondRev24!.Value); // byte-identical (the UNIQUE refused a re-insert)
        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.AutoPayoutBucket]);
        Assert.Equal("PROCESSED", rows[SettlementEmitterFixture.TerminationBucket]);
    }

    /// <summary>Step-5a cycle-2 residual (R8): <c>created_by</c> is immutable provenance and MUST
    /// participate in the conflict validation — a same-event row written by a DIFFERENT actor is
    /// never a benign redelivery of this write. The colliding row here is IDENTICAL on every other
    /// compared field (kind, reverses_line_id, mapping, period, quantity), so <c>created_by</c> is
    /// the SOLE discriminator.</summary>
    [Fact]
    public async Task SameEventConflict_WrongCreatedBy_DeadLetters_NotBenign()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s71_rev_");
        await SettlementEmitterFixture.SeedSettlementRowAsync(Cs, emp, state: "REVERSED");
        var s26LineId = await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, Guid.NewGuid(), SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 1);

        var eventId = await SettlementEmitterFixture.WriteSettlementReversedEventAsync(
            Factory, emp, settlementSequence: 1);

        // The colliding even-slot row: same event id, CORRECT reverses_line_id and an otherwise
        // byte-identical shape — only created_by deviates from the consumer's actor.
        await SettlementEmitterFixture.InsertLineRowAsync(
            Cs, emp, eventId, SettlementEmitterFixture.TerminationBucket,
            SettlementEmitterFixture.TerminationSentinelWageType, hours: 12.5m, sequence: 2,
            lineKind: "REVERSAL", reversesLineId: s26LineId,
            createdBy: "foreign-actor");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusForBucketAsync(
                Cs, eventId, SettlementEmitterFixture.EventLevelBucket) == "DEAD_LETTER");

        var rows = await SettlementEmitterFixture.InboxRowsAsync(Cs, eventId);
        Assert.Equal("DEAD_LETTER", rows[SettlementEmitterFixture.EventLevelBucket]);
        var lastError = await InboxLastErrorAsync(eventId);
        Assert.NotNull(lastError);
        Assert.Contains("created_by", lastError);
        Assert.DoesNotContain("reverses_line_id", lastError); // the sole-discriminator proof
    }
}
